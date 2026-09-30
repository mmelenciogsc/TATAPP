# Architecture

## Projects

- `TATAPP.Core` contains the framework-independent BGRA32 image model, 16-state stage catalog, body-region/default profiles, tattoo-ink texture preparation, format catalog, and deterministic transformation engine.
- `TATAPP.App` is a native WPF Windows 11 UI. It handles decoding, EXIF orientation, preview scaling, camera handoff, generated WPF 3D anatomy, synchronized visual/accessible placement controls, asynchronous rendering, atomic same-format encoding, and the optional local-Qwen accessibility workflow.
- `TATAPP.Tests` is a dependency-free executable test harness covering pixel behavior, outline ordering, anatomy defaults and regions, ink texture extraction, synchronized selection paths, determinism, cancellation, encoders, model selection, local API requests, complete description preloading, UI Automation metadata, and camera fallback behavior.

## Processing continuum

The engine composites transparent input pixels onto white for printable derived stages. Its source-domain render values interpolate color into perceptual grayscale, move through an Otsu-derived black/white threshold, blend stencil regions into Sobel-derived line art, and decrease morphological line thickness from four pixels to one source-pixel edge. Before WPF materializes decoded pixels, the loader rejects images above 32 megapixels or images whose estimated buffers would consume a reserved two gigabytes needed by Windows and assistive technology. Full-resolution derived saves repeat the current-memory check before allocating their larger working set.

The UI catalog maps the first eight semantic states to those source-domain renders. The next eight reverse their source representations—fine outline through full color—and apply them to a curved surface mesh. `TattooInkTexture` estimates and removes the clean design-sheet background, darkens marks to resemble ink beneath skin, retains supported color, and maps the result through WPF texture coordinates. Slider zero returns a byte-identical clone. Preview rendering uses a bounded 1600-pixel image and cancellation/debouncing. A flat save reruns the same engine against the full-resolution source; a placement save captures the exact final 3D viewport without reframing or cropping it.

## Generated anatomy and synchronized selection

`AnatomyViewportController` constructs a full-body mannequin from local ellipsoid and cylinder meshes, so no network model, WebView, or third-party 3D runtime is required. Its placement patches sit just above the relevant curved surface and provide texture coordinates for upper arms, forearms, wrists, shoulders, chest/abdomen, back, thighs, and calves. Hit testing maps visual taps—including inner versus outer arm surfaces—to `BodyRegionKind`; the main window then updates the native dropdown. Dropdown selection calls the same controller path and reorients the model to the selected surface. Sex, model-height scale, complexion, rotation, and zoom remain explicit application state and feed UI Automation descriptions.

Every anatomical development stage uses a deterministic region-aware camera plan. It begins with a context frame that keeps the selected surface and useful neighboring anatomy visible—for example, head, neck, left shoulder, upper arm, and upper-left chest—holds briefly, and then eases into a close inspection frame centered on the applied design. The plan covers every body region and stores separate context and detail targets and distances. A stage change replays the context-to-detail movement; manual rotation or zoom safely takes over from the camera animation. Offline AI capture bypasses motion and uses the same plan's settled detail frame, ensuring its description is grounded in the image users actually inspect.

## Offline AI description pipeline

Offline AI Describe is opt-in and inactive by default. Enabling it for a loaded photo performs this ordered workflow:

1. `GlobalMemoryStatusEx`, display-adapter registry data, and `nvidia-smi` when available provide a conservative hardware snapshot.
2. The selector chooses Qwen3-VL 2B, 4B, or 8B using conservative total/free-RAM and dedicated-VRAM thresholds. Eight-gigabyte and integrated-graphics systems always use 2B. The 4B tier requires 16 GB RAM, 9 GB currently available, and 6 GB dedicated VRAM; 8B requires 32 GB RAM, 16 GB available, and 10 GB dedicated VRAM.
3. TATAPP checks Ollama 0.12.7 or newer over the fixed `127.0.0.1:11434` endpoint with proxy use disabled. Missing model installation occurs only after explicit consent.
4. The deterministic engine renders one representative image for each of the eight flat states. For each of the eight placement states, TATAPP applies the corresponding reverse-stage texture and captures the actual WPF 3D viewport. Images are resized to the selected model tier's limit and encoded as in-memory PNGs.
5. The local model describes all 16 states strictly sequentially. Images, context, and output tokens are bounded; TATAPP leaves the backend's model-aware token-batch sizing intact because forcing a token batch of one is invalid for vision embeddings. Keep-alive is zero, the model receives an explicit unload after every state, large temporary buffers are reclaimed between states, and current available memory is rechecked before every render and model load. A lost safety reserve stops the operation before another inference begins. The prompt includes the authoritative stage name, processing definition, and verified subject context from the original stage, requires exactly two concise complete sentences, keeps descriptions within visible evidence, and tells the model not to infer body attributes that application state already knows.
6. Results are committed to the stage cache only after all 16 descriptions succeed. Until then the slider is disabled, progress is exposed through UI Automation, and a low-volume heartbeat sounds every four seconds.
7. Once committed, slider stage changes are dictionary lookups followed by a polite live-region event; no inference occurs during slider movement.

Cancellation or any runtime, model, memory, or malformed-response failure discards the incomplete cache, disables Offline AI Describe, restores the standard deterministic slider, and provides an accessible explanation. It never presents a partially prepared description set as complete.

Ollama and Qwen weights are not bundled. The only remote navigation is the official Ollama download page after a user's explicit choice, and model downloads are performed by the local Ollama service after separate consent. Photograph data is never sent to that download site or any remote model API.

## External organization link

The BLACK WIDOW TATTOO action uses Windows shell navigation to open one HTTPS constant in the user's default browser. It does not embed a browser, authenticate with Facebook, transmit photo data, or run while merely focused. Navigation occurs only after the button is activated, and shell-launch failure returns through TATAPP's accessible error path.

## File integrity

Decoding uses WPF's built-in codecs with `OnLoad`, so source files remain unlocked. Common EXIF orientation values are normalized. Saving writes a sibling `.partial` file with the encoder selected from the source format and then atomically replaces the requested destination. JPEG uses quality 95; TIFF uses ZIP compression. Pixel dimensions and source DPI are retained.

## Camera workflow

TATAPP launches the trusted Windows Camera application through its registered URI. Before launch it snapshots supported files in local and OneDrive Camera Roll locations. After TATAPP loses and regains focus, it loads the newest qualifying file. No camera frame, photograph, or metadata leaves the computer.
