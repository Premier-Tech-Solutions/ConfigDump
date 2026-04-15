namespace ConfigDump.Device;

using System.Net;
using System.Net.Sockets;

public class DeviceInfo
{
    public MerakiInfo? meraki;
    public required List<Device> devices;
}

public class Credential
{
    public required Uri url;
    public required string username;
    public required string password;

    public Uri GetBaseUri(string scheme) => new($"{scheme}://{url.Host}/");
}

public class Device
{
    public required string id;
    public required string serial;
    public required string model;
    public required List<IPAddress> ips;
    public required List<Credential> credentials;

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

    public IPAddress GetLocalIp() => ips.First(ip =>
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
            return credentials.First(cred => cred.username.Contains("admin", StringComparison.InvariantCultureIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return credentials[0];
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