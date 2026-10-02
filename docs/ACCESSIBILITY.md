# Accessibility contract

TATAPP treats accessibility as application behavior. Every task has a standard
control path; neither spatial discovery nor manipulation of the anatomical
model is required.

## Windows

The WPF application retains native UI Automation behavior for JAWS, Narrator,
NVDA, keyboard input, touch, Windows text scaling, and High Contrast:

- buttons, radio buttons, dropdowns, sliders, previews, and the 3D viewport
  expose names and useful help text;
- access keys and tab order follow capture/select, save, preview, anatomy, and
  visual development;
- body-surface taps and the native Body region dropdown remain synchronized;
- the preview and Offline AI description expose dynamic, focusable text;
- consequential progress, stage, save, and error updates use polite live
  regions without announcing every slider pixel;
- Windows file dialogs and message boxes remain native.

The Windows default announcement identifies male, 163 centimeters (average
Filipino adult), light brown to medium tan Filipino complexion, and the outer
left upper arm from deltoid to elbow. The quiet heartbeat during optional
Ollama preprocessing is accompanied by visible/live progress and is never the
only status channel.

## Android semantic behavior

Android uses native View accessibility APIs rather than web ARIA or
Windows-only automation properties.

- Headings are marked through `AccessibilityHeading` on supported releases.
- Buttons, checkboxes, switches, radio buttons, spinners, sliders, preview, and
  anatomy view expose native roles, names, state/value descriptions, enabled
  state, and concise help where useful.
- Hierarchy order is stable: app heading; Take photo, Select photo, SAVE current
  look, BLACK WIDOW TATTOO; Start or Workspace content; then the final status
  region. Within Workspace the source/previews precede anatomy controls, stage
  controls, Offline AI, progress, and status.
- The stage control is a native SeekBar with range semantics. It adds custom
  **Previous stage** and **Next stage** actions only when that action is
  available. Separate 48 dp Previous Stage and Next Stage buttons provide the
  same behavior for switch, keyboard, and direct-touch users.
- Moving through pixels updates visible value text but does not speak on every
  pixel. Crossing into a new semantic stage announces one loading state; after
  the 65 ms cancellable settle/render, one ready state names the exact stage.
- The final application status and dedicated Offline AI description are polite
  live regions. The description region remains focusable and is updated in
  place; focus is not repeatedly forced into it.
- The preview description names the imported file, stage number/name, and
  visual effect. The anatomy description contains equivalent textual placement
  state. Decorative containers are excluded from the accessibility tree.
- All native action controls have at least 48 dp width/height and spacing.
  Text uses scaled pixels inside a scrolling layout that reflows between
  portrait/landscape and vertical/horizontal action rows. Platform colors and
  explicit outlines preserve selection/focus and light/dark-theme contrast;
  meaning is also expressed in text and shape rather than color alone.
- Back from Workspace returns to Start and exposes **Resume workspace** without
  destroying the current image. Cancellation, corrupt images, save failure,
  unavailable camera/provider, and unavailable Offline AI produce textual
  status plus a native dialog when recovery requires attention.

## Anatomical alternatives

Sighted users can tap a projected mesh surface, drag horizontally to rotate,
and use the visible selected-surface outline. TalkBack touch exploration causes
the model to stop consuming drag/tap gestures, avoiding conflict with Explore
by Touch.

The complete non-visual path is the native Body region picker plus Male/Female,
Rotate left/right, Zoom in/out, Body size, Skin tone and complexion, and Reduce
anatomy motion controls. Picker changes update the model; model taps update the
picker through a guarded one-way transaction so no feedback loop occurs. State
updates identify the region, height, complexion, rotation, and camera distance.
Reduced motion replaces context-to-detail camera animation with the settled
detail view.

## Offline AI announcements

Offline AI Describe is opt-in. The checkbox identifies local model use,
installation consent, and the no-upload boundary. Download progress has an
accessible percentage; within one install phase, UI updates require both a
five-percentage-point advance and at least 750 ms (phase changes and completion
are immediate). One Original-image inference is followed by deterministic
construction of all stage descriptions in a readable non-live region rather
than announcements of transient N-of-N values. The sole polite status
region publishes one final readiness sentence after cache commit, heartbeat stop,
and control restoration. The heartbeat is supplementary, follows Android's
independently controlled accessibility volume, runs only while foreground
preprocessing is active, and stops on success, cancellation, error,
backgrounding, or low-memory cleanup. **Test processing heartbeat** runs the
exact player for three pulses in about ten seconds; its text changes to **Stop
heartbeat test**, Back also stops it, and Offline AI cannot start concurrently.

A completed description cache must contain every current catalog stage before
the UI says ready. Selecting a preloaded stage changes the readable description
immediately without moving focus. Original includes a clearly labeled unverified
model observation. Derived descriptions use authoritative stage metadata and
exact anatomical renderer state, including visibility and completed detail
framing, and never claim that Qwen visually inspected the derived stage.

The exact Android model/projector pair first passed a synthetic original-stage,
two-call Qwen3-VL runtime smoke on an API 36 x86_64 emulator. It later completed
all 16 model-generated descriptions through a prior production UI build on an
API 36 ARM64 Poco F7 Ultra.
Wi-Fi and mobile data were disabled from stage 6 through stage 16; the controls
remained disabled during preprocessing, the all-stages-ready status appeared,
and the heartbeat stopped. UI hierarchy captures record cached descriptions at
stages 1, 8, 9, and 16. No OOM occurred, and PSS fell from approximately 2.4 GB
during inference to approximately 302 MB after session disposal.

This is device evidence for the offline preload and UI state transitions, not a
human TalkBack acceptance result. No listener completed the full script below,
and the anatomical-stage descriptions observed in this run were weak. The UI
must never claim ready merely because files downloaded or a bounded smoke
succeeded.

## Touchscreen operation without TalkBack

1. Tap **Take photo** to use the installed camera, or **Select photo** to use the
   system photo picker.
2. Swipe the page vertically to reach anatomy and stage controls.
3. Tap a visible body surface or choose it from **Body region**. Drag the model
   horizontally to rotate, or use the rotation buttons for exact increments.
4. Use Zoom, Body size, Skin tone and complexion, and Reduce anatomy motion as
   needed.
5. Drag the stage slider or use Previous Stage/Next Stage. Wait for the visible
   `Ready` status before saving.
6. Tap **SAVE current look**, choose a new document name/location, and confirm
   the stated format. TATAPP does not overwrite the import silently.

## Verification matrix

The connected instrumentation runner recorded 27 passed, 0 failed, and 2
explicit skips on an API 36 x86_64 16 KiB emulator. The skips were the
loaded-model integrated UI/heartbeat workflow and human TalkBack speech/Explore
by Touch. The later physical Poco run covered the former through production UI
evidence; it did not close the human TalkBack skip. The final multi-ABI APK
separately passed install, cold offline launch, and same-version reinstall on
the emulator. No human listener performed the full TalkBack script, and the
observations below do not establish speech or broad visual quality.

On 2026-10-02, a 20,198,175-byte signed APK with SHA-256
`7e9469cdf2c8f87e52c6b5fce8d546f14159eb5358d601b7c3f1d3c095715a77`
replacement-installed on the Poco F7 Ultra; its installed `base.apk` matched
byte-for-byte. With Google TalkBack enabled and accessibility volume at 10/15,
the user heard all three two-tone pulses across the direct production-path
heartbeat test's roughly ten seconds and TalkBack announced start and
completion. This does not validate a fresh full 16-stage AI run or the revised
readiness announcement. A connected-instrumentation attempt installed the
target app but HyperOS rejected the temporary harness with
`INSTALL_FAILED_USER_RESTRICTED`; the earlier 27-pass instrumentation run
therefore remains historical rather than being superseded.

| Scenario | Automated evidence | Required human/device evidence | Current human status |
| --- | --- | --- | --- |
| Launch, focus order, semantics, 48 dp controls | Native view/node assertions passed | Listen to TalkBack order; Explore by Touch; switch/keyboard activation | TalkBack UNVERIFIED; final APK cold/offline launch passed on emulator |
| Picker, cancellation, corrupt input, rapid replacement | Real fixture and direct result/lifecycle assertions passed | System picker/provider behavior with small and large designs | Partial: emulator system picker loaded 256x256 PNG and 6000x4000 JPEG; no TalkBack or physical-device usability check |
| Stage ordering, Previous/Next, rapid slider, settled status | Exact labels/actions and one polite status source asserted and passed | Listen for one loading/ready sequence and no pixel chatter | Partial: stage 2 visibly settled ready; spoken behavior UNVERIFIED |
| Anatomy defaults and non-default synchronization | Shared-catalog count, picker/model tap, complexion pixel change, semantics and recreation asserted and passed | Judge visible placement/highlight; TalkBack alternative and touch-exploration behavior | Partial: rotation retained stage 12 anatomy; human visual and TalkBack quality UNVERIFIED |
| Save current visible stage | Test-only SAF provider compares exported pixels and forces write failure | System DocumentsUI success/cancel/denial and visual comparison | Partial: final APK saved a selected 256x256 Original image as a new PNG through DocumentsUI; cancel/denial and human visual comparison remain UNVERIFIED |
| Font/display scale, rotation and recreation | 2x-text/320 dp structural stress and orientation recreation passed | Maximum practical system font/display scales in portrait/landscape | Partial: 2x-font start screen usable and rotation retained state on emulator; maximum practical settings and human accessibility remain UNVERIFIED |
| Memory/lifecycle | Critical trim/low-memory callback usability and stale-source fences passed | Repeated imports, backgrounding and profiler evidence on constrained hardware | Emulator editing samples ranged from about 50 MB cold to 111 MB after repeated import. One ARM64 full preload completed without OOM at approximately 2.4 GB active PSS and fell to approximately 302 MB after session disposal; repeated inference, cancellation/recreation, and thermal endurance remain unverified. |
| BLACK WIDOW TATTOO | Focus emits no intent; activation emits exact browsable URL targeted to the configured browser package | Browser launch, return, and preserved TalkBack focus | Partial: Poco logs prove TATAPP package-targeted Chrome with the exact URL; Chrome then handed the destination to Facebook. The capture returned by explicitly relaunching TATAPP, so direct Back restoration and TalkBack focus preservation remain unverified. |
| Offline AI and heartbeat | False-ready fence passed; separate actual-model two-call x86_64 smoke passed. The current source contract publishes `AI-assisted, renderer-grounded descriptions are ready for all 16 stages. The offline model analyzed Original once; transformed stages use renderer and placement state.` through the sole polite status region only after cache commit, heartbeat stop, and control restoration. | Exact-model install, offline 16-stage preload, cancellation, memory, speech, and heartbeat lifetime | Physical ARM64 evidence includes the earlier complete 16-stage run and the 2026-10-02 direct heartbeat test: all three revised pulses were audible and TalkBack announced test start/completion. The revised full-AI readiness utterance has not been human reverified; anatomical description quality was weak. |

The SAF provider exists only in the test APK; it is not a production exported
component. The release verifier separately rejects unexpected exported
production components.

## Manual TalkBack acceptance script

Status: **not physically performed for this Android release candidate**. Record
device model/build, font/display scale, TalkBack version, APK SHA-256, and every
deviation when this script is actually run. Automated node inspection is not a
substitute for listening to TalkBack.

Prerequisites: one small and one large isolated design; one corrupt or renamed
non-image file; Android's largest practical font/display setting; portrait
orientation; TalkBack enabled; an external browser; and, for steps 11–12 only,
a separately validated/installed catalog model that is eligible while the
device is offline.

1. **Launch and order.** Cold-launch TATAPP. Swipe right from the top. Confirm
   `TATAPP` is a heading; Take photo, Select photo, disabled SAVE current look,
   and BLACK WIDOW TATTOO occur in that order; Start is a heading; and status
   says a photo is required. Explore by Touch and activate each primary control
   target without needing adjacent visual context.
2. **Picker cancellation.** Activate Select photo, press Back in the system
   picker, and confirm one `Photo selection canceled` status with no error or
   focus trap.
3. **Load and source state.** Select the small design, then rapidly select the
   large design and the small design again. Confirm only the final selection
   becomes active and no stale render replaces it. Confirm the Workspace
   heading/source dimensions, enabled SAVE/Offline AI controls, and
   `Stage 1 of 16: Original image, Value 0 percent`. Focus the preview and
   confirm its description names the file and Original image.
4. **Stage actions and settled speech.** On the stage slider, open TalkBack
   actions. Confirm Previous stage is absent at stage 1 and Next stage is
   present. Invoke Next repeatedly and verify each significant stage produces
   no rapid pixel chatter, one loading announcement, then one exact ready
   announcement. Use the separate Previous Stage/Next Stage buttons with swipe
   navigation and a switch/keyboard if available.
5. **Four representative render states.** Navigate to and inspect the preview
   at Original image (stage 1/value 0), Binarized stencil (stage 4/value 21),
   Fine outline (stage 8/value 51), and Placement — full color (stage 16/value
   100). Confirm the descriptions and images change, first/last actions disable
   correctly, and the source versus anatomical state is unambiguous.
6. **Body alternative and synchronization.** Using only swipe navigation,
   confirm Male, outer surface of left upper arm, 163 centimeters, and light
   brown to medium tan Filipino complexion. Change to Female and Right calf,
   change size and complexion, rotate, and zoom. Confirm each settled state is
   spoken once and the anatomy description matches. With TalkBack off briefly,
   tap a supported surface and confirm the picker follows; restore TalkBack and
   verify Explore by Touch does not rotate/select the model.
7. **Reduced motion and rotation.** Enable Reduce anatomy motion, navigate from
   a flat stage to an anatomical stage, and confirm the detail view appears
   without animation and the same textual placement information remains
   available. Rotate the device during rendering and confirm source, stage,
   anatomy selections, enabled state, and focus order restore without a stale
   result replacing the selected stage.
8. **Save.** On Fine outline, activate SAVE current look, choose a new filename
   in the system document UI, and confirm success names Fine outline and the
   format. Cancel a second save and confirm the source/current workspace remain.
   Force or select an unwritable destination where the device permits it and
   confirm a named failure without workspace loss. Repeat with decodable
   BMP/TIFF/GIF input (record a decoder rejection rather than calling it an
   export failure) and confirm the PNG fallback is stated before saving. Compare
   the saved image with the visible stage outside TATAPP.
9. **Error and recovery.** Attempt the corrupt input. Confirm a concise named
   error/dialog and actionable recovery, then dismiss it and verify the prior
   workspace remains usable. Exercise unavailable camera/provider or denied
   destination when the test device can simulate it.
10. **Back and external link.** From Workspace press Back, activate Resume
    workspace, and verify state. Focus BLACK WIDOW TATTOO without activating it
    and confirm no navigation. Double-tap it, verify the device's configured
    default browser opens exactly `https://www.facebook.com/profile.php?id=61583807836781&sk=directory_contact_info`,
    then return and confirm the workspace and TalkBack focus remain usable.
11. **Model consent/error boundary.** Activate Offline AI Describe without an
    installed model. Confirm the dialog states the exact tier and combined size,
    HTTPS/local-storage behavior, and no-upload boundary. Cancel and confirm the
    checkbox returns off with one readable status. On a constrained profile,
    confirm a failed resource gate gives an unavailable explanation and never
    says ready.
12. **Validated-model AI gate.** On a device satisfying every configured gate,
    consent to installation, wait for both exact artifacts to verify, then
    disconnect networking and enable Offline AI Describe. Confirm visible
    progress and heartbeat start/stop together, cancellation stops both, and
    readiness is announced once only after all stages complete.
    Revisit the four stages in step 5 and verify each cached description updates
    immediately, corresponds to the actual flat/anatomical render, states
    uncertainty honestly, and does not steal focus. Background/foreground the
    app during a fresh preload and confirm it cancels or resumes only as
    documented, never reporting a partial cache ready.
    Before enabling AI, activate **Test processing heartbeat** and hear three
    quiet two-note pulses over about ten seconds at the device's accessibility
    volume. Confirm the label becomes **Stop heartbeat test**, activate it to
    stop a repeat test immediately, and repeat once using Back. Confirm each
    start/stop/failure status is spoken once without moving focus, Workspace
    navigation remains enabled, and Offline AI is disabled only while the test
    is active.
13. **Large text/layout.** Repeat core navigation at maximum font/display scale
    in portrait and landscape. Confirm all controls scroll into view, labels are
    not clipped beyond understanding, focus order remains logical, touch targets
    remain usable, and no control is reachable only by drag or visual position.
14. **Resource and lifecycle stress.** Repeatedly alternate the small and large
    designs, move the slider rapidly, rotate during processing, background and
    foreground the app, cancel work, and exercise simulated low-memory and low-
    storage conditions. Record managed/native memory before and after repeated
    workflows. Confirm retained memory stabilizes, audio stops on every terminal
    path, stale results never replace the current design, and any resource
    reduction is explained instead of ending in an OOM or crash.

Mark physical TalkBack, touchscreen, save-provider, model, and device lifecycle
checks unverified unless a person actually performs them on the exact final APK.
