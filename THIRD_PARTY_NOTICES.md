# Third-party notices

TATAPP's ordinary image-processing and anatomy code does not require a remote
service. Android includes the managed/native inference runtime but does not
include model weights. Windows does not redistribute its optional Ollama
runtime or Qwen weights.

## LLamaSharp

The Android project references LLamaSharp 0.27.0 by SciSharp STACK under the
MIT License:

- Source: https://github.com/SciSharp/LLamaSharp
- License: https://github.com/SciSharp/LLamaSharp/blob/master/LICENSE
- Packaged license copy: `src/TATAPP.Android/Resources/raw/license_llamasharp.txt`

Copyright (c) 2025 SciSharp STACK.

## Microsoft.Extensions.DependencyInjection

The Android project references Microsoft.Extensions.DependencyInjection
10.0.0 by the .NET Foundation and contributors under the MIT License:

- Package: https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/10.0.0
- Source: https://github.com/dotnet/runtime
- License: https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT
- Packaged license copy: `src/TATAPP.Android/Resources/raw/license_microsoft_extensions_dependencyinjection.txt`

## llama.cpp and embedded native components

The Android native runtime is compiled from llama.cpp commit
`3f7c29d318e317b63f54c558bc69803963d7d88c`, the revision pinned by
LLamaSharp 0.27.0. llama.cpp is distributed under the MIT License:

- Source: https://github.com/ggml-org/llama.cpp
- License: https://github.com/ggml-org/llama.cpp/blob/master/LICENSE
- Packaged license copy: `src/TATAPP.Android/Resources/raw/license_llama_cpp.txt`

The pinned native source incorporates `stb_image` by Sean Barrett under its
MIT/public-domain option and `miniaudio` by David Reid under its MIT-0/public-
domain option. TATAPP selects the permissive license alternatives recorded in:

- `scripts/android/NATIVE-RUNTIME-NOTICES.md`
- `src/TATAPP.Android/Resources/raw/native_runtime_notices.txt`

The native build script also copies llama.cpp's exact license and the notices
beside generated binaries. Do not replace the pinned source or native binary
set without reviewing and updating these notices.

## Qwen3-VL model and GGUF conversion

The configurable Android catalog references—but does not redistribute—the
following external model repository:

- GGUF repository: https://huggingface.co/unsloth/Qwen3-VL-2B-Instruct-GGUF
- Base model: https://huggingface.co/Qwen/Qwen3-VL-2B-Instruct
- Upstream source: https://github.com/QwenLM/Qwen3-VL
- Declared model/repository license: Apache License 2.0
- License text: https://www.apache.org/licenses/LICENSE-2.0

The GGUF repository attributes the base model to Qwen and is maintained by
Unsloth. Users and distributors must review the model card, repository
metadata, acceptable-use terms, and license matching the manifest's exact
artifact digests before enabling or redistributing a model. TATAPP downloads no
model unless the user explicitly consents, and model files stay in app-private
storage.

The Android catalog entry is marked tested only for the narrowly recorded API
36 x86_64 emulator smoke described in `docs/ANDROID.md`. Listing its URLs,
sizes, checksums, or that one result is not a license grant or a broader
hardware/quality claim. Model files, downloaded archives, and derived weights
must not be committed to this repository or bundled into an APK.

## Ollama

The Windows Offline AI Describe workflow optionally uses a separately installed
Ollama runtime under the MIT License:

- Source and license: https://github.com/ollama/ollama
- Windows download: https://ollama.com/download/windows
- Qwen3-VL library metadata: https://ollama.com/library/qwen3-vl

Ollama and its model files are not redistributed by TATAPP. Users are
responsible for reviewing and complying with the terms for each selected model.
