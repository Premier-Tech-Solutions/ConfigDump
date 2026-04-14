# ConfigDump
A command-line utility to bulk dump config files from various devices.

## Device Request Format

```json
{
    "devices": [
        {
            "id": "device-1",
            "serial": "AAAA-BBBB-CCCC",
            "model": "Aruba ASDF",
            "ip": "1.2.3.4",
            "username": "admin",
            "password": "p@ssw0rd",
        },
        ...
    ]
}
```

## Usage

1. Download the latest release from the releases tab.
2. Create a file in the above JSON format with a list of all devices to backup, and required access information.
3. Run the command below, replacing `<DEVICES>` with the URL to get the device information from, and `<CONFIGS>` to post the configs to.

```
configdump <DEVICES> <CONFIGS>
```

## Contributing

Ensure you have the .NET SDK 10.0 and the .NET Sign Tool[^1] installed.

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