# Native speech runtime provenance

- sherpa-onnx **v1.13.8**, Apache-2.0. [Release](https://github.com/k2-fsa/sherpa-onnx/releases/tag/v1.13.8), [source and license](https://github.com/k2-fsa/sherpa-onnx/tree/v1.13.8).
- Android archive: [sherpa-onnx-v1.13.8-android.tar.bz2](https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/sherpa-onnx-v1.13.8-android.tar.bz2), SHA256 `2ff63469a71cb6009aa2e3ed5f4a670f8abdcbe4bb9ffd23776afc792a6b4f44`.
- macOS Apple Silicon archive: [sherpa-onnx-v1.13.8-osx-arm64-shared-lib.tar.bz2](https://github.com/k2-fsa/sherpa-onnx/releases/download/v1.13.8/sherpa-onnx-v1.13.8-osx-arm64-shared-lib.tar.bz2), SHA256 `ae77050cdae565496059d96f5ab33d77b397a0864282a4e4a26e3b3b3effb948`.
- Managed bindings: [org.k2fsa.sherpa.onnx 1.13.8](https://www.nuget.org/packages/org.k2fsa.sherpa.onnx/1.13.8), unmodified `lib/netstandard2.0/sherpa-onnx.dll`, SHA256 of NuGet archive `f89f28fc6fbbc894a46c2c73b040780ec6ea7a21400d9d219dce4d2333981d57`.
- ONNX Runtime **1.28.2**, MIT. Both platform binaries are copied from the matching sherpa archives, not mixed with another runtime. [Source and license](https://github.com/microsoft/onnxruntime/tree/v1.28.2). The embedded version string in the Android binary is `1.28.2`.
- Android ships only `libsherpa-onnx-c-api.so` and `libonnxruntime.so`, ARM64. Their other dependencies are Android system libraries. macOS ships the two corresponding ARM64 dylibs for Editor use.

The sherpa license is in `LICENSE`. ONNX Runtime license follows verbatim.

## ONNX Runtime MIT license

MIT License

Copyright (c) Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
