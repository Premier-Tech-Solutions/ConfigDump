# Needed to catch errors, and also prevents program from rolling through fatal errors
$ErrorActionPreference = "Stop"

$FILEPATH = "C:\ConfigDump.exe"

# Get current ConfigDump version
Write-Host "Getting current ConfigDump version information..."
$release = Invoke-RestMethod https://api.github.com/repos/Premier-Tech-Solutions/ConfigDump/releases/latest

# Check ConfigDump release ID
$is_outdated = $false
try {
    $is_outdated = $release.id -Ne $(Get-Content $FILEPATH -Stream ReleaseID)
} catch [System.Management.Automation.ItemNotFoundException], [System.IO.FileNotFoundException] {
    # Either ConfigDump.exe doesn't exist, or it doesn't have a valid ReleaseID stream
    $is_outdated = $true
}

# Download new version if outdated
if ($is_outdated) {
    Write-Host "Updating/downloading ConfigDump..."
    
    # Hide progress bar as it slows down the download for large files
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest $release.assets[0].browser_download_url -OutFile $FILEPATH

    # Update release ID stream
    Set-Content $FILEPATH -Stream ReleaseID -Value $release.id
}

# Run the executable with the parameters from Rewst
& $FILEPATH $device_url $post_url