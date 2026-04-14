namespace ConfigDump;

using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using ConfigDump.Device;

class Program
{
    public static Version AppVersion
    {
        get => Assembly.GetEntryAssembly()?.GetName()?.Version ?? new();
    }

    public static async Task<int> Main(string[] args)
    {
        JsonSerializerOptions jsonOptions = JsonSerializerOptions.Web;
        HttpClient httpClient = new();
        DeviceInfo? deviceInfo = null;
        Uri postUrl;

        // Fetch the devices to dump the configs for
        try
        {
            postUrl = new Uri(args[1]);
            deviceInfo = await httpClient.GetFromJsonAsync<DeviceInfo>(args[0], jsonOptions)
                ?? throw new NullReferenceException();
        }
        catch (IndexOutOfRangeException)
        {
            Console.WriteLine($@"ConfigDump: bulk dump config files from various devices
        Release {AppVersion}
        Usage: configdump <DEVICES> <CONFIGS>

        DEVICES is the web URL to get the device information from.
        CONFIGS is the web URL to post the configs to.
        ");
            return ExitCode.INVALID_COMMAND_LINE;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Timed out while receiving device information from devices URL.");
            return ExitCode.TIMEOUT;
        }
        catch (Exception ex) when (ex is UriFormatException || ex is InvalidOperationException)
        {
            Console.Error.WriteLine("Invalid devices URL.");
            return ExitCode.INVALID_DATA;
        }
        catch (HttpRequestException)
        {
            Console.Error.WriteLine("Could not get device information from devices URL.");
            return ExitCode.BAD_NET_RESP;
        }
        catch (Exception ex) when (ex is JsonException || ex is NullReferenceException)
        {
            Console.Error.WriteLine("Device JSON is invalid.");
            return ExitCode.INVALID_DATA;
        }

        // Initialise the Meraki Cloud helper if we have an API key
        if (deviceInfo.meraki is not null)
            await Meraki.Initialise(deviceInfo.meraki);

        // Dump all the configs asynchronously
        Dictionary<string, ConfigResult> configs = [];
        await Task.WhenAll(deviceInfo.devices.Select(async device =>
        {
            ConfigResult result;

            try
            {
                result = await device.DumpConfig();
            }
            catch (Exception ex)
            {
                result = new ConfigResult(ex);
            }

            configs.Add(device.id, result);
        }));

        // Send the configs back
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(postUrl, configs, jsonOptions);
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
}