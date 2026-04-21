namespace ConfigDump.Device;

using System.Net;
using Newtonsoft.Json.Linq;
using Renci.SshNet;

public static class Aruba
{
    // Designed based on Aruba 2930M-24G (JL319A)
    public static bool IsAruba(this Device device)
        => device.Model.Contains("aruba", StringComparison.InvariantCultureIgnoreCase) &&
            device.Model.Contains("JL", StringComparison.InvariantCultureIgnoreCase);

    [Obsolete("This method is currently unfinished due to SSH misbehaving.", false)]
    public async static Task<byte[]> DumpSSH(Device device)
    {
        Credential credential = device.GetAdminLogin();

        SshClient client = new(credential.Url.Host, 22, credential.Username, credential.Password);
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
        Credential credential = device.GetAdminLogin();

        HttpClient httpClient = new()
        {
            BaseAddress = credential.GetBaseUri("http")
        };

        // Go to index to get session token cookie
        response = await httpClient.GetAsync("");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not get session token, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Login to the dashboard
        response = await httpClient.PostAsync(
            "html/logincheck",
            new FormUrlEncodedContent([
                KeyValuePair.Create("user", credential.Username),
                KeyValuePair.Create("pass", credential.Password),
                KeyValuePair.Create("Submit", "Login"),
            ])
        );

        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not login, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Go to home to get the request token
        StreamReader reader = new(await httpClient.GetStreamAsync("html/homeStatus.html"));
        string requestToken = "";

        // Parse request token
        const string TOKEN_PREFIX = "'Request-Token': \"";
        while (reader.Peek() >= 0)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null) break;
            if (line.StartsWith(TOKEN_PREFIX))
            {
                requestToken += line[TOKEN_PREFIX.Length..^2];
                break;
            }
        }

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
                configsJson += line[CONFIG_PREFIX.Length..^1];
                break;
            }
        }

        // Parse config information, using Newtonsoft.JSON since this is Relaxed JSON
        JArray configs = JArray.Parse(configsJson);

        // Find the primary config, or secondary config, or any config
        JObject config;
        try
        {
            config = (JObject)configs.First(config => (bool?)((JObject?)config)?["priDefault"] ?? false);
        }
        catch (InvalidOperationException)
        {
            try
            {
                config = (JObject)configs.First(config => (bool?)((JObject?)config)?["priDefault"] ?? false);
            }
            catch (InvalidOperationException)
            {
                config = (JObject)configs[0];
            }
        }

        // Download config and return it
        response = await httpClient.GetAsync(
            $"html/json.html?method:downloadConfigFileToPC&requestToken={requestToken}&name={config["name"]}"
        );

        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not download config, got {(int)response.StatusCode} {response.ReasonPhrase}");

        return new ConfigResult(await response.Content.ReadAsByteArrayAsync(), "pcc");
    }
}