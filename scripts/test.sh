#!/usr/bin/env bash

set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/.." && pwd -P)"
windows_dotnet="${TATAPP_WINDOWS_DOTNET:-}"

has_required_sdk() {
    [[ -x "$1" ]] && "$1" --list-sdks 2>/dev/null | tr -d '\r' | rg -q '^10\.0\.401 '
}

if [[ -z "$windows_dotnet" ]] && has_required_sdk "/mnt/c/Program Files/dotnet/dotnet.exe"; then
    windows_dotnet="/mnt/c/Program Files/dotnet/dotnet.exe"
fi
if [[ -z "$windows_dotnet" ]]; then
    shopt -s nullglob
    for candidate in /mnt/c/Users/*/AppData/Local/*/dotnet/dotnet.exe; do
        if has_required_sdk "$candidate"; then windows_dotnet="$candidate"; break; fi
    done
    shopt -u nullglob
fi

if command -v dotnet >/dev/null 2>&1; then
    dotnet restore "$repo_root/TATAPP.slnx"
    dotnet build "$repo_root/TATAPP.slnx" --configuration Release --no-restore
    dotnet "$repo_root/tests/TATAPP.Tests/bin/Release/net10.0-windows/TATAPP.Tests.dll"
elif [[ -x "$windows_dotnet" ]] && command -v wslpath >/dev/null 2>&1; then
    repo_windows="$(wslpath -w "$repo_root")"
    "$windows_dotnet" restore "$repo_windows\\TATAPP.slnx"
    "$windows_dotnet" build "$repo_windows\\TATAPP.slnx" --configuration Release --no-restore
    "$windows_dotnet" "$repo_windows\\tests\\TATAPP.Tests\\bin\\Release\\net10.0-windows\\TATAPP.Tests.dll"
else
    echo "A .NET 10 SDK was not found." >&2
    exit 2
fi
