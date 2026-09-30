#!/usr/bin/env bash

set -euo pipefail
export MALLOC_ARENA_MAX=2

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/.." && pwd -P)"
work="$repo_root/artifacts/walkthrough"
input="$work/TATAPP-accessible-walkthrough.mp4"
output="$work/TATAPP-accessible-walkthrough-compact.mp4"
maximum_bytes=20000000
# Leave one megabyte below the hard limit for MP4 muxing variation while using
# the remaining budget for image detail.
target_bytes=19000000
audio_bitrate=64000
muxing_reserve_bitrate=8000

if [[ ! -f "$input" ]]; then
  echo "Final walkthrough is missing: $input" >&2
  exit 1
fi

available_kib="$(awk '/^MemAvailable:/ { print $2 }' /proc/meminfo)"
if [[ -n "$available_kib" && "$available_kib" -lt 1572864 ]]; then
  echo "Compact encoding stopped: less than 1.5 GiB of memory is available." >&2
  exit 1
fi

duration="$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$input")"
video_bitrate="$(awk -v bytes="$target_bytes" -v seconds="$duration" -v audio="$audio_bitrate" -v reserve="$muxing_reserve_bitrate" \
  'BEGIN { value=int((bytes*8/seconds)-audio-reserve); if (value < 250000) exit 1; print value }')"

temporary_dir="$(mktemp -d "$work/.compact-encode.XXXXXX")"
temporary_output="$temporary_dir/output.mp4"
pass_log="$temporary_dir/x264-pass"
trap 'rm -rf -- "$temporary_dir"' EXIT

common_video_options=(
  -c:v libx264
  -preset slow
  -profile:v high
  -level 4.1
  -pix_fmt yuv420p
  -b:v "$video_bitrate"
  -threads 2
  -g 300
  -keyint_min 30
  -sc_threshold 40
  -x264-params "aq-mode=3:aq-strength=0.9"
)

echo "Compact pass 1 of 2: targeting ${video_bitrate} bits/s for video."
ffmpeg -hide_banner -loglevel error -y -i "$input" \
  -map 0:v:0 "${common_video_options[@]}" \
  -pass 1 -passlogfile "$pass_log" -an -f mp4 /dev/null

echo "Compact pass 2 of 2: encoding final H.264/AAC MP4."
ffmpeg -hide_banner -loglevel error -y -i "$input" \
  -map 0:v:0 -map 0:a:0 "${common_video_options[@]}" \
  -pass 2 -passlogfile "$pass_log" \
  -c:a aac -b:a 64k -ar 48000 -ac 2 \
  -movflags +faststart \
  -metadata title="TATAPP Accessible Walkthrough — Compact" \
  -metadata comment="Full-HD compact edition; background music: 78 PULSE by kjartan_abel, CC BY 4.0" \
  "$temporary_output"

actual_bytes="$(stat -c '%s' "$temporary_output")"
if (( actual_bytes >= maximum_bytes )); then
  echo "Compact encode is ${actual_bytes} bytes, which does not satisfy the under-20-MB limit." >&2
  exit 1
fi

mv -f -- "$temporary_output" "$output"
trap - EXIT
rm -rf -- "$temporary_dir"

echo "Created $output (${actual_bytes} bytes)."
ffprobe -v error \
  -show_entries format=duration,size,bit_rate \
  -show_entries stream=index,codec_name,codec_type,width,height,r_frame_rate,pix_fmt,sample_rate,channels \
  -of json "$output"
