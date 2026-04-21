namespace ConfigDump.Device;

using System.Net;

public static class Ruckus
{
    // Designed based on Ruckus R310
    public static bool IsRuckus(this Device device)
        => device.Model.Contains("Ruckus", StringComparison.InvariantCultureIgnoreCase);

    public static async Task<ConfigResult> DumpHTTPS(Device device)
    {
        Credential credential = device.GetAdminLogin();
        HttpResponseMessage response;

        // Create HTTP Client that ignores invalid SSL certificates, and disables auto-redirects
        // This is needed as the dashboard has an invalid SSL certificate
        HttpClientHandler httpHandler = new()
        {
            AllowAutoRedirect = false,
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        HttpClient httpClient = new(httpHandler)
        {
            BaseAddress = credential.GetBaseUri("https")
        };

        // Check if this is the master (will redirect to master otherwise)
        response = await httpClient.GetAsync("");
        // Ignore OK status codes since they mean we're already on the master
        if (response.StatusCode == HttpStatusCode.OK) { }
        else if (response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location is not null)
            // We've got a redirect to the master, so set that as the new base address
            httpClient = new()
            {
                BaseAddress = new($"https://{response.Headers.Location.Host}/")
            };
        else
            throw new Exception($"Could not get index, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Login to get session token
        response = await httpClient.PostAsync("admin/login.jsp", new FormUrlEncodedContent([
            KeyValuePair.Create("username", credential.Username),
            KeyValuePair.Create("password", credential.Password),
            KeyValuePair.Create("ok", "Log\u00A0in"),
        ]));
        // Redirect means correct credentials in this instance
        if (response.StatusCode == HttpStatusCode.OK)
            throw new Exception("Login credentials invalid.");
        else if (response.StatusCode != HttpStatusCode.Redirect)
            throw new Exception($"Could not get session token, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Download backup
        response = await httpClient.GetAsync("admin/webPage/system/admin/_savebackup.jsp");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not download backup, got {(int)response.StatusCode} {response.ReasonPhrase}");

        return new ConfigResult(await response.Content.ReadAsByteArrayAsync(), "bak");
    }
}