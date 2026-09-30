$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\TATAPP.App\TATAPP.App.csproj"
$output = Join-Path $root "artifacts\publish\win-x64"
$dotnet = if ([string]::IsNullOrWhiteSpace($env:TATAPP_WINDOWS_DOTNET)) { "dotnet" } else { $env:TATAPP_WINDOWS_DOTNET }
& $dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
    --output $output -p:PublishSingleFile=false -p:PublishTrimmed=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
Write-Output "Published self-contained TATAPP to $output"
