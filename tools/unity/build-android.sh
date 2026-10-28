#!/usr/bin/env bash
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
unity=${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.5f1/Unity.app/Contents/MacOS/Unity}
project="$repo/unity/RedPandaSensei"
mkdir -p "$project/Logs"
bash "$repo/tools/unity/sync-engine.sh"
bash "$repo/tools/unity/sync-art.sh"
if ! "$unity" -batchmode -quit -projectPath "$project" -buildTarget Android \
    -executeMethod Sensei.Editor.BuildScript.BuildAndroid -logFile "$project/Logs/build-android.log"; then
  tail -80 "$project/Logs/build-android.log" >&2
  exit 1
fi
apk="$project/Build/RedPandaSensei.apk"
test -s "$apk"
echo 'Android build succeeded.'
echo 'Backend: IL2CPP'
echo 'Architecture: ARM64'
echo "APK: $apk"
echo "Size: $(wc -c < "$apk" | tr -d ' ') bytes"
