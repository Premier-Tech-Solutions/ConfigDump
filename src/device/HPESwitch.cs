namespace ConfigDump.Device;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

public static class HPESwitch
{
    // Designed based on HP hpe1920S_24G

    public static bool IsHPESwitch(this Device device)
        => device.Model.Contains("HPE", StringComparison.InvariantCultureIgnoreCase) && (
            device.Model.Contains("1420") ||
            device.Model.Contains("1820") ||
            device.Model.Contains("1920S") ||
            device.Model.Contains("1950")
        );

    public async static Task<ConfigResult> DumpHTTPS(Device device)
    {
        Credential credential = device.GetAdminLogin();
        HttpResponseMessage response;

        // Create HTTP Client that ignores invalid SSL certificates
        // This is needed as the dashboard has an invalid SSL certificate
        HttpClientHandler httpHandler = new()
        {
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        HttpClient httpClient = new(httpHandler)
        {
            BaseAddress = credential.GetBaseUri("https")
        };

        // Login to get session token
        response = await httpClient.PostAsJsonAsync("htdocs/login/login.lua", new
        {
            credential.Username,
            credential.Password
        });
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not login, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Get configuration file link
        TimeSpan epochTime = DateTime.UtcNow - new DateTime(1970, 1, 1);
        response = await httpClient.PostAsync("htdocs/lua/ajax/file_upload_ajax.lua?protocol=6", new FormUrlEncodedContent([
            KeyValuePair.Create("file_type_sel[]", "config"),
            KeyValuePair.Create("http_token", ((ulong)epochTime.TotalMilliseconds).ToString()),
        ]));
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could get configuration file, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Parse JSON to find query parameters
        Stream bodyStream = await response.Content.ReadAsStreamAsync();
        JsonDocument jsonBody = await JsonDocument.ParseAsync(bodyStream);
        string? queryParams = jsonBody.RootElement.GetProperty("queryParams").GetString();

        return new ConfigResult(
            await httpClient.GetByteArrayAsync("htdocs/pages/base/file_http_download.lsp" + queryParams),
            "txt"
        );
    }
}