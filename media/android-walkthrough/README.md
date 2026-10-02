# Android walkthrough production inputs

These tracked files define the reproducible edit; generated media remains under
the ignored `artifacts/android-walkthrough/final/` directory.

`timeline.template.json` uses only checksum-verified foreground captures and
screenshots. Its 1920-by-1080 layout keeps the complete portrait device at
`x=1392`, `y=60`, `432x960`, reserves a left guide column, and places a
timeline-defined live crop in the center. The edit visibly covers all three
licensed designs used by the Windows walkthrough: Wolf Head Painting, time is
running out, and Sugar Skull - Coloured. It uses the real Poco captures 09/10,
11/11b/12, and 13/14/15, followed by the checksum-locked sugar-skull Stage 16
JPEG produced by the Android save flow. Long silent stage sweeps are accelerated
deterministically; retained TalkBack/system audio is never time-compressed.

Every app screenshot, capture, and export names its capture session. The older
core 0.3.0 files are checksum-verified, but their exact per-source installed APK
and commit were not recorded; that session is explicitly
`historical_unresolved`. The separately attributed corrected-link session is
`historical_verified`. Both remain usable for non-release demonstrations but
cannot satisfy a 0.3.1 release-capture gate. The release target is session
`poco_20261002_release_031`: package `com.grayscaleconsultants.tatapp`, version
0.3.1/code 2, APK SHA-256
`7f83b72ae3f1af5024d0dade5c3a220387762ccad5ac7c4f2812cccc0e1f42c9`,
certificate SHA-256
`eac3df9aba3e08437bc988682566f072e52d2dde6bda373daa998cdee74d9f90`, and
commit `db66a1d7a0e7100906dc69b9bd705a7ae3463276`. The renderer and verifier compare
that identity exactly instead of accepting a global APK label detached from the
individual source.

The three required release-evidence sources were captured from that exact APK
on the Poco with TalkBack and checksum-locked:

- `raw/20-workspace-navigation-talkback.mp4`
- `raw/21-processing-heartbeat-talkback.mp4`
- `raw/22-offline-ai-full-ready-talkback.mp4`

The enabled edit windows are 75.0–130.0 seconds for workspace navigation,
62.0–84.5 seconds for the direct heartbeat test, and 55.0–95.0 seconds for the
offline preload-to-ready transition. Complete-source decoding passed. Native
MIUI variable timestamps produce duplicate-DTS warnings when decoded to a null
muxer; the renderer intentionally regenerates CFR timestamps. CPU Whisper
recovered the TalkBack start/completion and ready announcements. Independent
spectral analysis located the three expected two-note pulses at approximately
67.8, 71.7, and 75.8 seconds. These automated checks leave every human media
review gate false.

The historical `ai_ready` segment is now disabled and retained only as
traceable 0.3.0 provenance. The enabled `ai_ready_031` replacement shows the
exact v5 build transitioning from real on-phone Qwen3-VL 2B preprocessing to
one ready announcement and its grounded Stage 1 description. Supporting UI and
device-state evidence records network isolation separately because that fact
cannot be inferred from pixels in an MP4.

Render and validate the tracked inputs without producing media:

```bash
scripts/render-android-walkthrough.py --validate-only
```

Create a review render after Piper, FFmpeg, and the declared inputs are present:

```bash
scripts/render-android-walkthrough.py
scripts/verify-android-walkthrough.py --review
```

The output is 1920x1080 CFR 30, H.264 High Profile Level 4.1, BT.709,
`yuv420p`, with AAC-LC stereo at 48 kHz. The complete Poco portrait remains at
the exact documented geometry while the center pane magnifies the live region
without substituting a synthetic screen. A segment may use a separately
checksum-locked source in the magnifier—for example, the licensed imported
artwork or actual exported JPEG—while the complete device pane remains the real
capture. Music is one continuous, checksum-locked 78 PULSE bed; it is ducked
under Piper narration and retained
TalkBack/system audio. The final program is measured after AAC encoding against
the -16 LUFS and -1.5 dBTP limits. Mastering targets 6 LU of loudness range to
retain codec/measurement margin under the unchanged 7 LU verification ceiling.

The three release-evidence segments use `source_audio_profile` value
`accessibility_evidence`. In those windows the renderer mutes music, generated
transition sound, and narration, and carries captured system audio at a constant
1.0 segment gain without `dynaudnorm`. This preserves the relative level and
timing of TalkBack and the real processing heartbeat. Whole-program mastering
still applies the documented deterministic final gain and peak limit.

Narration uses the checksum-locked Piper Amy medium voice at length scale 0.96;
the spoken product name is deliberately written as “Tat App” for reliable
pronunciation. FFmpeg generates the short transition chime/swoosh from a fixed
expression, so it has no external asset provenance. Any app heartbeat comes
only from retained real system audio; the renderer never synthesizes one.

The older BLACK WIDOW browser capture is deliberately excluded because it
predates the corrected product URL. The included replacement capture and log
show Tat App package-targeting the exact HTTPS request to Chrome, the configured
default browser. Chrome subsequently hands the Facebook link to the installed
Facebook app, where the correct page is visible; the edit does not claim that
the browser remains foreground after the site's app handoff. The distributable
uses a derived clip with an opaque source-space redaction at
`x=40,y=1050,w=1000,h=320` before phone/magnifier composition. This removes a
personal account-name row while retaining the business title and logo. The
manifest records the derived clip hash, source hash, and exact transform; the
unredacted screenshot and UI hierarchy are not distributable evidence.

The renderer writes the MP4, SASRT, resolved timeline, attribution, hashes, and
a release-evidence manifest to `artifacts/android-walkthrough/final/`. Complete
every `REQUIRED_BEFORE_RELEASE` field in the generated manifest, perform the
human checks, and set only checks actually performed to `true`.

The 2026-10-02 review candidate is
`artifacts/android-walkthrough/final/TATAPP-Android-accessible-walkthrough.mp4`
(39,618,709 bytes; SHA-256
`008e67a3b6822b815bf969f008f0dbc7dcebb647a282a48dc543ac4d69039e5e`).
Its accessible subtitle file is
`artifacts/android-walkthrough/final/TATAPP-Android-accessible-walkthrough-SASRT.srt`
(SHA-256
`74e7db8df80e95305eb03c9b8c69efdf19019e289edfd056399781d1a6a6557d`).
The strict review verifier passed 13,251 CFR frames over 441.700 seconds at
-16.2 LUFS and -2.2 dBTP with three non-overlapping SASRT cues. Curated stills,
their checksum list, and a 15-frame contact sheet are in
`artifacts/android-walkthrough/final/keyframes/`. These generated artifacts are
ignored by Git. All human gates in the generated manifest remain false.

After those manual gates are documented, run:

```bash
scripts/verify-android-walkthrough.py \
  --manifest artifacts/android-walkthrough/final/FINAL-MEDIA-MANIFEST.json
```

The verifier checks media structure, full decoding, CFR, loudness, SRT timing
and speech exclusion, hashes, attribution, per-source sessions, exact target
application identity, required release segments, and the evidence-audio
profile. A non-review run cannot pass until workspace navigation, the direct
three-pulse processing heartbeat, and the full Offline AI preload-to-ready flow
all use verified target-session captures. Human release gates additionally
require direct checks of workspace TalkBack navigation, the three real pulses
with TalkBack start/completion, and the single full-preload ready announcement.
It cannot certify visual meaning, pronunciation, comfort, TalkBack player
behavior, or privacy; those remain explicit human release gates.

Changing either verification script invalidates the script hashes stored in an
older generated manifest. Re-render before verifying a new candidate; do not
edit ignored final artifacts merely to make a historical review render pass.
