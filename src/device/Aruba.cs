namespace ConfigDump.Device;

using System.Net;
using System.Text.Json;
using Renci.SshNet;

public static class Aruba
{
    // Designed based on Aruba 2930M-24G (JL319A)
    public static bool IsAruba(this Device device)
        => device.model.Contains("aruba", StringComparison.InvariantCultureIgnoreCase);

    [Obsolete("This method is currently unfinished due to SSH misbehaving.", false)]
    public async static Task<byte[]> DumpSSH(Device device)
    {
        Credential credential = device.GetAdminLogin();

        SshClient client = new(credential.url.Host, 22, credential.username, credential.password);
        await client.ConnectAsync(new CancellationToken(false));
        ShellStream stream = client.CreateShellStream("", 200, 100, uint.MaxValue, uint.MaxValue, 1024);

        byte[] buffer = new byte[8192];
        int readBytes;

        readBytes = await stream.ReadAsync(buffer);

        await stream.WriteAsync("A"u8.ToArray());
        await stream.FlushAsync();

        await stream.WriteAsync("show running-config\r\n"u8.ToArray());
        await stream.FlushAsync();

        readBytes = await stream.ReadAsync(buffer);

        Console.WriteLine(Convert.ToBase64String(buffer, 0, readBytes));

        throw new NotImplementedException();
    }

    public async static Task<ConfigResult> DumpHTTP(Device device)
    {
        HttpResponseMessage response;
        Credential credential = device.credentials[0];

        HttpClient httpClient = new()
        {
            BaseAddress = credential.url
        };

        // Go to index to get session token cookie
        response = await httpClient.GetAsync("");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not get session token, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Login to the dashboard
        response = await httpClient.PostAsync(
            "html/logincheck",
            new FormUrlEncodedContent([
                KeyValuePair.Create("user", credential.username),
                KeyValuePair.Create("pass", credential.password),
                KeyValuePair.Create("Submit", "Login"),
            ])
        );

        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not login, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Go to home to get request headers, such as the request token
        StreamReader reader = new(await httpClient.GetStreamAsync("html/homeStatus.html"));

        // Read until headers
        while (reader.Peek() >= 0)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null) break;
            if (line == "Ext.Ajax.defaultHeaders = {") break;
        }

        string headersJson = "{";

        // Read headers until end curly brace
        while (reader.Peek() >= 0)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null) break;
            if (line == "};") break;

            headersJson += line;
        }

        // Parse headers and add them to default request headers
        Dictionary<string, string>? headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson + "}", JsonSerializerOptions.Web);
        foreach (KeyValuePair<string, string> kvp in headers ?? [])
            httpClient.DefaultRequestHeaders.Add(kvp.Key, kvp.Value);

        // Go to system updates to get config information
        reader = new(await httpClient.GetStreamAsync("html/sysUp-dw.html"));
        string configsJson = "";

        // Find line with config information
        const string CONFIG_PREFIX = "var configAccess = ";
        while (reader.Peek() >= 0)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null) break;
            if (line.StartsWith(CONFIG_PREFIX))
            {
                configsJson += line.Substring(CONFIG_PREFIX.Length, line.Length - CONFIG_PREFIX.Length - 1);
                break;
            }
        }

        // Parse config information
        JsonElement configs = JsonElement.Parse(configsJson);

        // Find the primary config, or secondary config, or any config
        JsonElement config;
        try
        {
            config = configs.EnumerateArray().First(config => config.GetProperty("priDefault").GetBoolean());
        }
        catch (InvalidOperationException)
        {
            try
            {
                config = configs.EnumerateArray().FirstOrDefault(config => config.GetProperty("secDefault").GetBoolean());
            }
            catch (InvalidOperationException)
            {
                config = configs[0];
            }
        }

        // Download config and return it
        response = await httpClient.GetAsync(
            "html/json.html?method:downloadConfigFileToPC&name=" + config.GetProperty("name").GetString()
        );

        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not download config, got {(int)response.StatusCode} {response.ReasonPhrase}");

        return new ConfigResult(await response.Content.ReadAsByteArrayAsync(), "pcc");
    }
}