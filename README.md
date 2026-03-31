# ConfigDump
A command-line utility to bulk dump config files from various devices.

## Device Request Format

```json
{
    "device-1": {
        "type": "aruba",
        "ip": "1.2.3.4",
        "username": "admin",
        "password": "p@ssw0rd",
    },
    ...
}
```

## Usage

1. Download the latest release from the releases tab.
2. Create a file in the above JSON format with a list of all devices to backup, and required access information.
3. Run the command below, replacing `<DEVICES>` with the file path to the device json file, and `<URL>` to post the result JSON to.

```
configdump <DEVICES> <URL>
```

## Contributing

Ensure you have .NET SDK 10.0 installed.

```cmd
git clone git@github.com:PremierTech/ConfigDump.git
cd ConfigDump
dotnet build
```

Make sure to create a new GitHub Release if you have made a new version.
