#!/usr/bin/env bash

set -euo pipefail
export MALLOC_ARENA_MAX=2

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/.." && pwd -P)"
work="$repo_root/artifacts/walkthrough"
render_dir="$work/render"
narration_dir="$work/narration"
screen="$render_dir/screen-sequence.mkv"
music_source="$work/music/78-pulse-kjartan-abel.mp3"
output="$work/TATAPP-accessible-walkthrough.mp4"
temporary_output="$work/TATAPP-accessible-walkthrough.remastered.mp4"
previous_output="$work/TATAPP-accessible-walkthrough.previous-audio-mix.mp4"
reuse_rendered_video=false
force_rebuild=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --reuse-rendered-video) reuse_rendered_video=true ;;
    --force) force_rebuild=true ;;
    *) echo "Usage: $0 [--reuse-rendered-video] [--force]" >&2; exit 2 ;;
  esac
  shift
done

ensure_memory_headroom() {
  local available_kib
  available_kib="$(awk '/^MemAvailable:/ { print $2 }' /proc/meminfo)"
  if [[ -n "$available_kib" && "$available_kib" -lt 1572864 ]]; then
    echo "Audio/video mastering stopped before the next FFmpeg process: less than 1.5 GiB is available. Existing intermediates are retained for a safe resume." >&2
    exit 1
  fi
}

for required in "$screen" "$music_source" "$work/overlays.ass"; do
  if [[ ! -f "$required" ]]; then
    echo "Required walkthrough asset is missing: $required" >&2
    exit 1
  fi
done
jaws_windows_manifest="$render_dir/jaws-audio-windows.tsv"
if [[ ! -f "$jaws_windows_manifest" ]]; then
  echo "Required JAWS speech-window manifest is missing: $jaws_windows_manifest" >&2
  exit 1
fi

narration_script="$work/narration-script.json"
narration_hash_file="$narration_dir/.script.sha256"
if [[ ! -f "$narration_hash_file" ]]; then
  echo "Narration provenance is missing. Run scripts/synthesize-walkthrough.ps1 before mastering." >&2
  exit 1
fi
current_narration_hash="$(sha256sum "$narration_script" | awk '{ print tolower($1) }')"
recorded_narration_hash="$(tr -d '[:space:]' < "$narration_hash_file" | tr '[:upper:]' '[:lower:]')"
if [[ "$current_narration_hash" != "$recorded_narration_hash" ]]; then
  echo "Narration audio is stale relative to narration-script.json. Run scripts/synthesize-walkthrough.ps1 before mastering." >&2
  exit 1
fi

duration="$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$screen")"
fade_out_start="$(awk -v duration="$duration" 'BEGIN { printf "%.3f", duration - 2.0 }')"
program_fade_in_seconds="0.8"
program_fade_out_seconds="2.5"
program_fade_out_start="$(awk -v duration="$duration" -v fade="$program_fade_out_seconds" 'BEGIN { printf "%.3f", duration - fade }')"

# Normalize every Piper utterance before placement. Processing the short files
# separately keeps their perceived loudness consistent without measuring the
# long silent gaps between narration cues.
normalized_narration="$render_dir/normalized-narration"
mkdir -p "$normalized_narration"
narration_ids=(intro controls wolf-stages offline-ai processing jaws-demo clock-roses sugar-skull save black-widow outro)
for narration_id in "${narration_ids[@]}"; do
  if [[ ! -f "$narration_dir/$narration_id.wav" ]]; then
    echo "Current narration clip is missing: $narration_dir/$narration_id.wav" >&2
    exit 1
  fi
done
narration_needs_mix="$force_rebuild"
for narration_id in "${narration_ids[@]}"; do
  normalized_file="$normalized_narration/$narration_id.wav"
  if [[ "$force_rebuild" == true || ! -f "$normalized_file" || "$narration_dir/$narration_id.wav" -nt "$normalized_file" ]]; then
    ensure_memory_headroom
    ffmpeg -hide_banner -loglevel error -threads 2 -filter_threads 1 -y \
      -i "$narration_dir/$narration_id.wav" \
      -af "aresample=48000,aformat=channel_layouts=stereo,loudnorm=I=-16.5:TP=-2:LRA=4,aresample=48000" \
      -ar 48000 -ac 2 -c:a pcm_s16le "$normalized_file"
    narration_needs_mix=true
  fi
done

if [[ "$narration_needs_mix" == true || ! -f "$render_dir/narration-full.wav" ]]; then
  ensure_memory_headroom
  ffmpeg -hide_banner -loglevel error -threads 2 -filter_complex_threads 1 -y \
    -i "$normalized_narration/intro.wav" -i "$normalized_narration/controls.wav" -i "$normalized_narration/wolf-stages.wav" \
    -i "$normalized_narration/offline-ai.wav" -i "$normalized_narration/processing.wav" -i "$normalized_narration/jaws-demo.wav" \
    -i "$normalized_narration/clock-roses.wav" -i "$normalized_narration/sugar-skull.wav" -i "$normalized_narration/save.wav" \
    -i "$normalized_narration/black-widow.wav" -i "$normalized_narration/outro.wav" \
    -filter_complex "[0:a]adelay=650:all=1[n0];[1:a]adelay=10100:all=1[n1];[2:a]adelay=31000:all=1[n2];[3:a]adelay=75800:all=1[n3];[4:a]adelay=84400:all=1[n4];[5:a]adelay=89700:all=1[n5];[6:a]adelay=130600:all=1[n6];[7:a]adelay=189550:all=1[n7];[8:a]adelay=229300:all=1[n8];[9:a]adelay=242800:all=1[n9];[10:a]adelay=260300:all=1[n10];[n0][n1][n2][n3][n4][n5][n6][n7][n8][n9][n10]amix=inputs=11:duration=longest:normalize=0,apad=whole_dur=$duration,atrim=0:$duration[narration]" \
    -map "[narration]" -ar 48000 -ac 2 -c:a pcm_s16le "$render_dir/narration-full.wav"
fi

# Generate short, equal-level interface tones. Clamping elapsed time before the
# exponential prevents the pre-event overflow that corrupted the original mix.
click_times=(13.08 16.11 25.00 29.80 32.87 35.94 39.00 42.06 45.13 48.18 51.25 54.31 57.37 60.43 63.49 66.55 69.61 72.67 78.78 81.79 83.10 84.30 85.50 86.70 138.13 142.48 145.54 148.60 151.67 155.45 158.52 163.50 168.49 172.59 176.69 188.59 194.44 199.59 202.69 205.76 208.85 211.93 217.15 221.23 225.33 229.43 234.89 241.60 245.64)
expression="0"
frequency=720
for time in "${click_times[@]}"; do
  end="$(awk -v value="$time" 'BEGIN { printf "%.3f", value + 0.18 }')"
  expression+="+0.15*sin(2*PI*$frequency*(t-$time))*exp(-20*max(t-$time\\,0))*between(t\\,$time\\,$end)"
  frequency=$((frequency == 720 ? 920 : 720))
done
if [[ "$force_rebuild" == true || ! -f "$render_dir/interface-sfx.wav" ]]; then
  ensure_memory_headroom
  ffmpeg -hide_banner -loglevel error -threads 2 -filter_threads 1 -y -f lavfi \
    -i "aevalsrc=$expression:s=48000:d=$duration" \
    -af "aformat=channel_layouts=stereo" -c:a pcm_s16le "$render_dir/interface-sfx.wav"
fi

# Three copies are crossfaded into one continuous bed, then kept deliberately
# quieter than speech. The final fade follows the actual rendered media length.
music_needs_bed="$force_rebuild"
if [[ "$force_rebuild" == true || ! -f "$render_dir/music-normalized.wav" || "$music_source" -nt "$render_dir/music-normalized.wav" ]]; then
  ensure_memory_headroom
  ffmpeg -hide_banner -loglevel error -threads 2 -filter_threads 1 -y -i "$music_source" \
    -af "aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,loudnorm=I=-26:TP=-5:LRA=5,aresample=48000" \
    -ar 48000 -ac 2 -c:a pcm_s16le "$render_dir/music-normalized.wav"
  music_needs_bed=true
fi
if [[ "$music_needs_bed" == true || ! -f "$render_dir/background-music.wav" ]]; then
  ensure_memory_headroom
  ffmpeg -hide_banner -loglevel error -threads 2 -filter_complex_threads 1 -y \
    -i "$render_dir/music-normalized.wav" -i "$render_dir/music-normalized.wav" -i "$render_dir/music-normalized.wav" -i "$render_dir/music-normalized.wav" \
    -filter_complex "[0:a]atrim=0:101.59,afade=t=out:st=99:d=2.59,asetpts=PTS-STARTPTS[m0];[1:a]atrim=0:101.59,afade=t=in:st=0:d=2.59,afade=t=out:st=99:d=2.59,asetpts=PTS-STARTPTS,adelay=99000:all=1[m1];[2:a]atrim=0:101.59,afade=t=in:st=0:d=2.59,afade=t=out:st=99:d=2.59,asetpts=PTS-STARTPTS,adelay=198000:all=1[m2];[3:a]atrim=0:101.59,afade=t=in:st=0:d=2.59,asetpts=PTS-STARTPTS,adelay=297000:all=1[m3];[m0][m1][m2][m3]amix=inputs=4:duration=longest:normalize=0,atrim=0:$duration,afade=t=in:st=0:d=1.5,afade=t=out:st=$fade_out_start:d=2[bed]" \
    -map "[bed]" -ar 48000 -ac 2 -c:a pcm_s16le "$render_dir/background-music.wav"
fi

# Only the four explicitly requested cached AI descriptions are retained from
# recorded system audio. Each is normalized independently, reduced by about
# three decibels, and softly gated at its boundaries. This excludes slider,
# checkbox, dialog, progress, and non-wolf JAWS speech from the final program.
mapfile -t jaws_windows < "$jaws_windows_manifest"
if [[ "${#jaws_windows[@]}" -ne 4 ]]; then
  echo "Expected four JAWS description windows, found ${#jaws_windows[@]}." >&2
  exit 1
fi
system_filter="[0:a]asplit=4[sys0][sys1][sys2][sys3];"
system_mix_inputs=""
for index in 0 1 2 3; do
  IFS=$'\t' read -r label start end <<< "${jaws_windows[$index]}"
  window_duration="$(awk -v start="$start" -v end="$end" 'BEGIN { printf "%.3f", end-start }')"
  fade_out="$(awk -v duration="$window_duration" 'BEGIN { value=duration-0.15; if(value<0)value=0; printf "%.3f",value }')"
  delay_ms="$(awk -v start="$start" 'BEGIN { printf "%d", start*1000 }')"
  system_filter+="[sys$index]atrim=start=$start:end=$end,asetpts=PTS-STARTPTS,highpass=f=65,lowpass=f=12000,dynaudnorm=f=200:g=15:p=0.70:m=7:r=0.10:s=5,volume=0.70,afade=t=in:st=0:d=0.08,afade=t=out:st=$fade_out:d=0.15,adelay=$delay_ms:all=1[jaws$index];"
  system_mix_inputs+="[jaws$index]"
done
system_filter+="${system_mix_inputs}amix=inputs=4:duration=longest:normalize=0,apad=whole_dur=$duration,atrim=0:$duration,aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo[system];"

# Piper remains the foreground voice. The music bed ducks under either voice,
# with slow attack and release to avoid pumping.
ensure_memory_headroom
ffmpeg -hide_banner -loglevel error -threads 2 -filter_complex_threads 1 -y \
  -i "$screen" -i "$render_dir/narration-full.wav" -i "$render_dir/interface-sfx.wav" -i "$render_dir/background-music.wav" \
  -filter_complex "$system_filter[1:a]aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo,asplit=2[nside][nmix];[system][nside]sidechaincompress=threshold=0.025:ratio=6:attack=90:release=650:knee=6,aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo[systemducked];[systemducked][nmix]amix=inputs=2:duration=longest:normalize=0,alimiter=limit=0.88:level=false,aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo,asplit=2[voices][voiceside];[3:a]aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo[music];[music][voiceside]sidechaincompress=threshold=0.020:ratio=8:attack=120:release=900:knee=6,aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo[duckedmusic];[2:a]aresample=48000,aformat=sample_rates=48000:sample_fmts=fltp:channel_layouts=stereo[sfx];[voices][duckedmusic][sfx]amix=inputs=3:duration=longest:normalize=0,alimiter=limit=0.90:level=false,atrim=0:$duration,afade=t=in:st=0:d=$program_fade_in_seconds,afade=t=out:st=$program_fade_out_start:d=$program_fade_out_seconds[premaster]" \
  -map "[premaster]" -ar 48000 -c:a pcm_s24le "$render_dir/audio-premaster.wav"

# Measure first, then perform a linear second loudness pass for predictable
# streaming playback: -16 LUFS integrated, <= -1.5 dB true peak, and <= 7 LU LRA.
ensure_memory_headroom
analysis="$(ffmpeg -hide_banner -nostats -threads 2 -filter_threads 1 -i "$render_dir/audio-premaster.wav" \
  -af loudnorm=I=-16:TP=-1.5:LRA=7:print_format=json -f null - 2>&1)"
measured_i="$(sed -n 's/.*"input_i" : "\([^"]*\)".*/\1/p' <<< "$analysis")"
measured_tp="$(sed -n 's/.*"input_tp" : "\([^"]*\)".*/\1/p' <<< "$analysis")"
measured_lra="$(sed -n 's/.*"input_lra" : "\([^"]*\)".*/\1/p' <<< "$analysis")"
measured_thresh="$(sed -n 's/.*"input_thresh" : "\([^"]*\)".*/\1/p' <<< "$analysis")"
offset="$(sed -n 's/.*"target_offset" : "\([^"]*\)".*/\1/p' <<< "$analysis")"
for value in "$measured_i" "$measured_tp" "$measured_lra" "$measured_thresh" "$offset"; do
  if [[ -z "$value" ]]; then
    echo "FFmpeg did not return complete loudness measurements." >&2
    exit 1
  fi
done

if [[ "$reuse_rendered_video" == true ]]; then
  if [[ ! -f "$output" ]]; then
    echo "Cannot reuse rendered video because $output does not exist." >&2
    exit 1
  fi
  video_input="$output"
  video_options=(-c:v copy)
else
  video_input="$screen"
  video_options=(-vf "ass=$work/overlays.ass" -c:v libx264 -preset medium -crf 18 -profile:v high -level 4.1 -pix_fmt yuv420p)
fi

ensure_memory_headroom
ffmpeg -hide_banner -loglevel error -threads 2 -filter_threads 1 -y \
  -i "$video_input" -i "$render_dir/audio-premaster.wav" \
  -map 0:v:0 -map 1:a:0 \
  -af "loudnorm=I=-16:TP=-1.5:LRA=7:measured_I=$measured_i:measured_TP=$measured_tp:measured_LRA=$measured_lra:measured_thresh=$measured_thresh:offset=$offset:linear=true" \
  "${video_options[@]}" -threads 2 \
  -c:a aac -b:a 192k -ar 48000 -movflags +faststart \
  -metadata title="TATAPP Accessible Walkthrough" \
  -metadata comment="Background music: 78 PULSE by kjartan_abel, CC BY 4.0" \
  "$temporary_output"

if [[ -f "$output" && ! -f "$previous_output" ]]; then
  cp -p -- "$output" "$previous_output"
fi
mv -f -- "$temporary_output" "$output"

echo "Remastered walkthrough audio and updated $output"
ffprobe -v error -show_entries format=duration,size,bit_rate \
  -show_entries stream=index,codec_name,codec_type,width,height,r_frame_rate,sample_rate,channels \
  -of json "$output"
