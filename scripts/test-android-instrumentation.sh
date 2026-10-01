#!/usr/bin/env bash
set -Eeuo pipefail
export LC_ALL=C

repo_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
dotnet_bin=${DOTNET_BIN:-$(command -v dotnet 2>/dev/null || true)}
android_sdk=${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}
java_home=${JAVA_HOME:-}
device_serial=${ANDROID_SERIAL:-}

[[ -x "$dotnet_bin" ]] || { printf 'Set DOTNET_BIN to the .NET 10.0.401 dotnet executable.\n' >&2; exit 2; }
"$dotnet_bin" --list-sdks 2>/dev/null | grep -Eq '^10\.0\.401([[:space:]]|$)' || {
  printf 'DOTNET_BIN does not provide the pinned .NET 10.0.401 SDK.\n' >&2
  exit 2
}
[[ -n "$android_sdk" && -d "$android_sdk" ]] || {
  printf 'Set ANDROID_SDK_ROOT (or ANDROID_HOME) to the Android SDK.\n' >&2
  exit 2
}
[[ -n "$java_home" && -x "$java_home/bin/javac" ]] || {
  printf 'Set JAVA_HOME to a JDK 17 installation.\n' >&2
  exit 2
}
"$java_home/bin/javac" -version 2>&1 | grep -Eq '^javac 17([.]|$)' || {
  printf 'JAVA_HOME must provide JDK 17.\n' >&2
  exit 2
}
adb_bin="$android_sdk/platform-tools/adb"
[[ -x "$adb_bin" ]] || { printf 'Android platform-tools adb was not found.\n' >&2; exit 2; }

if [[ -z "$device_serial" ]]; then
  device_serial=$($adb_bin devices | awk 'NR > 1 && $2 == "device" { print $1; exit }')
fi
if [[ -z "$device_serial" ]]; then
  printf 'No authorized Android target is connected.\n' >&2
  exit 2
fi
device_abi=$($adb_bin -s "$device_serial" shell getprop ro.product.cpu.abi | tr -d '\r')
case "$device_abi" in
  arm64-v8a) app_rid=android-arm64 ;;
  x86_64) app_rid=android-x64 ;;
  *)
    printf 'Unsupported target ABI %s; expected arm64-v8a or x86_64.\n' "$device_abi" >&2
    exit 2
    ;;
esac

export ANDROID_HOME="$android_sdk"
export ANDROID_SDK_ROOT="$android_sdk"
export JAVA_HOME="$java_home"

app_project="$repo_root/src/TATAPP.Android/TATAPP.Android.csproj"
default_app_apk="$repo_root/src/TATAPP.Android/bin/Release/net10.0-android36.0/$app_rid/com.grayscaleconsultants.tatapp-Signed.apk"
app_apk_override=${ANDROID_APP_APK:-}
app_apk=${app_apk_override:-$default_app_apk}
test_root="$repo_root/tests/TATAPP.Android.Instrumentation"
test_build="$test_root/obj/Release/native"
test_bin="$test_root/bin/Release"
android_jar="$android_sdk/platforms/android-36/android.jar"
build_tools="$android_sdk/build-tools/36.0.0"
keystore=${ANDROID_TEST_KEYSTORE:-}
: "${ANDROID_TEST_KEYSTORE:?Set ANDROID_TEST_KEYSTORE to an external test keystore.}"
: "${ANDROID_TEST_KEYSTORE_PASSWORD:?Set ANDROID_TEST_KEYSTORE_PASSWORD for the external evaluation keystore.}"
key_alias=${ANDROID_TEST_KEY_ALIAS:-androiddebugkey}
key_password=${ANDROID_TEST_KEY_PASSWORD:-$ANDROID_TEST_KEYSTORE_PASSWORD}
export ANDROID_TEST_KEY_PASSWORD="$key_password"
[[ -f "$keystore" ]] || { printf 'External test keystore not found: %s\n' "$keystore" >&2; exit 2; }

if [[ -z "$app_apk_override" && "${ANDROID_SKIP_APP_BUILD:-0}" != "1" ]]; then
  "$dotnet_bin" restore "$app_project" \
    -p:AndroidSdkDirectory="$android_sdk" -p:JavaSdkDirectory="$java_home"
  "$dotnet_bin" build "$app_project" -c Release -f net10.0-android36.0 -r "$app_rid" --no-restore \
    -p:AndroidSdkDirectory="$android_sdk" -p:JavaSdkDirectory="$java_home"
fi

if [[ ! -f "$app_apk" ]]; then
  printf 'Expected app APK was not found: %s\n' "$app_apk" >&2
  exit 1
fi
if [[ -n "$app_apk_override" ]]; then
  printf 'Using explicit prebuilt app APK without rebuilding it: %s\n' "$app_apk"
  ANDROID_SDK_ROOT="$android_sdk" "$repo_root/scripts/android/verify-apk.sh" "$app_apk"
fi

rm -rf "$test_build" "$test_bin"
mkdir -p "$test_build/classes" "$test_build/dex" "$test_bin"
mapfile -d '' java_sources < <(find "$test_root/src" -type f -name '*.java' -print0)
[[ ${#java_sources[@]} -gt 0 ]] || { printf 'No instrumentation Java sources were found.\n' >&2; exit 1; }
"$java_home/bin/javac" --release 8 -encoding UTF-8 -Xlint:all \
  -classpath "$android_jar" -d "$test_build/classes" "${java_sources[@]}"
mapfile -d '' class_files < <(find "$test_build/classes" -type f -name '*.class' -print0)
[[ ${#class_files[@]} -gt 0 ]] || { printf 'Instrumentation compilation produced no class files.\n' >&2; exit 1; }
"$build_tools/d8" --min-api 26 --lib "$android_jar" --output "$test_build/dex" \
  "${class_files[@]}"
"$build_tools/aapt2" link --manifest "$test_root/AndroidManifest.xml" -I "$android_jar" \
  --min-sdk-version 26 --target-sdk-version 36 -o "$test_build/test-unaligned.apk"
(cd "$test_build/dex" && zip -q -j "$test_build/test-unaligned.apk" classes.dex)
"$build_tools/zipalign" -f 4 "$test_build/test-unaligned.apk" "$test_build/test-unsigned.apk"
"$build_tools/apksigner" sign --ks "$keystore" --ks-key-alias "$key_alias" \
  --ks-pass env:ANDROID_TEST_KEYSTORE_PASSWORD --key-pass env:ANDROID_TEST_KEY_PASSWORD \
  --out "$test_bin/com.grayscaleconsultants.tatapp.instrumentation.apk" "$test_build/test-unsigned.apk"
"$build_tools/apksigner" verify --verbose "$test_bin/com.grayscaleconsultants.tatapp.instrumentation.apk"

test_apk="$test_bin/com.grayscaleconsultants.tatapp.instrumentation.apk"

# Incremental installs can SIGBUS in the Android linker on 16 KB-page
# emulators while a mapped native library is still backed by incfs.
$adb_bin -s "$device_serial" install --no-incremental -r "$app_apk"
$adb_bin -s "$device_serial" install --no-incremental -t -r "$test_apk"

# Supplemental source/package contracts. On-device semantic assertions below
# remain authoritative for the behavior they exercise.
aapt_bin="$android_sdk/build-tools/36.0.0/aapt"
permission_dump=$($aapt_bin dump permissions "$app_apk")
if rg -q 'android.permission.(CAMERA|READ_EXTERNAL_STORAGE|WRITE_EXTERNAL_STORAGE|READ_MEDIA_IMAGES)' <<<"$permission_dump"; then
  printf 'Unexpected broad or runtime media permission in app APK.\n' >&2
  exit 1
fi
rg -q 'AccessibilityLiveRegion = AccessibilityLiveRegion.Polite' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'SetMinimumHeight\(Dp\(48\)\)' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'PickPhotoRequest = 1001' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'TakePhotoRequest = 1002' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'mediaLauncher = services.GetRequiredService<IAndroidMediaLauncher>\(\)' \
  "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'mediaLauncher.LaunchPhotoPicker\(this, PickPhotoRequest\)' \
  "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'mediaLauncher.LaunchCamera\(this, output, TakePhotoRequest\)' \
  "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'AddSingleton<IAndroidMediaLauncher, AndroidMediaLauncher>' \
  "$repo_root/src/TATAPP.Android/TatappApplication.cs"
rg -Uq '(?s)public void LaunchPhotoPicker\(Activity activity, int requestCode\).*?activity\.StartActivityForResult\(intent, requestCode\);' \
  "$repo_root/src/TATAPP.Android/AndroidPlatformServices.cs"
rg -Uq '(?s)public void LaunchCamera\(Activity activity, Uri output, int requestCode\).*?activity\.StartActivityForResult\(intent, requestCode\);' \
  "$repo_root/src/TATAPP.Android/AndroidPlatformServices.cs"
rg -q 'AddTransient<OfflineAiService>' "$repo_root/src/TATAPP.Android/TatappApplication.cs"
rg -q 'RoleManager.RoleBrowser' "$repo_root/src/TATAPP.Android/AndroidPlatformServices.cs"
rg -q 'BrowserProbeUrl = "https://www.example.com/"' "$repo_root/src/TATAPP.Android/AndroidPlatformServices.cs"
rg -q 'intent.SetPackage(browserPackage)' "$repo_root/src/TATAPP.Android/AndroidPlatformServices.cs"
rg -q 'No default web browser is configured' "$repo_root/src/TATAPP.Android/AndroidPlatformServices.cs"
if rg -q 'com[.]android[.]chrome' "$repo_root/src/TATAPP.Android/AndroidPlatformServices.cs"; then
  printf 'The external-link launcher must not hardcode a browser package.\n' >&2
  exit 1
fi
if rg -q 'AddSingleton<OfflineAiService>' "$repo_root/src/TATAPP.Android/TatappApplication.cs"; then
  printf 'OfflineAiService must be Activity-owned, not a process-lifetime singleton.\n' >&2
  exit 1
fi
rg -q 'LLama.Exceptions.RuntimeError' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'catch \(OutOfMemoryException exception\)' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'Cancel offline AI' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'if \(aiWorkActive\)' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'CoalescingModelInstallProgress' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'SetOnApplyWindowInsetsListener' "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -q 'capture = anatomy.CapturePlacement\(preparedTattoo!, anatomySnapshot, 1f\)' \
  "$repo_root/src/TATAPP.Android/MainActivity.cs"
rg -Uq '(?s)public Bitmap CapturePlacement\(Bitmap preparedTattoo, AnatomicalWorkflowState placementState,.*?finally.*?tattoo = previousTattoo;.*?state = previousState;.*?placementVisible = previousPlacementVisible;.*?focusProgress = previousFocus;' \
  "$repo_root/src/TATAPP.Android/AnatomyView.cs"
rg -q 'ApplyRotation\(delta \* 0.45, notifySettled: false\)' "$repo_root/src/TATAPP.Android/AnatomyView.cs"

runner='com.grayscaleconsultants.tatapp.instrumentation/com.grayscaleconsultants.tatapp.instrumentation.TatappInstrumentation'
expected_region_count=$(sed -n \
  '/public static IReadOnlyList<BodyRegionDefinition> All/,/^    ];/p' \
  "$repo_root/src/TATAPP.Core/AnatomyProfile.cs" | rg -c 'new\(BodyRegionKind\.')
[[ "$expected_region_count" =~ ^[1-9][0-9]*$ ]] || {
  printf 'Could not derive the body-region count from the shared catalog.\n' >&2
  exit 1
}
output=$($adb_bin -s "$device_serial" shell am instrument -w \
  -e expectedRegionCount "$expected_region_count" "$runner")
printf '%s\n' "$output"
rg -q 'RESULT: [0-9][0-9]* passed, 0 failed' <<<"$output"
if rg -q 'INSTRUMENTATION_CODE:' <<<"$output"; then
  rg -q 'INSTRUMENTATION_CODE: -1' <<<"$output"
fi
