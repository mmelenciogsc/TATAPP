$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = if ([string]::IsNullOrWhiteSpace($env:TATAPP_WINDOWS_DOTNET)) { "dotnet" } else { $env:TATAPP_WINDOWS_DOTNET }
$projects = @(
    (Join-Path $root "tests\TATAPP.Core.Tests\TATAPP.Core.Tests.csproj"),
    (Join-Path $root "tests\TATAPP.Tests\TATAPP.Tests.csproj")
)
foreach ($project in $projects) {
    & $dotnet restore $project
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed for $project with exit code $LASTEXITCODE." }
    & $dotnet build $project --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $project with exit code $LASTEXITCODE." }
}
