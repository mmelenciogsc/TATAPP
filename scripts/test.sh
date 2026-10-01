#!/usr/bin/env bash

set -Eeuo pipefail
export LC_ALL=C

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/.." && pwd -P)"
readonly script_dir repo_root

has_required_sdk() {
    [[ -x "$1" ]] && "$1" --list-sdks 2>/dev/null | tr -d '\r' | grep -Eq '^10\.0\.401([[:space:]]|$)'
}

dotnet_bin="${DOTNET_BIN:-}"
if [[ -n "$dotnet_bin" ]] && ! has_required_sdk "$dotnet_bin"; then
    printf 'DOTNET_BIN does not provide the pinned .NET 10.0.401 SDK: %s\n' "$dotnet_bin" >&2
    exit 2
fi
if [[ -z "$dotnet_bin" ]]; then
    candidate="$(command -v dotnet 2>/dev/null || true)"
    if has_required_sdk "$candidate"; then dotnet_bin=$candidate; fi
fi
if [[ -z "$dotnet_bin" ]] && has_required_sdk "$repo_root/../.dotnet-tatapp/dotnet"; then
    dotnet_bin="$repo_root/../.dotnet-tatapp/dotnet"
fi

if [[ -n "$dotnet_bin" ]]; then
    core_tests="$repo_root/tests/TATAPP.Core.Tests/TATAPP.Core.Tests.csproj"
    "$dotnet_bin" restore "$core_tests"
    "$dotnet_bin" build "$core_tests" --configuration Release --no-restore
    "$dotnet_bin" "$repo_root/tests/TATAPP.Core.Tests/bin/Release/net10.0/TATAPP.Core.Tests.dll"

    android_sdk="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
    native_root="${TATAPP_LLAMA_NATIVE_DIR:-$repo_root/artifacts/android/native/llamasharp-0.27.0/native}"
    if [[ -n "$android_sdk" && -f "$android_sdk/platforms/android-36/android.jar" ]] &&
       "$dotnet_bin" workload list 2>/dev/null | grep -Eq '^[[:space:]]*android[[:space:]]' &&
       [[ -f "$native_root/android-arm64-v8a/libllama.so" &&
          -f "$native_root/android-x86_64/libllama.so" ]]; then
        DOTNET_BIN="$dotnet_bin" "$script_dir/build-android.sh"
    else
        printf 'SKIP Android build: configure Android workload/API 36 and both validated native ABI folders.\n'
    fi
    exit 0
fi

windows_dotnet="${TATAPP_WINDOWS_DOTNET:-}"
if [[ -z "$windows_dotnet" ]] && has_required_sdk "/mnt/c/Program Files/dotnet/dotnet.exe"; then
    windows_dotnet="/mnt/c/Program Files/dotnet/dotnet.exe"
fi
if [[ -z "$windows_dotnet" ]]; then
    shopt -s nullglob
    for candidate in /mnt/c/Users/*/AppData/Local/*/dotnet/dotnet.exe; do
        if has_required_sdk "$candidate"; then windows_dotnet=$candidate; break; fi
    done
    shopt -u nullglob
fi

if [[ -x "$windows_dotnet" ]] && command -v wslpath >/dev/null 2>&1; then
    repo_windows="$(wslpath -w "$repo_root")"
    core_tests="$repo_windows\tests\TATAPP.Core.Tests\TATAPP.Core.Tests.csproj"
    windows_tests="$repo_windows\tests\TATAPP.Tests\TATAPP.Tests.csproj"
    "$windows_dotnet" restore "$core_tests"
    "$windows_dotnet" build "$core_tests" --configuration Release --no-restore
    "$windows_dotnet" "$repo_windows\tests\TATAPP.Core.Tests\bin\Release\net10.0\TATAPP.Core.Tests.dll"
    "$windows_dotnet" restore "$windows_tests"
    "$windows_dotnet" build "$windows_tests" --configuration Release --no-restore
    "$windows_dotnet" "$repo_windows\tests\TATAPP.Tests\bin\Release\net10.0-windows\TATAPP.Tests.dll"
    printf 'SKIP Android build: the selected CLI is Windows .NET invoked through WSL.\n'
    exit 0
fi

printf 'A .NET 10.0.401 SDK was not found. Set DOTNET_BIN or TATAPP_WINDOWS_DOTNET.\n' >&2
exit 2
