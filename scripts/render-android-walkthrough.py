#!/usr/bin/env python3
"""Render the Android walkthrough from checksum-locked real captures."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import tempfile
import textwrap
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_TIMELINE = ROOT / "media/android-walkthrough/timeline.template.json"
DEFAULT_ATTRIBUTION = ROOT / "media/android-walkthrough/attribution.template.json"
DEFAULT_MANIFEST = ROOT / "media/android-walkthrough/final-media-manifest.template.json"
DEFAULT_OUTPUT = ROOT / "artifacts/android-walkthrough/final"
CAPTURE_PROVENANCE_KINDS = {"app_capture", "app_screenshot", "app_export"}
CAPTURE_SESSION_STATES = {
    "historical_unresolved",
    "historical_verified",
    "verified",
    "awaiting_real_capture",
}
REQUIRED_RELEASE_FEATURES = (
    "workspace_navigation",
    "processing_heartbeat",
    "offline_ai_full_ready",
)
SOURCE_AUDIO_PROFILES = {"dialogue", "accessibility_evidence"}
EXPECTED_RELEASE_APPLICATION = {
    "package": "com.grayscaleconsultants.tatapp",
    "version_name": "0.3.1",
    "version_code": 2,
    "installed_apk_sha256": "7f83b72ae3f1af5024d0dade5c3a220387762ccad5ac7c4f2812cccc0e1f42c9",
    "signing_certificate_sha256": "eac3df9aba3e08437bc988682566f072e52d2dde6bda373daa998cdee74d9f90",
    "git_commit": "db66a1d7a0e7100906dc69b9bd705a7ae3463276",
}


def fail(message: str) -> None:
    raise SystemExit(f"Android walkthrough render stopped: {message}")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def repo_path(value: str) -> Path:
    path = Path(value)
    return path if path.is_absolute() else ROOT / path


def portable_path(path: Path) -> str:
    resolved = path.resolve()
    try:
        return str(resolved.relative_to(ROOT))
    except ValueError:
        return str(resolved)


def run(
    command: list[str], *, input_text: str | None = None, capture: bool = False
) -> str:
    result = subprocess.run(
        command,
        input=input_text,
        text=True,
        stdout=subprocess.PIPE if capture else None,
        stderr=subprocess.PIPE if capture else None,
        check=False,
    )
    if result.returncode:
        detail = (result.stderr or result.stdout or "").strip()
        fail(f"command failed ({result.returncode}): {' '.join(command)}\n{detail}")
    return result.stdout or ""


def require_tools(*names: str) -> None:
    missing = [name for name in names if shutil.which(name) is None]
    if missing:
        fail("missing required command(s): " + ", ".join(missing))


def is_sha256(value: object) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def capture_application(session: dict) -> dict:
    application = session.get("application", {})
    return {
        "package": application.get("package"),
        "version_name": application.get("version_name"),
        "version_code": application.get("version_code"),
        "installed_apk_sha256": application.get("apk_sha256"),
        "signing_certificate_sha256": application.get("signing_certificate_sha256"),
        "git_commit": application.get("git_commit"),
    }


def load_json(path: Path) -> dict:
    try:
        with path.open(encoding="utf-8") as stream:
            value = json.load(stream)
    except (OSError, json.JSONDecodeError) as error:
        fail(f"cannot read {path}: {error}")
    if not isinstance(value, dict):
        fail(f"{path} must contain a JSON object")
    return value


def probe_visual_dimensions(path: Path) -> tuple[int, int]:
    value = (
        run(
            [
                "ffprobe",
                "-v",
                "error",
                "-select_streams",
                "v:0",
                "-show_entries",
                "stream=width,height",
                "-of",
                "csv=p=0",
                str(path),
            ],
            capture=True,
        )
        .strip()
        .splitlines()
    )
    if not value:
        fail(f"no visual stream found in {path}")
    try:
        width, height = value[0].split(",")[:2]
        return int(width), int(height)
    except (TypeError, ValueError):
        fail(f"invalid visual dimensions reported for {path}: {value[0]!r}")


def has_audio_stream(path: Path) -> bool:
    return bool(
        run(
            [
                "ffprobe",
                "-v",
                "error",
                "-select_streams",
                "a:0",
                "-show_entries",
                "stream=index",
                "-of",
                "csv=p=0",
                str(path),
            ],
            capture=True,
        ).strip()
    )


def validate_configuration(timeline: dict, attribution: dict) -> list[str]:
    warnings: list[str] = []
    if timeline.get("schema_version") != 1:
        fail("unsupported timeline schema")
    output = timeline.get("output", {})
    expected = {
        "width": 1920,
        "height": 1080,
        "fps": 30,
        "video_codec": "h264",
        "video_profile": "High",
        "video_level": 41,
        "pixel_format": "yuv420p",
        "color_space": "bt709",
        "audio_codec": "aac",
        "audio_sample_rate": 48000,
        "audio_channels": 2,
    }
    for key, value in expected.items():
        if output.get(key) != value:
            fail(f"output.{key} must be {value!r}")
    if output.get("phone") != {"x": 1392, "y": 60, "width": 432, "height": 960}:
        fail("the phone viewport must remain x=1392, y=60, 432x960")
    if output.get("magnifier") != {
        "x": 620,
        "y": 230,
        "width": 720,
        "height": 720,
    }:
        fail("the magnifier must remain x=620, y=230, 720x720")

    tools = timeline.get("tools", {})
    for key in ("piper", "piper_model", "piper_config", "font_regular", "font_bold"):
        path = repo_path(str(tools.get(key, "")))
        if not path.is_file():
            fail(f"declared tool asset is missing: {key}={path}")
    for key, hash_key in (
        ("piper", "piper_sha256"),
        ("piper_model", "piper_model_sha256"),
        ("piper_config", "piper_config_sha256"),
        ("font_regular", "font_regular_sha256"),
        ("font_bold", "font_bold_sha256"),
    ):
        path = repo_path(tools[key])
        if sha256(path) != tools.get(hash_key):
            fail(f"{key} checksum does not match the timeline")

    sessions = timeline.get("capture_sessions")
    target_session_id = timeline.get("release_target_capture_session")
    if not isinstance(sessions, dict) or not sessions:
        fail("timeline.capture_sessions must be a nonempty object")
    if not isinstance(target_session_id, str) or target_session_id not in sessions:
        fail("release_target_capture_session must name a declared capture session")
    for session_id, session in sessions.items():
        state = session.get("state")
        if state not in CAPTURE_SESSION_STATES:
            fail(f"capture session {session_id} has invalid state {state!r}")
        if state != "awaiting_real_capture" and not session.get("capture_date"):
            fail(f"capture session {session_id} has no capture date")
        application = capture_application(session)
        if (
            not application["package"]
            or not application["version_name"]
            or not isinstance(application["version_code"], int)
            or application["version_code"] <= 0
            or not (
                is_sha256(application["installed_apk_sha256"])
                or (
                    state == "historical_unresolved"
                    and application["installed_apk_sha256"] is None
                )
            )
            or not is_sha256(application["signing_certificate_sha256"])
        ):
            fail(f"capture session {session_id} has invalid application identity")
        commit = application["git_commit"]
        if commit is not None and not re.fullmatch(r"[0-9a-f]{40}", str(commit)):
            fail(f"capture session {session_id} has an invalid Git commit")
        if session_id == target_session_id and commit is None:
            fail("the release-target capture session must declare its Git commit")

    sources = timeline.get("sources")
    if not isinstance(sources, dict) or not sources:
        fail("timeline.sources must be a nonempty object")
    for source_id, source in sources.items():
        state = source.get("state")
        expected_hash = source.get("sha256")
        path = repo_path(str(source.get("path", "")))
        provenance_kind = source.get("provenance_kind")
        capture_session_id = source.get("capture_session")
        if provenance_kind in CAPTURE_PROVENANCE_KINDS:
            if capture_session_id not in sessions:
                fail(f"source {source_id} has no declared capture session")
            if (
                state != "awaiting_real_capture"
                and sessions[capture_session_id].get("state") == "awaiting_real_capture"
            ):
                fail(
                    f"source {source_id} claims captured media from an awaiting session"
                )
        elif provenance_kind == "licensed_asset":
            if capture_session_id is not None:
                fail(f"licensed source {source_id} cannot declare a capture session")
        else:
            fail(f"source {source_id} has invalid provenance_kind")
        if state == "awaiting_real_capture":
            if expected_hash is not None:
                fail(
                    f"awaiting source {source_id} must not carry an unverified checksum"
                )
            warnings.append(
                f"{source_id} is awaiting a real capture and remains unusable"
            )
            continue
        if not path.is_file():
            fail(f"source is missing: {source_id}={path}")
        if not is_sha256(expected_hash):
            fail(f"source {source_id} has no valid SHA-256")
        if sha256(path) != expected_hash:
            fail(f"source checksum mismatch: {source_id}")
        if source_id != "music":
            dimensions = source.get("dimensions", [1080, 2400])
            if (
                not isinstance(dimensions, list)
                or len(dimensions) != 2
                or not all(isinstance(value, int) and value > 0 for value in dimensions)
            ):
                fail(f"source {source_id} has invalid declared dimensions")
            if probe_visual_dimensions(path) != tuple(dimensions):
                fail(f"source {source_id} does not match its declared dimensions")

    segments = timeline.get("segments")
    if not isinstance(segments, list) or not segments:
        fail("timeline.segments must be a nonempty array")
    identifiers = [str(segment.get("id", "")) for segment in segments]
    if any(not identifier for identifier in identifiers) or len(identifiers) != len(
        set(identifiers)
    ):
        fail("timeline contains an empty or duplicate segment id")
    release_features: set[str] = set()
    enabled_claims = 0
    for segment in segments:
        segment_id = str(segment["id"])
        enabled = segment.get("enabled", True)
        if not isinstance(enabled, bool):
            fail(f"segment {segment_id} enabled must be true or false")
        if segment.get("kind") not in ("capture", "still"):
            fail(f"segment {segment_id} has unsupported kind")
        if segment.get("source") not in sources:
            fail(f"segment {segment_id} references an unknown source")
        source = sources[segment["source"]]
        if source.get("dimensions", [1080, 2400]) != [1080, 2400]:
            fail(f"segment {segment_id} phone source must be an exact 1080x2400 image")
        magnifier_source_id = segment.get("magnifier_source")
        if magnifier_source_id is not None and magnifier_source_id not in sources:
            fail(f"segment {segment_id} references an unknown magnifier source")
        magnifier_source = (
            sources[magnifier_source_id] if magnifier_source_id is not None else source
        )
        magnifier_dimensions = magnifier_source.get("dimensions", [1080, 2400])
        crop = segment.get("crop", [0, 0, 1080, 1080])
        if (
            not isinstance(crop, list)
            or len(crop) != 4
            or not all(isinstance(value, int) for value in crop)
            or crop[0] < 0
            or crop[1] < 0
            or crop[2] <= 0
            or crop[3] <= 0
            or crop[0] + crop[2] > magnifier_dimensions[0]
            or crop[1] + crop[3] > magnifier_dimensions[1]
        ):
            fail(f"segment {segment_id} crop must fit its magnifier source")

        release_feature = segment.get("release_feature")
        if release_feature is not None:
            if release_feature not in REQUIRED_RELEASE_FEATURES:
                fail(f"segment {segment_id} declares an unknown release feature")
            if release_feature in release_features:
                fail(f"release feature {release_feature} is declared more than once")
            release_features.add(release_feature)
        audio_profile = segment.get("source_audio_profile", "dialogue")
        if audio_profile not in SOURCE_AUDIO_PROFILES:
            fail(f"segment {segment_id} has an unknown source audio profile")
        if audio_profile == "accessibility_evidence" and (
            segment.get("kind") != "capture"
            or not segment.get("retain_source_audio")
            or segment.get("narration")
        ):
            fail(
                f"segment {segment_id} accessibility evidence must retain only capture audio"
            )

        if not enabled:
            if source.get("state") != "awaiting_real_capture" and (
                segment.get("duration") is None or segment.get("source_start") is None
            ):
                fail(
                    f"disabled segment {segment_id} may omit timing only while its source awaits capture"
                )
            continue
        try:
            duration = float(segment.get("duration", 0))
            playback_rate = float(segment.get("playback_rate", 1.0))
        except (TypeError, ValueError):
            fail(f"segment {segment_id} has invalid numeric timing")
        if duration <= 0:
            fail(f"segment {segment_id} must have a positive duration")
        if source.get("state") == "awaiting_real_capture":
            fail(f"segment {segment_id} enables a source that has not been captured")
        if magnifier_source.get("state") == "awaiting_real_capture":
            fail(
                f"segment {segment_id} enables a magnifier source that has not been captured"
            )
        if not 0.25 <= playback_rate <= 4.0:
            fail(f"segment {segment_id} playback rate must be between 0.25 and 4")
        if segment["kind"] != "capture" and playback_rate != 1.0:
            fail(f"still segment {segment_id} cannot declare a playback rate")
        if release_feature is not None:
            if source.get("capture_session") != target_session_id:
                fail(
                    f"release segment {segment_id} does not use the release-target capture session"
                )
            if sessions[target_session_id].get("state") != "verified":
                fail(
                    f"release segment {segment_id} enables an unverified release-target session"
                )
        if segment.get("claim") == "offline_ai_ready":
            enabled_claims += 1
            if source.get("state") != "verified" or not source.get("sha256"):
                fail(
                    "Offline AI readiness cannot be enabled without a verified real capture and SHA-256"
                )
        if segment.get("narration") and segment.get("retain_source_audio"):
            fail(
                f"segment {segment_id} cannot overlap narration and retained system speech"
            )
        replacement = segment.get("replaces_segment")
        if replacement is not None:
            if replacement not in identifiers:
                fail(f"segment {segment_id} replaces an unknown segment")
            replaced = next(item for item in segments if item["id"] == replacement)
            if replaced.get("enabled", True):
                fail(
                    f"segment {segment_id} cannot be enabled until {replacement} is disabled"
                )
        if segment["kind"] == "capture":
            source_path = repo_path(source["path"])
            try:
                source_start = float(segment.get("source_start", 0))
            except (TypeError, ValueError):
                fail(f"segment {segment_id} has invalid source_start")
            if segment.get("retain_source_audio") and playback_rate != 1.0:
                fail(f"segment {segment_id} cannot speed retained system audio")
            if source_start < 0 or (
                source_start + duration * playback_rate
                > probe_duration(source_path) + 0.050
            ):
                fail(f"segment {segment_id} exceeds its capture bounds")
            if segment.get("retain_source_audio") and not has_audio_stream(source_path):
                fail(
                    f"segment {segment_id} requests retained audio but its capture has none"
                )
        cue = segment.get("srt")
        if cue:
            start = float(cue.get("start_offset", -1))
            end = float(cue.get("end_offset", -1))
            if not (0 <= start < end <= duration):
                fail(f"segment {segment_id} has invalid SRT offsets")
            words = len(
                re.findall(r"\b[\w’'-]+\b", str(cue.get("text", "")), re.UNICODE)
            )
            if words == 0 or words / ((end - start) / 60.0) > 180.0:
                fail(
                    f"segment {segment_id} SRT exceeds 180 words per minute or is empty"
                )
            if segment.get("narration") or segment.get("retain_source_audio"):
                fail(f"segment {segment_id} SRT overlaps a declared speech segment")
    missing_feature_plans = set(REQUIRED_RELEASE_FEATURES) - release_features
    if missing_feature_plans:
        fail(
            "timeline omits required release-feature plans: "
            + ", ".join(sorted(missing_feature_plans))
        )
    if enabled_claims > 1:
        fail("only one Offline AI ready claim segment may be enabled")

    if attribution.get("schema_version") != 1 or not attribution.get("assets"):
        fail("attribution template is missing its asset records")
    for asset in attribution["assets"]:
        local = asset.get("local_path")
        expected_hash = asset.get("sha256")
        if local and expected_hash:
            path = repo_path(local)
            if not path.is_file() or sha256(path) != expected_hash:
                fail(f"attribution asset mismatch: {asset.get('title', local)}")
        if str(asset.get("license", "")).startswith("REQUIRED"):
            warnings.append(
                f"release attribution remains unresolved: {asset.get('title')}"
            )
    return warnings


def escape_filter_path(path: Path) -> str:
    return str(path).replace("\\", "\\\\").replace(":", "\\:").replace("'", "\\'")


def probe_duration(path: Path) -> float:
    value = run(
        [
            "ffprobe",
            "-v",
            "error",
            "-show_entries",
            "format=duration",
            "-of",
            "default=noprint_wrappers=1:nokey=1",
            str(path),
        ],
        capture=True,
    ).strip()
    try:
        return float(value)
    except ValueError:
        fail(f"ffprobe returned no duration for {path}")


def write_wrapped_text(path: Path, value: str, width: int) -> None:
    wrapper = textwrap.TextWrapper(
        width=width,
        break_long_words=False,
        break_on_hyphens=False,
    )
    lines: list[str] = []
    for source_line in value.strip().splitlines():
        lines.extend(wrapper.wrap(source_line) or [""])
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def synthesize_narration(segment: dict, tools: dict, directory: Path) -> Path:
    raw = directory / f"{segment['id']}-raw.wav"
    normalized = directory / f"{segment['id']}.wav"
    run(
        [
            str(repo_path(tools["piper"])),
            "--model",
            str(repo_path(tools["piper_model"])),
            "--config",
            str(repo_path(tools["piper_config"])),
            "--output_file",
            str(raw),
            "--length-scale",
            "0.96",
            "--sentence-silence",
            "0.20",
        ],
        input_text=str(segment["narration"]) + "\n",
    )
    run(
        [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-i",
            str(raw),
            "-af",
            "aresample=48000,aformat=channel_layouts=stereo,"
            "loudnorm=I=-16.5:TP=-2:LRA=4,aresample=48000",
            "-ar",
            "48000",
            "-ac",
            "2",
            "-c:a",
            "pcm_s24le",
            str(normalized),
        ]
    )
    if probe_duration(normalized) + 0.65 > float(segment["duration"]) - 0.25:
        fail(f"narration for {segment['id']} does not fit its segment")
    return normalized


def render_segment(
    segment: dict,
    source: Path,
    magnifier_source: Path | None,
    music: Path,
    music_start: float,
    narration: Path | None,
    transition_sfx: Path,
    output_path: Path,
    work: Path,
    first: bool,
    last: bool,
    settings: dict,
    fonts: dict,
) -> None:
    duration = float(segment["duration"])
    kind = segment["kind"]
    playback_rate = float(segment.get("playback_rate", 1.0))
    header_file = work / f"{segment['id']}-header.txt"
    subheader_file = work / f"{segment['id']}-subheader.txt"
    write_wrapped_text(header_file, str(segment.get("header", "")), 20)
    write_wrapped_text(subheader_file, str(segment.get("subheader", "")), 34)
    crop = [int(value) for value in segment.get("crop", [0, 0, 1080, 1080])]
    if len(crop) != 4 or crop[2] <= 0 or crop[3] <= 0:
        fail(f"segment {segment['id']} has an invalid crop")

    if kind == "capture":
        command = [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-ss",
            str(float(segment.get("source_start", 0))),
            "-t",
            str(duration * playback_rate),
            "-i",
            str(source),
        ]
    else:
        command = [
            "ffmpeg",
            "-hide_banner",
            "-loglevel",
            "error",
            "-y",
            "-loop",
            "1",
            "-framerate",
            "30",
            "-t",
            str(duration),
            "-i",
            str(source),
        ]
    if magnifier_source is not None:
        command += [
            "-loop",
            "1",
            "-framerate",
            "30",
            "-t",
            str(duration),
            "-i",
            str(magnifier_source),
        ]
    music_index = 2 if magnifier_source is not None else 1
    command += ["-ss", f"{music_start:.6f}", "-i", str(music)]
    if narration is not None:
        command += ["-i", str(narration)]
    use_transition_sfx = bool(segment.get("narration"))
    if use_transition_sfx:
        command += ["-i", str(transition_sfx)]

    fade = ""
    if first:
        fade += ",fade=t=in:st=0:d=0.8"
    if last:
        fade_start = max(0.0, duration - 2.0)
        fade += f",fade=t=out:st={fade_start:.3f}:d=2.0"
    phone = settings["phone"]
    mag = settings["magnifier"]
    regular = escape_filter_path(repo_path(fonts["font_regular"]))
    bold = escape_filter_path(repo_path(fonts["font_bold"]))
    header = escape_filter_path(header_file)
    subheader = escape_filter_path(subheader_file)
    if magnifier_source is not None:
        source_video_filter = (
            f"[0:v]setpts=(PTS-STARTPTS)/{playback_rate:.8f},fps=30,format=yuv420p[phonein];"
            "[1:v]fps=30,format=yuv420p[cropin];"
        )
    else:
        source_video_filter = (
            f"[0:v]setpts=(PTS-STARTPTS)/{playback_rate:.8f},fps=30,format=yuv420p,"
            "split=2[phonein][cropin];"
        )
    video_filter = (
        f"color=c=0x111018:s=1920x1080:r=30:d={duration}[bg];"
        f"{source_video_filter}"
        f"[phonein]scale={phone['width']}:{phone['height']}:flags=lanczos[phone];"
        f"[cropin]crop={crop[2]}:{crop[3]}:{crop[0]}:{crop[1]},"
        f"scale={mag['width']}:{mag['height']}:force_original_aspect_ratio=decrease:flags=lanczos,"
        f"pad={mag['width']}:{mag['height']}:(ow-iw)/2:(oh-ih)/2:0x08080c[crop];"
        f"[bg][crop]overlay={mag['x']}:{mag['y']}[b1];"
        f"[b1][phone]overlay={phone['x']}:{phone['y']},"
        f"drawbox=x=584:y=60:w=2:h=960:color=0x9c6ade:t=fill,"
        f"drawbox=x={mag['x'] - 3}:y={mag['y'] - 3}:w={mag['width'] + 6}:h={mag['height'] + 6}:color=0x9c6ade:t=3,"
        f"drawbox=x={phone['x'] - 3}:y={phone['y'] - 3}:w={phone['width'] + 6}:h={phone['height'] + 6}:color=0x9c6ade:t=3,"
        f"drawtext=fontfile='{bold}':textfile='{header}':fontcolor=white:fontsize=34:"
        f"line_spacing=6:x=60:y=140,"
        f"drawtext=fontfile='{regular}':textfile='{subheader}':fontcolor=0xd9c8ef:fontsize=24:"
        f"line_spacing=10:x=60:y=250,"
        f"drawtext=fontfile='{bold}':text='MAGNIFIED LIVE VIEW':fontcolor=white:fontsize=24:x={mag['x']}:y=185,"
        f"drawtext=fontfile='{regular}':text='COMPLETE DEVICE VIEW':fontcolor=white:fontsize=22:x={phone['x']}:y=28"
        f"{fade}[v]"
    )

    audio_profile = segment.get("source_audio_profile", "dialogue")
    speech_label: str | None = None
    music_filter = "volume=0," if audio_profile == "accessibility_evidence" else ""
    audio_filter = (
        f"[{music_index}:a]atrim=0:{duration},asetpts=PTS-STARTPTS,aresample=48000,"
        "aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo,"
        f"{music_filter}anull[music];"
    )
    if narration is not None:
        narration_index = music_index + 1
        audio_filter += (
            f"[{narration_index}:a]adelay=650:all=1,apad=whole_dur={duration},atrim=0:{duration},asetpts=PTS-STARTPTS,"
            "aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo[speechbase];"
        )
        speech_label = "speech"
    elif kind == "capture" and segment.get("retain_source_audio", False):
        if audio_profile == "accessibility_evidence":
            audio_filter += (
                f"[0:a]apad=whole_dur={duration},atrim=0:{duration},asetpts=PTS-STARTPTS,"
                "aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:"
                "channel_layouts=stereo,volume=1.0[evidence];"
            )
            speech_label = "evidence"
        else:
            audio_filter += (
                f"[0:a]apad=whole_dur={duration},atrim=0:{duration},asetpts=PTS-STARTPTS,"
                "aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo,"
                "highpass=f=65,lowpass=f=12000,dynaudnorm=f=200:g=15:p=0.70:m=7:r=0.10:s=5,"
                "volume=0.70[speechbase];"
            )
            speech_label = "speech"
    if use_transition_sfx:
        sfx_index = music_index + 2 if narration is not None else music_index + 1
        audio_filter += (
            f"[{sfx_index}:a]apad=whole_dur={duration},atrim=0:{duration},asetpts=PTS-STARTPTS,"
            "aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo[sfx];"
        )
    if speech_label == "speech":
        audio_filter += (
            "[speechbase]asplit=2[speechside][speechmix];"
            "[music][speechside]sidechaincompress=threshold=0.020:ratio=8:attack=120:release=900:knee=6[ducked];"
            "[ducked][speechmix]amix=inputs=2:duration=longest:normalize=0[program];"
        )
    elif speech_label == "evidence":
        audio_filter += (
            "[music][evidence]amix=inputs=2:duration=longest:normalize=0[program];"
        )
    else:
        audio_filter += "[music]anull[program];"
    if use_transition_sfx:
        audio_filter += (
            f"[program][sfx]amix=inputs=2:duration=longest:normalize=0,"
            f"apad=whole_dur={duration},atrim=0:{duration}[a]"
        )
    else:
        audio_filter += f"[program]apad=whole_dur={duration},atrim=0:{duration}[a]"

    command += [
        "-filter_complex",
        video_filter + ";" + audio_filter,
        "-map",
        "[v]",
        "-map",
        "[a]",
        "-t",
        str(duration),
        "-c:v",
        "libx264",
        "-preset",
        "veryfast",
        "-crf",
        "18",
        "-profile:v",
        "high",
        "-level:v",
        "4.1",
        "-pix_fmt",
        "yuv420p",
        "-r",
        "30",
        "-g",
        "60",
        "-keyint_min",
        "60",
        "-sc_threshold",
        "0",
        "-color_primaries",
        "bt709",
        "-color_trc",
        "bt709",
        "-colorspace",
        "bt709",
        "-color_range",
        "tv",
        "-c:a",
        "pcm_s24le",
        "-ar",
        "48000",
        "-ac",
        "2",
        str(output_path),
    ]
    run(command)


def srt_timestamp(seconds: float) -> str:
    milliseconds = round(seconds * 1000)
    hours, milliseconds = divmod(milliseconds, 3_600_000)
    minutes, milliseconds = divmod(milliseconds, 60_000)
    secs, milliseconds = divmod(milliseconds, 1000)
    return f"{hours:02d}:{minutes:02d}:{secs:02d},{milliseconds:03d}"


def parse_loudness(text: str) -> dict[str, float]:
    matches = re.findall(r"\{\s*\"input_i\".*?\}", text, re.DOTALL)
    if not matches:
        fail("FFmpeg did not return loudnorm JSON")
    try:
        values = json.loads(matches[-1])
        return {
            key: float(values[key])
            for key in (
                "input_i",
                "input_tp",
                "input_lra",
                "input_thresh",
                "target_offset",
            )
        }
    except (KeyError, ValueError, json.JSONDecodeError) as error:
        fail(f"invalid loudnorm result: {error}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--timeline", type=Path, default=DEFAULT_TIMELINE)
    parser.add_argument("--attribution", type=Path, default=DEFAULT_ATTRIBUTION)
    parser.add_argument("--manifest-template", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--output-dir", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    require_tools("ffmpeg", "ffprobe")
    timeline = load_json(args.timeline)
    attribution = load_json(args.attribution)
    manifest_template = load_json(args.manifest_template)
    if manifest_template.get("schema_version") != 1 or not all(
        isinstance(manifest_template.get(key), dict)
        for key in (
            "capture",
            "application",
            "capture_sessions",
            "source_provenance",
            "release_capture_requirements",
            "offline_ai",
            "render",
            "outputs",
        )
    ):
        fail("manifest template is missing required version-1 sections")
    target_session_id = timeline.get("release_target_capture_session")
    if (
        manifest_template["capture"].get("provenance_model")
        != "per_source_capture_session"
        or manifest_template["capture"].get("release_target_capture_session")
        != target_session_id
    ):
        fail("manifest and timeline disagree on the release-target capture session")
    target_session = timeline.get("capture_sessions", {}).get(target_session_id, {})
    if (
        capture_application(target_session) != EXPECTED_RELEASE_APPLICATION
        or manifest_template["application"] != EXPECTED_RELEASE_APPLICATION
    ):
        fail(
            "release-target capture session does not match manifest application identity"
        )
    warnings = validate_configuration(timeline, attribution)
    enabled = [
        segment for segment in timeline["segments"] if segment.get("enabled", True)
    ]
    print(
        f"Validated {len(timeline['sources'])} sources and {len(enabled)} enabled segments."
    )
    for warning in warnings:
        print(f"WARNING: {warning}")
    if args.validate_only:
        return

    output_dir = args.output_dir.resolve()
    output_dir.mkdir(parents=True, exist_ok=True)
    output_mp4 = output_dir / "TATAPP-Android-accessible-walkthrough.mp4"
    output_srt = output_dir / "TATAPP-Android-accessible-walkthrough-SASRT.srt"
    resolved_timeline_path = output_dir / "final-edit-timeline.json"
    attribution_path = output_dir / "ASSET_ATTRIBUTION.json"
    manifest_path = output_dir / "FINAL-MEDIA-MANIFEST.json"
    checksum_path = output_dir / "SHA256SUMS.txt"
    audit_path = output_dir / "MEDIA-VERIFICATION-AUDIT.json"
    for stale_path in (
        output_mp4,
        output_srt,
        resolved_timeline_path,
        attribution_path,
        manifest_path,
        checksum_path,
        audit_path,
    ):
        stale_path.unlink(missing_ok=True)

    settings = timeline["output"]
    tools = timeline["tools"]
    music_source = repo_path(timeline["sources"]["music"]["path"])
    planned_duration = sum(float(segment["duration"]) for segment in enabled)
    with tempfile.TemporaryDirectory(prefix="tatapp-android-walkthrough-") as temporary:
        work = Path(temporary)
        segments_dir = work / "segments"
        narration_dir = work / "narration"
        segments_dir.mkdir()
        narration_dir.mkdir()

        normalized_music = work / "music-normalized.wav"
        run(
            [
                "ffmpeg",
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-stream_loop",
                "-1",
                "-i",
                str(music_source),
                "-t",
                f"{planned_duration:.6f}",
                "-af",
                "aresample=48000,aformat=channel_layouts=stereo,"
                "loudnorm=I=-26:TP=-5:LRA=5,aresample=48000",
                "-ar",
                "48000",
                "-ac",
                "2",
                "-c:a",
                "pcm_s24le",
                str(normalized_music),
            ]
        )
        transition_sfx = work / "transition-sfx.wav"
        # Original, deterministic synthesis: a quiet descending swoosh plus a
        # two-note chime. App heartbeat audio is never synthesized here.
        run(
            [
                "ffmpeg",
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-f",
                "lavfi",
                "-i",
                "aevalsrc=0.055*sin(2*PI*(980-520*t)*t)*exp(-7*t)+"
                "0.035*sin(2*PI*660*t)*exp(-11*max(t-0.18\\,0))*between(t\\,0.18\\,0.48):s=48000:d=0.55",
                "-af",
                "aformat=channel_layouts=stereo,afade=t=out:st=0.42:d=0.13",
                "-ar",
                "48000",
                "-ac",
                "2",
                "-c:a",
                "pcm_s24le",
                str(transition_sfx),
            ]
        )

        rendered: list[dict] = []
        music_cursor = 0.0
        for index, segment in enumerate(enabled):
            narration = None
            if segment.get("narration"):
                narration = synthesize_narration(segment, tools, narration_dir)
            source = repo_path(timeline["sources"][segment["source"]]["path"])
            magnifier_source = None
            if segment.get("magnifier_source"):
                magnifier_source = repo_path(
                    timeline["sources"][segment["magnifier_source"]]["path"]
                )
            segment_path = segments_dir / f"{index:02d}-{segment['id']}.mkv"
            render_segment(
                segment,
                source,
                magnifier_source,
                normalized_music,
                music_cursor,
                narration,
                transition_sfx,
                segment_path,
                work,
                first=index == 0,
                last=index == len(enabled) - 1,
                settings=settings,
                fonts=tools,
            )
            actual_duration = probe_duration(segment_path)
            if abs(actual_duration - float(segment["duration"])) > 1 / 30 + 0.01:
                fail(f"rendered segment duration drifted: {segment['id']}")
            rendered.append(
                {"segment": segment, "path": segment_path, "duration": actual_duration}
            )
            music_cursor += float(segment["duration"])
            print(f"Rendered {index + 1}/{len(enabled)}: {segment['id']}")

        concat_list = work / "concat.txt"
        concat_list.write_text(
            "".join(
                f"file '{str(item['path']).replace(chr(39), chr(39) + chr(92) + chr(39) + chr(39))}'\n"
                for item in rendered
            ),
            encoding="utf-8",
        )
        screen_sequence = work / "screen-sequence.mkv"
        run(
            [
                "ffmpeg",
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-f",
                "concat",
                "-safe",
                "0",
                "-i",
                str(concat_list),
                "-c",
                "copy",
                str(screen_sequence),
            ]
        )
        total_duration = probe_duration(screen_sequence)

        premaster = work / "audio-premaster.wav"
        fade_out_start = max(0.0, total_duration - float(settings["fade_out_seconds"]))
        run(
            [
                "ffmpeg",
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-i",
                str(screen_sequence),
                "-vn",
                "-af",
                f"afade=t=in:st=0:d={settings['fade_in_seconds']},"
                f"afade=t=out:st={fade_out_start:.3f}:d={settings['fade_out_seconds']},"
                "alimiter=limit=0.90:level=false",
                "-ar",
                "48000",
                "-ac",
                "2",
                "-c:a",
                "pcm_s24le",
                str(premaster),
            ]
        )
        analysis = subprocess.run(
            [
                "ffmpeg",
                "-hide_banner",
                "-nostats",
                "-i",
                str(premaster),
                "-af",
                "loudnorm=I=-16:TP=-1.8:LRA=6:print_format=json",
                "-f",
                "null",
                "-",
            ],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        if analysis.returncode:
            fail("loudness analysis failed\n" + analysis.stderr)
        loudness = parse_loudness(analysis.stderr)
        loudnorm = (
            "loudnorm=I=-16:TP=-1.8:LRA=6:"
            f"measured_I={loudness['input_i']}:measured_TP={loudness['input_tp']}:"
            f"measured_LRA={loudness['input_lra']}:measured_thresh={loudness['input_thresh']}:"
            f"offset={loudness['target_offset']}:linear=false,"
            "alimiter=limit=0.75:attack=5:release=50:level=false"
        )
        run(
            [
                "ffmpeg",
                "-hide_banner",
                "-loglevel",
                "error",
                "-y",
                "-i",
                str(screen_sequence),
                "-i",
                str(premaster),
                "-map",
                "0:v:0",
                "-map",
                "1:a:0",
                "-vf",
                "fps=30,setpts=N/(30*TB),format=yuv420p",
                "-c:v",
                "libx264",
                "-preset",
                "medium",
                "-crf",
                "18",
                "-profile:v",
                "high",
                "-level:v",
                "4.1",
                "-pix_fmt",
                "yuv420p",
                "-r",
                "30",
                "-g",
                "60",
                "-keyint_min",
                "60",
                "-sc_threshold",
                "0",
                "-video_track_timescale",
                "90000",
                "-tag:v",
                "avc1",
                "-color_primaries",
                "bt709",
                "-color_trc",
                "bt709",
                "-colorspace",
                "bt709",
                "-color_range",
                "tv",
                "-af",
                loudnorm,
                "-c:a",
                "aac",
                "-b:a",
                "192k",
                "-ar",
                "48000",
                "-ac",
                "2",
                "-movflags",
                "+faststart",
                "-metadata",
                "title=TATAPP Android Accessible Walkthrough",
                "-metadata",
                "comment=Wolf Head Painting by AlepouTheFox, CC BY-SA 3.0; time is running out and Sugar Skull - Coloured by __april, CC BY 2.0; 78 PULSE by kjartan_abel, CC BY 4.0",
                str(output_mp4),
            ]
        )
        final_analysis = subprocess.run(
            [
                "ffmpeg",
                "-hide_banner",
                "-nostats",
                "-i",
                str(output_mp4),
                "-af",
                "loudnorm=I=-16:TP=-1.5:LRA=7:print_format=json",
                "-f",
                "null",
                "-",
            ],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
        if final_analysis.returncode:
            fail("final loudness analysis failed\n" + final_analysis.stderr)
        final_loudness = parse_loudness(final_analysis.stderr)

        cursor = 0.0
        speech_windows: list[dict] = []
        srt_cues: list[dict] = []
        resolved_segments: list[dict] = []
        for item in rendered:
            segment = item["segment"]
            duration = item["duration"]
            start, end = cursor, cursor + duration
            if segment.get("narration") or segment.get("retain_source_audio"):
                speech_windows.append(
                    {"segment": segment["id"], "start": start, "end": end}
                )
            if segment.get("srt"):
                cue = segment["srt"]
                srt_cues.append(
                    {
                        "segment": segment["id"],
                        "start": start + float(cue["start_offset"]),
                        "end": start + float(cue["end_offset"]),
                        "text": cue["text"],
                    }
                )
            resolved_segments.append(
                {
                    "id": segment["id"],
                    "source": segment["source"],
                    "source_capture_session": timeline["sources"][
                        segment["source"]
                    ].get("capture_session"),
                    "magnifier_source": segment.get("magnifier_source"),
                    "playback_rate": float(segment.get("playback_rate", 1.0)),
                    "start": start,
                    "end": end,
                    "duration": duration,
                    "claim": segment.get("claim"),
                    "release_feature": segment.get("release_feature"),
                    "retained_system_audio": bool(segment.get("retain_source_audio")),
                    "source_audio_profile": segment.get(
                        "source_audio_profile", "dialogue"
                    ),
                    "tts_narration": bool(segment.get("narration")),
                }
            )
            cursor = end

        output_srt.write_text(
            "\n".join(
                f"{index}\n{srt_timestamp(cue['start'])} --> {srt_timestamp(cue['end'])}\n{cue['text']}\n"
                for index, cue in enumerate(srt_cues, 1)
            ),
            encoding="utf-8",
        )
        resolved_timeline = {
            "schema_version": 1,
            "source_template_sha256": sha256(args.timeline.resolve()),
            "release_target_capture_session": timeline[
                "release_target_capture_session"
            ],
            "capture_sessions": timeline["capture_sessions"],
            "source_provenance": {
                source_id: dict(source)
                for source_id, source in timeline["sources"].items()
            },
            "output": settings,
            "duration_seconds": total_duration,
            "segments": resolved_segments,
            "speech_windows": speech_windows,
            "srt_cues": srt_cues,
            "offline_ai_ready_claim_included": any(
                item["segment"].get("claim") == "offline_ai_ready" for item in rendered
            ),
        }
        resolved_timeline_path.write_text(
            json.dumps(resolved_timeline, indent=2) + "\n", encoding="utf-8"
        )
        attribution_path.write_text(
            json.dumps(attribution, indent=2) + "\n", encoding="utf-8"
        )

        manifest = manifest_template
        manifest["release_state"] = "review_render_unverified"
        manifest["render"].update(
            {
                "timeline": portable_path(args.timeline),
                "attribution": portable_path(args.attribution),
                "manifest_template": portable_path(args.manifest_template),
                "renderer": portable_path(Path(__file__)),
                "verifier": portable_path(
                    ROOT / "scripts/verify-android-walkthrough.py"
                ),
            }
        )
        manifest["render"]["ffmpeg_version"] = run(
            ["ffmpeg", "-version"], capture=True
        ).splitlines()[0]
        manifest["render"]["piper_sha256"] = sha256(repo_path(tools["piper"]))
        manifest["render"]["timeline_template_sha256"] = sha256(args.timeline.resolve())
        manifest["render"]["attribution_template_sha256"] = sha256(
            args.attribution.resolve()
        )
        manifest["render"]["manifest_template_sha256"] = sha256(
            args.manifest_template.resolve()
        )
        manifest["render"]["renderer_sha256"] = sha256(Path(__file__))
        verifier = ROOT / "scripts/verify-android-walkthrough.py"
        manifest["render"]["verifier_sha256"] = (
            sha256(verifier) if verifier.is_file() else None
        )
        manifest["capture_sessions"] = timeline["capture_sessions"]
        manifest["source_provenance"] = resolved_timeline["source_provenance"]
        manifest["release_capture_requirements"] = {
            feature: any(
                segment.get("release_feature") == feature for segment in enabled
            )
            for feature in REQUIRED_RELEASE_FEATURES
        }
        ai_ready_segments = [
            item["segment"]
            for item in rendered
            if item["segment"].get("claim") == "offline_ai_ready"
        ]
        if len(ai_ready_segments) > 1:
            fail("only one Offline AI ready claim segment may be rendered")
        ai_ready_included = bool(ai_ready_segments)
        ai_ready_source = (
            timeline["sources"][ai_ready_segments[0]["source"]]
            if ai_ready_included
            else {}
        )
        manifest["offline_ai"].update(
            {
                "ready_clip_state": (
                    ai_ready_source.get("state")
                    if ai_ready_included
                    else "awaiting_real_capture"
                ),
                "ready_clip_path": (
                    ai_ready_source.get("path") if ai_ready_included else None
                ),
                "ready_clip_sha256": (
                    ai_ready_source.get("sha256") if ai_ready_included else None
                ),
                "ready_capture_session": (
                    ai_ready_source.get("capture_session")
                    if ai_ready_included
                    else None
                ),
                "full_preload_claim_allowed": ai_ready_included,
            }
        )
        manifest["outputs"].update(
            {
                "mp4": output_mp4.name,
                "mp4_bytes": output_mp4.stat().st_size,
                "mp4_sha256": sha256(output_mp4),
                "srt": output_srt.name,
                "srt_sha256": sha256(output_srt),
                "duration_seconds": total_duration,
                "frame_count": int(round(total_duration * 30)),
                "integrated_lufs": final_loudness["input_i"],
                "true_peak_dbtp": final_loudness["input_tp"],
                "lra_lu": final_loudness["input_lra"],
            }
        )
        manifest_path.write_text(
            json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
        )
        checksum_paths = [
            output_mp4,
            output_srt,
            resolved_timeline_path,
            attribution_path,
            manifest_path,
        ]
        checksum_path.write_text(
            "".join(f"{sha256(path)}  {path.name}\n" for path in checksum_paths),
            encoding="ascii",
        )

    print(f"Review MP4: {output_mp4}")
    print(f"Accessible SRT: {output_srt}")
    print(
        "Release remains blocked until provenance and every human-verification field are resolved."
    )


if __name__ == "__main__":
    main()
