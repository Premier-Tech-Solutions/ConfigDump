namespace ConfigDump.Device;

using System.Net;
using System.Net.Sockets;

public class DeviceInfo
{
    public MerakiInfo? Meraki { get; set; }
    public required List<Device> Devices { get; set; }

    public async Task<Dictionary<string, ConfigResult>> DumpConfigs()
    {
        Dictionary<string, ConfigResult> configs = [];

        await Task.WhenAll(Devices.Select(async device =>
        {
            ConfigResult result;

            await Console.Out.WriteLineAsync($"Dumping configuration for {device.Id}...");
            try
            {
                result = await device.DumpConfig();
                await Console.Out.WriteLineAsync($"{device.Id} configuration dumped!");
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"{device.Id} configuration dump failed! Reason: {ex.Message}");
                result = new ConfigResult(ex);
            }

            configs.Add(device.Id, result);
        }));

        return configs;
    }
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
        // Use lambda expression so Meraki.Instance is only read if needed
        (this.IsMeraki, (device) => Meraki.Instance.DumpCloud(device)),
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
    public bool Error { get; private set; }
    public string Value { get; private set; }
    public string? FileType { get; private set; }

    public ConfigResult(byte[] value, string filetype)
    {
        Error = false;
        Value = Convert.ToBase64String(value);
        FileType = filetype;
    }

    public ConfigResult(Exception ex)
    {
        Error = true;
        Value = ex.Message;
    }
}