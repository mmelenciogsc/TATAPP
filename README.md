# TATAPP

TATAPP—Tattoo Art Prepper—is a local Windows 11 desktop application for turning a clear, isolated design image into a tattoo-ready visual reference. A single slider moves through:

1. the original color image;
2. fading color and grayscale;
3. black-and-white binarization;
4. line art;
5. thick, medium, and fine outlines;
6. anatomical previews that reverse the development sequence from fine outline back through full color on the selected body surface.

Use a high-resolution design on a clean or transparent background—not a photograph of an existing tattoo on skin—for the clearest stencil and contour results. The **Save current look** button writes the selected flat stage at full source dimensions and DPI, or captures the anatomical placement preview, in the original input format. JPEG, PNG, BMP, TIFF, and GIF are supported. To protect Windows and an active screenreader from allocation failure, decoded input is capped at 32 megapixels and both loading and full-resolution processing require a two-gigabyte system-memory reserve.

## Anatomical Visualization

The locally generated full-body 3D mannequin can be changed between male and female, dragged or rotated with accessible buttons, zoomed for placement inspection, and selected by tapping a body surface. An equivalent **Body region** dropdown supports keyboard and JAWS operation and stays synchronized with visual taps in both directions. It covers upper-arm and forearm surfaces, wrists, shoulders, upper chest, full chest and abdomen, upper and full back, thighs, and calves.

Body-size and complexion sliders update the model. Defaults are male, the outer left upper arm from deltoid to elbow, 163 centimeters (an average Filipino adult), and light-brown to medium-tan Filipino complexion. After **Fine outline**, eight placement stages show the design wrapped over the chosen contour: fine, medium, and thick outline; line art; black and white; grayscale; returning color; and full color.

## Offline AI Describe

Check **Offline AI Describe** after loading a photo to prepare a vivid, screenreader-ready description of all 16 meaningful flat and anatomical visual states. TATAPP first inventories system and currently available memory, discrete graphics, and dedicated video memory, then safely selects a local Qwen3-VL tier:

| Tier | Selection guidance | Model |
| --- | --- | --- |
| Compact | Default on 8 GB and integrated-graphics systems; requires at least 4 GB currently available | `qwen3-vl:2b-instruct` |
| Balanced | At least 16 GB RAM, 9 GB available, and a discrete GPU with at least 6 GB VRAM | `qwen3-vl:4b-instruct` |
| Professional | At least 32 GB RAM, 16 GB available, and a discrete GPU with at least 10 GB VRAM | `qwen3-vl:8b-instruct` |

Ollama 0.12.7 or newer is the local model runtime. If Ollama is absent, TATAPP offers to open its official Windows download page. If the selected Qwen model is absent, TATAPP explains the download and asks permission before Ollama installs it. Model installation needs internet access; image interpretation does not.

During preparation, the slider is disabled and a quiet two-note heartbeat confirms that work continues. TATAPP processes all stages sequentially with a one-image inference batch, zero model keep-alive, bounded input dimensions, an explicit unload after every stage, and a fresh available-memory check before every model load. If the safety reserve is no longer present, it stops cleanly before inference and retains no partial cache. Qwen sees each actually rendered flat image and each actual captured 3D placement viewport. Once all 16 descriptions are complete, the slider unlocks and changing stage displays and announces its cached description immediately, without inference lag. Exact selected region, body type, size, complexion, and rotation come from application state rather than model guesses.

## Accessibility

The UI is designed for keyboard and screenreader use:

- `Alt+T` opens Windows Camera, `Alt+S` selects an existing photo, and `Alt+A` saves the current look (the exact access key may follow the underlined letter shown by Windows);
- every actionable control has an accessible name and help text;
- the development slider supports Arrow keys and Page Up/Page Down;
- the anatomical model supports mouse/touch selection and drag rotation, while synchronized native radio buttons, dropdown, sliders, and rotation buttons provide the complete keyboard/JAWS path;
- stage changes, camera guidance, loading, saving, and errors use a polite UI Automation live region;
- the preview has a dynamic accessible description containing the file and current visual stage;
- Offline AI descriptions appear in a keyboard-focusable polite live region tested for JAWS-style UI Automation behavior;
- system brushes preserve Windows High Contrast behavior and native focus indicators.

Windows Camera saves into Camera Roll. When the user returns to TATAPP, a newly created supported image is loaded automatically. If Windows or OneDrive uses a different folder, **Select photo** is the accessible fallback.

The prominently displayed **BLACK WIDOW TATTOO** button (`Alt+B`) opens the configured Facebook page in the user's default browser. Its current destination is `https://www.facebook.com/grayscaleconsultants` and is isolated in one application constant so the official Black Widow Tattoo URL can replace it later.

## Build and run

Requirements: Windows 11 and the .NET 10 SDK.

```powershell
.\scripts\test.ps1
dotnet run --project .\src\TATAPP.App\TATAPP.App.csproj
```

Create a self-contained 64-bit Windows build that does not require a separately installed .NET runtime:

```powershell
.\scripts\publish.ps1
```

The runnable output folder is `artifacts\publish\win-x64`; launch `TATAPP.exe` from that folder and keep its companion runtime files together.

The standard image workflow has no third-party runtime dependency. Offline AI Describe optionally uses a separately installed Ollama runtime and Qwen3-VL model. Image pixels are sent only to Ollama's fixed IPv4 loopback address on the same computer; TATAPP performs no image upload or telemetry. See [Third-party notices](THIRD_PARTY_NOTICES.md).

## Accessible walkthrough

The project includes a real Windows 11 and JAWS walkthrough covering three licensed tattoo designs, all visual-development stages, Offline AI preloading, cached descriptions, saving, and the BLACK WIDOW TATTOO action. Its companion SASRT supplies concise visual context without repeating Piper narration or speaking over the recorded JAWS demonstration. Piper, JAWS, effects, and the royalty-free music bed are independently leveled before measured two-pass mastering. See [walkthrough documentation](docs/WALKTHROUGH.md).

## Current scope

TATAPP provides capture/select, deterministic image processing, an interactive accessible 3D placement preview, optional cached offline visual descriptions, and same-format save. It prepares files for the tattoo artist's existing printer software; direct vendor-specific printer drivers and print dialogs are outside the current scope.

See [Architecture](docs/ARCHITECTURE.md) and [Accessibility](docs/ACCESSIBILITY.md) for implementation details.
