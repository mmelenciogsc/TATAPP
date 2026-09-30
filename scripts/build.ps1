$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ([string]::IsNullOrWhiteSpace($env:TATAPP_WINDOWS_DOTNET)) { "dotnet" } else { $env:TATAPP_WINDOWS_DOTNET }
& $dotnet restore (Join-Path $root "TATAPP.slnx")
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
& $dotnet build (Join-Path $root "TATAPP.slnx") --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }
