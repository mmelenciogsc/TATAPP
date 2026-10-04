# Windows installer

TATAPP's Windows package is a 64-bit per-user MSI built with WiX. It installs
the single-file, self-contained `win-x64` application under
`%LOCALAPPDATA%\Programs\TATAPP`, adds a Start menu shortcut, and does not
require a separately installed .NET runtime or administrator access.

## Build and verify

Run the documented Windows tests first, then build and inspect the installer:

```powershell
.\scripts\test.ps1
.\scripts\build-installer.ps1
.\scripts\verify-installer.ps1 `
  -InstallerPath .\artifacts\installer\TATAPP-0.3.1-win-x64.msi
```

The first installer build requires internet access to restore the repository-
pinned WiX SDK package from NuGet.org. Later builds can use the local NuGet
cache.

Generated publish files, MSI packages, symbols, caches, and signing material
are ignored by Git. The MSI is intentionally unsigned unless a release
operator signs it outside the repository with separately protected
credentials. Always report an unsigned package as unsigned; never add a
certificate or private key to this repository.

## Local development signing

For testing on a machine you control, create a non-exportable self-signed
certificate and trust its public certificate for the current Windows user:

```powershell
$certificate = .\scripts\create-local-signing-certificate.ps1
.\scripts\sign-installer.ps1 -Thumbprint $certificate.Thumbprint
```

The signing script publishes the application, signs `TATAPP.exe`, rebuilds the
MSI around that signed executable, signs the MSI, and verifies both signatures.
The private key remains in the current user's Windows certificate store. The
exported `.cer` contains only the public certificate and is placed under the
ignored `artifacts` directory.

This local certificate is trusted only for the Windows user and machine where
the creation script runs. It does not establish a publicly trusted publisher,
does not provide SmartScreen reputation, and must not be represented as release
signing. Public distribution requires a certificate from a publicly trusted
code-signing provider or a managed service such as Azure Artifact Signing.

## Install and uninstall

Double-click the MSI for the normal Windows Installer interface. A release
operator can perform a quiet validation with logging by running:

```powershell
msiexec.exe /i .\artifacts\installer\TATAPP-0.3.1-win-x64.msi /qn /norestart /l*v .\artifacts\installer\install.log
msiexec.exe /x .\artifacts\installer\TATAPP-0.3.1-win-x64.msi /qn /norestart /l*v .\artifacts\installer\uninstall.log
```

Before a quiet test, check that no separately installed copy of TATAPP is in
use. After uninstall, the installed application directory and Start menu
shortcut should be absent. User-created images and other files outside the
application directory are never removed.
