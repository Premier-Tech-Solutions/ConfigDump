namespace ConfigDump.Device;

public class DeviceInfo
{
    public MerakiInfo? Meraki { get; set; }
    public required Dictionary<string, Device> Devices { get; set; }

    public async Task<Dictionary<string, ConfigResult>> DumpConfigs()
    {
        // Create result dictionary with reserved capacity to optimise allocation since we know the key count
        Dictionary<string, ConfigResult> configs = new(Devices.Count);

        await Task.WhenAll(Devices.Select(async device =>
        {
            ConfigResult result;

            await Console.Out.WriteLineAsync($"Dumping configuration for {device.Key}...");
            try
            {
                result = await device.Value.DumpConfig();
                await Console.Out.WriteLineAsync($"{device.Key} configuration dumped!");
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"{device.Key} configuration dump failed! Reason: {ex.Message}");
#if DEBUG
                // If running in debug environment, print stacktrace too.
                await Console.Out.WriteLineAsync(ex.StackTrace);
#endif
                result = new ConfigResult(ex);
            }

            configs.Add(device.Key, result);
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
    public required string Serial { get; set; }
    public required string Model { get; set; }
    public required string PrimaryIp { get; set; }
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