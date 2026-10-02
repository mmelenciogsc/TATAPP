#!/usr/bin/env bash

set -Eeuo pipefail
export LC_ALL=C

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/../.." && pwd -P)"
readonly script_dir repo_root

# shellcheck disable=SC1091 # Resolved from this script's canonical directory.
. "$script_dir/release-config.sh"

project="$repo_root/src/TATAPP.Android/TATAPP.Android.csproj"
manifest="$repo_root/src/TATAPP.Android/Properties/AndroidManifest.xml"
instrumentation_manifest="$repo_root/tests/TATAPP.Android.Instrumentation/AndroidManifest.xml"

xml_element() {
    local element=$1 file=$2
    sed -n "s:.*<$element>\([^<]*\)</$element>.*:\1:p" "$file"
}

xml_attribute() {
    local attribute=$1 file=$2
    sed -n "s/.*$attribute=\"\([^\"]*\)\".*/\1/p" "$file"
}

require_equal() {
    local label=$1 expected=$2 actual=$3
    [[ "$actual" == "$expected" ]] || {
        printf '%s mismatch: expected %s, found %s.\n' "$label" "$expected" "${actual:-<missing>}" >&2
        exit 1
    }
}

require_equal 'Android package ID' "$TATAPP_ANDROID_PACKAGE_ID" \
    "$(xml_element ApplicationId "$project")"
require_equal 'Android project versionCode' "$TATAPP_ANDROID_VERSION_CODE" \
    "$(xml_element ApplicationVersion "$project")"
require_equal 'Android project versionName' "$TATAPP_ANDROID_VERSION_NAME" \
    "$(xml_element ApplicationDisplayVersion "$project")"
require_equal 'Production manifest versionCode' "$TATAPP_ANDROID_VERSION_CODE" \
    "$(xml_attribute android:versionCode "$manifest")"
require_equal 'Production manifest versionName' "$TATAPP_ANDROID_VERSION_NAME" \
    "$(xml_attribute android:versionName "$manifest")"
require_equal 'Instrumentation manifest versionCode' "$TATAPP_ANDROID_VERSION_CODE" \
    "$(xml_attribute android:versionCode "$instrumentation_manifest")"
require_equal 'Instrumentation manifest versionName' "$TATAPP_ANDROID_VERSION_NAME" \
    "$(xml_attribute android:versionName "$instrumentation_manifest")"
require_equal 'Android artifact basename' \
    "TATAPP-${TATAPP_ANDROID_VERSION_NAME}-evaluation-arm64-x86_64.apk" \
    "$TATAPP_ANDROID_ARTIFACT_BASENAME"

printf 'PASS: Android release contract %s (%s), %s\n' \
    "$TATAPP_ANDROID_VERSION_NAME" "$TATAPP_ANDROID_VERSION_CODE" \
    "$TATAPP_ANDROID_ARTIFACT_BASENAME"
