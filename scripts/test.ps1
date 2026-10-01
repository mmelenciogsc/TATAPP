$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build.ps1")
$dotnet = if ([string]::IsNullOrWhiteSpace($env:TATAPP_WINDOWS_DOTNET)) { "dotnet" } else { $env:TATAPP_WINDOWS_DOTNET }
& $dotnet (Join-Path $root "tests\TATAPP.Core.Tests\bin\Release\net10.0\TATAPP.Core.Tests.dll")
if ($LASTEXITCODE -ne 0) { throw "TATAPP Core tests failed with exit code $LASTEXITCODE." }
& $dotnet (Join-Path $root "tests\TATAPP.Tests\bin\Release\net10.0-windows\TATAPP.Tests.dll")
if ($LASTEXITCODE -ne 0) { throw "TATAPP Windows tests failed with exit code $LASTEXITCODE." }
