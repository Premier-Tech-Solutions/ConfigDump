namespace ConfigDump.Device;

using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.OpenApi;

public static class MerakiExtensions
{
    public static bool IsMeraki(this Device device)
        => device.Model.Contains("Meraki", StringComparison.InvariantCultureIgnoreCase);
}

public class MerakiInfo
{
    public required string api_key;
    public required string organization_id;
}

public class Meraki
{
    // Designed for Meraki Cloud
    private const string MERAKI_SPEC = "https://api.meraki.com/api/v1/openapiSpec";
    private const string MERAKI_REPO = "https://api.github.com/repos/meraki/automation-scripts/commits?per_page=1";

    private class GetOperation
    {
        public required string Logic { get; set; }
        public required string OperationId { get; set; }
        public required string Tags { get; set; }
        public required string Description { get; set; }
        public required string Parameters { get; set; }
    }

    private readonly string OrganizationId;

    private readonly HttpClient ApiClient;

    private readonly OpenApiDocument MerakiSpec;
    private readonly List<GetOperation> GetOperations;
    private readonly List<JsonElement> DefaultConfigs;

    private static Meraki? _Instance = null;

    public static Meraki Instance
    {
        get => _Instance ?? throw new Exception("Meraki Cloud handler not initialised");
    }

    private Meraki(MerakiInfo info, OpenApiDocument merakiSpec, List<GetOperation> getOperations, List<JsonElement> defaultConfigs)
    {
        OrganizationId = info.organization_id;

        HttpClientHandler handler = new()
        {
            // This slows down the API requests to one at a time.
            // This technically makes the program slower, but is less likely to run into rate limits.
            MaxConnectionsPerServer = 1
        };

        ApiClient = new(handler)
        {
            BaseAddress = new("https://api.meraki.com/api/v1/")
        };

        ApiClient.DefaultRequestHeaders.Authorization = new("Bearer", info.api_key);

        MerakiSpec = merakiSpec;
        GetOperations = getOperations;
        DefaultConfigs = defaultConfigs;
        _Instance = this;
    }

    // Set up the Meraki Cloud helper
    public static async Task<Meraki> Initialise(MerakiInfo info)
    {
        Console.WriteLine("Initialising Meraki Cloud helper.");

        HttpClient httpClient = new();
        Stream bodyStream;
        JsonDocument jsonBody;

        // GitHub API needs a user agent
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new("ConfigDump", Program.AppVersion.ToString())
        );

        List<GetOperation>? getOperations = null;
        List<JsonElement> defaultConfigs = [];

        // Get Meraki Cloud OpenAPI specification
        bodyStream = await httpClient.GetStreamAsync(MERAKI_SPEC);
        (OpenApiDocument? merakiSpec, _) = await OpenApiDocument.LoadAsync(bodyStream);
        if (merakiSpec is null)
            throw new Exception("Could not get Meraki Cloud OpenAPI specification.");

        // GitHub defines an unauthorised rate-limit of 60 requests per hour
        // This usually isn't reached in regular program execution, but can be reached if the program is repeatedly executed.
        // Therefore, the program checks for a github token to authorise with if it's available.

        string? githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (githubToken is not null)
            httpClient.DefaultRequestHeaders.Authorization = new("Bearer", githubToken);

        // Get Git file tree for the Meraki Cloud backup script
        httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        bodyStream = await httpClient.GetStreamAsync(MERAKI_REPO);
        jsonBody = await JsonDocument.ParseAsync(bodyStream);

        string tree = jsonBody.RootElement[0].GetProperty("commit").GetProperty("tree").GetProperty("url").GetString()
            ?? throw new Exception("Could not parse Meraki Cloud automation scripts repository.");
        bodyStream = await httpClient.GetStreamAsync(tree + "?recursive=1");
        jsonBody = await JsonDocument.ParseAsync(bodyStream);

        // Loop through files, grabbing their raw blob data if needed
        httpClient.DefaultRequestHeaders.Remove("Accept");
        httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.raw+json");
        foreach (JsonElement file in jsonBody.RootElement.GetProperty("tree").EnumerateArray())
        {
            string? path = file.GetProperty("path").GetString();
            string? url = file.GetProperty("url").GetString();
            if (path is null || url is null)
                throw new Exception("Could not parse Meraki Cloud automation scripts repository.");

            if (path == "backup_configs/backup_GET_operations.csv")
            {
                // Download the GetOperations to send
                CsvConfiguration csvConfig = new(CultureInfo.InvariantCulture)
                {
                    // Needed since the field name case doesn't match
                    PrepareHeaderForMatch = args => args.Header.ToLowerInvariant(),
                };

                bodyStream = await httpClient.GetStreamAsync(url);
                using CsvReader csv = new(new StreamReader(bodyStream), csvConfig);
                getOperations = [.. csv.GetRecords<GetOperation>()];
            }
            else if (path.StartsWith("backup_configs/defaults/") && path.EndsWith(".json"))
            {
                // Download default configurations to check against
                bodyStream = await httpClient.GetStreamAsync(url);
                jsonBody = await JsonDocument.ParseAsync(bodyStream);
                defaultConfigs.Add(jsonBody.RootElement);
            }
        }

        if (getOperations is null)
            throw new Exception("Could not find backup GET operations.");

        Console.WriteLine("Meraki Cloud handler initialised.");
        return new Meraki(info, merakiSpec, getOperations, defaultConfigs);
    }

    // Backup device configuration using Meraki Cloud
    public async Task<ConfigResult> DumpCloud(Device device)
    {
        // Save files into a zip file stored in memory
        MemoryStream zipStream = new();
        ZipArchive zip = new(zipStream, ZipArchiveMode.Update);

        string deviceFamily = GetDeviceType(device) ?? "";

        // Backup device configuration
        await PerformOperations(
            zip,
            $"devices/{device.Model} - {device.Serial}/",
            // Adapted roughly from https://github.com/meraki/automation-scripts/blob/master/backup_configs/backup_configs.py#L315
            (operation, spec) =>
                operation.OperationId.StartsWith("getDevice") &&
                operation.Logic != "skipped" &&
                operation.Logic != "script" && (
                    HasScope(spec, deviceFamily) ||
                    HasScope(spec, "devices") && (
                        deviceFamily == "wireless" ||
                        deviceFamily == "switch" ||
                        deviceFamily == "appliance"
                    )
                ),
            new Dictionary<string, string>()
            {
                {"device", device.Serial}
            }
        );

        // Get device information to get network ID
        Stream responseStream = await ApiClient.GetStreamAsync("devices/" + device.Serial);
        JsonDocument responseBody = await JsonDocument.ParseAsync(responseStream);
        string networkId = responseBody.RootElement.GetProperty("networkId").GetString()
            ?? throw new Exception("Could not find device in Meraki Cloud.");

        // Get network information
        responseStream = await ApiClient.GetStreamAsync("networks/" + networkId);
        responseBody = await JsonDocument.ParseAsync(responseStream);

        string networkName = responseBody.RootElement.GetProperty("name").GetString()
            ?? throw new Exception("Could not find device network in Meraki Cloud.");
        List<string> productTypes = [.. responseBody.RootElement
            .GetProperty("productTypes")
            .EnumerateArray()
            .Select(x => x.GetString())
            .OfType<string>()
        ];

        string networkFilePath = $"networks/{networkName} - {networkId}/";

        // Backup network configuration
        await PerformOperations(
            zip,
            networkFilePath,
            // Adapted roughly from https://github.com/meraki/automation-scripts/blob/master/backup_configs/backup_configs.py#L353
            (operation, spec) =>
                operation.OperationId.StartsWith("getNetwork") &&
                operation.Logic != "skipped" &&
                operation.Logic != "script" &&
                operation.Logic != "ssids" && (
                    productTypes.Any(x => HasScope(spec, x)) ||
                    HasScope(spec, "networks") &&
                    productTypes.Any(x => operation.Logic.Contains(x))
                ),
            new Dictionary<string, string>()
            {
                {"networkId", networkId}
            }
        );

        // Backup configuration for VLANs, VLAN ports, and single LAN networks
        // Adapted roughly from https://github.com/meraki/automation-scripts/blob/master/backup_configs/backup_configs.py#L398
        if (productTypes.Contains("appliance"))
        {
            // Check if VLANs enabled, as presence of the VLAN settings file indicates non-default configuration
            if (zip.GetEntry(networkFilePath + "appliance_vlans_settings.json") is null)
            {
                // VLANs are disabled, save single LAN configuration
                await PerformOperation(
                    zip,
                    networkFilePath + "appliance_single_lan",
                    $"networks/{networkId}/appliance/singleLan"
                );
            }
            else
            {
                // VLANs are enabled, save VLAN & VLAN port configuration
                await PerformOperation(
                    zip,
                    networkFilePath + "appliance_vlans",
                    $"networks/{networkId}/appliance/vlans"
                );

                await PerformOperation(
                    zip,
                    networkFilePath + "appliance_ports",
                    $"networks/{networkId}/appliance/ports"
                );
            }
        }

        if (productTypes.Contains("wireless"))
        {
            // Backup configuration for SSID-specific settings
            await BackupSSIDs(zip, networkFilePath, networkId);

            // Backup configuration for Bluetooth device settings if using unique BLE advertising
            ZipArchiveEntry? entry = zip.GetEntry(networkFilePath + "wireless_bluetooth_settings.json");
            if (entry is not null)
            {
                JsonDocument bluetoothSettings = await JsonDocument.ParseAsync(await entry.OpenAsync());
                if (
                    deviceFamily == "wireless" &&
                    bluetoothSettings.RootElement.GetProperty("advertisingEnabled").GetBoolean() &&
                    bluetoothSettings.RootElement.GetProperty("majorMinorAssignmentMode").GetString() == "Unique"
                )
                {
                    await PerformOperation(
                        zip,
                        networkFilePath + $"wireless_bluetooth_settings_{device.Serial}",
                        $"devices/{device.Serial}/wireless/bluetooth/settings"
                    );
                }
            }
        }

        // Close Zip file and return it
        await zip.DisposeAsync();
        return new ConfigResult(zipStream.ToArray(), "zip");
    }

    // Backup configuration for SSID-specific settings
    private async Task BackupSSIDs(ZipArchive zip, string filePath, string networkId)
    {
        // Adapted roughly from https://github.com/meraki/automation-scripts/blob/master/backup_configs/backup_configs.py#L501
        ZipArchiveEntry? entry = zip.GetEntry(filePath + "wireless_ssids.json");
        if (entry is null) return;

        // Parse SSIDs and loop through them, attempting to backup each of them
        JsonDocument SSIDs = await JsonDocument.ParseAsync(await entry.OpenAsync());
        foreach (JsonElement SSID in SSIDs.RootElement.EnumerateArray())
        {
            int number = SSID.GetProperty("number").GetInt32();
            string? name = SSID.GetProperty("name").GetString();
            if (name is null) continue;

            // Loop through all operations, continuing if they're an ssid operation
            foreach (GetOperation operation in GetOperations)
            {
                // Find the operation in the OpenAPI specification, used to find the endpoint path
                if (operation.Logic != "ssids") continue;
                string operationPath;
                try
                {
                    (operationPath, _) = FindOperationInSpec(operation);
                }
                catch (KeyNotFoundException)
                {
                    continue;
                }

                await PerformOperation(
                    zip,
                    filePath + GenerateFileName(operation.OperationId) + $"_ssid_{number}",
                    // Skip leading slash, then replace variables
                    operationPath[1..].Replace("{networkId}", networkId).Replace("{number}", number.ToString())
                );
            }
        }
    }

    // Backup a set of operations that match a given predicate
    private async Task PerformOperations(
        ZipArchive zip,
        string filePath,
        Func<GetOperation, OpenApiOperation, bool> predicate,
        Dictionary<string, string> variables
    )
    {
        // Loop through all operations, backing them up if they match a given predicate
        foreach (GetOperation operation in GetOperations)
        {
            // Find the operation in the OpenAPI specification, used by some predicates and to find the endpoint path
            string operationPath;
            OpenApiOperation specOperation;
            try
            {
                (operationPath, specOperation) = FindOperationInSpec(operation);
            }
            catch (KeyNotFoundException)
            {
                continue;
            }

            // Check predicate
            if (!predicate(operation, specOperation)) continue;

            // Replace variables in URL
            foreach ((string key, string value) in variables)
            {
                operationPath = operationPath.Replace($"{{{key}}}", value);
            }

            // Perform backup
            await PerformOperation(
                zip,
                filePath + GenerateFileName(operation.OperationId),
                // Skip leading slash
                operationPath[1..]
            );
        }
    }

    // Backup one operation
    private async Task PerformOperation(
        ZipArchive zip,
        string filePath,
        string operationPath
    )
    {
#warning Skip unscoped endpoints. This is a bad fix.
        if (operationPath.Contains("inboundFirewallRule")) return;

        // Download configuration from endpoint, then parse it as JSON
        Stream responseStream = await ApiClient.GetStreamAsync(
            operationPath.Replace("{organizationId}", OrganizationId)
        );

        JsonDocument responseBody = await JsonDocument.ParseAsync(responseStream);

        // Skip files which are just defaults
        foreach (JsonElement defaultConfig in DefaultConfigs)
        {
            if (JsonElement.DeepEquals(defaultConfig, responseBody.RootElement))
            {
                return;
            }
        }

        // Skip empty objects
        try
        {
            if (responseBody.RootElement.GetPropertyCount() < 1) return;
        }
        // This is raised if RootElement was not an Object, so ignore it to save every non-object JSON
        catch (InvalidOperationException) { }

        // Save configuration into a file in the zip archive
        ZipArchiveEntry file = zip.CreateEntry(filePath + ".json");
        using Utf8JsonWriter writer = new(
            await file.OpenAsync(),
            new JsonWriterOptions()
            {
                Indented = true,
                IndentSize = 4,
            }
        );

        responseBody.WriteTo(writer);
    }

    // Check if a given operation has a given scope/tag
    private static bool HasScope(OpenApiOperation operation, string scope)
    {
        if (operation.Tags is null)
            return false;

        foreach (OpenApiTagReference tag in operation.Tags)
        {
            if (tag.Name is null) continue;
            if (tag.Name.Equals(scope, StringComparison.InvariantCultureIgnoreCase))
                return true;
        }

        return false;
    }

    // Finds an operation in the OpenAPI specification
    private (string Path, OpenApiOperation Item) FindOperationInSpec(GetOperation operation)
    {
        foreach ((string path, IOpenApiPathItem item) in MerakiSpec.Paths)
        {
            if (!(item.Operations?.ContainsKey(HttpMethod.Get) ?? false)) continue;

            OpenApiOperation specOperation = item.Operations[HttpMethod.Get];
            if (specOperation.OperationId == operation.OperationId)
                return (path, specOperation);
        }

        throw new KeyNotFoundException();
    }

    // Get the device family from a device model
    private static string? GetDeviceType(Device device)
    {
        // Adapted from https://github.com/meraki/automation-scripts/blob/master/backup_configs/backup_configs.py#L58
        string family = device.Model[..2];
        return family switch
        {
            "MR" => "wireless",
            "MS" => "switch",
            "MV" => "camera",
            "MG" => "cellularGateway",
            "MX" or "vM" or "Z3" or "Z1" => "appliance",
            _ => null,
        };
    }

    // Generate the filename for a given operation
    private static string GenerateFileName(string operationId)
    {
        // Adapted roughly from https://github.com/meraki/automation-scripts/blob/master/backup_configs/backup_configs.py#L75
        string fileName = "";
        string operation = operationId
            .Replace("getOrganization", "")
            .Replace("getDevice", "")
            .Replace("getNetwork", "");

        // Convert to snake case
        fileName += char.ToLowerInvariant(operation[0]);
        foreach (char letter in operation[1..])
        {
            if (char.IsUpper(letter))
                fileName += "_" + char.ToLowerInvariant(letter);
            else
                fileName += letter;
        }

        return fileName;
    }
}