# TATAPP feature and invariant matrix

This matrix records the behavior derived from commit `b29bb63` before the Android port. The catalogs and transformation formulas in `TATAPP.Core` remain authoritative; platform implementations must not duplicate them.

| Capability | Shared invariant | Windows implementation | Android implementation boundary | Verification |
| --- | --- | --- | --- | --- |
| Image acquisition | One validated source replaces all prior derived work | Windows Camera handoff and WPF file dialog | API 33+ system photo picker or older `ACTION_OPEN_DOCUMENT`; camera intent with an app-private `FileProvider` URI; no camera/storage permission | Cancellation, URI restoration, corrupt and oversized inputs |
| Decode | JPEG, PNG, BMP, TIFF and GIF signature identity; EXIF orientation; 32-megapixel ceiling | WPF codecs and a 1,600-pixel preview | Android platform decoder, color-space normalization and sampled preview; a recognized signature is not a promise that the device codec can decode it, especially TIFF | Orientation, dimensions, memory pressure, decoder failure and unsupported formats |
| Stage continuum | Exactly 16 catalog entries and the existing slider/source-render mappings | Continuous 0–100 WPF slider | Semantic 16-position navigation with visible stage number, name and representative value plus Previous/Next actions | Every boundary, ordering rule and endpoint |
| Image processing | Original pixel clone; luminance fade; Otsu binary; Sobel line art; thick-to-fine dilation | Shared Core called from WPF | Same Core implementation over a bounded preview; export attempts a full decode only when its estimated working set fits the memory gate, otherwise it explicitly exports the preview dimensions | Determinism, pixel/channel invariants, cancellation and bounded-export behavior |
| Anatomy | 22 regions, shared defaults, triangle meshes, placement surfaces, region camera plans and reverse eight-stage sequence | Generated WPF `Viewport3D` mannequin | Native Android `Canvas` perspective-projects the shared 3D mesh; the same projected triangles drive hit testing and placement | Default state, every region, mesh bounds/fingerprints, hit mapping and placement parameters |
| Accessible anatomy | Visual hit and standard picker stay synchronized | WPF hit test plus `ComboBox` | Native accessible picker is complete without requiring the rendered model; one immutable reducer prevents feedback loops | Picker/model bidirectional synchronization and traversal |
| Placement motion | Context frame precedes centered detail frame | 450 ms hold plus 2,350 ms cubic movement | Lifecycle-safe animation or immediate detail frame when reduced motion is active | All regions, interruption, rotation and reduced motion |
| Export | Export the captured stage/state; never alter the imported source | Full-size flat render or settled viewport capture; atomic sibling file | SAF `ACTION_CREATE_DOCUMENT`; immutable displayed-stage/anatomy snapshot; JPEG/PNG or explicit non-destructive PNG fallback for successfully decoded BMP/TIFF/GIF | Exact current stage, success/failure/cancel and no overwrite |
| Offline description | Actual rendered images, all current catalog stages, original-stage grounding, sequential work, atomic complete cache | Loopback Ollama and WPF viewport capture | App-private verified model and in-process LLamaSharp/llama.cpp CPU runtime behind shared contracts | Ordering, one-session reuse, repeated headroom checks and atomic cache tests; exact-model x86_64 smoke plus one physical ARM64 16-stage preload/ready/heartbeat run; anatomy output quality and human TalkBack remain unverified |
| Model policy | Only a tested manifest entry satisfying API, ABI, storage, memory pressure, CPU and acceleration gates is eligible | Windows 2B/4B/8B Ollama policy | Configurable quantized Android manifest; smallest suitable tested variant wins and fallback must be both smaller and lower-ranked; the current single-tier catalog has no smaller fallback and returns to explained non-AI editing on failure | Every threshold, shipped-manifest value and fallback ordering; device admission remains independent of the narrow smoke |
| Model installation | Explicit consent precedes download; incomplete files are never ready | Ollama-managed pull | Resumable HTTPS partials, displayed size, SHA-256 verification and atomic app-private placement | Consent, resume, checksum, shortage, cancel and cleanup |
| Progress/audio | Audio is supplementary and stops on every terminal path | Two-note heartbeat during stage preprocessing | Lifecycle/foreground-owned heartbeat plus visible progress; model-install updates require a five-percentage-point change and at least 750 ms in the same phase, while stage preload uses bounded milestones | Heartbeat lifetime and one ready/error announcement |
| Lifecycle | Stale work cannot replace a newer source or control state | Cancellation tokens on source and window close | Compact saved state plus monotonic generation fences; bounded caches cleared under trim-memory callbacks | Recreation, backgrounding, rapid replacement and stale-result rejection |
| External navigation | BLACK WIDOW TATTOO opens `https://www.facebook.com/profile.php?id=61583807836781&sk=directory_contact_info` only after activation | Windows shell launch | Initial package-targeted browsable Android `ACTION_VIEW` to the configured default browser after deliberate activation; recoverable error if none exists; subsequent browser-to-app handoff is browser policy | Exact destination, browser package, activation and error path |

## Android platform invariants

The Android project is native .NET for Android targeting
`net10.0-android36.0`, not MAUI or a WebView. It has package ID
`com.grayscaleconsultants.tatapp`, minimum API 26, target API 36, and packages
`arm64-v8a` plus `x86_64`. Its production manifest declares only `INTERNET`,
reserved for explicit model installation; ordinary editing has no network
dependency and no image-upload path.

All native action controls use at least a 48 dp minimum. The stage slider
exposes native range state and conditional Previous/Next custom actions, while
the separate buttons remain an equivalent no-drag path. Headings and settled
status/description changes use Android accessibility APIs. The connected runner
performs native-node, compact-layout, test-provider SAF, recreation,
stale-result, and memory-callback assertions. The physical Poco full-preload
evidence does not replace human TalkBack speech, Explore by Touch, real maximum
font/display scaling, or other unexercised device gates; see `ACCESSIBILITY.md`.

## Authoritative stage order

The eight flat stages are Original image, Colors fading, Grayscale, Binarized stencil, Line art, Thick outline, Medium outline and Fine outline. The reverse anatomical sequence is Placement — fine outline, medium outline, thick outline, line art, black and white, grayscale, returning color and full color. Representative slider values are `0, 7, 14, 21, 28, 35, 43, 51, 59, 65, 71, 77, 83, 89, 95, 100`; source-render values are `0, 12, 23, 35, 48, 62, 78, 91, 91, 78, 62, 48, 35, 23, 12, 0`.

## Authoritative anatomy

The 22 regions are left/right outer and inner upper arm, left/right outer and inner forearm, left/right wrist, upper-left/right chest, full upper chest, full chest and abdomen, full upper back, full back, left/right shoulder, left/right thigh and left/right calf.

Defaults are male, left outer upper arm from deltoid to elbow, 163 centimeters (average Filipino adult), skin-tone value 55 (light brown to medium tan, Filipino), the region's preferred left view, overview camera distance 25 and normal motion. The shared geometry catalog retains each region's `DefaultTattooScalePercent`, but neither current renderer applies it as a separate tattoo-size control; Android maps the design across the selected placement mesh, while the body-size control scales the anatomical model.
