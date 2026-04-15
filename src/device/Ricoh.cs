using System.Net;
using System.Net.Http.Json;

namespace ConfigDump.Device;

public static class Ricoh
{
    // Designed based on Ricoh MP 501

    public static bool IsRicoh(this Device device)
        => device.model.Contains("Ricoh", StringComparison.InvariantCultureIgnoreCase);

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
            BaseAddress = new("http://" + device.GetLocalIp() + "/web/guest/en/websys/")
        };


        // Attempt to log in, trying again with default credentials if it doesn't work
        bool loggedIn = false;
        if (device.credentials.Count > 0)
        {
            // Try logging in with credentials provided
            Credential credential = device.GetAdminLogin();
            response = await httpClient.PostAsJsonAsync("webArch/login.cgi", new
            {
                userid = credential.username,
                credential.password,
            });
            // Redirect means correct credentials in this instance
            if (response.StatusCode == HttpStatusCode.Redirect)
                loggedIn = true;
            else if (response.StatusCode != HttpStatusCode.OK)
                throw new Exception($"Could not login, got {(int)response.StatusCode} {response.ReasonPhrase}");
        }

        if (!loggedIn)
        {
            // Try again with default credentials
            response = await httpClient.PostAsJsonAsync("webArch/login.cgi", new
            {
                userid = "admin",
                password = "",
            });
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
        response = await httpClient.PostAsJsonAsync("prefsImpExp/setPrefsImpExp.cgi", new
        {
            mode = "EXPORT",
            accessConf = 3,
            rwInfo = 3,
            expMachUniqInfo = true,
            impMachUniqInfo = false,
            impLogDownload = false,
            passwdKind = "",
            paramControl = "",
            wayTo = "",
            wimToken,
        });
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not set-up configuration export, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Start export of configuration
        response = await httpClient.PostAsJsonAsync("prefsImpExp/doPrefsExport.cgi", new
        {
            mode = "EXPORT",
            importFile = "",
            wimToken,
        });
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not start configuration export, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Wait until export is done
        do
        {
            response = await httpClient.PostAsJsonAsync("prefsImpExp/getPrefsImpExpProgress.cgi", new
            {
                mode = "EXPORT",
                paramControl = "",
                wayTo = "",
                wimToken,
            });
            if (response.StatusCode != HttpStatusCode.OK)
                throw new Exception($"Could not get configuration export status, got {(int)response.StatusCode} {response.ReasonPhrase}");

            // Wait a second before checking again
            await Task.Delay(1000);
        } while (await response.Content.ReadAsStringAsync() != "[100]");

        // Download configuration
        response = await httpClient.PostAsJsonAsync("prefsImpExp/getPrefsExportData.cgi", new
        {
            mode = "EXPORT",
            impLogDownload = false,
            resultErrorMessage = "",
            paramControl = "",
            wayTo = "",
            wimToken,
        });
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not download configuration, got {(int)response.StatusCode} {response.ReasonPhrase}");

        return new ConfigResult(await response.Content.ReadAsByteArrayAsync(), "csv");
    }
}