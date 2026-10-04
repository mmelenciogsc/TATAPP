param(
    [string]$Configuration = "Release",
    [switch]$PublishOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\TATAPP.App\TATAPP.App.csproj"
$installerProject = Join-Path $root "installer\TATAPP.Installer.wixproj"
$publishDirectory = Join-Path $root "artifacts\installer\publish\win-x64"
$installerDirectory = Join-Path $root "artifacts\installer"
$dotnet = if ([string]::IsNullOrWhiteSpace($env:TATAPP_WINDOWS_DOTNET)) { "dotnet" } else { $env:TATAPP_WINDOWS_DOTNET }

[xml]$projectDefinition = Get-Content -LiteralPath $project
$productVersion = [string]$projectDefinition.Project.PropertyGroup.VersionPrefix
if ($productVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "TATAPP.App.csproj must contain a three-part numeric VersionPrefix for MSI packaging."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

& $dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:Version=$productVersion
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$publishedFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File)
if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne "TATAPP.exe") {
    $publishedNames = $publishedFiles.Name -join ", "
    throw "Expected a single self-contained TATAPP.exe, but publish produced: $publishedNames"
}

if ($PublishOnly) {
    Write-Output "Published self-contained TATAPP application: $($publishedFiles[0].FullName)"
    return
}

& $dotnet build $installerProject `
    --configuration $Configuration `
    --output $installerDirectory `
    -p:ProductVersion=$productVersion `
    -p:PublishDir=$publishDirectory
if ($LASTEXITCODE -ne 0) { throw "WiX installer build failed with exit code $LASTEXITCODE." }

$installerPath = Join-Path $installerDirectory "TATAPP-$productVersion-win-x64.msi"
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Expected installer was not created at $installerPath."
}

Write-Output "Built self-contained TATAPP installer: $installerPath"
