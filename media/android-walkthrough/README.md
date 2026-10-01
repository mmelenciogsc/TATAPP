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

The enabled `ai_ready` segment is the only route to an Offline AI readiness
claim. It uses a checksum-locked real-device capture containing the real app
heartbeat, the 16-of-16 ready announcement, and a cached description. Its
supporting manifest records the separate UI and device-state evidence for
network isolation; that fact is not inferred from pixels in the MP4. The
renderer fails closed if an enabled readiness claim lacks a verified capture
and SHA-256. The separate preprocessing-only clip remains disabled and is
explicitly labeled as not proving readiness.

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
capture. Music is one continuous,
checksum-locked 78 PULSE bed; it is ducked under Piper narration and retained
TalkBack/system audio. The final program is measured after AAC encoding against
the -16 LUFS and -1.5 dBTP limits.

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
human checks, set only checks actually performed to `true`, then run:

```bash
scripts/verify-android-walkthrough.py \
  --manifest artifacts/android-walkthrough/final/FINAL-MEDIA-MANIFEST.json
```

The verifier checks media structure, full decoding, CFR, loudness, SRT timing
and speech exclusion, hashes, attribution, and provenance. It cannot certify
visual meaning, pronunciation, comfort, TalkBack player behavior, or privacy;
those remain explicit human release gates.
