#!/usr/bin/env bash

set -euo pipefail
export MALLOC_ARENA_MAX=2
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/.." && pwd -P)"
work="$repo_root/artifacts/walkthrough"
raw="$work/raw-capture.mkv"
capture_timeline="$work/capture-timeline.json"
jaws_raw="$work/offline-jaws-capture.mkv"
jaws_timeline="$work/offline-jaws-timeline.json"
segments_dir="$work/render/segments"
render_dir="$work/render"
font_regular="/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
font_bold="/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"
intro_visual_fade_seconds="1.5"
outro_visual_fade_start_seconds="11.0"
outro_visual_fade_seconds="2.0"

mkdir -p "$segments_dir"

for required in "$raw" "$capture_timeline" "$jaws_raw" "$jaws_timeline"; do
  if [[ ! -f "$required" ]]; then
    echo "Required walkthrough source is missing: $required" >&2
    exit 1
  fi
done

event_time() {
  local event_id="$1"
  local occurrence="${2:-0}"
  jq -er --arg id "$event_id" --argjson occurrence "$occurrence" \
    '[.[] | select(.id == $id)] | .[$occurrence].seconds' "$capture_timeline"
}

jaws_event_time() {
  local event_id="$1"
  jq -er --arg id "$event_id" '.[] | select(.id == $id) | .seconds' "$jaws_timeline"
}

offset_time() {
  awk -v value="$1" -v offset="$2" 'BEGIN { printf "%.3f", value + offset }'
}

duration_between() {
  awk -v start="$1" -v end="$2" 'BEGIN { printf "%.3f", end - start }'
}

raw_duration="$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$raw")"
capture_end_event="$(event_time capture-end)"
# The recorder starts two seconds before the event stopwatch and retains the
# scripted three-second hold after capture-end. Deriving that pre-roll from the
# files avoids a fragile hard-coded offset between event and media time.
capture_offset="$(awk -v media="$raw_duration" -v end="$capture_end_event" \
  'BEGIN { value=media-end-3.0; if (value < 0 || value > 5) exit 1; printf "%.3f", value }')"

source_event_time() {
  offset_time "$(event_time "$1" "${2:-0}")" "$capture_offset"
}

ensure_memory_headroom() {
  local available_kib
  available_kib="$(awk '/^MemAvailable:/ { print $2 }' /proc/meminfo)"
  if [[ -n "$available_kib" && "$available_kib" -lt 1572864 ]]; then
    echo "Walkthrough render stopped before the next FFmpeg process: less than 1.5 GiB is available. Close memory-intensive applications and run this script again." >&2
    exit 1
  fi
}

# Build source selections from the authoritative capture timeline. This keeps
# edits stable when Qwen preparation takes a different amount of time. Every
# reverse anatomical stage gets its complete context-hold and camera move;
# progress is shortened, while the four requested JAWS readings are trimmed to
# their actual speech and taken from the dedicated post-cache capture.
capture_start="$(source_event_time capture-start)"
wolf_dialog="$(source_event_time wolf-head-dialog)"
wolf_color="$(source_event_time wolf-color-fade)"
wolf_placement_fine="$(source_event_time wolf-placement-fine)"
wolf_placement_medium="$(source_event_time wolf-placement-medium)"
wolf_placement_thick="$(source_event_time wolf-placement-thick)"
wolf_placement_line="$(source_event_time wolf-placement-line-art)"
wolf_placement_bw="$(source_event_time wolf-placement-black-white)"
wolf_placement_gray="$(source_event_time wolf-placement-grayscale)"
wolf_placement_color="$(source_event_time wolf-placement-returning-color)"
wolf_placement_full="$(source_event_time wolf-placement-full-color)"
wolf_original_return="$(source_event_time wolf-original-return)"
offline_start="$(source_event_time offline-ai-start)"
offline_ready="$(source_event_time offline-ai-ready)"
offline_off="$(source_event_time offline-ai-off)"
clock_dialog="$(source_event_time clock-roses-dialog)"
skull_dialog="$(source_event_time sugar-skull-dialog)"
capture_end="$(source_event_time capture-end)"

first_start="$(offset_time "$capture_start" 0.2)"
first_end="$(offset_time "$wolf_dialog" 0.65)"
photo_end="$(offset_time "$wolf_color" -0.15)"
flat_end="$(offset_time "$wolf_placement_fine" -0.10)"
original_end="$(offset_time "$offline_start" 3.0)"
ready_start="$(offset_time "$offline_ready" -0.20)"
clock_start="$(offset_time "$offline_off" -0.10)"
clock_end="$(offset_time "$skull_dialog" -0.10)"
skull_start="$clock_end"
skull_end="$(offset_time "$capture_end" -0.10)"

segments=(
  "controls|$raw|$first_start|$(duration_between "$first_start" "$first_end")"
  "select-wolf|$raw|$first_end|$(duration_between "$first_end" "$photo_end")"
  "wolf-flat-stages|$raw|$photo_end|$(duration_between "$photo_end" "$flat_end")"
  "wolf-placement-fine|$raw|$(offset_time "$wolf_placement_fine" -0.05)|$(duration_between "$(offset_time "$wolf_placement_fine" -0.05)" "$(offset_time "$wolf_placement_medium" -0.05)")"
  "wolf-placement-medium|$raw|$(offset_time "$wolf_placement_medium" -0.05)|$(duration_between "$(offset_time "$wolf_placement_medium" -0.05)" "$(offset_time "$wolf_placement_thick" -0.05)")"
  "wolf-placement-thick|$raw|$(offset_time "$wolf_placement_thick" -0.05)|$(duration_between "$(offset_time "$wolf_placement_thick" -0.05)" "$(offset_time "$wolf_placement_line" -0.05)")"
  "wolf-placement-line-art|$raw|$(offset_time "$wolf_placement_line" -0.05)|$(duration_between "$(offset_time "$wolf_placement_line" -0.05)" "$(offset_time "$wolf_placement_bw" -0.05)")"
  "wolf-placement-black-white|$raw|$(offset_time "$wolf_placement_bw" -0.05)|$(duration_between "$(offset_time "$wolf_placement_bw" -0.05)" "$(offset_time "$wolf_placement_gray" -0.05)")"
  "wolf-placement-grayscale|$raw|$(offset_time "$wolf_placement_gray" -0.05)|$(duration_between "$(offset_time "$wolf_placement_gray" -0.05)" "$(offset_time "$wolf_placement_color" -0.05)")"
  "wolf-placement-returning-color|$raw|$(offset_time "$wolf_placement_color" -0.05)|$(duration_between "$(offset_time "$wolf_placement_color" -0.05)" "$(offset_time "$wolf_placement_full" -0.05)")"
  "wolf-placement-full-color|$raw|$(offset_time "$wolf_placement_full" -0.05)|$(duration_between "$(offset_time "$wolf_placement_full" -0.05)" "$(offset_time "$wolf_original_return" -0.05)")"
  "offline-ai-start|$raw|$(offset_time "$wolf_original_return" -0.05)|6.000"
)

progress_index=1
for occurrence in 0 2 4 5; do
  progress_time="$(source_event_time offline-ai-progress "$occurrence")"
  segments+=("offline-ai-progress-$progress_index|$raw|$(offset_time "$progress_time" -0.10)|1.200")
  progress_index=$((progress_index + 1))
done

# FFmpeg begins writing roughly two seconds before the dedicated recorder's
# stopwatch. These speech-complete windows were verified with silencedetect at
# -35 dB and retain a short lead/tail without the long scripted waiting gaps.
jaws_capture_offset=2.0
jaws_level_1_start="$(offset_time "$(jaws_event_time ai-level-1-description)" "$(awk 'BEGIN{print 2.0-0.14}')")"
jaws_level_8_start="$(offset_time "$(jaws_event_time ai-level-8-description)" "$(awk 'BEGIN{print 2.0-0.07}')")"
jaws_level_9_start="$(offset_time "$(jaws_event_time ai-level-9-description)" "$(awk 'BEGIN{print 2.0-0.15}')")"
jaws_level_16_start="$(offset_time "$(jaws_event_time ai-level-16-description)" "$(awk 'BEGIN{print 2.0-0.10}')")"

segments+=(
  "offline-ai-ready|$raw|$ready_start|10.000"
  "jaws-level-1|$jaws_raw|$jaws_level_1_start|6.750"
  "jaws-level-8|$jaws_raw|$jaws_level_8_start|8.500"
  "jaws-level-9|$jaws_raw|$jaws_level_9_start|8.250"
  "jaws-level-16|$jaws_raw|$jaws_level_16_start|9.800"
  "clock-and-roses|$raw|$clock_start|$(duration_between "$clock_start" "$clock_end")"
  "sugar-skull|$raw|$skull_start|$(duration_between "$skull_start" "$skull_end")"
  "final-hold|$raw|$skull_end|$(duration_between "$skull_end" "$raw_duration")"
)

segment_count="${#segments[@]}"
segment_manifest="$render_dir/source-segments.tsv"
: > "$segment_manifest"

index=1
for definition in "${segments[@]}"; do
  ensure_memory_headroom
  IFS='|' read -r label source start duration <<< "$definition"
  output=$(printf "%s/%02d.mkv" "$segments_dir" "$index")
  if [[ "$label" == "final-hold" ]]; then
    ffmpeg -hide_banner -loglevel error -threads 2 -filter_threads 1 -y -ss "$start" -t "$duration" -i "$source" \
      -map 0:v:0 -map 0:a:0 -vf "setpts=PTS-STARTPTS,fps=30,format=yuv420p,tpad=stop_mode=clone:stop_duration=3" \
      -af "asetpts=PTS-STARTPTS,aresample=48000,apad=pad_dur=3" -t 9.5 \
      -c:v libx264 -threads 2 -preset veryfast -crf 18 -g 60 -keyint_min 60 -sc_threshold 0 \
      -c:a pcm_s16le "$output"
  else
    ffmpeg -hide_banner -loglevel error -threads 2 -filter_threads 1 -y -ss "$start" -t "$duration" -i "$source" \
      -map 0:v:0 -map 0:a:0 -vf "setpts=PTS-STARTPTS,fps=30,format=yuv420p" -af "asetpts=PTS-STARTPTS,aresample=48000" \
      -c:v libx264 -threads 2 -preset veryfast -crf 18 -g 60 -keyint_min 60 -sc_threshold 0 \
      -c:a pcm_s16le "$output"
  fi
  printf '%s\t%s\t%s\t%s\n' "$label" "$(basename "$source")" "$start" "$duration" >> "$segment_manifest"
  echo "Rendered walkthrough segment $index of $segment_count: $label."
  index=$((index + 1))
done

# Record final-timeline windows for the audio master. Raw JAWS/system audio is
# permitted only in these four ranges; all other app/system speech is muted.
jaws_window_manifest="$render_dir/jaws-audio-windows.tsv"
: > "$jaws_window_manifest"
final_cursor=9.0
segment_index=1
while IFS=$'\t' read -r label _source _source_start _requested_duration; do
  encoded_segment=$(printf "%s/%02d.mkv" "$segments_dir" "$segment_index")
  encoded_duration="$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$encoded_segment")"
  final_end="$(awk -v start="$final_cursor" -v duration="$encoded_duration" 'BEGIN { printf "%.3f", start+duration }')"
  if [[ "$label" == jaws-level-* ]]; then
    printf '%s\t%.3f\t%.3f\n' "$label" "$final_cursor" "$final_end" >> "$jaws_window_manifest"
  fi
  final_cursor="$final_end"
  segment_index=$((segment_index + 1))
done < "$segment_manifest"

ensure_memory_headroom
ffmpeg -hide_banner -loglevel error -threads 2 -filter_complex_threads 1 -y \
  -loop 1 -framerate 30 -t 9 -i "$work/assets/wolf_head.png" \
  -loop 1 -framerate 30 -t 9 -i "$work/assets/clock_roses.jpg" \
  -loop 1 -framerate 30 -t 9 -i "$work/assets/sugar_skull.jpg" \
  -f lavfi -t 9 -i "anullsrc=r=48000:cl=stereo" \
  -filter_complex "[0:v]scale=720:1220:force_original_aspect_ratio=increase,crop=640:1080,zoompan=z='min(zoom+0.00022,1.06)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=270:s=640x1080:fps=30[p0];[1:v]scale=720:1220:force_original_aspect_ratio=increase,crop=640:1080,zoompan=z='min(zoom+0.00018,1.05)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=270:s=640x1080:fps=30[p1];[2:v]scale=720:1220:force_original_aspect_ratio=increase,crop=640:1080,zoompan=z='min(zoom+0.00025,1.07)':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d=270:s=640x1080:fps=30[p2];[p0][p1][p2]hstack=inputs=3,drawbox=x=0:y=0:w=iw:h=ih:color=black@0.48:t=fill,drawtext=fontfile='$font_bold':text='TATAPP':fontcolor=white:fontsize=104:x=(w-text_w)/2:y=320:shadowcolor=black@0.8:shadowx=4:shadowy=4,drawtext=fontfile='$font_regular':text='Tattoo Art Prepper':fontcolor=white:fontsize=46:x=(w-text_w)/2:y=455,drawtext=fontfile='$font_bold':text='3 DESIGNS  •  16 STAGES  •  3D ANATOMY  •  OFFLINE AI':fontcolor=0xF4C9FF:fontsize=29:x=(w-text_w)/2:y=565,fade=t=in:st=0:d=$intro_visual_fade_seconds,fade=t=out:st=8.3:d=0.7[v]" \
  -map "[v]" -map 3:a -t 9 -c:v libx264 -threads 2 -preset veryfast -crf 18 -g 60 -keyint_min 60 \
  -sc_threshold 0 -pix_fmt yuv420p -c:a pcm_s16le "$render_dir/00-intro.mkv"
echo "Rendered opening title sequence."

ensure_memory_headroom
ffmpeg -hide_banner -loglevel error -threads 2 -filter_complex_threads 1 -y \
  -loop 1 -framerate 30 -t 13 -i "$work/assets/sugar_skull.jpg" \
  -loop 1 -framerate 30 -t 13 -i "$work/saved/sugar-skull-left-upper-arm-full-color.jpg" \
  -f lavfi -t 13 -i "anullsrc=r=48000:cl=stereo" \
  -filter_complex "[0:v]scale=960:1080:force_original_aspect_ratio=decrease,pad=960:1080:(ow-iw)/2:(oh-ih)/2:0x19131f[p0];[1:v]scale=960:1080:force_original_aspect_ratio=decrease,pad=960:1080:(ow-iw)/2:(oh-ih)/2:0x19131f[p1];[p0][p1]hstack=inputs=2,drawbox=x=0:y=0:w=iw:h=165:color=black@0.72:t=fill,drawbox=x=0:y=865:w=iw:h=215:color=black@0.72:t=fill,drawtext=fontfile='$font_bold':text='CHOOSE. DESCRIBE. PLACE. SAVE.':fontcolor=white:fontsize=52:x=(w-text_w)/2:y=48,drawtext=fontfile='$font_bold':text='ISOLATED DESIGN':fontcolor=white:fontsize=30:x=360:y=885,drawtext=fontfile='$font_bold':text='SAVED 3D PLACEMENT':fontcolor=white:fontsize=30:x=1240:y=885,drawtext=fontfile='$font_regular':text='TATAPP  •  TATTOO ART PREPPER':fontcolor=0xF4C9FF:fontsize=34:x=(w-text_w)/2:y=975,fade=t=in:st=0:d=0.6,fade=t=out:st=$outro_visual_fade_start_seconds:d=$outro_visual_fade_seconds[v]" \
  -map "[v]" -map 2:a -t 13 -c:v libx264 -threads 2 -preset veryfast -crf 18 -g 60 -keyint_min 60 \
  -sc_threshold 0 -pix_fmt yuv420p -c:a pcm_s16le "$render_dir/22-outro.mkv"
echo "Rendered closing comparison card."

concat_file="$render_dir/concat.txt"
: > "$concat_file"
printf "file '%s'\n" "$render_dir/00-intro.mkv" >> "$concat_file"
for number in $(seq -w 1 "$segment_count"); do
  printf "file '%s'\n" "$segments_dir/$number.mkv" >> "$concat_file"
done
printf "file '%s'\n" "$render_dir/22-outro.mkv" >> "$concat_file"
ensure_memory_headroom
ffmpeg -hide_banner -loglevel error -threads 2 -y -f concat -safe 0 -i "$concat_file" -c copy "$render_dir/screen-sequence.mkv"
echo "Assembled the visual walkthrough timeline."

"$script_dir/remaster-walkthrough-audio.sh" --force
