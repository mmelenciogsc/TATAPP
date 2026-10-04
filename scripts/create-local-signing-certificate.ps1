param(
    [string]$Subject = "CN=TATAPP Local Development",
    [ValidateRange(1, 10)]
    [int]$ValidYears = 3
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$publicDirectory = Join-Path $root "artifacts\signing"
$publicCertificatePath = Join-Path $publicDirectory "TATAPP-Local-Development.cer"

$certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert |
    Where-Object {
        $_.Subject -eq $Subject -and
        $_.HasPrivateKey -and
        $_.NotAfter -gt (Get-Date).AddDays(30)
    } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if ($null -eq $certificate) {
    $certificate = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $Subject `
        -FriendlyName "TATAPP Local Development Code Signing" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddYears($ValidYears)
}

New-Item -ItemType Directory -Path $publicDirectory -Force | Out-Null
Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Force | Out-Null

foreach ($store in @("Cert:\CurrentUser\Root", "Cert:\CurrentUser\TrustedPublisher")) {
    $trusted = Get-ChildItem $store | Where-Object { $_.Thumbprint -eq $certificate.Thumbprint }
    if ($null -eq $trusted) {
        Import-Certificate -FilePath $publicCertificatePath -CertStoreLocation $store | Out-Null
    }
}

[pscustomobject]@{
    Subject = $certificate.Subject
    Thumbprint = $certificate.Thumbprint
    NotAfter = $certificate.NotAfter
    HasPrivateKey = $certificate.HasPrivateKey
    PublicCertificatePath = $publicCertificatePath
    TrustScope = "Current Windows user on this machine only"
}
