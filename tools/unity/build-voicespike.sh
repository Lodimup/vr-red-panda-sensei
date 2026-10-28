#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
if [[ -n "${UNITY_EDITOR:-}" ]]; then
  UNITY="$UNITY_EDITOR"
else
  UNITY_APP=$(mdfind 'kMDItemCFBundleIdentifier == "com.unity3d.UnityEditor5.x"' | awk '/\/6000[.]6[.]5f1\// { print; exit }')
  [[ -n "$UNITY_APP" ]] || { echo 'Unity 6000.6.5f1 not found; set UNITY_EDITOR.' >&2; exit 1; }
  UNITY="$UNITY_APP/Contents/MacOS/Unity"
fi
PROJECT="$ROOT/unity/spikes/VoiceSpike"
mkdir -p "$PROJECT/Build"
for asset in supertonic3/voice.bin sensevoice/tokens.txt asr-corpus/corpus.tsv; do
  [[ -f "$PROJECT/Assets/StreamingAssets/$asset" ]] || { echo "Missing $asset; fetch models and generate corpus first." >&2; exit 1; }
done
"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" -buildTarget Android \
  -executeMethod VoiceSpikeBuild.Build -logFile "$PROJECT/Build/build.log"
[[ -s "$PROJECT/Build/VoiceSpike.apk" ]]
MERGED="$PROJECT/Library/Bee/Android/Prj/IL2CPP/Gradle/launcher/build/intermediates/merged_manifests/debug/processDebugManifest/AndroidManifest.xml"
uv run --no-project python - "$MERGED" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
ns = '{http://schemas.android.com/apk/res/android}'
features = [x for x in root.findall('uses-feature') if x.get(ns + 'name') == 'oculus.software.handtracking']
permissions = [x for x in root.findall('uses-permission') if x.get(ns + 'name') == 'com.oculus.permission.HAND_TRACKING']
if len(features) != 1 or features[0].get(ns + 'required') != 'false' or not permissions:
    raise SystemExit('Merged manifest missing optional Quest hand tracking feature or permission')
print('Verified merged manifest: optional Quest hand tracking feature and permission.')
PY
echo "VoiceSpike APK built successfully."
