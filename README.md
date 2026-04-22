# ConfigDump
A command-line utility to bulk dump config files from various devices.

## Usage

1. Download the latest release from the releases tab.
2. Run the command below, replacing `<DEVICES>` with the URL to get the device information from,
and `<CONFIGS>` to post the configs to.

```
configdump <DEVICES> <CONFIGS>
```

## Device Request Format

```json
{
    "meraki": {
        "api_key": "0123456890abcdef",
        "organization_id": "1234567890",
    },
    "devices": {
        "device-1": {
            "serial": "AAAA-BBBB-CCCC",
            "model": "Aruba ASDF",
            "primary_ip": "192.168.0.123",
            "credentials": [{
                "url": "https://192.168.0.10/",
                "username": "admin",
                "password": "p@ssw0rd"
            }]
        },
        ...
    }
}
```

## Device Config Format

```json
{
    "device-1": {
        "error": false,
        "file_type": "pcc",
        "value": "..."
    },
    ...
}
```

## Contributing

Ensure you have the .NET SDK 10.0 and the .NET Sign Tool[^1] installed.

The program can be tested without a webserver by using the `--test` argument.
This makes the program take the device information JSON from the console input (hit enter twice to confirm),
and dumps the configs to `configs.json` in the current working directory.

```cmd
git clone git@github.com:PremierTech/ConfigDump.git
cd ConfigDump
dotnet publish
dotnet sign code certificate-store bin\Release\net10.0\win-x64\publish\*.exe -cfp SHA256THUMBPRINT
```

> [!NOTE]
> To generate a new code-signing certificate, run the following command in powershell
> ```powershell
> # Make sure to fill in the Subject CN and email
> $params = @{
>     DNSName = "www.premiertech.com.au"
>     Subject = "CN=FirstName LastName, OU=Centralised Services, O=Premier Technology Solutions, L=Melbourne, S=Victoria, C=AU, E=email@premiertech.com.au"
>     Type = "CodeSigningCert"
>     KeyUsage = "DigitalSignature"
>     KeySpec = "Signature"
>     KeyAlgorithm = "RSA"
>     KeyLength = 2048
>     CertStoreLocation = "Cert:\CurrentUser\My"
> }
> $cert = New-SelfSignedCertificate @params
> $cert.GetCertHashString("SHA256") # This gives you the SHA-256 Thumbprint
> ```

Make sure to create a new GitHub Release if you have made a new version.

[^1]: The .NET Sign Tool can be installed with the command: `dotnet tool install --prerelease sign`.