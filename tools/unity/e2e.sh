#!/usr/bin/env bash
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
runs=/Users/calicoagent/Projects/vr-red-panda-sensei_runs/unity/e2e
python_bin=$(uv python find)
serial=2G0YC5ZF8402ZP
export QUEST_SERIAL=$serial
adb_device=(adb -s "$serial")
scenario=${1:-}
if [[ $scenario == prepare ]]; then
  scenario=${2:-}
  [[ $# == 2 && $scenario =~ ^a(01|10|11)$ ]] || { echo 'Usage: e2e.sh prepare a01|a10|a11' >&2; exit 2; }
  dest="$repo/unity/RedPandaSensei/Assets/StreamingAssets/e2e"
  mkdir -p "$dest" "$HOME/scratchpad"
  make_wav() { local name=$1 voice=$2 words=$3; local aiff; aiff=$(mktemp "$HOME/scratchpad/rps-XXXXXX.aiff"); say -v "$voice" -o "$aiff" "$words"; afconvert -f WAVE -d LEI16@16000 -c 1 "$aiff" "$dest/$name.wav"; rm -f "$aiff"; }
  case $scenario in
    a01) make_wav novice Samantha "I don't know any Japanese at all."; make_wav red Samantha 'What is that red one?'; make_wav request Kyoko 'りんごをください'; make_wav thanks Kyoko 'ありがとう';;
    a10) make_wav interrupt Samantha 'Please show me an apple.';;
  esac
  exit 0
fi
[[ $# == 1 && $scenario =~ ^a(01|10|11)$ ]] || { echo 'Usage: e2e.sh [prepare] a01|a10|a11' >&2; exit 2; }
# The caller holds quest and android-build leases for the entire invocation.
out="$runs/$scenario"; mkdir -p "$out"
rm -f "$out/clip.mp4" "$out/logcat.txt" "$out/summary.json" "$out/verdict.txt"
package=com.supercapybara.redpandasensei
files="/sdcard/Android/data/$package/files"
cleanup() {
  if [[ -n ${logger:-} ]]; then kill "$logger" 2>/dev/null || true; fi
  if [[ -n ${recorder:-} ]] && kill -0 "$recorder" 2>/dev/null; then kill -TERM "$recorder" 2>/dev/null || true; wait "$recorder" 2>/dev/null || true; fi
  if [[ ${RPS_KEEP_AWAKE:-0} == 1 ]]; then return; fi
  "${adb_device[@]}" shell setprop debug.oculus.guardian_pause 0 >/dev/null 2>&1 || true
  "${adb_device[@]}" shell am broadcast -a com.oculus.vrpowermanager.automation_disable >/dev/null 2>&1 || true
}
trap cleanup EXIT
"${adb_device[@]}" install -r "$repo/unity/RedPandaSensei/Build/RedPandaSensei.apk" >/dev/null
"${adb_device[@]}" shell am force-stop "$package"
if [[ $scenario == a01 ]]; then
  if "${adb_device[@]}" shell test -d "$files/profiles"; then backup="$out/profile-backup-$(date +%Y%m%d-%H%M%S)"; [[ ! -e "$backup" ]] || { echo "Profile backup already exists: $backup" >&2; exit 1; }; "${adb_device[@]}" pull "$files/profiles" "$backup" >/dev/null; "${adb_device[@]}" shell rm -rf "$files/profiles"; fi
fi
bash "$repo/tools/unity/push-env.sh" --test-script "$scenario.txt"
"${adb_device[@]}" push "$repo/tools/unity/e2e/$scenario.txt" "$files/$scenario.txt" >/dev/null
"${adb_device[@]}" shell am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null
"${adb_device[@]}" shell setprop debug.oculus.guardian_pause 1
"$python_bin" "$repo/tools/unity/e2e/frame_capture.py" capture "$out" >"$out/record.log" 2>&1 & recorder=$!
sleep 3
if ! kill -0 "$recorder" 2>/dev/null; then echo "STUCK: frame capture failed: $(cat "$out/record.log")" >&2; exit 1; fi
set +e
bash "$repo/tools/unity/run-quest.sh" --keep-awake >"$out/launch.log" 2>&1
launch_status=$?
set -e
pid=$("${adb_device[@]}" shell pidof "$package" | tr -d '\r' | awk '{print $1}')
if [[ -z $pid ]]; then echo 'STUCK: app PID not found' >&2; kill "$recorder" 2>/dev/null || true; exit 1; fi
# Read the process-bound ring buffer repeatedly: an ADB reconnect can close a stream.
for ((i=0; i<300; i++)); do
  if timeout 10 "${adb_device[@]}" logcat -d --pid="$pid" -s Unity -v threadtime >"$out/logcat.next" 2>"$out/logcat-error.txt"; then
    mv "$out/logcat.next" "$out/logcat.txt"
    if rg -q 'RPS_E2E done (pass|fail)' "$out/logcat.txt"; then break; fi
  fi
  if ! kill -0 "$recorder" 2>/dev/null; then break; fi
  sleep 2
done
kill -TERM "$recorder" 2>/dev/null || true
if ! wait "$recorder"; then echo "STUCK: frame capture failed: $(cat "$out/record.log")" >&2; exit 1; fi
recorder=
"$python_bin" "$repo/tools/unity/e2e/frame_capture.py" encode "$out"
export E2E_OUT="$out" E2E_SCENARIO="$scenario" E2E_LAUNCH_STATUS="$launch_status"
uv run --no-project python "$repo/tools/unity/e2e/evaluate.py"
