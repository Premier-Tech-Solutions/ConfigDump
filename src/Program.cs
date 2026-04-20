namespace ConfigDump;

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ConfigDump.Device;

class Program(Uri deviceUri, Uri postUri)
{
    public static Version AppVersion
    {
        get => Assembly.GetEntryAssembly()?.GetName()?.Version ?? new();
    }

    private readonly HttpClient httpClient = new();
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public readonly Uri DeviceUri = deviceUri;
    public readonly Uri PostUri = postUri;

    public async Task<(ExitCode, DeviceInfo?)> GetDevices()
    {
        try
        {
            Console.WriteLine("Downloading device information.");
            return (ExitCode.SUCCESS, await httpClient.GetFromJsonAsync<DeviceInfo>(DeviceUri, jsonOptions));
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Timed out while receiving device information from devices URL.");
            return (ExitCode.TIMEOUT, null);
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine("Could not get device information from devices URL.");
            return (ExitCode.BAD_NET_RESP, null);
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("Device JSON is invalid.");
            return (ExitCode.INVALID_DATA, null);
        }
    }

    public async Task<ExitCode> SendConfigs(Dictionary<string, ConfigResult> configs)
    {
        HttpResponseMessage response;
        try
        {
            Console.WriteLine("Uploading device configs.");
            response = await httpClient.PostAsJsonAsync(PostUri, configs, jsonOptions);
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine("Could not send configs to response URL.");
            return ExitCode.BAD_NET_RESP;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Timed out while sending configs to response URL.");
            return ExitCode.TIMEOUT;
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Console.Error.WriteLine($"Got {(int)response.StatusCode} {response.ReasonPhrase} while sending configs to POST URL.");
            return ExitCode.BAD_NET_RESP;
        }

        return ExitCode.SUCCESS;
    }

    public static async Task<int> Main(string[] args)
    {
        // Process command-line arguments
        if (args.Length != 2)
        {
            Console.WriteLine($@"ConfigDump: bulk dump config files from various devices
Release {AppVersion}
Usage: configdump <DEVICES> <CONFIGS>

DEVICES is the web URL to get the device information from.
CONFIGS is the web URL to post the configs to.
");
            return (int)ExitCode.INVALID_COMMAND_LINE;
        }

        Uri deviceUri;
        Uri postUri;

        try
        {
            deviceUri = new(args[0]);
            postUri = new(args[1]);
        }
        catch (Exception ex) when (ex is UriFormatException || ex is InvalidOperationException)
        {
            Console.Error.WriteLine("Invalid URI argument.");
            return (int)ExitCode.INVALID_COMMAND_LINE;
        }

        Program instance = new(deviceUri, postUri);

        (ExitCode exitCode, DeviceInfo? deviceInfo) = await instance.GetDevices();
        if (exitCode != ExitCode.SUCCESS)
        {
            return (int)exitCode;
        }
        else if (deviceInfo is null)
        {
            Console.Error.WriteLine("No Device JSON returned.");
            return (int)ExitCode.INVALID_DATA;
        }

        // Initialise the Meraki Cloud helper if we have an API key
        if (deviceInfo.Meraki is not null)
            await Meraki.Initialise(deviceInfo.Meraki);

        Dictionary<string, ConfigResult> configs = await deviceInfo.DumpConfigs();
        return (int)await instance.SendConfigs(configs);
    }
}