namespace ConfigDump.Device;

using System.Net;
using System.Net.Sockets;

public class DeviceInfo
{
    public MerakiInfo? Meraki { get; set; }
    public required List<Device> Devices { get; set; }
}

public class Credential
{
    public required Uri Url { get; set; }
    public required string Username { get; set; }
    public required string Password { get; set; }

    public Uri GetBaseUri(string scheme) => new($"{scheme}://{Url.Host}/");
}

public class Device
{
    public required string Id { get; set; }
    public required string Serial { get; set; }
    public required string Model { get; set; }
    public required List<string> IPs { get; set; }
    public required List<Credential> Credentials { get; set; }

    private (Func<bool>, Func<Device, Task<ConfigResult>>)[] ConfigDumpers => [
        (this.IsMeraki, Meraki.Instance.DumpCloud),
        (this.IsAruba, Aruba.DumpHTTP),
        (this.IsRuckus, Ruckus.DumpHTTPS),
        (this.IsLexmark, Lexmark.DumpHTTP),
        (this.IsHPEUPS, HPEUPS.DumpHTTPS),
        (this.IsHPESwitch, HPESwitch.DumpHTTPS),
        (this.IsArubaION, ArubaION.DumpHTTPS),
        (this.IsRicoh, Ricoh.DumpHTTP),
    ];

    public async Task<ConfigResult> DumpConfig()
    {
        foreach ((Func<bool> predicate, Func<Device, Task<ConfigResult>> dumper) in ConfigDumpers)
        {
            if (predicate.Invoke())
                return await dumper.Invoke(this);
        }

        return new ConfigResult(new Exception("No config dumping method for device."));
    }

    public IPAddress GetLocalIp() => IPs.Select(IPAddress.Parse).First(ip =>
    {
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal;

        byte[] bytes = ip.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            172 => bytes[1] >= 16 && bytes[1] <= 31,
            192 => bytes[1] == 168,
            _ => false,
        };
    });

    public Credential GetAdminLogin()
    {
        try
        {
            return Credentials.First(cred => cred.Username.Contains("admin", StringComparison.InvariantCultureIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return Credentials[0];
        }
    }
}

public class ConfigResult
{
    public readonly bool error;
    public readonly string value;
    public readonly string? filetype;

    public ConfigResult(byte[] value, string filetype)
    {
        error = false;
        this.value = Convert.ToBase64String(value);
        this.filetype = filetype;
    }

    public ConfigResult(Exception ex)
    {
        error = true;
        value = ex.Message;
    }
}

// public static class Utilities
// {
//     public static Uri WithScheme(string uri, string scheme)
//     {
//         return new UriBuilder(uri)
//         {
//             Scheme = scheme,
//             Port = -1,
//         }.Uri;
//     }
// }