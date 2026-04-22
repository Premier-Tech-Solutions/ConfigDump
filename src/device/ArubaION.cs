namespace ConfigDump.Device;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

public static class ArubaION
{
    // Designed based on HP ARUBA ION 1930 24G 4SFP+ POE+ 195W SWITCH

    public static bool IsArubaION(this Device device)
        => device.Model.Contains("aruba", StringComparison.InvariantCultureIgnoreCase) && (
            device.Model.Contains("ION", StringComparison.InvariantCultureIgnoreCase) ||
            device.Model.Contains("Instant On", StringComparison.InvariantCultureIgnoreCase)
        );

    public async static Task<ConfigResult> DumpHTTPS(Device device)
    {
        CookieContainer cookies = new();
        Credential credential = device.GetAdminLogin();
        Uri baseAddress = credential.GetBaseUri("https");
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
            BaseAddress = new(baseAddress + "csa73a445d/hpe/"),
        };

        string queryParams =
            $"user={Uri.EscapeDataString(credential.Username)}&password={Uri.EscapeDataString(credential.Password)}&ssd=true&";

        // Get encryption setting
        Uri encryptionUri = new(
            baseAddress + "device/wcd?{EncryptionSetting}",
            new UriCreationOptions
            {
                // This is needed since the query string contains curly braces the HttpClient wants to escape;
                // however, the webserver expects them to not be escaped.
                DangerousDisablePathAndQueryCanonicalization = true,
            }
        );

        Stream bodyStream = await httpClient.GetStreamAsync(encryptionUri);
        XDocument bodyXml = await XDocument.LoadAsync(bodyStream, LoadOptions.None, default);
        XElement? encryptionSetting = bodyXml.Root?.Element("DeviceConfiguration")?.Element("EncryptionSetting");
        if (encryptionSetting is not null && encryptionSetting.Element("passwEncryptEnable")?.Value == "1")
        {
            // Encryption is enabled, so encrypt the login credentials
            string publicKey = encryptionSetting.Element("rsaPublicKey")?.Value
                ?? throw new Exception("Could not find credential encryption key.");

            RSA rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);
            byte[] cred = rsa.Encrypt(Encoding.UTF8.GetBytes(queryParams), RSAEncryptionPadding.Pkcs1);
            queryParams = "cred=" + Convert.ToHexStringLower(cred);
        }

        // Login to get session token
        response = await httpClient.GetAsync("config/system.xml?action=login&" + queryParams);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not login, got {(int)response.StatusCode} {response.ReasonPhrase}");

        string sessionID = response.Headers.GetValues("sessionID").First();
        cookies.Add(baseAddress, new Cookie("userName", credential.Username));
        cookies.Add(baseAddress, new Cookie("sessionID", sessionID[..sessionID.IndexOf(';')]));

        // Download and return configuration
        return new ConfigResult(
            await httpClient.GetByteArrayAsync("http_download?action=3&ssd=4"),
            "txt"
        );
    }
}