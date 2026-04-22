namespace ConfigDump.Device;

using System.Net;
using System.Text;

public static class Ricoh
{
    // Designed based on Ricoh MP 501

    public static bool IsRicoh(this Device device)
        => device.Model.Contains("Ricoh", StringComparison.InvariantCultureIgnoreCase);

    public async static Task<ConfigResult> DumpHTTP(Device device)
    {
        HttpResponseMessage response;
        // Disable auto-redirects so we can use it to check if we're logged in
        HttpClientHandler httpHandler = new()
        {
            AllowAutoRedirect = false,
        };
        HttpClient httpClient = new(httpHandler)
        {
            BaseAddress = new("http://" + device.PrimaryIp + "/web/guest/en/websys/")
        };

        // Attempt to log in, trying again with default credentials if it doesn't work
        bool loggedIn = false;
        if (device.Credentials.Count > 0)
        {
            // Try logging in with credentials provided
            Credential credential = device.GetAdminLogin();
            response = await httpClient.PostAsync("webArch/login.cgi", new FormUrlEncodedContent([
                KeyValuePair.Create("userid", Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.Username))),
                KeyValuePair.Create("password", Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.Password))),
            ]));
            // Redirect means correct credentials in this instance
            if (response.StatusCode == HttpStatusCode.Redirect)
                loggedIn = true;
            else if (response.StatusCode != HttpStatusCode.OK)
                throw new Exception($"Could not login, got {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        if (!loggedIn)
        {
            // Try again with default credentials
            response = await httpClient.PostAsync("webArch/login.cgi", new FormUrlEncodedContent([
                KeyValuePair.Create("userid", "YWRtaW4="), // "admin" in base64
                KeyValuePair.Create("password", ""),
            ]));
            // Redirect means correct credentials in this instance
            if (response.StatusCode == HttpStatusCode.OK)
                throw new Exception($"No valid login credentials.");
            else if (response.StatusCode != HttpStatusCode.Redirect)
                throw new Exception($"Could not login, got {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        // Get wimToken
        string? wimToken = null;
        StreamReader reader = new(await httpClient.GetStreamAsync("prefsImpExp/getPrefsImpExp.cgi"));
        while (reader.Peek() >= 0)
        {
            string? line = await reader.ReadLineAsync();
            if (line is null) break;
            if (!line.Contains("wimToken")) continue;

            // Find the wimToken, it's inside <input type="hidden" name="wimToken" value="12345">
            wimToken = line[(line.IndexOf("value=\"") + 7)..].Split("\"")[0];
        }

        if (wimToken is null)
            throw new Exception("Could not find wimToken.");

        // Setup configuration export
        response = await httpClient.PostAsync("prefsImpExp/setPrefsImpExp.cgi", new FormUrlEncodedContent([
            KeyValuePair.Create("wimToken", wimToken),
            KeyValuePair.Create("mode", "EXPORT"),
            KeyValuePair.Create("accessConf", "3"),
            KeyValuePair.Create("rwInfo", "3"),
            KeyValuePair.Create("expMachUniqInfo", "true"),
            KeyValuePair.Create("impMachUniqInfo", "false"),
            KeyValuePair.Create("impLogDownload", "false"),
            KeyValuePair.Create("passwdKind", ""),
            KeyValuePair.Create("paramControl", ""),
            KeyValuePair.Create("wayTo", ""),
        ]));
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not set-up configuration export, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Start export of configuration
        response = await httpClient.PostAsync("prefsImpExp/doPrefsExport.cgi", new FormUrlEncodedContent([
            KeyValuePair.Create("wimToken", wimToken),
            KeyValuePair.Create("mode", "EXPORT"),
            KeyValuePair.Create("importFile", ""),
        ]));
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not start configuration export, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Wait until export is done, or 120 seconds
        for (int i = 0; i < 120; i++)
        {
            response = await httpClient.PostAsync("prefsImpExp/getPrefsImpExpProgress.cgi", new FormUrlEncodedContent([
                KeyValuePair.Create("wimToken", wimToken),
                KeyValuePair.Create("mode", "EXPORT"),
                KeyValuePair.Create("paramControl", ""),
                KeyValuePair.Create("wayTo", ""),
            ]));
            if (response.StatusCode != HttpStatusCode.OK)
                throw new Exception($"Could not get configuration export status, got {(int)response.StatusCode} {response.ReasonPhrase}");

            string content = await response.Content.ReadAsStringAsync();
            if (!(content.StartsWith('[') && content.EndsWith(']')))
                throw new Exception("Got unexpected error while waiting for configuration export.");
            // Break early if complete
            else if (content == "[100]")
                break;

            // Wait a second before checking again
            await Task.Delay(1000);
        }

        // Download configuration
        response = await httpClient.PostAsync("prefsImpExp/getPrefsExportData.cgi", new FormUrlEncodedContent([
            KeyValuePair.Create("wimToken", wimToken),
            KeyValuePair.Create("mode", "EXPORT"),
            KeyValuePair.Create("impLogDownload", "false"),
            KeyValuePair.Create("resultErrorMessage", ""),
            KeyValuePair.Create("paramControl", ""),
            KeyValuePair.Create("wayTo", ""),
        ]));
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not download configuration, got {(int)response.StatusCode} {response.ReasonPhrase}");

        return new ConfigResult(await response.Content.ReadAsByteArrayAsync(), "csv");
    }
}