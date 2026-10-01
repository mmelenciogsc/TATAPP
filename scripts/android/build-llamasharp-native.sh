#!/usr/bin/env bash

set -Eeuo pipefail
export LC_ALL=C

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
repo_root="$(cd -- "$script_dir/../.." && pwd -P)"
readonly script_dir repo_root
readonly llama_commit="3f7c29d318e317b63f54c558bc69803963d7d88c"
readonly llamasharp_version="0.27.0"
readonly ndk_version="27.0.12077973"
readonly cmake_version="3.22.1"
readonly android_api="26"
readonly upstream_url="https://github.com/ggml-org/llama.cpp.git"

usage() {
    cat <<EOF
Usage: $(basename "$0") [--output DIR] [--cache DIR] [--jobs COUNT]

Build the five CPU/MTMD libraries pinned by LLamaSharp $llamasharp_version for
Android arm64-v8a and x86_64 with 16 KiB flexible-page support.

Defaults:
  output  $repo_root/artifacts/android/native/llamasharp-$llamasharp_version
  cache   $repo_root/.android-native-cache

Required environment:
  ANDROID_SDK_ROOT (ANDROID_HOME is accepted as a fallback)
EOF
}

output_root="${TATAPP_ANDROID_NATIVE_OUTPUT:-$repo_root/artifacts/android/native/llamasharp-$llamasharp_version}"
cache_root="${TATAPP_ANDROID_NATIVE_CACHE:-$repo_root/.android-native-cache}"
jobs="${JOBS:-$(getconf _NPROCESSORS_ONLN)}"

while (($#)); do
    case "$1" in
        --output)
            [[ $# -ge 2 ]] || { usage >&2; exit 2; }
            output_root=$2
            shift 2
            ;;
        --cache)
            [[ $# -ge 2 ]] || { usage >&2; exit 2; }
            cache_root=$2
            shift 2
            ;;
        --jobs)
            [[ $# -ge 2 ]] || { usage >&2; exit 2; }
            jobs=$2
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown argument: %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
done

[[ "$jobs" =~ ^[1-9][0-9]*$ ]] || { printf 'JOBS must be a positive integer.\n' >&2; exit 2; }

android_sdk="${ANDROID_SDK_ROOT:-${ANDROID_HOME:-}}"
[[ -n "$android_sdk" ]] || {
    printf 'Set ANDROID_SDK_ROOT to the Android SDK directory.\n' >&2
    exit 2
}

readonly ndk="$android_sdk/ndk/$ndk_version"
readonly cmake="$android_sdk/cmake/$cmake_version/bin/cmake"
readonly ninja="$android_sdk/cmake/$cmake_version/bin/ninja"
readonly llvm_bin="$ndk/toolchains/llvm/prebuilt/linux-x86_64/bin"
readonly readelf="$llvm_bin/llvm-readelf"
readonly nm="$llvm_bin/llvm-nm"
readonly strip="$llvm_bin/llvm-strip"

for tool in git sha256sum awk sort wc; do
    command -v "$tool" >/dev/null || { printf 'Required command not found: %s\n' "$tool" >&2; exit 2; }
done
for tool in "$cmake" "$ninja" "$readelf" "$nm" "$strip"; do
    [[ -x "$tool" ]] || { printf 'Required executable not found: %s\n' "$tool" >&2; exit 2; }
done

mkdir -p "$cache_root" "$output_root"
cache_root="$(cd -- "$cache_root" && pwd -P)"
output_root="$(cd -- "$output_root" && pwd -P)"
readonly source_dir="$cache_root/llama.cpp-$llama_commit"
readonly build_root="$cache_root/build-$llama_commit-ndk$ndk_version-api$android_api"

if [[ ! -e "$source_dir" ]]; then
    mkdir -p "$source_dir"
    git -C "$source_dir" init --quiet
    git -C "$source_dir" remote add origin "$upstream_url"
    GIT_TERMINAL_PROMPT=0 git -C "$source_dir" -c protocol.version=2 \
        fetch --depth=1 --filter=blob:none origin "$llama_commit"
    git -C "$source_dir" sparse-checkout init --cone
    git -C "$source_dir" sparse-checkout set cmake common ggml include licenses src tools vendor
    git -C "$source_dir" -c advice.detachedHead=false checkout --detach FETCH_HEAD
elif [[ ! -d "$source_dir/.git" ]]; then
    printf 'Refusing non-git source cache: %s\n' "$source_dir" >&2
    exit 2
fi

actual_commit="$(git -C "$source_dir" rev-parse HEAD 2>/dev/null || true)"
[[ "$actual_commit" == "$llama_commit" ]] || {
    printf 'Cached source is not the pinned commit: %s\n' "$source_dir" >&2
    exit 1
}
[[ -z "$(git -C "$source_dir" status --porcelain --untracked-files=normal)" ]] || {
    printf 'Cached source has local changes; refusing to build it: %s\n' "$source_dir" >&2
    exit 1
}
[[ -f "$source_dir/src/models/qwen3vl.cpp" && -f "$source_dir/tools/mtmd/models/qwen3vl.cpp" ]] || {
    printf 'Pinned source is missing Qwen3-VL text or MTMD support.\n' >&2
    exit 1
}

staging_root="$(mktemp -d "$cache_root/staging.XXXXXX")"
trap 'rm -rf -- "$staging_root"' EXIT

readonly -a libraries=(libggml-base.so libggml-cpu.so libggml.so libllama.so libmtmd.so)

verify_load_alignment() {
    local library=$1
    "$readelf" -lW "$library" | awk '
        $1 == "LOAD" { count += 1; if ($NF != "0x4000") bad = 1 }
        END { exit !(count > 0 && bad == 0) }
    '
}

verify_symbols() {
    local abi=$1
    local artifact_dir=$2
    local expected_count expected_hash actual_count actual_hash symbol_file
    symbol_file="$staging_root/symbols-$abi.txt"
    : > "$symbol_file"

    for library in "${libraries[@]}"; do
        "$nm" -D --defined-only --format=posix "$artifact_dir/$library" \
            | awk '$1 ~ /^(ggml|gguf|llama|mtmd)_/ { print $1 }' >> "$symbol_file"
    done
    sort -u -o "$symbol_file" "$symbol_file"

    case "$abi" in
        arm64-v8a)
            expected_count=1195
            expected_hash=cdb15263585f9ae12446a1faa8b3999ce993419672b50dcfc880ccf774675332
            ;;
        x86_64)
            expected_count=1176
            expected_hash=8dc4fdfb1f44b96f2a5727c095d4734e23c5cfc7ce45493f970ced42a4493055
            ;;
        *)
            printf 'No symbol contract for ABI %s.\n' "$abi" >&2
            return 1
            ;;
    esac

    actual_count="$(wc -l < "$symbol_file" | tr -d ' ')"
    actual_hash="$(sha256sum "$symbol_file" | awk '{ print $1 }')"
    [[ "$actual_count" == "$expected_count" && "$actual_hash" == "$expected_hash" ]] || {
        printf 'Public C ABI mismatch for %s: count=%s hash=%s\n' "$abi" "$actual_count" "$actual_hash" >&2
        return 1
    }

    for symbol in \
        llama_model_load_from_file llama_init_from_model llama_decode llama_tokenize \
        mtmd_context_params_default mtmd_init_from_file mtmd_helper_bitmap_init_from_buf \
        mtmd_tokenize mtmd_helper_eval_chunks mtmd_support_vision; do
        grep -Fxq "$symbol" "$symbol_file" || {
            printf 'Required symbol %s is missing for %s.\n' "$symbol" "$abi" >&2
            return 1
        }
    done
}

for abi in arm64-v8a x86_64; do
    build_dir="$build_root/$abi"
    artifact_dir="$staging_root/native/android-$abi"

    "$cmake" -S "$source_dir" -B "$build_dir" -G Ninja \
        -DCMAKE_MAKE_PROGRAM="$ninja" \
        -DCMAKE_TOOLCHAIN_FILE="$ndk/build/cmake/android.toolchain.cmake" \
        -DANDROID_ABI="$abi" \
        -DANDROID_PLATFORM="android-$android_api" \
        -DANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON \
        -DCMAKE_BUILD_TYPE=Release \
        -DBUILD_SHARED_LIBS=ON \
        -DGGML_NATIVE=OFF \
        -DGGML_OPENMP=OFF \
        -DGGML_LLAMAFILE=OFF \
        -DGGML_CCACHE=OFF \
        -DLLAMA_BUILD_COMMON=ON \
        -DLLAMA_BUILD_TESTS=OFF \
        -DLLAMA_BUILD_EXAMPLES=OFF \
        -DLLAMA_BUILD_TOOLS=ON \
        -DLLAMA_BUILD_SERVER=OFF \
        -DLLAMA_OPENSSL=OFF
    "$cmake" --build "$build_dir" --target mtmd --parallel "$jobs"

    mkdir -p "$artifact_dir"
    for library in "${libraries[@]}"; do
        install -m 0755 "$build_dir/bin/$library" "$artifact_dir/$library"
    done
    "$strip" --strip-unneeded "$artifact_dir"/*.so

    for library in "${libraries[@]}"; do
        verify_load_alignment "$artifact_dir/$library" || {
            printf 'Library is not 16 KiB page compatible: %s/%s\n' "$abi" "$library" >&2
            exit 1
        }
        soname=$("$readelf" -d "$artifact_dir/$library" | awk '/SONAME/ { gsub(/\[|\]/, "", $5); print $5 }')
        [[ "$soname" == "$library" ]] || {
            printf 'Unexpected SONAME for %s/%s: %s\n' "$abi" "$library" "$soname" >&2
            exit 1
        }
        if "$readelf" -d "$artifact_dir/$library" | grep -Fq 'libc++_shared.so'; then
            printf 'Unexpected shared C++ runtime dependency in %s/%s.\n' "$abi" "$library" >&2
            exit 1
        fi
    done
    verify_symbols "$abi" "$artifact_dir"
done

mkdir -p "$output_root/native/android-arm64-v8a" "$output_root/native/android-x86_64"
for abi in arm64-v8a x86_64; do
    for library in "${libraries[@]}"; do
        install -m 0755 "$staging_root/native/android-$abi/$library" \
            "$output_root/native/android-$abi/$library"
    done
done
install -m 0644 "$source_dir/LICENSE" "$output_root/LICENSE-llama.cpp"
install -m 0644 "$script_dir/NATIVE-RUNTIME-NOTICES.md" "$output_root/NATIVE-RUNTIME-NOTICES.md"
install -m 0755 "$script_dir/build-llamasharp-native.sh" "$output_root/rebuild-llamasharp-native.sh"

cat > "$output_root/BUILD-PROVENANCE.txt" <<EOF
LLamaSharp managed API version: $llamasharp_version
llama.cpp URL: $upstream_url
llama.cpp commit: $llama_commit
Android NDK: $ndk_version
Android native API: $android_api
CMake: $cmake_version
ABIs: arm64-v8a x86_64
Flexible page sizes: ON
EOF

(
    cd -- "$output_root"
    sha256sum native/android-arm64-v8a/*.so native/android-x86_64/*.so > SHA256SUMS
    sha256sum -c SHA256SUMS
)

printf 'Verified LLamaSharp %s Android native runtime: %s\n' "$llamasharp_version" "$output_root"
