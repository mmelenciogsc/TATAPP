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

The reproducible Android accessibility walkthrough is defined in
[media/android-walkthrough/README.md](media/android-walkthrough/README.md).
Run `scripts/render-android-walkthrough.py` and then
`scripts/verify-android-walkthrough.py --review`. The generated MP4, SASRT, and
evidence manifest are written under the ignored
`artifacts/android-walkthrough/final/` directory.

## Offline AI Describe

Windows continues to use the separately installed Ollama/Qwen workflow
described in the architecture document. Android contains an in-process
LLamaSharp/llama.cpp implementation and a configurable two-file Qwen3-VL model
manifest. Android downloads model files only after consent, resumes partial
HTTPS downloads, verifies declared sizes and SHA-256 digests, and atomically
moves the complete set into app-private storage.

The current Android catalog entry is marked `tested: true` after the exact
checksum-verified model and projector completed an offline, original-stage,
two-call Qwen3-VL runtime smoke on an API 36 x86_64 emulator with 16 KiB pages.
A prior Poco F7 Ultra API 36 ARM64 build checksum-verified the same catalog pair
and completed model-generated descriptions for all 16 stages. Wi-Fi and mobile
data remained disabled from stage 6 through stage 16; the UI reached its
all-stages-ready state, stopped the heartbeat, and did not crash or exhaust
memory. PSS fell from approximately
2.4 GB during active inference to approximately 302 MB after model-session
disposal. TATAPP could not reuse the separately installed Machine Perception
Node because that app's inference service and model files are private and
non-exported and its package uses a different signer. This validates one
physical full-batch workflow, not broad output quality, thermal,
repeated-session, cancellation/recreation, or human TalkBack behavior. In
particular, the observed anatomical-stage descriptions were weak. The current
v5 description contract therefore invokes Qwen only once on Original, labels
that observation unverified, and constructs all 16 cached descriptions from
authoritative stage metadata and anatomical renderer state. It does not claim
that Qwen inspected any derived stage.

A later Poco listening check found the former short timer-driven music-stream
cue inaudible even though preprocessing completed. Android now uses the
accessibility-volume route and keeps one four-second static loop active: about
900 ms of pre-roll, the shared 420 ms two-note pulse at an Android-only bounded
gain, then silence. The Offline AI-assisted descriptions section includes **Test processing
heartbeat**, which plays the exact production path for three pulses in about ten
seconds and can be stopped with the same button or Back. On 2026-10-02, the
signed 20,198,175-byte APK (SHA-256
`7e9469cdf2c8f87e52c6b5fce8d546f14159eb5358d601b7c3f1d3c095715a77`)
replacement-installed on a Poco F7 Ultra and its installed `base.apk` matched
byte-for-byte. With Google TalkBack enabled and accessibility volume at 10/15,
the user heard all three two-tone pulses across about ten seconds and TalkBack
announced start and completion. This validates only the direct production-path
heartbeat test, not a fresh full 16-stage AI or readiness-announcement run;
visible status/progress remains authoritative.
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
`https://www.facebook.com/profile.php?id=61583807836781&sk=directory_contact_info`
only after deliberate activation. Android package-targets the initial request
to the device's configured default browser; that browser may subsequently hand
the destination to its associated native app under the user's browser settings.

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

The Android 0.3.1 application source at commit `db66a1d` built the full solution
in Release with warnings as errors and 0 warnings/0 errors; Core passed 51/51. The
WPF project compiled with 0 warnings/0 errors on Linux, but its runtime tests
could not run there because `Microsoft.WindowsDesktop.App` 10.0 is unavailable.
API 36 x86_64 16 KiB-emulator instrumentation recorded 27 passed, 0 failed,
and 2 skipped: loaded-model integrated UI/heartbeat and human TalkBack. The
later Poco ARM64 run supplied physical evidence for the full loaded-model
preload/ready/heartbeat path; no human listener completed the full TalkBack
script. A 2026-10-02 connected-instrumentation attempt installed the target app
but HyperOS rejected the temporary harness with
`INSTALL_FAILED_USER_RESTRICTED`; it is not a new harness pass, so the earlier
27-pass run remains historical evidence.

The signed and physically checked 0.3.1 evaluation artifact is
`artifacts/android/TATAPP-0.3.1-evaluation-arm64-x86_64.apk` (20,198,175 bytes,
SHA-256
`7f83b72ae3f1af5024d0dade5c3a220387762ccad5ac7c4f2812cccc0e1f42c9`;
signer SHA-256
`eac3df9aba3e08437bc988682566f072e52d2dde6bda373daa998cdee74d9f90`). It
contains `arm64-v8a` and `x86_64`, targets API 36 with minimum API 26, declares
only `INTERNET`, passed v2/v3 signature checks, and has all 132 packaged ELF
files 16 KiB aligned. The Poco accepted this exact APK, its pulled `base.apk`
matched byte-for-byte, and cold and offline launches passed. The external
evaluation keystore remains outside Git. A true older-version-to-0.3.1 upgrade
and the full manual TalkBack script remain unverified.
An available `armeabi-v7a` watch is outside the supported ABI set. Full evidence
and the remaining non-claims are in
[docs/ANDROID.md](docs/ANDROID.md).

On Windows 11 with .NET SDK 10.0.401, the original application remains:

```powershell
.\scripts\test.ps1
dotnet run --project .\src\TATAPP.App\TATAPP.App.csproj
.\scripts\publish.ps1
```

The self-contained Windows output is `artifacts\publish\win-x64`. Build and
inspect the per-user 64-bit Windows MSI with:

```powershell
.\scripts\build-installer.ps1
.\scripts\verify-installer.ps1 -InstallerPath .\artifacts\installer\TATAPP-0.3.1-win-x64.msi
```

Generated packages and signing material remain outside Git. Release installers
are unsigned unless a release operator signs them outside the repository with
separately protected credentials. Android has separate build/signing
requirements.

## Documentation

- [Android build, release, privacy, model, and troubleshooting guide](docs/ANDROID.md)
- [Windows installer build and verification](docs/WINDOWS-INSTALLER.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Accessibility and manual TalkBack script](docs/ACCESSIBILITY.md)
- [Feature and invariant matrix](docs/FEATURE_MATRIX.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)
- [Windows accessible walkthrough](docs/WALKTHROUGH.md)
