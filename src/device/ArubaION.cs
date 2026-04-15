using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ConfigDump.Device;

public static class ArubaION
{
    public static bool IsArubaION(this Device device)
        => device.model.Contains("aruba", StringComparison.InvariantCultureIgnoreCase) && (
            device.model.Contains("ION", StringComparison.InvariantCultureIgnoreCase) ||
            device.model.Contains("Instant On", StringComparison.InvariantCultureIgnoreCase)
        );

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

        string queryParams =
            $"user={Uri.EscapeDataString(credential.username)}&password={Uri.EscapeDataString(credential.password)}&ssd=true&";

        // Get encryption setting
        Stream bodyStream = await httpClient.GetStreamAsync("device/wcd?{EncryptionSetting}");
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
        response = await httpClient.GetAsync("csa73a445d/hpe/config/system.xml?action=login&" + queryParams);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not login, got {(int)response.StatusCode} {response.ReasonPhrase}");

        cookies.Add(new Cookie("userName", credential.username));
        cookies.Add(new Cookie("sessionID", response.Headers.GetValues("sessionid").First()));

        // Download and return configuration
        return new ConfigResult(
            await httpClient.GetByteArrayAsync("csa73a445d/hpe/http_download?action=3&ssd=4"),
            "txt"
        );
    }
}