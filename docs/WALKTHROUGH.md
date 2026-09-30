# Accessible video walkthrough

The walkthrough is a real 1920-by-1080, 30 FPS capture of Tattoo Art Prepper running on Windows 11 with JAWS 2025 active. It uses three clean, isolated design images rather than photographs of tattoos on skin. It demonstrates all eight flat preparation stages, the eight reverse-order anatomical placement stages, synchronized visual and dropdown body-region selection, sex/body-size/complexion/rotation controls, Offline AI Describe preloading, four instant cached wolf descriptions read by JAWS at levels 1, 8, 9, and 16, saving the current look, and the BLACK WIDOW TATTOO action. The other two designs remain visual demonstrations and produce no recorded JAWS speech.

## Deliverables

- `artifacts/walkthrough/TATAPP-accessible-walkthrough.mp4` — 271.467-second H.264/AAC walkthrough with a synchronized 1.5-second visual and 0.8-second whole-mix opening fade, a 2-second visual and 2.5-second whole-mix closing fade, normalized Piper narration, four selectively retained and reduced-level JAWS descriptions, synchronized interface tones, a ducked royalty-free music bed, and concise on-screen section labels.
- `artifacts/walkthrough/TATAPP-accessible-walkthrough-compact.mp4` — under-20-MB two-pass H.264/AAC edition that retains the full 1920-by-1080 frame, 30 FPS cadence, complete revised duration, fades, and accessibility audio. Rebuild it with `scripts/create-compact-walkthrough.sh`.
- `artifacts/walkthrough/COMPACT_ENCODING_AUDIT.json` — compact-edition size, stream, frame-count, loudness, structural-similarity, decode, and checksum verification.
- `artifacts/walkthrough/TATAPP-accessible-walkthrough-SASRT.srt` — optional screenreader-accessible visual-description track. It does not transcribe the narration, and it deliberately remains silent during the recorded JAWS demonstration.
- `artifacts/walkthrough/SASRT-audit.json` — cue timing, speech-budget, source-state, inclusion-reason, and validation record.
- `artifacts/walkthrough/AUDIO_MASTERING_AUDIT.json` — final loudness targets, measured results, component checks, and mixing policy.
- `artifacts/walkthrough/saved/sugar-skull-left-upper-arm-full-color.jpg` — the composed anatomical-placement JPEG saved during the recorded workflow.
- `artifacts/walkthrough/ASSET_ATTRIBUTION.json` — machine-readable attribution and license details for every design used in the final edit.
- `artifacts/walkthrough/final-edit-timeline.json` — authoritative capture offsets and final scene boundaries used by the render and accessibility artifacts.
- `artifacts/walkthrough/screenshots/` — 21 curated 1920-by-1080 screenshots covering the walkthrough’s most meaningful states, plus a contact sheet, a readable accessibility guide, and machine-readable one-paragraph alt-text for every image.

The walkthrough is rebuilt from the real capture with `scripts/render-walkthrough.sh`. The renderer derives the recorder pre-roll offset from the authoritative capture timeline, so every anatomical stage retains its complete context hold and smooth context-to-detail camera move. A separate post-cache source produced by `scripts/record-offline-jaws.ps1` supplies speech-tight JAWS demonstrations without making FFmpeg compete with Qwen for memory. Piper narration is generated transactionally by `scripts/synthesize-walkthrough.ps1`, which records the exact narration-script SHA-256; mastering refuses stale or incomplete narration audio. The complete Windows interaction capture remains reproducible through `scripts/record-walkthrough.ps1`.

## Licensed isolated demonstration designs

1. “Wolf Head Painting,” a high-resolution isolated frontal wolf portrait by AlepouTheFox, [Wikimedia Commons](https://commons.wikimedia.org/wiki/File:Wolf_Head_Painting.png), CC BY-SA 3.0.
2. “time is running out,” an isolated clock-and-roses design by __april, [Flickr](https://www.flickr.com/photos/26044298@N02/4077997473), CC BY 2.0.
3. “Sugar Skull - Coloured” by __april, [Flickr](https://www.flickr.com/photos/26044298@N02/4311331263), CC BY 2.0.

Background music: “78 PULSE” by kjartan_abel, [Freesound](https://freesound.org/people/kjartan_abel/sounds/541944), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The calm cinematic synth pulse is crossfaded into a continuous bed and smoothly ducked beneath Piper and JAWS speech.

The demonstration artwork was obtained from Wikimedia Commons and sources discovered through the Openverse API. Original files, source URLs, license URLs, and retrieval dates are retained in `ASSET_ATTRIBUTION.json`.

## Accessibility-editing rules

The spoken tutorial and SASRT have separate jobs. Piper explains actions and intent. The SASRT contributes only unavailable visual facts: composition, spatial state, visible transformation results, save confirmation, and the closing comparison. Cue windows are planned at 180 words per minute, do not overlap Piper narration, remain inside the exact media duration, and leave the entire recorded JAWS demonstration free of competing SASRT speech.

Audio mastering is reproducible through `scripts/remaster-walkthrough-audio.sh`. Each Piper cue is normalized independently. Recorded system audio is gated to the four requested JAWS description windows, normalized in short windows, reduced by 3.1 dB, and softly faded at every boundary; all slider, dialog, progress, and non-wolf JAWS speech is excluded. Interface tones use a bounded expression, and the complete mixed program receives smooth boundary fades before measured two-pass normalization to -16 LUFS with a -1.5 dB true-peak ceiling. Recording and rendering use bounded queues, no parallel segment jobs, two FFmpeg threads, and a free-memory preflight before each process; interrupted jobs retain their completed intermediates for a safe retry.
