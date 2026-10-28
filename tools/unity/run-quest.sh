#!/usr/bin/env bash
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
serial=${QUEST_SERIAL:-${ANDROID_SERIAL:-}}
adb_device=(adb)
if [[ -n "$serial" ]]; then adb_device+=(-s "$serial"); fi
apk="$repo/unity/RedPandaSensei/Build/RedPandaSensei.apk"
package=com.supercapybara.redpandasensei
"${adb_device[@]}" install -r "$apk"
"${adb_device[@]}" shell am force-stop "$package"
"${adb_device[@]}" logcat -c
trap '"${adb_device[@]}" shell setprop debug.oculus.guardian_pause 0 >/dev/null 2>&1 || true; "${adb_device[@]}" shell am broadcast -a com.oculus.vrpowermanager.automation_disable >/dev/null 2>&1 || true' EXIT
"${adb_device[@]}" shell am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null
# RULING(Handler 2026-10-28): pause the boundary for automated desk runs, restore it on exit.
"${adb_device[@]}" shell setprop debug.oculus.guardian_pause 1
activity=$("${adb_device[@]}" shell cmd package resolve-activity --brief "$package" | tr -d '\r' | tail -1)
case "$activity" in "$package/"*) ;; *) echo "Cannot resolve launch activity: $activity" >&2; exit 1;; esac
export RPS_LAUNCH_MONOTONIC
RPS_LAUNCH_MONOTONIC=$(uv run --no-project python -c 'import time; print(time.monotonic())')
"${adb_device[@]}" shell am start -n "$activity"
sleep 2
if "${adb_device[@]}" shell dumpsys activity activities | grep -Eq 'topResumedActivity=.*(LaunchCheck|GuardianDialog)'; then
  # RULING(Handler 2026-10-28): clear a stale Quest system dialog, then retry the launch once.
  "${adb_device[@]}" shell am force-stop com.oculus.vrshell
  sleep 12
  "${adb_device[@]}" shell am broadcast -a com.oculus.vrpowermanager.prox_close >/dev/null
  "${adb_device[@]}" shell setprop debug.oculus.guardian_pause 1
  "${adb_device[@]}" shell am force-stop "$package"
  "${adb_device[@]}" shell am start -n "$activity"
fi
export RPS_QUEST_SERIAL="$serial"
uv run --no-project python - <<'PY'
import os, selectors, subprocess, sys, time
serial = os.environ['RPS_QUEST_SERIAL']
command = ['adb'] + (['-s', serial] if serial else []) + ['logcat', '-s', 'Unity', '-v', 'brief']
process = subprocess.Popen(command, stdout=subprocess.PIPE)
selector = selectors.DefaultSelector()
selector.register(process.stdout, selectors.EVENT_READ)
deadline = float(os.environ['RPS_LAUNCH_MONOTONIC']) + 180
status = 1
buffer = b''
try:
    while time.monotonic() < deadline:
        if not selector.select(timeout=min(1, max(0, deadline-time.monotonic()))):
            if process.poll() is not None: break
            continue
        chunk = os.read(process.stdout.fileno(), 65536)
        if not chunk: break
        buffer += chunk
        while b'\n' in buffer:
            raw, buffer = buffer.split(b'\n', 1)
            line = raw.decode('utf-8', errors='replace').rstrip('\r')
            if 'RPS_' not in line: continue
            print(line, flush=True)
            if 'RPS_SMOKE error' in line: sys.exit(1)
            if 'RPS_SMOKE done' in line:
                elapsed = time.monotonic() - float(os.environ['RPS_LAUNCH_MONOTONIC'])
                print(f'launch_to_done_seconds={elapsed:.3f}', flush=True)
                dump = subprocess.run(['adb'] + (['-s', serial] if serial else []) +
                                      ['shell', 'dumpsys', 'activity', 'activities'],
                                      text=True, capture_output=True, check=True).stdout
                top = [item.strip() for item in dump.splitlines() if 'topResumedActivity=' in item]
                app = next((item for item in top if 'com.supercapybara.redpandasensei' in item), None)
                print('dumpsys_top=' + (app or (top[0] if top else 'not found')), flush=True)
                screenshot = os.environ.get('RPS_SCREENSHOT_PATH')
                if screenshot:
                    time.sleep(2) # Let the last status reach the headset display.
                    with open(screenshot, 'wb') as output:
                        subprocess.run(['adb'] + (['-s', serial] if serial else []) +
                                       ['exec-out', 'screencap', '-p'], stdout=output, check=True)
                sys.exit(0)
    print('RPS_SMOKE error timeout: no completed turn within 180 seconds', flush=True)
finally:
    selector.close()
    process.terminate()
    try: process.wait(timeout=5)
    except subprocess.TimeoutExpired: process.kill(); process.wait()
sys.exit(status)
PY
