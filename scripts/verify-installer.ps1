param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"
$resolved = Get-Item -LiteralPath $InstallerPath
$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$database = $windowsInstaller.GetType().InvokeMember(
    "OpenDatabase",
    "InvokeMethod",
    $null,
    $windowsInstaller,
    @($resolved.FullName, 0)
)

function Get-MsiProperty([string]$name) {
    $query = "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$name'"
    $view = $database.GetType().InvokeMember("OpenView", "InvokeMethod", $null, $database, @($query))
    $view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null
    $record = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)
    if ($null -eq $record) { return $null }
    return $record.GetType().InvokeMember("StringData", "GetProperty", $null, $record, 1)
}

function Get-MsiColumnValues([string]$query) {
    $view = $database.GetType().InvokeMember("OpenView", "InvokeMethod", $null, $database, @($query))
    $view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null
    $values = [System.Collections.Generic.List[string]]::new()
    while ($true) {
        $record = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)
        if ($null -eq $record) { break }
        $values.Add([string]$record.GetType().InvokeMember("StringData", "GetProperty", $null, $record, 1))
    }
    return $values.ToArray()
}

$summary = $database.GetType().InvokeMember("SummaryInformation", "GetProperty", $null, $database, $null)
$template = $summary.GetType().InvokeMember("Property", "GetProperty", $null, $summary, 7)
$signature = Get-AuthenticodeSignature -LiteralPath $resolved.FullName
$hash = Get-FileHash -LiteralPath $resolved.FullName -Algorithm SHA256
$payloadFiles = @(Get-MsiColumnValues "SELECT ``FileName`` FROM ``File`` ORDER BY ``File``")

[pscustomobject]@{
    Path = $resolved.FullName
    ProductName = Get-MsiProperty "ProductName"
    ProductVersion = Get-MsiProperty "ProductVersion"
    ProductCode = Get-MsiProperty "ProductCode"
    UpgradeCode = Get-MsiProperty "UpgradeCode"
    Manufacturer = Get-MsiProperty "Manufacturer"
    ArchitectureTemplate = $template
    PayloadFileCount = $payloadFiles.Count
    PayloadFiles = $payloadFiles -join ", "
    LengthBytes = $resolved.Length
    SHA256 = $hash.Hash
    AuthenticodeStatus = $signature.Status.ToString()
    AuthenticodeSigner = if ($null -eq $signature.SignerCertificate) { "(none)" } else { $signature.SignerCertificate.Subject }
}
