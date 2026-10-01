#!/usr/bin/env bash

set -Eeuo pipefail
export LC_ALL=C

usage() {
    cat <<'EOF'
Usage: verify-apk.sh APK

The expected package, versionCode, versionName, and ABI set come from the
repository's shared Android release configuration.

Environment overrides:
  ANDROID_SDK_ROOT or ANDROID_HOME
  ANDROID_BUILD_TOOLS_VERSION (default 36.0.0)
  ANDROID_NDK_VERSION         (default 27.0.12077973)
  EXPECTED_MIN_SDK            (default 26)
  EXPECTED_TARGET_SDK         (default 36)
  EXPECTED_SIGNER_SHA256      (optional 64-digit signing-certificate digest)
EOF
}

if [[ ${1:-} == "-h" || ${1:-} == "--help" ]]; then
    usage
    exit 0
fi
[[ $# -eq 1 ]] || { usage >&2; exit 2; }

apk=$1
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/../.." && pwd -P)"
# shellcheck disable=SC1091 # Resolved from this script's canonical directory.
. "$script_dir/release-config.sh"
expected_package=$TATAPP_ANDROID_PACKAGE_ID
expected_version_code=$TATAPP_ANDROID_VERSION_CODE
expected_version_name=$TATAPP_ANDROID_VERSION_NAME
expected_abis=$TATAPP_ANDROID_ABIS
expected_min_sdk=${EXPECTED_MIN_SDK:-26}
expected_target_sdk=${EXPECTED_TARGET_SDK:-36}
android_sdk="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
build_tools_version=${ANDROID_BUILD_TOOLS_VERSION:-36.0.0}
ndk_version=${ANDROID_NDK_VERSION:-27.0.12077973}

[[ -f "$apk" ]] || { printf 'APK not found: %s\n' "$apk" >&2; exit 2; }
[[ -n "$android_sdk" ]] || { printf 'Set ANDROID_SDK_ROOT to the Android SDK directory.\n' >&2; exit 2; }

readonly aapt="$android_sdk/build-tools/$build_tools_version/aapt"
readonly zipalign="$android_sdk/build-tools/$build_tools_version/zipalign"
readonly apksigner="$android_sdk/build-tools/$build_tools_version/apksigner"
readonly readelf="$android_sdk/ndk/$ndk_version/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-readelf"

for tool in "$aapt" "$zipalign" "$apksigner" "$readelf"; do
    [[ -x "$tool" ]] || { printf 'Required Android tool not found: %s\n' "$tool" >&2; exit 2; }
done
for tool in awk cmp grep paste sed sort tail tr unzip; do
    command -v "$tool" >/dev/null || { printf 'Required command not found: %s\n' "$tool" >&2; exit 2; }
done

work_dir="$(mktemp -d)"
trap 'rm -rf -- "$work_dir"' EXIT

badging_file="$work_dir/badging.txt"
permissions_file="$work_dir/permissions.txt"
manifest_file="$work_dir/manifest.txt"
entries_file="$work_dir/native-entries.txt"
"$aapt" dump badging "$apk" > "$badging_file"
"$aapt" dump permissions "$apk" > "$permissions_file"
"$aapt" dump xmltree "$apk" AndroidManifest.xml > "$manifest_file"

catalog_source="$repo_root/src/TATAPP.Android/Resources/raw/offline_ai_models.json"
catalog_packaged="$work_dir/offline_ai_models.json"
[[ -f "$catalog_source" ]] || { printf 'Shipping model catalog source is missing: %s\n' "$catalog_source" >&2; exit 1; }
unzip -p "$apk" res/raw/offline_ai_models.json > "$catalog_packaged" || {
    printf 'APK does not contain the shipping offline AI model catalog.\n' >&2
    exit 1
}
cmp -s "$catalog_source" "$catalog_packaged" || {
    printf 'APK offline AI model catalog does not match the current shipping source. Rebuild the APK.\n' >&2
    exit 1
}

package_line="$(awk '/^package:/{ print; exit }' "$badging_file")"
actual_package="$(sed -n "s/^package: name='\([^']*\)'.*/\1/p" <<< "$package_line")"
version_code="$(sed -n "s/.* versionCode='\([^']*\)'.*/\1/p" <<< "$package_line")"
version_name="$(sed -n "s/.* versionName='\([^']*\)'.*/\1/p" <<< "$package_line")"
min_sdk="$(sed -n "s/^sdkVersion:'\([^']*\)'.*/\1/p" "$badging_file")"
target_sdk="$(sed -n "s/^targetSdkVersion:'\([^']*\)'.*/\1/p" "$badging_file")"

[[ "$actual_package" == "$expected_package" ]] || {
    printf 'Package mismatch: expected %s, found %s.\n' "$expected_package" "$actual_package" >&2
    exit 1
}
[[ "$version_code" == "$expected_version_code" && "$version_name" == "$expected_version_name" ]] || {
    printf 'Version mismatch: expected %s (%s), found %s (%s).\n' \
        "$expected_version_name" "$expected_version_code" "$version_name" "$version_code" >&2
    exit 1
}
[[ "$min_sdk" == "$expected_min_sdk" && "$target_sdk" == "$expected_target_sdk" ]] || {
    printf 'SDK mismatch: expected min/target %s/%s, found %s/%s.\n' \
        "$expected_min_sdk" "$expected_target_sdk" "$min_sdk" "$target_sdk" >&2
    exit 1
}
if grep -q '^application-debuggable' "$badging_file"; then
    printf 'Release APK is marked debuggable.\n' >&2
    exit 1
fi

mapfile -t actual_permissions < <(
    sed -n "s/^uses-permission[^:]*: name='\([^']*\)'.*/\1/p" "$permissions_file" | sort -u
)
if [[ ${#actual_permissions[@]} -ne 1 || ${actual_permissions[0]} != android.permission.INTERNET ]]; then
    printf 'Permission mismatch: expected only android.permission.INTERNET; found:\n' >&2
    printf '  %s\n' "${actual_permissions[@]:-<none>}" >&2
    exit 1
fi

components_file="$work_dir/components.txt"
awk '
    function emit() {
        if (component != "") print component "|" name "|" exported
    }
    /^      E: (activity|activity-alias|provider|receiver|service) / {
        emit()
        component = $2
        name = ""
        exported = "missing"
        next
    }
    component != "" && /^        A: android:name/ {
        name = $0
        sub(/^[^"]*"/, "", name)
        sub(/".*/, "", name)
        next
    }
    component != "" && /^        A: android:exported/ {
        exported = $0 ~ /0xffffffff/ ? "true" : "false"
    }
    END { emit() }
' "$manifest_file" > "$components_file"

ambiguous_components="$(awk -F'|' '$2 == "" || $3 == "missing" { print }' "$components_file")"
if [[ -n "$ambiguous_components" ]]; then
    printf 'Production components must have explicit names and exported states:\n%s\n' \
        "$ambiguous_components" >&2
    exit 1
fi

grep -Eq '^activity\|[^|]*[.]MainActivity\|true$' "$components_file" || {
    printf 'The production launcher MainActivity is missing or is not exported.\n' >&2
    exit 1
}
unexpected_exported="$(awk -F'|' '$3 == "true" && !($1 == "activity" && $2 ~ /[.]MainActivity$/) { print }' "$components_file")"
if [[ -n "$unexpected_exported" ]]; then
    printf 'Unexpected exported production component(s):\n%s\n' "$unexpected_exported" >&2
    exit 1
fi
if grep -Fq 'OfflineAiSmokeActivity' "$manifest_file"; then
    printf 'The opt-in OfflineAiSmokeActivity must not be present in a production APK.\n' >&2
    exit 1
fi

unzip -Z1 "$apk" | awk -F/ '$1 == "lib" && NF == 3 && $3 ~ /\.so$/ { print }' > "$entries_file"
[[ -s "$entries_file" ]] || { printf 'APK contains no native libraries.\n' >&2; exit 1; }

actual_abis="$(awk -F/ '{ print $2 }' "$entries_file" | sort -u | paste -sd, -)"
normalized_expected_abis="$(tr ',' '\n' <<< "$expected_abis" | sed '/^$/d' | sort -u | paste -sd, -)"
[[ "$actual_abis" == "$normalized_expected_abis" ]] || {
    printf 'ABI mismatch: expected %s, found %s.\n' "$normalized_expected_abis" "$actual_abis" >&2
    exit 1
}

readonly -a offline_ai_libraries=(libggml-base.so libggml-cpu.so libggml.so libllama.so libmtmd.so)
while IFS= read -r abi; do
    for library in "${offline_ai_libraries[@]}"; do
        grep -Fxq "lib/$abi/$library" "$entries_file" || {
            printf 'Missing offline-AI library: lib/%s/%s\n' "$abi" "$library" >&2
            exit 1
        }
    done
done < <(tr ',' '\n' <<< "$normalized_expected_abis")

elf_count=0
while IFS= read -r entry; do
    elf_count=$((elf_count + 1))
    extracted="$work_dir/elf-$elf_count.so"
    alignments="$work_dir/elf-$elf_count.alignments"
    unzip -p "$apk" "$entry" > "$extracted"
    "$readelf" -h "$extracted" >/dev/null 2>&1 || {
        printf 'Native APK member is not a valid ELF file: %s\n' "$entry" >&2
        exit 1
    }
    "$readelf" -lW "$extracted" | awk '$1 == "LOAD" { print $NF }' > "$alignments"
    [[ -s "$alignments" ]] || { printf 'ELF has no load segments: %s\n' "$entry" >&2; exit 1; }
    while IFS= read -r alignment; do
        alignment_value=$((alignment))
        if ((alignment_value < 16384 || alignment_value % 16384 != 0)); then
            printf 'ELF is not 16 KiB compatible: %s has LOAD alignment %s.\n' "$entry" "$alignment" >&2
            exit 1
        fi
    done < "$alignments"
done < "$entries_file"

if ! "$zipalign" -c -P 16 -v 4 "$apk" > "$work_dir/zipalign.txt"; then
    cat "$work_dir/zipalign.txt" >&2
    exit 1
fi
if ! "$apksigner" verify --verbose --print-certs "$apk" > "$work_dir/signature.txt"; then
    cat "$work_dir/signature.txt" >&2
    exit 1
fi
grep -Eq '^Verified using v(2|3) scheme .*: true$' "$work_dir/signature.txt" || {
    printf 'APK lacks a verified v2 or v3 signature.\n' >&2
    exit 1
}
mapfile -t signer_digests < <(
    sed -n 's/^Signer #[0-9][0-9]* certificate SHA-256 digest: //p' "$work_dir/signature.txt" |
        tr '[:upper:]' '[:lower:]'
)
[[ ${#signer_digests[@]} -gt 0 ]] || {
    printf 'APK signer certificate digest was not reported by apksigner.\n' >&2
    exit 1
}
if [[ -n ${EXPECTED_SIGNER_SHA256:-} ]]; then
    expected_signer_sha256="$(tr -d ':[:space:]' <<< "$EXPECTED_SIGNER_SHA256" | tr '[:upper:]' '[:lower:]')"
    [[ "$expected_signer_sha256" =~ ^[0-9a-f]{64}$ ]] || {
        printf 'EXPECTED_SIGNER_SHA256 must be exactly 64 hexadecimal digits.\n' >&2
        exit 2
    }
    if [[ ${#signer_digests[@]} -ne 1 || ${signer_digests[0]} != "$expected_signer_sha256" ]]; then
        printf 'Signing certificate mismatch: expected %s; found %s.\n' \
            "$expected_signer_sha256" "$(IFS=,; printf '%s' "${signer_digests[*]}")" >&2
        exit 1
    fi
fi

printf 'Package: %s\nVersion: %s (%s)\nSDK: min %s, target %s\nABIs: %s\n' \
    "$actual_package" "$version_name" "$version_code" "$min_sdk" "$target_sdk" "$actual_abis"
printf 'Permissions:\n'
printf '  %s\n' "${actual_permissions[@]}"
printf 'Signer certificate SHA-256: %s\n' "$(IFS=,; printf '%s' "${signer_digests[*]}")"
printf 'Exported production components: MainActivity only; OfflineAiSmokeActivity absent.\n'
printf 'Verified %d embedded ELF files; every LOAD segment is at least 16 KiB aligned.\n' "$elf_count"
tail -n 1 "$work_dir/zipalign.txt"
grep -E '^Verified using v(1|2|3|3\.1|4) scheme' "$work_dir/signature.txt"
