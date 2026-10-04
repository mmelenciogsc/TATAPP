param(
    [Parameter(Mandatory = $true)]
    [string]$Thumbprint,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\TATAPP.App\TATAPP.App.csproj"
$installerProject = Join-Path $root "installer\TATAPP.Installer.wixproj"
$publishDirectory = Join-Path $root "artifacts\installer\publish\win-x64"
$installerDirectory = Join-Path $root "artifacts\installer"
$applicationPath = Join-Path $publishDirectory "TATAPP.exe"
$dotnet = if ([string]::IsNullOrWhiteSpace($env:TATAPP_WINDOWS_DOTNET)) { "dotnet" } else { $env:TATAPP_WINDOWS_DOTNET }
$normalizedThumbprint = ($Thumbprint -replace '\s', '').ToUpperInvariant()

$certificate = Get-Item "Cert:\CurrentUser\My\$normalizedThumbprint" -ErrorAction Stop
if (-not $certificate.HasPrivateKey) {
    throw "The selected certificate has no private key."
}
if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) {
    throw "The selected certificate is not currently valid."
}
$isCodeSigningCertificate = @($certificate.EnhancedKeyUsageList | Where-Object {
    $_.FriendlyName -eq "Code Signing" -or $_.Value -eq "1.3.6.1.5.5.7.3.3"
}).Count -gt 0
if (-not $isCodeSigningCertificate) {
    throw "The selected certificate does not have the Code Signing enhanced key usage."
}

$signTool = if (-not [string]::IsNullOrWhiteSpace($env:TATAPP_SIGNTOOL)) {
    $env:TATAPP_SIGNTOOL
} else {
    Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if ([string]::IsNullOrWhiteSpace($signTool) -or -not (Test-Path -LiteralPath $signTool -PathType Leaf)) {
    throw "A 64-bit Windows SDK signtool.exe was not found."
}

function Invoke-AuthenticodeSign([string]$path) {
    & $signTool sign `
        /sha1 $normalizedThumbprint `
        /s My `
        /fd SHA256 `
        /td SHA256 `
        /tr $TimestampUrl `
        /d "Tattoo Art Prepper (TATAPP)" `
        /du "https://github.com/mmelenciogsc/TATAPP" `
        /v $path
    if ($LASTEXITCODE -ne 0) { throw "Authenticode signing failed for $path with exit code $LASTEXITCODE." }

    & $signTool verify /pa /all /v $path
    if ($LASTEXITCODE -ne 0) { throw "Authenticode verification failed for $path with exit code $LASTEXITCODE." }

    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        throw "PowerShell reported signature status $($signature.Status) for $path."
    }
}

& (Join-Path $PSScriptRoot "build-installer.ps1") -Configuration $Configuration -PublishOnly
Invoke-AuthenticodeSign $applicationPath

[xml]$projectDefinition = Get-Content -LiteralPath $project
$productVersion = [string]$projectDefinition.Project.PropertyGroup.VersionPrefix
& $dotnet build $installerProject `
    --configuration $Configuration `
    --output $installerDirectory `
    -p:ProductVersion=$productVersion `
    -p:PublishDir=$publishDirectory
if ($LASTEXITCODE -ne 0) { throw "WiX rebuild with the signed application failed with exit code $LASTEXITCODE." }

$installerPath = Join-Path $installerDirectory "TATAPP-$productVersion-win-x64.msi"
Invoke-AuthenticodeSign $installerPath

[pscustomobject]@{
    ApplicationPath = $applicationPath
    ApplicationSHA256 = (Get-FileHash -LiteralPath $applicationPath -Algorithm SHA256).Hash
    InstallerPath = $installerPath
    InstallerSHA256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    CertificateSubject = $certificate.Subject
    CertificateThumbprint = $certificate.Thumbprint
    CertificateExpires = $certificate.NotAfter
}
