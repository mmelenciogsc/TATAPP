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
