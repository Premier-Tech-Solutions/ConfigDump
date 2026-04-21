namespace ConfigDump;

using System.Diagnostics;
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

    private static readonly HttpClient httpClient = new();
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static (ExitCode, DeviceInfo?) GetDevicesFromUri(Uri deviceUri)
    {
        try
        {
            // Use .GetAwaiter().GetResult() to run operation synchronously as there's no non-async version
            return (ExitCode.SUCCESS, httpClient.GetFromJsonAsync<DeviceInfo>(deviceUri, jsonOptions).GetAwaiter().GetResult());
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

    private static (ExitCode, DeviceInfo?) GetDevicesFromStdin()
    {
        // Keep on reading from STDIN until an empty line
        string input = "";
        string? line;
        do
        {
            line = Console.ReadLine();
            input += line;
        } while (!string.IsNullOrWhiteSpace(line));

        // Deserialize input
        try
        {
            return (ExitCode.SUCCESS, JsonSerializer.Deserialize<DeviceInfo>(input, jsonOptions));
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine("Device JSON is invalid.");
            Console.Error.WriteLine(ex);
            return (ExitCode.INVALID_DATA, null);
        }
    }

    private static ExitCode PostConfigs(Dictionary<string, ConfigResult> configs, Uri postUri)
    {
        HttpResponseMessage response;
        try
        {
            response = httpClient.PostAsJsonAsync(postUri, configs, jsonOptions).GetAwaiter().GetResult();
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

    public static int Main(string[] args)
    {
        // Check for testing mode or invalid command-line
        bool testingMode = false;
        if (args.Length == 1 && args[0] == "--test")
        {
            // Enable testing mode (read from STDIN, write to file)
            testingMode = true;
        }
        else if (args.Length != 2)
        {
            Console.WriteLine($@"ConfigDump: bulk dump config files from various devices
Release {AppVersion}
Usage: configdump <DEVICES> <CONFIGS>

DEVICES is the web URL to get the device information from.
CONFIGS is the web URL to post the configs to.");
            return (int)ExitCode.INVALID_COMMAND_LINE;
        }

        // Parse command-line arguments if in normal mode
        Uri? deviceUri = null;
        Uri? postUri = null;
        if (!testingMode)
        {
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
        }

        // Get device information, either from a GET request or from STDIN if in testing mode
        ExitCode exitCode;
        DeviceInfo? deviceInfo;
        if (testingMode)
        {
            (exitCode, deviceInfo) = GetDevicesFromStdin();
        }
        else
        {
            Debug.Assert(deviceUri is not null, "deviceUri is already initialised by this point in normal mode.");
            Console.WriteLine("Downloading device information...");
            (exitCode, deviceInfo) = GetDevicesFromUri(deviceUri);
        }

        if (exitCode != ExitCode.SUCCESS)
        {
            return (int)exitCode;
        }
        else if (deviceInfo is null)
        {
            Console.Error.WriteLine("Device JSON was null.");
            return (int)ExitCode.INVALID_DATA;
        }

        // Initialise the Meraki Cloud helper if we have an API key
        if (deviceInfo.Meraki is not null)
            Meraki.Initialise(deviceInfo.Meraki).RunSynchronously();

        // Dump all the device configs, running the operation synchronously
        Dictionary<string, ConfigResult> configs = deviceInfo.DumpConfigs().GetAwaiter().GetResult();

        // Return the device configs, either via a POST request or as a file if in testing mode
        if (testingMode)
        {
            using FileStream stream = File.Create("configs.json");
            JsonSerializer.Serialize(stream, configs, jsonOptions);
            return (int)ExitCode.SUCCESS;
        }
        else
        {
            Debug.Assert(postUri is not null, "postUri is already initialised by this point in normal mode.");
            Console.WriteLine("Uploading device configs...");
            return (int)PostConfigs(configs, postUri);
        }
    }
}