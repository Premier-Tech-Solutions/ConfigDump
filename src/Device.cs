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
}

public class Device
{
    public required string id;
    public required string serial;
    public required string model;
    public required List<IPAddress> ips;
    public required List<Credential> credentials;

    public async Task<ConfigResult> DumpConfig()
    {
        if (this.IsMeraki())
        {
            return await Meraki.Instance.DumpCloud(this);
        }
        else if (this.IsAruba())
        {
            return await Aruba.DumpHTTP(this);
        }
        else if (this.IsRuckus())
        {
            return await Ruckus.DumpHTTPS(this);
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