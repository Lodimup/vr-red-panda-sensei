#!/usr/bin/env bash
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
project="$repo/unity/RedPandaSensei"
unity=${UNITY_EDITOR:-/Applications/Unity/Hub/Editor/6000.6.5f1/Unity.app/Contents/MacOS/Unity}
mkdir -p "$project/Logs"
results="$project/Logs/editmode-results.xml"
rm -f "$results"
set +e
"$unity" -batchmode -projectPath "$project" -runTests -testPlatform EditMode -testResults "$results" -logFile "$project/Logs/editmode.log"
status=$?
set -e
if [[ ! -s "$results" ]]; then tail -80 "$project/Logs/editmode.log" >&2; exit 1; fi
uv run --no-project python - "$results" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
total = int(root.get('total', '0'))
failed = int(root.get('failed', '0'))
passed = int(root.get('passed', '0'))
print(f'NUnit total={total} passed={passed} failed={failed} skipped={root.get("skipped", "0")}')
for node in root.iter('test-case'):
    if node.get('result') == 'Failed': print(ET.tostring(node, encoding='unicode'))
sys.exit(0 if total > 0 and failed == 0 else 1)
PY
exit "$status"
