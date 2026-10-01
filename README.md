# TATAPP

TATAPP—Tattoo Art Prepper—is a local C# application for converting a clear,
isolated design image into tattoo-development and anatomical-placement
references. The repository contains the original Windows 11 WPF application,
a native .NET for Android application, shared deterministic processing and
workflow libraries, and platform-specific accessibility paths.

Use a high-resolution design on a clean or transparent background rather than
a photograph of an existing tattoo on skin. Imported photographs, generated
stages, previews, and exports remain on the device during ordinary editing.

## Workflow

The shared stage catalog is authoritative. It currently defines eight flat
stages—Original image, Colors fading, Grayscale, Binarized stencil, Line art,
Thick outline, Medium outline, and Fine outline—followed by eight anatomical
stages that reverse from fine outline toward full color on the selected body
surface. The UI shows the exact stage number, name, and slider value and offers
Previous Stage and Next Stage actions.

The anatomical visualization supports the repository's 22 surface choices,
male and female geometry, rotation, zoom, body size, and complexion. Its
defaults are male, 163 centimeters (average Filipino adult), light brown to
medium tan Filipino complexion, and the outer surface of the left upper arm
from deltoid to elbow. A standard body-region picker is synchronized with
visual surface taps and remains the complete non-visual path.

## Android application

The Android application uses native Android views on .NET 10 rather than a
WebView. The anatomy view projects the shared three-dimensional mesh onto an
Android `Canvas`; it is not an OpenGL ES renderer. It targets Android API 36,
supports API 26 and newer, packages `arm64-v8a` and `x86_64`, and uses package
ID `com.grayscaleconsultants.tatapp`.

Image selection uses Android's system photo picker, with `ACTION_OPEN_DOCUMENT`
on older supported releases. Capture delegates to the installed camera through
an app-private `FileProvider` URI. Saving uses the Storage Access Framework and
never silently overwrites the source. TATAPP recognizes JPEG, PNG, BMP, TIFF,
and GIF signatures, but import still requires a decoder supplied by the Android
device (TIFF support is not universal). Successfully decoded JPEG and PNG remain
in their original format; successfully decoded BMP, TIFF, and GIF inputs receive
an explicit, non-destructive PNG fallback because Android has no reliable
encoder for those formats.

The manifest declares only `android.permission.INTERNET`. TATAPP does not ask
for camera, microphone, broad storage/media, location, contacts, advertising,
or analytics permission. Internet access is reserved for an explicitly
consented optional model download; photos and generated descriptions are not
uploaded.

See [Android build, release, and operation](docs/ANDROID.md) and the
[accessibility contract](docs/ACCESSIBILITY.md).

## Offline AI Describe

Windows continues to use the separately installed Ollama/Qwen workflow
described in the architecture document. Android contains an in-process
LLamaSharp/llama.cpp implementation and a configurable two-file Qwen3-VL model
manifest. Android downloads model files only after consent, resumes partial
HTTPS downloads, verifies declared sizes and SHA-256 digests, and atomically
moves the complete set into app-private storage.

The current Android catalog entry is marked `tested: true` after the exact
checksum-verified model and projector completed one offline, original-stage,
two-call Qwen3-VL runtime smoke on an API 36 x86_64 emulator with 16 KiB pages.
That narrow smoke establishes runtime viability on that profile; it does not
claim ARM64 inference, full 16-stage preload, thermal, physical-device, or human
TalkBack validation.
Policy still admits the model only when every API, ABI, storage, current-memory,
CPU, and acceleration threshold passes. No model weights are stored in this
repository or redistributed in the APK.

## Accessibility

Windows retains native WPF/UI Automation behavior for JAWS, Narrator, NVDA,
keyboard input, and High Contrast. Android uses native accessibility semantics
for TalkBack, switch/keyboard access, Explore by Touch, headings, state/value
descriptions, polite status regions, and custom Previous/Next stage actions.
Controls have at least 48 dp targets, layouts scroll under large text, important
changes are announced only after a semantic stage settles, and reduced-motion
mode replaces the anatomical camera transition with an immediate detail view.

The prominent **BLACK WIDOW TATTOO** action opens
`https://www.facebook.com/grayscaleconsultants` in the platform browser only
after deliberate activation.

## Build and test

The repository pins .NET SDK 10.0.401. On Linux with the Android workload,
JDK 17, Android API 36, Build Tools 36.0.0, NDK 27.0.12077973, and CMake 3.22.1:

```bash
export DOTNET_BIN=/path/to/dotnet
export ANDROID_SDK_ROOT=/path/to/android-sdk
export JAVA_HOME=/path/to/jdk-17

scripts/android/build-llamasharp-native.sh
scripts/test.sh
scripts/build-android.sh
```

`scripts/test.sh` always runs the cross-platform Core harness. It builds
Android as an additional gate only when the Android workload, API 36, and both
validated native ABI folders are present; otherwise it prints an explicit
skip. Connected-device instrumentation is a separate opt-in command documented
in [docs/ANDROID.md](docs/ANDROID.md).

The recorded release based on commit `b29bb63` built the full solution in
Release with warnings as errors and 0 warnings/0 errors; Core passed 44/44. The
WPF project compiled with 0 warnings/0 errors on Linux, but its runtime tests
could not run there because `Microsoft.WindowsDesktop.App` 10.0 is unavailable.
API 36 x86_64 16 KiB-emulator instrumentation recorded 27 passed, 0 failed,
and 2 skipped: loaded-model integrated UI/heartbeat and human TalkBack.

The signed evaluation APK is
`artifacts/android/TATAPP-0.3.0-evaluation-arm64-x86_64.apk` (20,128,543 bytes,
SHA-256 `c84dd4cd5c0383577494ec580443f848b43e941f45c887820d6718057cec5ed2`).
It contains `arm64-v8a` and `x86_64`, targets API 36 with minimum API 26,
declares only `INTERNET`, passed v2/v3 signature checks, and has all 132 packaged
ELF files 16 KiB aligned. Install, cold offline launch, and same-version `-r`
installation passed on the emulator. An API 32 ARM64 Rokid accepted a fresh
install and resumed the Activity, but no human visual or accessibility review
was performed there. A Poco install was blocked by
`INSTALL_FAILED_USER_RESTRICTED`; an available `armeabi-v7a` watch is outside
the supported ABI set. Full evidence and the remaining non-claims are in
[docs/ANDROID.md](docs/ANDROID.md).

On Windows 11 with .NET SDK 10.0.401, the original application remains:

```powershell
.\scripts\test.ps1
dotnet run --project .\src\TATAPP.App\TATAPP.App.csproj
.\scripts\publish.ps1
```

The self-contained Windows output is `artifacts\publish\win-x64`. Android has
separate build/signing requirements; signing material must remain outside Git.

## Documentation

- [Android build, release, privacy, model, and troubleshooting guide](docs/ANDROID.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Accessibility and manual TalkBack script](docs/ACCESSIBILITY.md)
- [Feature and invariant matrix](docs/FEATURE_MATRIX.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [Windows accessible walkthrough](docs/WALKTHROUGH.md)
