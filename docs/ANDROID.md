# Android build, release, and operation

## Supported configuration

| Item | Selection |
| --- | --- |
| UI architecture | Native .NET for Android views on .NET 10; no MAUI or WebView |
| Target framework | `net10.0-android36.0` |
| Pinned SDK | .NET SDK 10.0.401, Android workload 36.1.69 |
| Android API | minimum 26, target/compile 36 |
| Package/version | `com.grayscaleconsultants.tatapp`, `0.3.0` (`versionCode` 1) |
| Packaged ABIs | `arm64-v8a`, `x86_64` |
| Java/toolchain | JDK 17; Build Tools 36.0.0; NDK 27.0.12077973; CMake 3.22.1 |
| Native inference | LLamaSharp 0.27.0 with llama.cpp commit `3f7c29d318e317b63f54c558bc69803963d7d88c` |

Native .NET for Android was selected because TATAPP already has a WPF UI and a
framework-independent Core library. This keeps Windows functional, shares the
processing/state contracts without introducing a second UI abstraction layer,
and maps lifecycle, `Canvas`, system picker, Storage Access Framework, and
Android accessibility APIs directly.

The Android anatomy renderer projects the shared three-dimensional triangle
meshes onto an Android `Canvas`. The same shared geometry supplies placement,
hit regions, pose, camera framing, and cache identity. This implementation is
not an OpenGL ES renderer.

## Prerequisites

On Linux or WSL install:

- the .NET 10.0.401 SDK and Android workload;
- JDK 17;
- Android SDK Platform 36 and Build Tools 36.0.0;
- Android NDK 27.0.12077973 and CMake 3.22.1;
- `git`, Ninja, `sha256sum`, `unzip`, and ShellCheck for script linting;
- Android platform tools (`adb`) only for device installation/tests.

Typical setup is:

```bash
dotnet workload install android
sdkmanager \
  "platforms;android-36" \
  "build-tools;36.0.0" \
  "platform-tools" \
  "ndk;27.0.12077973" \
  "cmake;3.22.1"

export DOTNET_BIN=/path/to/dotnet
export ANDROID_SDK_ROOT=/path/to/android-sdk
export JAVA_HOME=/path/to/jdk-17
```

The exact SDK is pinned by `global.json`. Use `dotnet workload list` to confirm
the Android workload and `sdkmanager --list_installed` to confirm Android
components.

On Windows, install the same .NET SDK/workload, JDK, and Android SDK components.
The native llama.cpp rebuild script currently targets the Linux NDK host tools,
so run it inside WSL with a Linux Android SDK, leaving its output under the
repository's ignored `artifacts/android/native/` directory. The Windows .NET
build can then consume those generated ABI folders by setting
`TATAPP_LLAMA_NATIVE_DIR` to their Windows path. No binary is committed.

## Restore, native runtime, build, and tests

Build the pinned 16 KiB-page-compatible native inference libraries first:

```bash
ANDROID_SDK_ROOT="$ANDROID_SDK_ROOT" \
  scripts/android/build-llamasharp-native.sh
```

The script fetches the exact llama.cpp commit, performs out-of-tree Release
builds for both ABIs, and verifies all five libraries per ABI: `libggml.so`,
`libggml-base.so`, `libggml-cpu.so`, `libllama.so`, and `libmtmd.so`. It checks
the public symbol contract, required multimodal symbols, SONAMEs, dependencies,
every ELF `LOAD` alignment, and output SHA-256 values. The default output is:

```text
artifacts/android/native/llamasharp-0.27.0/native/android-arm64-v8a/
artifacts/android/native/llamasharp-0.27.0/native/android-x86_64/
```

Run the portable Core tests and build Android when its prerequisites are
available:

```bash
DOTNET_BIN="$DOTNET_BIN" \
ANDROID_SDK_ROOT="$ANDROID_SDK_ROOT" \
JAVA_HOME="$JAVA_HOME" \
  scripts/test.sh
```

Run Android alone, or reproduce its individual commands:

```bash
scripts/build-android.sh

dotnet restore src/TATAPP.Android/TATAPP.Android.csproj \
  -p:AndroidSdkDirectory="$ANDROID_SDK_ROOT" \
  -p:JavaSdkDirectory="$JAVA_HOME"
dotnet build src/TATAPP.Android/TATAPP.Android.csproj \
  --configuration Release \
  --framework net10.0-android36.0 \
  --no-restore \
  -p:AndroidSdkDirectory="$ANDROID_SDK_ROOT" \
  -p:JavaSdkDirectory="$JAVA_HOME"
```

On Windows PowerShell, after the WSL native build:

```powershell
$env:TATAPP_LLAMA_NATIVE_DIR = "C:\path\to\TATAPP\artifacts\android\native\llamasharp-0.27.0\native"
$env:ANDROID_SDK_ROOT = "$env:LOCALAPPDATA\Android\Sdk"
$env:JAVA_HOME = "C:\path\to\jdk-17"

dotnet restore .\src\TATAPP.Android\TATAPP.Android.csproj `
  -p:AndroidSdkDirectory="$env:ANDROID_SDK_ROOT" `
  -p:JavaSdkDirectory="$env:JAVA_HOME"
dotnet build .\src\TATAPP.Android\TATAPP.Android.csproj `
  --configuration Release --framework net10.0-android36.0 --no-restore `
  -p:AndroidSdkDirectory="$env:ANDROID_SDK_ROOT" `
  -p:JavaSdkDirectory="$env:JAVA_HOME"

dotnet restore .\tests\TATAPP.Core.Tests\TATAPP.Core.Tests.csproj
dotnet build .\tests\TATAPP.Core.Tests\TATAPP.Core.Tests.csproj `
  --configuration Release --no-restore
dotnet .\tests\TATAPP.Core.Tests\bin\Release\net10.0\TATAPP.Core.Tests.dll
```

Windows WPF build/test commands remain `scripts\test.ps1` and
`scripts\publish.ps1`; they do not require the Android workload.

The Java instrumentation helper is Bash-based. From Windows, run it inside WSL
with an authorized device forwarded to WSL and the Linux SDK/JDK variables from
the preceding sections. The test APK targets the already built Android package;
there is no Windows-only substitute for its on-device assertions.

## Connected Android instrumentation

Connect one authorized API 26+ `arm64-v8a` device or API 36 `x86_64` emulator,
then provide a test-only external keystore:

```bash
export ANDROID_SERIAL=device-or-emulator-serial
export ANDROID_TEST_KEYSTORE=/outside/repository/evaluation.keystore
export ANDROID_TEST_KEY_ALIAS=evaluation
read -rsp 'Test keystore password: ' ANDROID_TEST_KEYSTORE_PASSWORD; export ANDROID_TEST_KEYSTORE_PASSWORD
read -rsp 'Test key password: ' ANDROID_TEST_KEY_PASSWORD; export ANDROID_TEST_KEY_PASSWORD
scripts/test-android-instrumentation.sh
```

The script builds the target app, compiles the native Java instrumentation and
its test-only document provider, signs it, installs both APKs with
`--no-incremental`, and invokes `am instrument`. It derives the expected body
region count from the shared Core catalog and checks native semantics/focus
order, 48 dp controls, real picker input, defaults, non-default picker/model
synchronization, stage actions and settled status, rapid slider and source
replacement, corrupt-input recovery, exact visible-stage SAF export and SAF
failure, reduced motion, the Offline AI readiness fence, picker/camera
cancellation, deliberate external-link activation, critical-memory recovery,
Back/Resume, non-default state recreation, and a 2x-text/320 dp structural
layout stress. The connected run must still be recorded against the exact final
APK; source compilation alone is not a pass. A runnable verified model and
heartbeat test and human TalkBack speech/Explore by Touch remain explicit skips.
A target may also ignore a requested orientation change; that condition is
reported as a skip rather than a pass. Real maximum system font/display scaling
and visual usability remain human/device gates.

## Image workflow, lifecycle, and export

- API 33+ uses the system photo picker. API 26–32 uses
  `ACTION_OPEN_DOCUMENT` with a read grant. The selected stream is copied into
  app-private storage so the workspace survives a transient URI grant or
  activity recreation.
- **Take photo** sends `ACTION_IMAGE_CAPTURE` to the installed camera with a
  narrowly scoped app-private `FileProvider` URI. TATAPP itself does not need
  camera permission. Canceled and abandoned captures are deleted.
- Inputs are validated by recognized JPEG/PNG/BMP/TIFF/GIF signature, MIME
  compatibility, compressed size (128 MiB maximum), decoded dimensions
  (32 megapixels maximum), and actual platform-decoder success. Recognition is
  not a decoder guarantee—TIFF availability varies by Android device. EXIF
  orientation is normalized and supported embedded profiles are rendered into
  an Android display-space ARGB bitmap.
- Preview input is bounded to 1,600 pixels on its longest side. A 65 ms
  cancellable settle delay prevents rapid slider movement from starting stale
  work. Monotonic generation checks prevent an older import or render from
  replacing a newer selection.
- The stage-frame LRU budget is one eighth of Android's reported memory class,
  clamped to 12–48 MiB. Low-memory/background callbacks clear it and cancel
  render or AI work. Bitmaps, streams, tensors, contexts, media embeddings, and
  model weights have explicit disposal paths; `largeHeap` is not requested.
- Recreation stores only reconstructable URI/path identity, stage and anatomy
  values, camera destination, and an immutable pending-export snapshot. AI
  preprocessing is canceled when the Activity stops and never restores a stale
  result.
- **SAVE current look** snapshots the displayed semantic stage and anatomy
  state before launching `ACTION_CREATE_DOCUMENT`. JPEG uses quality 95 and PNG
  is lossless. Successfully decoded BMP, TIFF, and GIF inputs explicitly fall
  back to a new PNG. A source URI is never silently overwritten. If full-size processing would exceed 45%
  of the memory class, the app exports the bounded high-quality preview and
  says so instead of risking an out-of-memory failure.

## Anatomy and touch operation

The visual model and standard **Body region** picker stay synchronized without
recursive updates. Sighted users may tap a highlighted mesh surface or drag the
model horizontally to rotate it. Everyone can use the Male/Female controls,
Body region picker, Rotate left/right, Zoom in/out, Body size, Skin tone and
complexion, and Reduce anatomy motion controls.

Placement stages initially show useful surrounding anatomy and then move to the
repository-defined centered detail frame. Reduced motion shows the detail state
without animation. TalkBack touch exploration is detected so exploratory touch
does not rotate or select the model; the standard controls remain equivalent.

## Offline AI model policy

Android's catalog revision is `qwen3-vl-2b-q4_0-f16-r2`. It currently contains
one two-artifact candidate:

| Artifact | HTTPS source | Download/installed bytes | SHA-256 |
| --- | --- | ---: | --- |
| `Qwen3-VL-2B-Instruct-Q4_0.gguf` | [Hugging Face](https://huggingface.co/unsloth/Qwen3-VL-2B-Instruct-GGUF/resolve/main/Qwen3-VL-2B-Instruct-Q4_0.gguf) | 1,056,784,064 | `d9ca31f524d063c04e49d1af7b0b37061b21e7f8a7e460141654efe287600234` |
| `mmproj-F16.gguf` | [Hugging Face](https://huggingface.co/unsloth/Qwen3-VL-2B-Instruct-GGUF/resolve/main/mmproj-F16.gguf) | 819,395,232 | `cd5a851d3928697fa1bd76d459d2cc409b6cf40c9d9682b2f5c8e7c6a9f9630f` |

Both HTTPS URLs and digests are in
`src/TATAPP.Android/Resources/raw/offline_ai_models.json`. The combined
download/installed size is 1,876,179,296 bytes. Admission reserves space for
download and installed copies plus 536,870,912 bytes, requiring 4,289,229,504
bytes free. The candidate requires API 26, `arm64-v8a` or `x86_64`, CPU
execution, a 201,326,592-byte memory class, and at least 4,294,967,296 bytes of
currently available memory. Its declared working set is 2,684,354,560 bytes
plus an 805,306,368-byte reserve (3,489,660,928 bytes); the separate 4 GiB
minimum is therefore the effective preload threshold. The shipping manifest
declares a 768-pixel input cap, 2,048 context tokens, 160 output tokens, four CPU
threads, and 512 visual tokens. The current in-process runtime applies an
additional output limit of 96 tokens. `largeMemoryClass` is observed but does not relax admission and the app
does not request `largeHeap`.

The candidate is `tested: true` based on one bounded two-call Qwen3-VL runtime
smoke with the exact checksum-verified model and projector. On an API 36 x86_64
emulator with 16 KiB pages, it described a synthetic 192-by-192 black-ring image
at the Original image stage with an accurate, nonempty result. The offline run
took approximately eight minutes, peaked near 2.17 GB proportional set size,
released inference memory afterward, and completed without OOM or crash.

The test used the opt-in `TatappEnableOfflineAiSmoke=true` Activity with its
generated fixture. That Activity is linked only into an explicit smoke build
and is absent from normal distributable builds.

That evidence is intentionally narrow. It does not validate ARM64 inference, a
real user photograph, all 16 stages, repeated sessions, cancellation, Activity
recreation, thermal/battery behavior, description quality across tattoo styles,
a physical device, or TalkBack. The `tested` flag makes the candidate eligible
for policy evaluation; selection still requires every live API, ABI, ordinary
memory-class, current-memory-pressure, storage, CPU, and acceleration gate.
The current catalog has no smaller tier, so it has no model fallback candidate.
If admission or inference fails, TATAPP explains the failure and returns to its
fully functional non-AI editing workflow rather than risking memory exhaustion.

When a tested entry is eligible, installation requires an affirmative
dialog showing total size. HTTP range requests resume `.partial` files in
app-private storage. Each declared byte length and SHA-256 must match before
the staging directory is atomically promoted; incomplete or invalid sets are
never reported ready. Preloading opens one model session, processes every
current catalog stage strictly in sequence with renewed memory checks, commits
the description cache only as a complete batch, and disposes media/tensor/model
resources on cancellation or completion. The heartbeat runs only during
foreground preprocessing, with textual progress and one final ready/error
status. No photograph or generated description is transmitted.

Model files and GGUF archives must not be committed or included in an APK.
Review their Apache-2.0 terms at the linked upstream repositories in
`THIRD_PARTY_NOTICES.md` before enabling or distributing a catalog entry.

## Signing and release APK

Keep keystores and passwords outside the repository. The release helper passes
passwords to Android signing through environment-variable references rather
than embedding their values in project files or command history:

```bash
export TATAPP_ANDROID_KEYSTORE=/outside/repository/tatapp-evaluation.keystore
export TATAPP_ANDROID_KEY_ALIAS=tatapp-evaluation
read -rsp 'Keystore password: ' TATAPP_ANDROID_KEYSTORE_PASSWORD; export TATAPP_ANDROID_KEYSTORE_PASSWORD
read -rsp 'Key password: ' TATAPP_ANDROID_KEY_PASSWORD; export TATAPP_ANDROID_KEY_PASSWORD

scripts/publish-android.sh
```

On Windows PowerShell, set the same four `TATAPP_ANDROID_*` variables and run:

```powershell
$publish = ".\artifacts\android\.publish-windows"
dotnet publish .\src\TATAPP.Android\TATAPP.Android.csproj `
  --configuration Release --framework net10.0-android36.0 `
  --output $publish `
  -p:AndroidSdkDirectory="$env:ANDROID_SDK_ROOT" `
  -p:JavaSdkDirectory="$env:JAVA_HOME" `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore="$env:TATAPP_ANDROID_KEYSTORE" `
  -p:AndroidSigningKeyAlias="$env:TATAPP_ANDROID_KEY_ALIAS" `
  -p:AndroidSigningStorePass="env:TATAPP_ANDROID_KEYSTORE_PASSWORD" `
  -p:AndroidSigningKeyPass="env:TATAPP_ANDROID_KEY_PASSWORD"
Copy-Item "$publish\com.grayscaleconsultants.tatapp-Signed.apk" `
  ".\artifacts\android\TATAPP-0.3.0-evaluation-arm64-x86_64.apk"
Get-FileHash `
  ".\artifacts\android\TATAPP-0.3.0-evaluation-arm64-x86_64.apk" `
  -Algorithm SHA256
```

Run `scripts/android/verify-apk.sh` from WSL against that exact copied APK
before distribution. Do not put password values into `.csproj`, `.props`, a
shell script, or a committed signing-properties file.

The helper builds, signs, verifies, and writes:

```text
artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk
artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk.sha256
```

The verifier checks exact package/version/SDK metadata, exact ABI coverage, the
exact permission set (`INTERNET` only), MainActivity as the sole exported
production component, absence of the opt-in smoke Activity, a byte-exact copy
of the current shipping model catalog, all five offline-AI libraries per ABI,
every packaged native library's ELF load alignment, 16 KiB ZIP alignment, and
APK v2/v3 signature verification:

```bash
scripts/android/verify-apk.sh \
  artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk
```

Install without incremental deployment so native mappings come from the final
APK rather than `incfs`:

```bash
adb -s "$ANDROID_SERIAL" install --no-incremental -r \
  artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk
```

For upgrades, retain the same signing identity, increment `versionCode`, and
use `-r`. Changing the signing identity requires uninstalling the prior app and
therefore removes its app-private imports and downloaded models.

## Privacy and permissions

The production manifest declares only `android.permission.INTERNET`, required
solely for a future consented HTTPS model installation. System pickers and URI
grants avoid camera and broad storage permissions. TATAPP includes no ads,
tracking, analytics, cloud inference, background upload, exported content
provider, or ordinary-editing network dependency. The launcher Activity is the
only exported production component. Imported images, temporary camera files,
model partials, verified models, and description caches remain app-private;
user-selected exports go only to the destination chosen in the system document
UI.

## Troubleshooting

- **Missing native runtime:** run `scripts/android/build-llamasharp-native.sh`
  or set `TATAPP_LLAMA_NATIVE_DIR` to a validated directory containing both
  `android-arm64-v8a` and `android-x86_64` folders.
- **SDK/workload error:** confirm .NET 10.0.401, the Android workload, API 36,
  Build Tools 36.0.0, JDK 17, and the exported SDK/JAVA paths.
- **16 KiB verification failure:** do not use LLamaSharp's stock Android native
  package. Rebuild from the pinned llama.cpp source with the repository script.
- **Picker/camera unavailable:** install or enable a system document provider
  or camera app. The current workspace remains intact after cancellation.
- **Unrecognized or undecodable image:** signature recognition and decode are
  separate gates. Some Android builds cannot decode TIFF; the prior workspace
  remains available after a decoder failure.
- **Save format changed:** Android supports JPEG/PNG export here; successfully
  decoded BMP/TIFF/GIF inputs deliberately create a new PNG and state the fallback.
- **Offline AI unavailable:** the tested entry is still rejected when API, ABI,
  memory pressure, storage, CPU, or acceleration gates fail. Ordinary editing
  is unaffected.
- **Installation fails on a 16 KiB emulator:** use `adb install
  --no-incremental`; then run `verify-apk.sh` and inspect `adb logcat` rather
  than raising heap limits.

## Current limitations

- Offline AI has one successful two-call synthetic original-stage Qwen3-VL
  x86_64 emulator smoke, but ARM64 inference, full 16-stage preload, thermal,
  cancellation, recreation, repeated-inference, description-quality, and
  TalkBack behavior remain unverified. Installing and resuming the ordinary app
  on ARM64 hardware did not exercise inference.
- Android emits JPEG and PNG. It recognizes BMP, TIFF, and GIF inputs, but
  actual import depends on the device's platform decoder (notably for TIFF);
  successfully decoded inputs use the explicit new-file PNG export fallback.
- The anatomy view is a deterministic Canvas projection of the shared generated
  mesh. It provides contour-aware placement and shading within that geometry;
  it is not photorealistic skin simulation or clinical placement guidance.
- Native instrumentation verifies the test-provider SAF path and the Android UI
  contracts listed below. A separate manual emulator pass exercised picker,
  lifecycle, layout, and memory scenarios. Human TalkBack, visual rendering,
  physical-device interaction, the loaded-model integrated UI, and a true
  version-to-version upgrade remain unverified.
- Only 64-bit ARM and x86 Android targets are packaged. There is no 32-bit ARM
  or x86 build; an `armeabi-v7a` watch is therefore unsupported.

## Release evidence record

This record applies to the Android port based on commit `b29bb63`. Automated
instrumentation used the API 36 x86_64 Release build from the same final source;
the multi-ABI evaluation APK was separately verified, installed, and launched.
No row implies human TalkBack or visual-quality validation.

| Gate | Exact command/evidence | Final result |
| --- | --- | --- |
| Solution and Core regression | Full solution Release build with warnings as errors; portable Core harness | Pass: 0 warnings, 0 errors; Core 44/44 |
| Windows regression | WPF Release compilation on Linux | Compile pass: 0 warnings, 0 errors. Runtime tests were unavailable because Linux lacks `Microsoft.WindowsDesktop.App` 10.0. |
| Native runtime packaging | Final APK plus `scripts/android/verify-apk.sh` | Pass: required runtime libraries are present for both packaged ABIs; 132 packaged ELF files are 16 KiB aligned. |
| Android Release build, warnings as errors | Full solution Release build | Pass: 0 warnings, 0 errors. |
| APK metadata, ABIs, permissions, alignment, signature | `scripts/android/verify-apk.sh artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk` | Pass: package `com.grayscaleconsultants.tatapp`, version 0.3.0/1, min 26, target 36, `arm64-v8a` + `x86_64`, only `INTERNET`, 132/132 ELF files 16 KiB aligned, APK v2/v3 verified. |
| Connected Java instrumentation | API 36 x86_64 16 KiB emulator; `scripts/test-android-instrumentation.sh` | Pass: 27 passed, 0 failed, 2 skipped. The stage-debounce/low-memory label-and-export regression passed; skips were loaded-model integrated UI/heartbeat and human TalkBack. |
| Exact-model offline smoke | API 36 x86_64 16 KiB emulator; synthetic 192 px Original image | Pass: actual Qwen3-VL two-call inference produced accurate nonempty black-ring output, took about 8 minutes, peaked near 2.17 GB PSS, and released inference memory. This is not a full 16-stage preload or ARM64 inference result. |
| Final APK install and launch | API 36 x86_64 emulator; non-incremental install, cold launch offline, and same-version `-r` install | Pass. The `-r` result validates reinstall mechanics, not migration from an older `versionCode`. |
| ARM64 installation | API 32 Rokid device; fresh install and resumed Activity | Pass for installation and Activity resumption only; no human visual or accessibility validation was performed. |
| Other available Android targets | API 36 Poco; `armeabi-v7a` watch | Poco install blocked externally with `INSTALL_FAILED_USER_RESTRICTED`; the 32-bit watch is outside the documented ABI range. |
| Manual image/lifecycle exercise | API 36 x86_64 emulator | Pass within observed scope: system picker loaded a 256x256 PNG and 6000x4000 JPEG; stage 2 settled ready; rotation retained stage 12 anatomy; background/resume and a background process kill restored the workspace; 2x-font start screen remained usable; system DocumentsUI saved the selected 256x256 Original image as a new PNG; deliberate BLACK WIDOW TATTOO activation opened the exact HTTPS URL and Back restored the workspace. |
| Managed/native memory observations | API 36 x86_64 emulator PSS samples | About 50 MB cold, 61 MB after the small image, 74 MB after the large source, 108 MB after anatomy/rotation/resume, 111 MB after repeated small import, then 90 MB after background trim. These samples are not an ARM64 or long-running inference profile. |
| Human TalkBack script | `docs/ACCESSIBILITY.md` | Unverified: no person performed the speech-quality and Explore-by-Touch script on the final APK. |
| Final APK size and SHA-256 | `stat`; `sha256sum` | `artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk`, 20,128,543 bytes, SHA-256 `c84dd4cd5c0383577494ec580443f848b43e941f45c887820d6718057cec5ed2`. |
