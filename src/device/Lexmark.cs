namespace ConfigDump.Device;

using System.Net;

public static class Lexmark
{
    // Designed based on Lexmark E120n
    public static bool IsLexmark(this Device device) => device.model.Contains("Lexmark", StringComparison.InvariantCultureIgnoreCase);

    public static async Task<ConfigResult> DumpHTTP(Device device)
    {
        // Download configuration HTML page and save it
        HttpResponseMessage response = await new HttpClient().GetAsync("http://" + device.GetLocalIp() + "/port_0/printer/menuspg");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new Exception($"Could not get configuration, got {(int)response.StatusCode} {response.ReasonPhrase}");

        return new ConfigResult(await response.Content.ReadAsByteArrayAsync(), "html");
    }
}