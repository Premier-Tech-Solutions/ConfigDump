namespace ConfigDump.Device;

using System.Net;

public static class Ruckus
{
    // Designed based on Ruckus R310
    public static bool IsRuckus(this Device device)
        => device.model.Contains("Ruckus", StringComparison.InvariantCultureIgnoreCase);

    public static async Task<ConfigResult> DumpHTTPS(Device device)
    {
        Credential credential = device.credentials[0];
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
            BaseAddress = credential.url
        };

        // Login to get session token
        response = await httpClient.PostAsync("admin/login.jsp", new FormUrlEncodedContent([
            KeyValuePair.Create("username", credential.username),
            KeyValuePair.Create("password", credential.password),
            KeyValuePair.Create("ok", "Log\u00A0in"),
        ]));
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not get session token, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Download backup
        response = await httpClient.GetAsync("admin/webPage/system/admin/_savebackup.jsp");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not download backup, got {(int)response.StatusCode} {response.ReasonPhrase}");

        return new ConfigResult(await response.Content.ReadAsByteArrayAsync(), "bak");
    }
}