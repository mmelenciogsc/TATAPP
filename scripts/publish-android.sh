#!/usr/bin/env bash

set -Eeuo pipefail
export LC_ALL=C

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/.." && pwd -P)"
readonly script_dir repo_root

# shellcheck disable=SC1091 # Resolved from this script's canonical directory.
. "$script_dir/android/release-config.sh"

: "${TATAPP_ANDROID_KEYSTORE:?Set TATAPP_ANDROID_KEYSTORE to an external keystore path.}"
: "${TATAPP_ANDROID_KEY_ALIAS:?Set TATAPP_ANDROID_KEY_ALIAS.}"
: "${TATAPP_ANDROID_KEYSTORE_PASSWORD:?Set TATAPP_ANDROID_KEYSTORE_PASSWORD.}"
: "${TATAPP_ANDROID_KEY_PASSWORD:?Set TATAPP_ANDROID_KEY_PASSWORD.}"

[[ -f "$TATAPP_ANDROID_KEYSTORE" ]] || {
    printf 'External keystore not found: %s\n' "$TATAPP_ANDROID_KEYSTORE" >&2
    exit 2
}

dotnet_bin="${DOTNET_BIN:-$(command -v dotnet 2>/dev/null || true)}"
android_sdk="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
java_home="${JAVA_HOME:-}"
native_root="${TATAPP_LLAMA_NATIVE_DIR:-$repo_root/artifacts/android/native/llamasharp-0.27.0/native}"
project="$repo_root/src/TATAPP.Android/TATAPP.Android.csproj"
artifact_root="${TATAPP_ANDROID_ARTIFACT_ROOT:-$repo_root/artifacts/android}"
output_apk="$artifact_root/$TATAPP_ANDROID_ARTIFACT_BASENAME"

[[ -x "$dotnet_bin" ]] || { printf 'Set DOTNET_BIN to the .NET 10.0.401 dotnet executable.\n' >&2; exit 2; }
"$dotnet_bin" --list-sdks 2>/dev/null | grep -Eq '^10\.0\.401([[:space:]]|$)' || {
    printf 'DOTNET_BIN does not provide the pinned .NET 10.0.401 SDK.\n' >&2
    exit 2
}
[[ -n "$android_sdk" && -d "$android_sdk" ]] || {
    printf 'Set ANDROID_SDK_ROOT (or ANDROID_HOME) to the Android SDK.\n' >&2
    exit 2
}
[[ -n "$java_home" && -x "$java_home/bin/java" ]] || {
    printf 'Set JAVA_HOME to a JDK 17 installation.\n' >&2
    exit 2
}
"$java_home/bin/javac" -version 2>&1 | grep -Eq '^javac 17([.]|$)' || {
    printf 'JAVA_HOME must provide JDK 17.\n' >&2
    exit 2
}
[[ -f "$android_sdk/platforms/android-36/android.jar" ]] || {
    printf 'Android SDK Platform 36 is not installed.\n' >&2
    exit 2
}
for abi in android-arm64-v8a android-x86_64; do
    for library in libggml-base.so libggml-cpu.so libggml.so libllama.so libmtmd.so; do
        [[ -f "$native_root/$abi/$library" ]] || {
            printf 'Missing validated native runtime: %s/%s\n' "$native_root/$abi" "$library" >&2
            exit 2
        }
    done
done

export ANDROID_HOME="$android_sdk"
export ANDROID_SDK_ROOT="$android_sdk"
export JAVA_HOME="$java_home"
export TATAPP_LLAMA_NATIVE_DIR="$native_root"

mkdir -p "$artifact_root"
staging="$(mktemp -d "$artifact_root/.publish.XXXXXX")"
trap 'rm -rf -- "$staging"' EXIT

"$dotnet_bin" restore "$project" \
    -p:AndroidSdkDirectory="$android_sdk" -p:JavaSdkDirectory="$java_home"
"$dotnet_bin" publish "$project" --configuration Release \
    --framework net10.0-android36.0 --no-restore --output "$staging" \
    -p:AndroidSdkDirectory="$android_sdk" -p:JavaSdkDirectory="$java_home" \
    -p:AndroidKeyStore=true \
    -p:AndroidSigningKeyStore="$TATAPP_ANDROID_KEYSTORE" \
    -p:AndroidSigningKeyAlias="$TATAPP_ANDROID_KEY_ALIAS" \
    -p:AndroidSigningStorePass=env:TATAPP_ANDROID_KEYSTORE_PASSWORD \
    -p:AndroidSigningKeyPass=env:TATAPP_ANDROID_KEY_PASSWORD

signed_apk="$staging/$TATAPP_ANDROID_PACKAGE_ID-Signed.apk"
[[ -f "$signed_apk" ]] || { printf 'Signed APK was not produced at %s\n' "$signed_apk" >&2; exit 1; }
ANDROID_SDK_ROOT="$android_sdk" "$script_dir/android/verify-apk.sh" "$signed_apk"
install -m 0644 "$signed_apk" "$output_apk"
(
    cd -- "$artifact_root"
    sha256sum "$(basename -- "$output_apk")" > "$(basename -- "$output_apk").sha256"
)
printf 'Published signed evaluation APK: %s\nChecksum: %s.sha256\n' "$output_apk" "$output_apk"
