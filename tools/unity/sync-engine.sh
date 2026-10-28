#!/usr/bin/env bash
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
mkdir -p "$HOME/scratchpad"
scratch=$(mktemp -d "$HOME/scratchpad/rps-engine.XXXXXX")
trap 'rm -rf "$scratch"' EXIT
publish="$scratch/publish"
renamed="$scratch/renamed"
dotnet publish "$repo/sensei-engine/src/Sensei.Engine" -c Release -f netstandard2.1 -o "$publish"
# RULING(Handler 2026-10-28): rename copies of assemblies Unity itself supplies.
# The first seven occur in the Unity 6000.6 netstandard2.1 reference pack;
# the annotations facade is also shipped by Unity's Mono runtime.
clashes=(
  Microsoft.Bcl.AsyncInterfaces
  Microsoft.Extensions.DependencyInjection.Abstractions
  Microsoft.Extensions.Logging.Abstractions
  System.ComponentModel.Annotations
  System.Diagnostics.DiagnosticSource
  System.Runtime.CompilerServices.Unsafe
  System.Text.Encodings.Web
  System.Text.Json
)
allow=(
  Sensei.Engine Google.Protobuf
  Microsoft.Agents.AI Microsoft.Agents.AI.Abstractions Microsoft.Agents.AI.OpenAI
  Microsoft.Bcl.AsyncInterfaces Microsoft.Bcl.HashCode Microsoft.Bcl.Memory
  Microsoft.Bcl.Numerics Microsoft.Bcl.TimeProvider
  Microsoft.Extensions.AI Microsoft.Extensions.AI.Abstractions Microsoft.Extensions.AI.OpenAI
  Microsoft.Extensions.Caching.Abstractions Microsoft.Extensions.Compliance.Abstractions
  Microsoft.Extensions.Configuration.Abstractions Microsoft.Extensions.DependencyInjection.Abstractions
  Microsoft.Extensions.Diagnostics.Abstractions Microsoft.Extensions.FileProviders.Abstractions
  Microsoft.Extensions.FileSystemGlobbing Microsoft.Extensions.Hosting.Abstractions
  Microsoft.Extensions.Logging.Abstractions Microsoft.Extensions.ObjectPool Microsoft.Extensions.Options
  Microsoft.Extensions.Primitives Microsoft.Extensions.VectorData.Abstractions Microsoft.ML.Tokenizers
  OpenAI System.ClientModel System.ComponentModel.Annotations System.Diagnostics.DiagnosticSource
  System.IO.Pipelines System.Memory.Data System.Net.ServerSentEvents System.Numerics.Tensors
  System.Runtime.CompilerServices.Unsafe System.Text.Encodings.Web System.Text.Json System.Threading.Channels
)
deny=(netstandard mscorlib System System.Runtime UnityEngine)
for path in "$publish"/*.dll; do
  assembly=$(basename "$path" .dll)
  case " ${deny[*]} " in *" $assembly "*) echo "Unexpected Unity platform assembly: $assembly" >&2; exit 1;; esac
  case " ${allow[*]} " in *" $assembly "*) ;; *) echo "Unclassified runtime dependency: $assembly" >&2; exit 1;; esac
done
for assembly in "${allow[@]}"; do test -f "$publish/$assembly.dll"; done
dotnet run --project "$repo/tools/unity/VendorRename/VendorRename.csproj" -c Release -- "$publish" "$renamed" "${clashes[@]}"
destination="$repo/unity/RedPandaSensei/Assets/Plugins/SenseiEngine"
mkdir -p "$destination"
for path in "$destination"/*.dll; do
  test -e "$path" || continue
  if [[ ! -f "$renamed/$(basename "$path")" ]]; then rm -f "$path" "$path.meta"; fi
done
for path in "$renamed"/*.dll; do
  target="$destination/$(basename "$path")"
  if ! cmp -s "$path" "$target"; then cp "$path" "$target"; fi
done
# The renamed types must not be auto-referenced by Unity packages, which use
# Unity's original types. The Sensei.Runtime asmdef explicitly references them.
uv run --no-project python - "$destination" <<'PY'
import hashlib, pathlib, re, sys
for dll in pathlib.Path(sys.argv[1]).glob('*.dll'):
    meta = pathlib.Path(str(dll) + '.meta')
    match = re.search(r'^guid: ([0-9a-f]{32})$', meta.read_text(), re.M) if meta.exists() else None
    guid = match.group(1) if match else hashlib.md5(('sensei-engine:' + dll.name).encode()).hexdigest()
    content = f'''fileFormatVersion: 2
guid: {guid}
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 3
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 1
  validateReferences: 0
  platformData:
    Any:
      enabled: 1
      settings: {{}}
    Editor:
      enabled: 1
      settings:
        CPU: AnyCPU
        OS: AnyOS
  userData:
  assetBundleName:
  assetBundleVariant:
'''
    if not meta.exists() or meta.read_text() != content: meta.write_text(content)
PY
echo "Synced ${#allow[@]} runtime DLLs; renamed ${#clashes[@]} Unity conflicts."
