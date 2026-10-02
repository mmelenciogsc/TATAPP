#!/usr/bin/env python3
"""Fail-closed verification for the rendered Android walkthrough and SASRT."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
REQUIRED_HUMAN_GATES = (
    "complete_visual_watch",
    "speaker_and_headphone_listen",
    "talkback_srt_workflow",
    "speech_and_music_balance",
    "stage_and_export_fidelity",
    "privacy_review",
    "workspace_navigation_talkback",
    "heartbeat_three_pulses_and_talkback",
    "full_ai_single_ready_announcement",
)
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
EXPECTED_RELEASE_APPLICATION = {
    "package": "com.grayscaleconsultants.tatapp",
    "version_name": "0.3.1",
    "version_code": 2,
    "installed_apk_sha256": "7f83b72ae3f1af5024d0dade5c3a220387762ccad5ac7c4f2812cccc0e1f42c9",
    "signing_certificate_sha256": "eac3df9aba3e08437bc988682566f072e52d2dde6bda373daa998cdee74d9f90",
    "git_commit": "db66a1d7a0e7100906dc69b9bd705a7ae3463276",
}


def fail(message: str) -> None:
    raise SystemExit(f"Android walkthrough verification failed: {message}")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


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
        fail(f"{path} must contain an object")
    return value


def run(command: list[str]) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        command, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False
    )
    if result.returncode:
        fail(
            f"command failed ({result.returncode}): {' '.join(command)}\n{result.stderr.strip()}"
        )
    return result


def unresolved_values(value: object, path: str = "") -> list[str]:
    result: list[str] = []
    if isinstance(value, dict):
        for key, item in value.items():
            result.extend(unresolved_values(item, f"{path}.{key}" if path else key))
    elif isinstance(value, list):
        for index, item in enumerate(value):
            result.extend(unresolved_values(item, f"{path}[{index}]"))
    elif isinstance(value, str) and value.startswith("REQUIRED_BEFORE_RELEASE"):
        result.append(path)
    return result


def verify_checksums(directory: Path) -> None:
    checksum_file = directory / "SHA256SUMS.txt"
    if not checksum_file.is_file():
        fail("SHA256SUMS.txt is missing")
    for line_number, line in enumerate(
        checksum_file.read_text(encoding="ascii").splitlines(), 1
    ):
        match = re.fullmatch(r"([0-9a-f]{64})  ([^/]+)", line)
        if not match:
            fail(f"invalid checksum line {line_number}")
        path = directory / match.group(2)
        if not path.is_file() or sha256(path) != match.group(1):
            fail(f"checksum mismatch: {match.group(2)}")


def verify_evidence_record(
    record: dict, label: str, *, allow_placeholder: bool = False
) -> None:
    path_value = record.get("path")
    hash_value = record.get("sha256")
    placeholder = any(
        isinstance(value, str) and value.startswith("REQUIRED_BEFORE_RELEASE")
        for value in (path_value, hash_value)
    )
    if placeholder and allow_placeholder:
        return
    if (
        not isinstance(path_value, str)
        or not isinstance(hash_value, str)
        or not re.fullmatch(r"[0-9a-f]{64}", hash_value)
    ):
        fail(f"{label} has an invalid path or SHA-256")
    path = Path(path_value)
    path = path if path.is_absolute() else ROOT / path
    if not path.is_file() or sha256(path) != hash_value:
        fail(f"{label} is missing or has a checksum mismatch: {path}")


def parse_srt(path: Path) -> list[dict]:
    raw = path.read_bytes()
    if raw.startswith(b"\xef\xbb\xbf"):
        fail("SRT must be UTF-8 without a BOM")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as error:
        fail(f"SRT is not UTF-8: {error}")
    if "\ufffd" in text or re.search(r"<[^>]+>", text):
        fail("SRT contains replacement characters or markup")
    blocks = re.split(r"\r?\n\s*\r?\n", text.strip()) if text.strip() else []
    cues: list[dict] = []

    def timestamp(value: str) -> float:
        match = re.fullmatch(r"(\d{2}):(\d{2}):(\d{2}),(\d{3})", value)
        if not match:
            fail(f"invalid SRT timestamp: {value}")
        hours, minutes, seconds, milliseconds = map(int, match.groups())
        if minutes >= 60 or seconds >= 60:
            fail(f"invalid SRT timestamp: {value}")
        return hours * 3600 + minutes * 60 + seconds + milliseconds / 1000

    for expected_index, block in enumerate(blocks, 1):
        lines = block.splitlines()
        if len(lines) < 3 or lines[0] != str(expected_index):
            fail(f"SRT cue {expected_index} has an invalid index or body")
        timing = re.fullmatch(r"(.+) --> (.+)", lines[1])
        if not timing:
            fail(f"SRT cue {expected_index} has invalid timing syntax")
        start, end = timestamp(timing.group(1)), timestamp(timing.group(2))
        cue_text = " ".join(line.strip() for line in lines[2:] if line.strip())
        if not cue_text or not start < end:
            fail(f"SRT cue {expected_index} is empty or non-positive")
        words = len(re.findall(r"\b[\w’'-]+\b", cue_text, re.UNICODE))
        wpm = words / ((end - start) / 60.0)
        if wpm > 180.0 + 1e-6:
            fail(f"SRT cue {expected_index} is {wpm:.1f} WPM; maximum is 180")
        if cues and start < cues[-1]["end"]:
            fail(f"SRT cue {expected_index} overlaps its predecessor")
        cues.append(
            {
                "index": expected_index,
                "start": start,
                "end": end,
                "text": cue_text,
                "wpm": wpm,
            }
        )
    if not cues:
        fail("SRT contains no visual-description cues")
    return cues


def top_level_atoms(path: Path) -> dict[str, int]:
    atoms: dict[str, int] = {}
    size = path.stat().st_size
    with path.open("rb") as stream:
        offset = 0
        while offset + 8 <= size:
            stream.seek(offset)
            header = stream.read(8)
            atom_size = int.from_bytes(header[:4], "big")
            atom_type = header[4:8].decode("latin-1")
            header_size = 8
            if atom_size == 1:
                atom_size = int.from_bytes(stream.read(8), "big")
                header_size = 16
            elif atom_size == 0:
                atom_size = size - offset
            if atom_size < header_size or offset + atom_size > size:
                fail(f"invalid MP4 atom {atom_type!r} at byte {offset}")
            atoms.setdefault(atom_type, offset)
            offset += atom_size
    return atoms


def parse_loudnorm(text: str) -> dict[str, float]:
    matches = re.findall(r"\{\s*\"input_i\".*?\}", text, re.DOTALL)
    if not matches:
        fail("FFmpeg did not emit loudnorm JSON")
    values = json.loads(matches[-1])
    return {key: float(values[key]) for key in ("input_i", "input_tp", "input_lra")}


def close(left: float, right: float, tolerance: float) -> bool:
    return abs(left - right) <= tolerance


def verify_capture_provenance(manifest: dict, timeline: dict) -> list[str]:
    capture = manifest.get("capture", {})
    target_session_id = capture.get("release_target_capture_session")
    if capture.get("provenance_model") != "per_source_capture_session":
        fail("manifest must use per-source capture-session provenance")
    sessions = manifest.get("capture_sessions")
    if not isinstance(sessions, dict) or target_session_id not in sessions:
        fail("manifest release-target capture session is missing")
    if timeline.get("release_target_capture_session") != target_session_id:
        fail("manifest and resolved timeline disagree on the release-target session")
    if timeline.get("capture_sessions") != sessions:
        fail("manifest and resolved timeline capture sessions differ")

    application = manifest.get("application", {})
    if (
        capture_application(sessions[target_session_id]) != EXPECTED_RELEASE_APPLICATION
        or application != EXPECTED_RELEASE_APPLICATION
    ):
        fail("release-target session application identity differs from the manifest")
    for session_id, session in sessions.items():
        state = session.get("state")
        if state not in CAPTURE_SESSION_STATES:
            fail(f"capture session {session_id} has invalid state {state!r}")
        if state != "awaiting_real_capture" and not session.get("capture_date"):
            fail(f"capture session {session_id} has no capture date")
        session_application = capture_application(session)
        if (
            not session_application["package"]
            or not session_application["version_name"]
            or not isinstance(session_application["version_code"], int)
            or session_application["version_code"] <= 0
            or not (
                is_sha256(session_application["installed_apk_sha256"])
                or (
                    state == "historical_unresolved"
                    and session_application["installed_apk_sha256"] is None
                )
            )
            or not is_sha256(session_application["signing_certificate_sha256"])
        ):
            fail(f"capture session {session_id} has invalid application identity")
        commit = session_application["git_commit"]
        if commit is not None and not re.fullmatch(r"[0-9a-f]{40}", str(commit)):
            fail(f"capture session {session_id} has an invalid Git commit")
        if session_id == target_session_id and commit is None:
            fail("release-target capture session has no Git commit")

    provenance = manifest.get("source_provenance")
    if not isinstance(provenance, dict) or not provenance:
        fail("manifest source provenance is missing")
    if timeline.get("source_provenance") != provenance:
        fail("manifest and resolved timeline source provenance differ")
    for source_id, source in provenance.items():
        kind = source.get("provenance_kind")
        session_id = source.get("capture_session")
        state = source.get("state")
        if kind in CAPTURE_PROVENANCE_KINDS:
            if session_id not in sessions:
                fail(f"source {source_id} has no declared capture session")
            if (
                state != "awaiting_real_capture"
                and sessions[session_id].get("state") == "awaiting_real_capture"
            ):
                fail(f"source {source_id} belongs to an awaiting capture session")
        elif kind == "licensed_asset":
            if session_id is not None:
                fail(f"licensed source {source_id} declares a capture session")
        else:
            fail(f"source {source_id} has invalid provenance kind")
        if state == "awaiting_real_capture":
            if source.get("sha256") is not None:
                fail(f"awaiting source {source_id} has an unverified checksum")
            continue
        verify_evidence_record(source, f"source provenance {source_id}")

    session_bound_records = [
        *(manifest.get("workflow_evidence") or []),
        *(manifest.get("offline_ai", {}).get("ui_evidence") or []),
        manifest.get("offline_ai", {}).get("network_isolation_evidence", {}),
    ]
    for index, record in enumerate(session_bound_records, 1):
        if (
            not isinstance(record, dict)
            or record.get("capture_session") not in sessions
        ):
            fail(f"session-bound evidence {index} has no declared capture session")

    segments = timeline.get("segments", [])
    requirements = manifest.get("release_capture_requirements")
    if not isinstance(requirements, dict) or set(requirements) != set(
        REQUIRED_RELEASE_FEATURES
    ):
        fail("manifest release-capture requirements are malformed")
    feature_counts = {
        feature: sum(segment.get("release_feature") == feature for segment in segments)
        for feature in REQUIRED_RELEASE_FEATURES
    }
    if any(count > 1 for count in feature_counts.values()):
        fail("a required release feature is represented by multiple segments")
    derived_requirements = {
        feature: feature_counts[feature] == 1 for feature in REQUIRED_RELEASE_FEATURES
    }
    if requirements != derived_requirements:
        fail("manifest release-capture requirements disagree with the resolved edit")
    for segment in segments:
        feature = segment.get("release_feature")
        if feature is None:
            continue
        if feature not in REQUIRED_RELEASE_FEATURES:
            fail(f"resolved segment {segment.get('id')} has an unknown release feature")
        source_id = segment.get("source")
        source = provenance.get(source_id, {})
        if (
            segment.get("source_capture_session") != target_session_id
            or source.get("capture_session") != target_session_id
            or source.get("state") != "verified"
            or sessions[target_session_id].get("state") != "verified"
        ):
            fail(
                f"release feature {feature} does not use verified release-target capture"
            )
        if (
            not segment.get("retained_system_audio")
            or segment.get("tts_narration")
            or segment.get("source_audio_profile") != "accessibility_evidence"
        ):
            fail(
                f"release feature {feature} does not use the accessibility-evidence audio profile"
            )
    return [feature for feature, included in requirements.items() if not included]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--manifest",
        type=Path,
        default=ROOT / "artifacts/android-walkthrough/final/FINAL-MEDIA-MANIFEST.json",
    )
    parser.add_argument(
        "--review",
        action="store_true",
        help="allow unresolved provenance and human gates while checking the media",
    )
    args = parser.parse_args()
    directory = args.manifest.resolve().parent
    manifest = load_json(args.manifest)
    outputs = manifest.get("outputs", {})
    mp4 = directory / str(outputs.get("mp4", ""))
    srt = directory / str(outputs.get("srt", ""))
    timeline_path = directory / "final-edit-timeline.json"
    attribution_path = directory / "ASSET_ATTRIBUTION.json"
    for path in (mp4, srt, timeline_path, attribution_path):
        if not path.is_file():
            fail(f"required final artifact is missing: {path}")
    verify_checksums(directory)
    if sha256(mp4) != outputs.get("mp4_sha256") or sha256(srt) != outputs.get(
        "srt_sha256"
    ):
        fail("manifest output hash does not match the final file")

    render = manifest.get("render", {})
    declared_files = (
        ("timeline", "timeline_template_sha256", "timeline template"),
        ("attribution", "attribution_template_sha256", "attribution template"),
        ("manifest_template", "manifest_template_sha256", "manifest template"),
        ("renderer", "renderer_sha256", "renderer"),
        ("verifier", "verifier_sha256", "verifier"),
    )
    for path_key, hash_key, label in declared_files:
        verify_evidence_record(
            {"path": render.get(path_key), "sha256": render.get(hash_key)}, label
        )
    if load_json(attribution_path) != load_json(ROOT / str(render["attribution"])):
        fail("generated attribution does not match its checksum-locked template")
    workflow_evidence = manifest.get("workflow_evidence", [])
    if not isinstance(workflow_evidence, list) or not workflow_evidence:
        fail("workflow evidence records are missing")
    for index, record in enumerate(workflow_evidence, 1):
        if not isinstance(record, dict):
            fail(f"workflow evidence {index} is not an object")
        verify_evidence_record(record, f"workflow evidence {index}")

    timeline = load_json(timeline_path)
    incomplete_release_captures = verify_capture_provenance(manifest, timeline)
    unresolved = unresolved_values(manifest)
    attribution = load_json(attribution_path)
    blockers = list(attribution.get("release_blockers", []))
    incomplete_human = [
        key
        for key in REQUIRED_HUMAN_GATES
        if manifest.get("human_verification", {}).get(key) is not True
    ]
    if not args.review and (
        unresolved or blockers or incomplete_human or incomplete_release_captures
    ):
        fail(
            "release evidence is incomplete: "
            + "; ".join(
                filter(
                    None,
                    [
                        "unresolved fields=" + ",".join(unresolved)
                        if unresolved
                        else "",
                        "attribution blockers=" + " | ".join(blockers)
                        if blockers
                        else "",
                        "human gates=" + ",".join(incomplete_human)
                        if incomplete_human
                        else "",
                        "release captures=" + ",".join(incomplete_release_captures)
                        if incomplete_release_captures
                        else "",
                    ],
                )
            )
        )

    probe = json.loads(
        run(
            [
                "ffprobe",
                "-v",
                "error",
                "-count_frames",
                "-show_streams",
                "-show_format",
                "-of",
                "json",
                str(mp4),
            ]
        ).stdout
    )
    streams = probe.get("streams", [])
    videos = [stream for stream in streams if stream.get("codec_type") == "video"]
    audios = [stream for stream in streams if stream.get("codec_type") == "audio"]
    if len(streams) != 2 or len(videos) != 1 or len(audios) != 1:
        fail("MP4 must contain exactly one video and one audio stream")
    video, audio = videos[0], audios[0]
    video_expected = {
        "codec_name": "h264",
        "profile": "High",
        "width": 1920,
        "height": 1080,
        "pix_fmt": "yuv420p",
        "level": 41,
        "r_frame_rate": "30/1",
        "avg_frame_rate": "30/1",
        "color_space": "bt709",
        "color_transfer": "bt709",
        "color_primaries": "bt709",
    }
    for key, value in video_expected.items():
        if video.get(key) != value:
            fail(f"video {key} must be {value!r}, found {video.get(key)!r}")
    if (
        audio.get("codec_name") != "aac"
        or audio.get("profile") != "LC"
        or audio.get("sample_rate") != "48000"
        or audio.get("channels") != 2
    ):
        fail("audio must be AAC-LC stereo at 48 kHz")
    for stream in (video, audio):
        if abs(float(stream.get("start_time", 0))) > 1 / 30 + 0.001:
            fail("a media stream starts more than one frame from zero")
    video_duration = float(video["duration"])
    audio_duration = float(audio["duration"])
    format_duration = float(probe["format"]["duration"])
    if abs(video_duration - audio_duration) > 0.050:
        fail("audio/video duration differs by more than 50 ms")
    if not close(format_duration, float(outputs["duration_seconds"]), 1 / 30 + 0.01):
        fail("container duration does not match the manifest")
    frame_count = int(video.get("nb_read_frames", -1))
    if (
        frame_count != int(outputs.get("frame_count", -2))
        or abs(frame_count - round(video_duration * 30)) > 1
    ):
        fail("decoded frame count is inconsistent with CFR duration or the manifest")
    if any(
        int(side.get("rotation", 0)) != 0 for side in video.get("side_data_list", [])
    ):
        fail("rotation metadata must be absent or zero")

    atoms = top_level_atoms(mp4)
    if "moov" not in atoms or "mdat" not in atoms or atoms["moov"] > atoms["mdat"]:
        fail("MP4 is not fast-started (moov must precede mdat)")
    run(
        [
            "ffmpeg",
            "-v",
            "error",
            "-xerror",
            "-i",
            str(mp4),
            "-map",
            "0:v:0",
            "-map",
            "0:a:0",
            "-f",
            "null",
            "-",
        ]
    )
    vfr = run(
        [
            "ffmpeg",
            "-hide_banner",
            "-i",
            str(mp4),
            "-map",
            "0:v:0",
            "-vf",
            "vfrdet",
            "-an",
            "-f",
            "null",
            "-",
        ]
    ).stderr
    if "VFR:0.000000" not in vfr:
        fail("video is not constant frame rate: " + vfr.splitlines()[-1])
    keyframe_text = run(
        [
            "ffprobe",
            "-v",
            "error",
            "-skip_frame",
            "nokey",
            "-select_streams",
            "v:0",
            "-show_entries",
            "frame=best_effort_timestamp_time",
            "-of",
            "csv=p=0",
            str(mp4),
        ]
    ).stdout
    keyframes: list[float] = []
    for line in keyframe_text.splitlines():
        # ffprobe's CSV writer may leave a trailing comma when frame side data is
        # present. Parse the timestamp field instead of requiring the whole row
        # to contain only a number.
        value = line.split(",", 1)[0].strip()
        if re.fullmatch(r"\d+(?:\.\d+)?", value):
            keyframes.append(float(value))
    if not keyframes or keyframes[0] > 1 / 30 + 0.001:
        fail("video does not begin with a keyframe")
    if any(right - left > 2.05 for left, right in zip(keyframes, keyframes[1:])):
        fail("keyframe interval exceeds 2.05 seconds")

    loudness_run = run(
        [
            "ffmpeg",
            "-hide_banner",
            "-nostats",
            "-i",
            str(mp4),
            "-af",
            "loudnorm=I=-16:TP=-1.5:LRA=7:print_format=json",
            "-f",
            "null",
            "-",
        ]
    )
    loudness = parse_loudnorm(loudness_run.stderr)
    if not close(loudness["input_i"], -16.0, 0.5):
        fail(f"integrated loudness is {loudness['input_i']} LUFS")
    if loudness["input_tp"] > -1.5 + 0.05:
        fail(f"true peak is {loudness['input_tp']} dBTP")
    if loudness["input_lra"] > 7.0 + 0.05:
        fail(f"loudness range is {loudness['input_lra']} LU")
    measured_manifest = {
        "integrated_lufs": loudness["input_i"],
        "true_peak_dbtp": loudness["input_tp"],
        "lra_lu": loudness["input_lra"],
    }
    for key, actual in measured_manifest.items():
        try:
            declared = float(outputs[key])
        except (KeyError, TypeError, ValueError):
            fail(f"manifest outputs.{key} must contain the post-encode measurement")
        if not close(declared, actual, 0.05):
            fail(f"manifest outputs.{key} does not match the post-encode measurement")

    output_contract = timeline.get("output", {})
    required_output_contract = {
        "width": 1920,
        "height": 1080,
        "fps": 30,
        "video_profile": "High",
        "video_level": 41,
        "pixel_format": "yuv420p",
        "color_space": "bt709",
        "phone": {"x": 1392, "y": 60, "width": 432, "height": 960},
        "magnifier": {"x": 620, "y": 230, "width": 720, "height": 720},
    }
    for key, expected in required_output_contract.items():
        if output_contract.get(key) != expected:
            fail(
                f"resolved timeline output.{key} does not match the production contract"
            )
    timeline_template = load_json(ROOT / str(render["timeline"]))
    if timeline.get("source_template_sha256") != render.get("timeline_template_sha256"):
        fail("resolved timeline and manifest disagree on the source timeline hash")
    piper_path = Path(str(timeline_template.get("tools", {}).get("piper", "")))
    if not piper_path.is_absolute():
        piper_path = ROOT / piper_path
    if not piper_path.is_file() or sha256(piper_path) != render.get("piper_sha256"):
        fail("Piper executable does not match the render manifest")
    resolved_ids = {segment.get("id") for segment in timeline.get("segments", [])}
    required_design_segments = {
        "wolf_stages_01_15",
        "wolf_stage_16",
        "clock_configuration",
        "clock_flat_stages",
        "clock_anatomical_stages",
        "sugar_import",
        "sugar_stages",
        "sugar_save",
        "sugar_export",
        "black_widow_default_browser",
    }
    missing_design_segments = sorted(required_design_segments - resolved_ids)
    if missing_design_segments:
        fail(
            "resolved timeline omits required real-design evidence: "
            + ", ".join(missing_design_segments)
        )
    cues = parse_srt(srt)
    if cues[-1]["end"] > format_duration + 0.001:
        fail("SRT extends beyond the MP4")
    speech_windows = timeline.get("speech_windows", [])
    for cue in cues:
        for speech in speech_windows:
            if cue["start"] < float(speech["end"]) and cue["end"] > float(
                speech["start"]
            ):
                fail(
                    f"SRT cue {cue['index']} overlaps speech segment {speech['segment']}"
                )
    expected_cues = timeline.get("srt_cues", [])
    if len(cues) != len(expected_cues):
        fail("SRT cue count does not match the resolved timeline")
    for actual, expected in zip(cues, expected_cues):
        if (
            actual["text"] != expected.get("text")
            or not close(actual["start"], float(expected.get("start", -1)), 0.001)
            or not close(actual["end"], float(expected.get("end", -1)), 0.001)
        ):
            fail(f"SRT cue {actual['index']} does not match the resolved timeline")
    ai_ready_segments = [
        segment
        for segment in timeline.get("segments", [])
        if segment.get("claim") == "offline_ai_ready"
    ]
    if len(ai_ready_segments) > 1:
        fail("resolved timeline contains multiple Offline AI ready claims")
    if timeline.get("offline_ai_ready_claim_included") != bool(ai_ready_segments):
        fail("resolved timeline Offline AI claim flag is inconsistent")
    if ai_ready_segments:
        offline = manifest.get("offline_ai", {})
        ai_source = manifest["source_provenance"].get(
            ai_ready_segments[0].get("source"), {}
        )
        if offline.get("ready_clip_state") != "verified" or not offline.get(
            "ready_clip_sha256"
        ):
            fail("the edit claims Offline AI ready without verified capture provenance")
        if offline.get("full_preload_claim_allowed") is not True:
            fail(
                "the edit claims Offline AI ready while its manifest claim gate is false"
            )
        if (
            offline.get("ready_clip_path") != ai_source.get("path")
            or offline.get("ready_clip_sha256") != ai_source.get("sha256")
            or offline.get("ready_capture_session") != ai_source.get("capture_session")
            or ai_ready_segments[0].get("source_capture_session")
            != ai_source.get("capture_session")
        ):
            fail("Offline AI ready metadata differs from its source provenance")
        verify_evidence_record(
            {
                "path": offline.get("ready_clip_path"),
                "sha256": offline.get("ready_clip_sha256"),
            },
            "Offline AI ready capture",
        )
        ui_evidence = offline.get("ui_evidence", [])
        if not isinstance(ui_evidence, list) or not ui_evidence:
            fail("Offline AI ready claim has no UI evidence records")
        for index, record in enumerate(ui_evidence, 1):
            if not isinstance(record, dict):
                fail(f"Offline AI UI evidence {index} is not an object")
            verify_evidence_record(record, f"Offline AI UI evidence {index}")
        network = offline.get("network_isolation_evidence", {})
        if (
            network.get("state") != "verified_during_capture"
            or network.get("wifi_state") != 0
            or network.get("mobile_data_connection_state") != 0
        ):
            fail(
                "Offline AI network-isolation evidence is not in the verified disabled-radio state"
            )
        verify_evidence_record(
            network,
            "Offline AI network-isolation evidence",
            allow_placeholder=args.review,
        )

    # Diagnostic detections are persisted for review; they are not accepted as
    # semantic proof because the timeline deliberately contains still holds and fades.
    black = run(
        [
            "ffmpeg",
            "-hide_banner",
            "-i",
            str(mp4),
            "-vf",
            "blackdetect=d=0.5:pic_th=0.98",
            "-an",
            "-f",
            "null",
            "-",
        ]
    ).stderr
    freeze = run(
        [
            "ffmpeg",
            "-hide_banner",
            "-i",
            str(mp4),
            "-vf",
            "freezedetect=n=-50dB:d=2",
            "-an",
            "-f",
            "null",
            "-",
        ]
    ).stderr
    audit = {
        "schema_version": 1,
        "result": "review_pass" if args.review else "release_pass",
        "media": {
            "duration_seconds": format_duration,
            "frame_count": frame_count,
            "integrated_lufs": loudness["input_i"],
            "true_peak_dbtp": loudness["input_tp"],
            "lra_lu": loudness["input_lra"],
        },
        "srt": {"cue_count": len(cues), "maximum_wpm": max(cue["wpm"] for cue in cues)},
        "unresolved_provenance": unresolved,
        "incomplete_human_gates": incomplete_human,
        "incomplete_release_captures": incomplete_release_captures,
        "diagnostics": {
            "blackdetect": [line for line in black.splitlines() if "black_" in line],
            "freezedetect": [line for line in freeze.splitlines() if "freeze_" in line],
        },
    }
    (directory / "MEDIA-VERIFICATION-AUDIT.json").write_text(
        json.dumps(audit, indent=2) + "\n", encoding="utf-8"
    )
    print(
        f"PASS: {frame_count} CFR frames; {format_duration:.3f}s; "
        f"{loudness['input_i']:.1f} LUFS; {loudness['input_tp']:.1f} dBTP; "
        f"{len(cues)} non-overlapping SASRT cues."
    )
    if args.review:
        print(
            "REVIEW ONLY: unresolved provenance and human gates remain release blockers."
        )


if __name__ == "__main__":
    main()
