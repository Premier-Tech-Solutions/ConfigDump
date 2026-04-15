using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ConfigDump.Device;

public static class HPEUPS
{
    // Designed based on HPE Single Phase 1Gb UPS Network Management Card
    public static bool IsHPEUPS(this Device device)
        => device.Model.Contains("HPE", StringComparison.InvariantCultureIgnoreCase) &&
            device.Model.Contains("UPS", StringComparison.InvariantCultureIgnoreCase);

    public async static Task<ConfigResult> DumpHTTPS(Device device)
    {
        CookieContainer cookies = new();
        Credential credential = device.GetAdminLogin();
        HttpResponseMessage response;

        // Create HTTP Client that ignores invalid SSL certificates
        // This is needed as the dashboard has an invalid SSL certificate
        HttpClientHandler httpHandler = new()
        {
            CookieContainer = cookies,
            ClientCertificateOptions = ClientCertificateOption.Manual,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        HttpClient httpClient = new(httpHandler)
        {
            BaseAddress = credential.GetBaseUri("https")
        };

        // Login to get access token
        response = await httpClient.PostAsJsonAsync("rest/mbdetnrs/1.0/oauth2/token", new
        {
            grant_type = "password",
            scope = "GUIAccess",
            credential.Username,
            credential.Password,
        });
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not get access token, got {(int)response.StatusCode} {response.ReasonPhrase}");

        // Parse JSON response to add cookies
        Stream bodyStream = await response.Content.ReadAsStreamAsync();
        JsonDocument jsonBody = await JsonDocument.ParseAsync(bodyStream);
        string? accessToken = jsonBody.RootElement.GetProperty("access_token").GetString();

        cookies.Add(new Cookie("eaton_user", credential.Username));
        cookies.Add(new Cookie("eaton_token", accessToken));
        httpClient.DefaultRequestHeaders.Authorization = new("Bearer", accessToken);

        // Download settings
        response = await httpClient.PostAsJsonAsync("rest/mbdetnrs/1.0/managers/1/actions/saveSettings", new
        {
            exclude = Array.Empty<string>(),
            passphrase = credential.Password,
        });
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not download settings, got {(int)response.StatusCode} {response.ReasonPhrase}");

        return new ConfigResult(await response.Content.ReadAsByteArrayAsync(), "json");
    }
}