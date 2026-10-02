# Android build, release, and operation

## Supported configuration

| Item | Selection |
| --- | --- |
| UI architecture | Native .NET for Android views on .NET 10; no MAUI or WebView |
| Target framework | `net10.0-android36.0` |
| Pinned SDK | .NET SDK 10.0.401, Android workload 36.1.69 |
| Android API | minimum 26, target/compile 36 |
| Package/version | `com.grayscaleconsultants.tatapp`, `0.3.1` (`versionCode` 2) |
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
heartbeat test is reported as a skip when the target has no installed verified
model; the later Poco run described below separately exercised that production
path. Human TalkBack speech/Explore by Touch remains an explicit skip. A target
may also ignore a requested orientation change; that condition is reported as a
skip rather than a pass. Real maximum system font/display scaling and visual
usability remain human/device gates.

To exercise the exact externally signed evaluation APK, set an explicit path:

```bash
export ANDROID_APP_APK="$PWD/artifacts/android/TATAPP-0.3.1-evaluation-arm64-x86_64.apk"
scripts/test-android-instrumentation.sh
```

When `ANDROID_APP_APK` is set, the helper does not rebuild or substitute the app
APK. It runs the full production APK verifier on that file before compiling the
test package or installing either package. Without the override, the existing
RID-specific build path and `ANDROID_SKIP_APP_BUILD` behavior are unchanged.

## Accessible walkthrough media

The reproducible edit contract, source provenance, offline-AI claim boundary,
and human review gates are documented in
[the Android walkthrough guide](../media/android-walkthrough/README.md).

From the repository root, validate inputs, render, and run the review verifier:

```bash
scripts/render-android-walkthrough.py --validate-only
scripts/render-android-walkthrough.py
scripts/verify-android-walkthrough.py --review
```

Generated handoff files are ignored by Git and have fixed paths:

```text
artifacts/android-walkthrough/final/TATAPP-Android-accessible-walkthrough.mp4
artifacts/android-walkthrough/final/TATAPP-Android-accessible-walkthrough-SASRT.srt
artifacts/android-walkthrough/final/FINAL-MEDIA-MANIFEST.json
```

The non-review verifier intentionally fails until the manifest's human gates
represent checks that a person actually completed. The render and verifier
never set those gates to true.

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

The candidate was initially marked `tested: true` based on one bounded two-call
Qwen3-VL runtime smoke with the exact checksum-verified model and projector. On
an API 36 x86_64 emulator with 16 KiB pages, it described a synthetic
192-by-192 black-ring image at the Original image stage with an accurate,
nonempty result. The offline run took approximately eight minutes, peaked near
2.17 GB proportional set size, released inference memory afterward, and
completed without OOM or crash.

The test used the opt-in `TatappEnableOfflineAiSmoke=true` Activity with its
generated fixture. That Activity is linked only into an explicit smoke build
and is absent from normal distributable builds.

A prior production-UI build on an API 36 ARM64 Poco F7 Ultra downloaded and
checksum-verified the exact catalog model and projector, then completed all 16
model-generated descriptions. Wi-Fi and mobile data were disabled from stage 6 through stage 16.
During preprocessing the stage controls remained disabled; completion published
`Offline descriptions are ready for all 16 stages.`, stopped the heartbeat, and
made descriptions immediately available at the captured stages 1, 8, 9, and
16. The process did not OOM. PSS was approximately 2.4 GB while inference was
active and approximately 302 MB after model-session disposal.

The current v5 source contract invokes Qwen exactly once on Original, disposes
the input and model session, then constructs all 16 descriptions from
authoritative stage metadata and exact anatomical renderer state. The unverified
source observation remains available only in the Original description; it is
not repeated or treated as evidence about any transformed stage, and the app
does not claim Qwen inspected those renders. It commits only a complete cache, stops
the heartbeat before deterministic text construction, restores controls, and
publishes one readiness update through the sole polite status region. This
revised path remains pending device and human TalkBack verification.

The installed Machine Perception Node could not be reused because its inference
service and model files are private/non-exported and its package is signed by a
different identity. TATAPP did not bypass that boundary: the full run used
TATAPP's own in-process LLamaSharp/llama.cpp CPU runtime entirely on the Poco.

This establishes one physical ARM64 full-batch workflow, not repeated-session,
cancellation/recreation, thermal/battery, broad tattoo-style quality, or human
TalkBack results. The observed anatomical-stage descriptions were weak. The
`tested` flag makes the candidate eligible for policy evaluation; selection
still requires every live API, ABI, ordinary memory-class,
current-memory-pressure, storage, CPU, and acceleration gate. The current
catalog has no smaller tier, so it has no model fallback candidate. If admission
or inference fails, TATAPP explains the failure and returns to its fully
functional non-AI editing workflow rather than risking memory exhaustion.

When a tested entry is eligible, installation requires an affirmative
dialog showing total size. HTTP range requests resume `.partial` files in
app-private storage. Each declared byte length and SHA-256 must match before
the staging directory is atomically promoted; incomplete or invalid sets are
never reported ready. Preloading opens one model session, performs one bounded
Original-image inference with renewed memory checks, disposes media/tensor/model
resources, then constructs every current catalog description in strict order
and commits only the complete batch. The Android heartbeat runs through
`USAGE_ASSISTANCE_ACCESSIBILITY` and `CONTENT_TYPE_SONIFICATION` so it follows
the independently controlled accessibility volume. One four-second static loop
contains about 900 ms of pre-roll, the shared 420 ms two-note pulse amplified by
a bounded Android-only 2.5x PCM gain (about -12 dBFS), and trailing silence. The
player verifies its write, loop, seek, and playing results, permits one bounded
recreation retry, and never requests audio focus or changes volume, DND, or
sound settings. It is active only during foreground Original-image preprocessing
and inference—not deterministic description construction—and releases
synchronously on every terminal/lifecycle path. **Test processing heartbeat**
exercises that exact player for three pulses in about ten seconds without
requiring a model; its Stop state and Back cancel immediately, and it cannot
overlap Offline AI. Textual progress and the sole polite status region remain
authoritative if audio is unavailable. No photograph or generated description
is transmitted.

The remediation above followed a Poco observation where media volume was 0,
accessibility volume was 10/15, DND was off, and the former short music-stream
timer pulse was not audible. On 2026-10-02, the revised signed APK
(20,198,175 bytes; SHA-256
`7e9469cdf2c8f87e52c6b5fce8d546f14159eb5358d601b7c3f1d3c095715a77`)
replacement-installed on the Poco F7 Ultra and its installed `base.apk` matched
byte-for-byte. With Google TalkBack enabled and accessibility volume at 10/15,
the user heard all three two-tone pulses across about ten seconds and TalkBack
announced the test start and completion. This validates the direct
production-path heartbeat test only, not a fresh full 16-stage AI run or its
readiness announcement.

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
  ".\artifacts\android\TATAPP-0.3.1-evaluation-arm64-x86_64.apk"
Get-FileHash `
  ".\artifacts\android\TATAPP-0.3.1-evaluation-arm64-x86_64.apk" `
  -Algorithm SHA256
```

Run `scripts/android/verify-apk.sh` from WSL against that exact copied APK
before distribution. Do not put password values into `.csproj`, `.props`, a
shell script, or a committed signing-properties file.

The helper builds in a unique isolated artifacts tree so a previously signed
APK cannot satisfy an incremental publish. Before promotion it derives the
SHA-256 certificate digest from the supplied external keystore and requires
the APK's sole signer to match it. It then verifies and atomically replaces:

```text
artifacts/android/TATAPP-0.3.1-evaluation-arm64-x86_64.apk
artifacts/android/TATAPP-0.3.1-evaluation-arm64-x86_64.apk.sha256
```

The verifier checks exact package/version/SDK metadata, exact ABI coverage, the
exact permission set (`INTERNET` only), MainActivity as the sole exported
production component, absence of the opt-in smoke Activity, a byte-exact copy
of the current shipping model catalog, all five offline-AI libraries per ABI,
every packaged native library's ELF load alignment, 16 KiB ZIP alignment, and
APK v2/v3 signature verification:

```bash
scripts/android/verify-apk.sh \
  artifacts/android/TATAPP-0.3.1-evaluation-arm64-x86_64.apk
```

Install without incremental deployment so native mappings come from the final
APK rather than `incfs`:

```bash
adb -s "$ANDROID_SERIAL" install --no-incremental -r \
  artifacts/android/TATAPP-0.3.1-evaluation-arm64-x86_64.apk
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
only exported production component. Its sole package-visibility query is a
generic browsable HTTPS intent used to resolve the configured default browser;
no browser package is hardcoded. Imported images, temporary camera files, model
partials, verified models, and description caches remain app-private;
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

- Offline AI has both the original two-call synthetic x86_64 smoke and one
  physical ARM64 Poco full 16-stage production-UI preload. The latter completed
  without OOM and released most inference memory, but repeated inference,
  cancellation/recreation, and thermal/battery endurance remain unverified.
  Anatomical-stage description quality was weak, and no human listener
  completed the full TalkBack script.
- Android emits JPEG and PNG. It recognizes BMP, TIFF, and GIF inputs, but
  actual import depends on the device's platform decoder (notably for TIFF);
  successfully decoded inputs use the explicit new-file PNG export fallback.
- The anatomy view is a deterministic Canvas projection of the shared generated
  mesh. It provides contour-aware placement and shading within that geometry;
  it is not photorealistic skin simulation or clinical placement guidance.
- Native instrumentation verifies the test-provider SAF path and the Android UI
  contracts listed below. Separate emulator and Poco passes exercised picker,
  lifecycle, layout, memory, and the loaded-model integrated UI within the
  recorded scopes. Human TalkBack, broad visual/description quality, and a true
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
| Prior signed 0.3.0 APK metadata, ABIs, permissions, alignment, signature | `scripts/android/verify-apk.sh artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk` | Historical pass: package `com.grayscaleconsultants.tatapp`, version 0.3.0/1, min 26, target 36, `arm64-v8a` + `x86_64`, only `INTERNET`, 132/132 ELF files 16 KiB aligned, APK v2/v3 verified. The current 0.3.1/2 artifact requires a fresh signed publish and verification. |
| Connected Java instrumentation | API 36 x86_64 16 KiB emulator; `scripts/test-android-instrumentation.sh` | Historical pass: 27 passed, 0 failed, 2 skipped. The stage-debounce/low-memory label-and-export regression passed; historical skips were loaded-model integrated UI/heartbeat and human TalkBack. On 2026-10-02 the target app installed on the Poco, but HyperOS blocked the temporary harness install with `INSTALL_FAILED_USER_RESTRICTED`; no new harness pass is claimed. |
| Exact-model offline smoke | API 36 x86_64 16 KiB emulator; synthetic 192 px Original image | Pass: actual Qwen3-VL two-call inference produced accurate nonempty black-ring output, took about 8 minutes, peaked near 2.17 GB PSS, and released inference memory. This remains the narrow x86_64 qualification result. |
| ARM64 full offline preload | API 36 Poco F7 Ultra; exact catalog model/projector; production UI; Wi-Fi/mobile disabled from stage 6 through 16 | Pass within observed scope: checksum verification completed, all 16 descriptions completed, controls remained disabled during work, ready status appeared, heartbeat stopped, stages 1/8/9/16 were captured, and no OOM occurred. Anatomical description quality was weak; no human TalkBack claim. |
| Direct production-path heartbeat test | API 36 Poco F7 Ultra; Google TalkBack enabled; accessibility volume 10/15 | Pass on 2026-10-02: all three two-tone pulses were heard across about ten seconds, and TalkBack announced start and completion. This does not validate a fresh full 16-stage AI run or its readiness announcement. |
| Final APK install and launch | API 36 x86_64 emulator; non-incremental install, cold launch offline, and same-version `-r` install | Pass. The `-r` result validates reinstall mechanics, not migration from an older `versionCode`. |
| ARM64 installation | API 32 Rokid device; fresh install and resumed Activity | Pass for installation and Activity resumption only; no human visual or accessibility validation was performed. |
| Other available Android targets | API 36 Poco; `armeabi-v7a` watch | Poco accepted the same-signed capture build and completed the bounded offline-AI run above. The final isolated APK then replacement-installed, its pulled `base.apk` matched byte-for-byte, and cold launch completed in 256 ms. The 32-bit watch is outside the documented ABI range. |
| Manual image/lifecycle exercise | API 36 x86_64 emulator | Pass within observed scope: system picker loaded a 256x256 PNG and 6000x4000 JPEG; stage 2 settled ready; rotation retained stage 12 anatomy; background/resume and a background process kill restored the workspace; 2x-font start screen remained usable; system DocumentsUI saved the selected 256x256 Original image as a new PNG. |
| BLACK WIDOW default-browser dispatch | API 36 Poco; package-resolution logs and foreground capture | Pass for initial dispatch: TATAPP package-targeted Chrome with the exact configured URL. Chrome then handed the destination to Facebook. The capture returned by explicitly relaunching TATAPP, so direct Back restoration and TalkBack focus preservation remain unverified. |
| Managed/native memory observations | API 36 x86_64 emulator editing samples; API 36 ARM64 Poco inference samples | Emulator editing ranged from about 50 MB cold to 111 MB after repeated import and 90 MB after background trim. Poco PSS was approximately 2.4 GB during active full-stage inference and approximately 302 MB after session disposal, with no observed OOM. This is one run, not a repeated or thermal/endurance profile. |
| Human TalkBack script | `docs/ACCESSIBILITY.md` | The direct heartbeat test's start/completion announcements passed with Google TalkBack on 2026-10-02. The complete speech-quality, focus, Explore-by-Touch, and full AI readiness-announcement script remains unverified. |
| Prior physically validated 0.3.0 APK identity, size and SHA-256 | `stat`; `sha256sum`; APK signer verification; pulled-Poco `base.apk` comparison | `artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk`, 20,198,175 bytes, SHA-256 `7e9469cdf2c8f87e52c6b5fce8d546f14159eb5358d601b7c3f1d3c095715a77`, signer SHA-256 `eac3df9aba3e08437bc988682566f072e52d2dde6bda373daa998cdee74d9f90`; the replacement-installed Poco APK matched byte-for-byte. No signed, installed, or device-validated 0.3.1 artifact is claimed by this historical record. |
