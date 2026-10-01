# Architecture

## Project boundaries

- `TATAPP.Core` targets `net10.0`. It owns the BGRA32 image model, stage
  catalog and mappings, deterministic transforms, tattoo-ink preparation,
  format metadata, anatomical definitions and generated triangle meshes,
  camera framing, immutable workflow state, byte-budget LRU infrastructure,
  offline-model policy, installation contracts, and sequential description
  orchestration.
- `TATAPP.App` targets `net10.0-windows` and remains the native Windows 11 WPF
  application. It owns Windows decoding/encoding, Camera handoff, WPF 3D,
  UI Automation, shell navigation, persistence, and Ollama integration.
- `TATAPP.Android` targets `net10.0-android36.0`. It uses native Android views
  and services for system picker/camera intents, app-private storage, decoding,
  `Canvas` rendering, lifecycle, TalkBack semantics, SAF export, external-link
  intents, model download, and LLamaSharp inference.
- `TATAPP.Core.Tests` is the cross-platform executable regression harness.
- `TATAPP.Tests` is the Windows/WPF executable regression harness.
- `TATAPP.Android.Instrumentation` is a dependency-free Java instrumentation
  runner built by `scripts/test-android-instrumentation.sh` for an authorized
  device or emulator.

This separation keeps transformation formulas, stage identity, body-region
meaning, model admission, validation, and description sequencing in one shared
library. Platform code supplies only the capabilities that differ: image I/O,
system intents, storage, rendering, audio, accessibility announcements, model
files, hardware probes, and external links. Android registers its services
through `Microsoft.Extensions.DependencyInjection`; work is asynchronous and
cancellable across those boundaries.

## Why native .NET for Android

The Android project uses the supported .NET Android SDK directly rather than
MAUI. Repository evidence favored this narrower architecture: the existing WPF
application must remain unchanged, shared Core has no UI dependency, and the
port needs exact Android lifecycle, native accessibility, URI-grant, Storage
Access Framework, and memory-pressure behavior. Adding MAUI would not make WPF
shared and would insert controls/handlers between TATAPP and the Android APIs it
must verify.

The selected baseline is .NET SDK 10.0.401, Android workload 36.1.69,
`net10.0-android36.0`, minimum API 26, target/compile API 36, package
`com.grayscaleconsultants.tatapp`, and ABIs `arm64-v8a` and `x86_64`. The APK
version is 0.3.0 (`versionCode` 1). The native inference build is pinned to NDK
27.0.12077973, CMake 3.22.1, LLamaSharp 0.27.0, and llama.cpp commit
`3f7c29d318e317b63f54c558bc69803963d7d88c`.

## Processing continuum

The engine composites transparent input pixels onto white for printable
derived stages. Source-domain render values interpolate color into perceptual
grayscale, move through an Otsu-derived black/white threshold, blend stencil
regions into Sobel-derived line art, and decrease morphological line thickness
from four pixels to one source-pixel edge. Slider zero returns a byte-identical
clone.

The shared catalog maps the first eight semantic stages to those flat renders.
The next eight reverse their source representations—fine outline through full
color—and apply them to a selected anatomical surface. UI code obtains names,
descriptions, representative slider values, source-render values, and ordering
from that catalog; it does not maintain a second formula table.

Windows preview rendering is bounded to 1,600 pixels and full-resolution saves
rerun memory checks. Android imports a maximum 128 MiB compressed stream,
rejects decoded images above 32 megapixels, normalizes EXIF orientation and
color into ARGB8888, and bounds preview input to 1,600 pixels. Android delays
expensive rendering for 65 ms after stage movement and cancels older work.
Generation numbers ensure stale import/render results cannot replace the
current source.

## Anatomy and synchronized selection

`AnatomicalGeometryCatalog` constructs the repository-defined body segments and
placement surfaces as shared three-dimensional triangle meshes. The WPF app
materializes those definitions with `Viewport3D`. Android transforms the same
vertices for sex, height, rotation, camera distance, and region framing,
perspective-projects them, and draws them on an Android `Canvas`. Projected
triangles also provide hit testing and selected-region bounds, so rendering and
touch cannot drift into separate region definitions. Android does not use
OpenGL ES.

Visual taps dispatch one body-region selection. The native picker then adopts
it while a synchronization guard suppresses recursive callbacks. Picker
selection follows the inverse route, applying the region's preferred rotation
and context camera. Sex, height, complexion, rotation, zoom, reduced-motion,
and stage remain explicit immutable workflow state. The standard picker and
buttons provide equivalent functionality when TalkBack touch exploration
disables model gestures.

Every anatomical stage uses the shared region camera plan. Normal motion begins
at the context frame and moves toward the centered detail frame; reduced motion
renders the detail state immediately. Offline-description capture uses the same
state without animation.

## Android state, memory, and lifecycle

The Android Activity owns four cancellation domains: Activity lifetime,
import, stage render, and AI preparation. A newer import or render cancels its
predecessor. Compact instance state contains reconstructable app-private source
identity, current stage/anatomy values, pending camera destination, and the
immutable export snapshot—not pixels, bitmaps, descriptions, or tensors.

The preview cache is LRU-bounded to one eighth of Android's memory class,
clamped to 12–48 MiB. Imports and capture files reside in app-private storage;
abandoned temporary files are deleted. `OnTrimMemory` clears cached frames and
cancels work as pressure/background state increases. `OnLowMemory`, Activity
stop, and Activity destruction stop audio and cancel inference. Replaced
bitmaps, model media, contexts, weights, streams, and cancellation sources are
explicitly disposed. The manifest does not request `largeHeap`.

Android export snapshots the currently displayed semantic stage and anatomy
before opening `ACTION_CREATE_DOCUMENT`. Flat stages rerender from full source
unless their estimated working allocation would exceed 45% of the memory
class, in which case the bounded high-quality preview is used with a user
notice. Anatomical export captures the snapshot's settled placement state.
Successfully decoded JPEG/PNG preserve format; successfully decoded BMP,
TIFF, and GIF inputs receive an explicit PNG fallback. Signature recognition
does not guarantee that a particular Android build supplies a TIFF decoder.
The original URI is never silently overwritten.

## Offline AI description pipelines

### Windows

Windows retains its opt-in local Ollama workflow. Hardware inspection selects
the established 2B/4B/8B tier, the user consents to model installation, and
actual flat renders/WPF viewport captures are described sequentially. Results
become visible only as a complete 16-stage cache.

### Android

Android uses shared policy/orchestration with platform implementations for
capability probing, app-private installation, rendered-stage capture, and an
in-process LLamaSharp session. The probe records API level, supported ABIs,
ordinary and large memory classes, current available/low-memory state, free
storage, CPU flags, and available acceleration. Policy does not rely on
`largeMemoryClass` and rejects every variant that is untested or misses any
resource gate.

The current configurable catalog contains one Qwen3-VL 2B Q4_0 text model plus
F16 multimodal projector. Its exact checksum-verified pair completed one
offline original-stage inference over a synthetic 192-pixel black-ring image
on an API 36 x86_64 emulator with 16 KiB pages. The roughly eight-minute smoke
peaked near 2.17 GB PSS, released inference memory afterward, and produced a
nonempty description without OOM or crash. This justifies the catalog's tested
flag for that narrow profile.

A later API 36 ARM64 Poco run installed and checksum-verified the exact catalog
model and projector, then completed the production UI's sequential 16-stage
preload. Both radios were disabled from stage 6 through completion. The UI kept
stage controls disabled during processing, published the all-stages-ready state,
stopped the heartbeat, and made cached descriptions available at stages 1, 8,
9, and 16 without OOM. PSS decreased from approximately 2.4 GB during active
inference to approximately 302 MB after the model session was disposed. The
Machine Perception Node could not be reused: its inference service and model
files are private/non-exported and its package has a different signer. TATAPP
therefore performed the entire run locally in its own process with its
LLamaSharp/llama.cpp CPU runtime; it did not bypass Android package isolation.

That run establishes one ARM64 full-batch execution, not broad description
quality, thermal/endurance, repeated-workflow, cancellation/recreation, or
assistive-technology results. The anatomical-stage prose observed in the run
was weak, and no human listener completed the full TalkBack script. Exact
artifact sizes, SHA-256 values, URLs, and thresholds are in `ANDROID.md`.

For any eligible tested catalog entry, installation is explicit and resumable over
HTTPS. Partial files remain under an app-private staging directory. Length and
SHA-256 verification precede atomic directory promotion, and ready status
requires the exact artifact count and installed-byte total. Model selection
chooses the smallest eligible tested variant; fallback candidates must be both
lower-ranked and lower-working-set. The current one-entry catalog therefore has
no smaller model fallback; an admission or runtime failure returns to the
clearly explained non-AI editing workflow.

Preloading opens one model session and renders every current catalog stage
strictly in order with a fresh memory check before each stage. Images, visual
tokens, context, output, and CPU threads are capped. A batch is committed to the
bounded description cache only after every stage succeeds. Cancellation,
pressure, malformed output, or inference failure disposes the session and
retains no partial ready cache. The foreground-only heartbeat is supplementary
to visible progress and stops on completion, cancellation, error, stop, or
low-memory callbacks.

## Privacy and external navigation

The Android manifest declares only `INTERNET`, reserved for a future consented
model download. Image selection/capture/export rely on scoped system URI grants,
so no camera or broad storage permission is needed. There is no analytics,
advertising, cloud image inference, or ordinary-editing network dependency.

BLACK WIDOW TATTOO stores one HTTPS destination in shared product metadata.
Windows uses shell navigation. On API 29+, Android first checks availability of
the system browser role; the public app API does not disclose another app's
role-holder package, so the holder is resolved through a neutral-domain HTTPS
intent with `MATCH_DEFAULT_ONLY`. API 26–28 uses the same neutral resolution.
The system resolver is rejected, and the final browsable `ACTION_VIEW` is
package-targeted to the resolved browser, preventing Android's initial resolver
from substituting a destination-specific handler. The browser can still apply
its own verified-link or native-app handoff policy afterward. If no default
browser is configured, Android reports a recoverable error instead of launching
an arbitrary handler. Neither path opens on focus, authenticates, or transmits
a photo.

## Build and supply-chain boundary

The Android project references managed LLamaSharp only. LLamaSharp's stock
Android backend is not used because its current shared objects have 4 KiB ELF
alignment. `scripts/android/build-llamasharp-native.sh` rebuilds the five
required libraries for both ABIs from the exact pinned llama.cpp commit with
flexible-page support and records hashes/provenance/licenses. Generated native
libraries, model weights, APKs, keystores, and credentials remain ignored and
outside source control. `scripts/android/verify-apk.sh` independently checks
the final package, permissions, ABIs, native alignment, ZIP alignment, and
signature.
