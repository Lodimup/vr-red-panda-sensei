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
import os, re, selectors, subprocess, sys, time
serial = os.environ['RPS_QUEST_SERIAL']
adb = ['adb'] + (['-s', serial] if serial else [])
package = 'com.supercapybara.redpandasensei'
launch = float(os.environ['RPS_LAUNCH_MONOTONIC'])
deadline = launch + 180
pid_deadline = time.monotonic() + 20
process = None
selector = selectors.DefaultSelector()
pid = None
buffer = b''
booted = False
turn_done = False
def current_pid():
    result = subprocess.run(adb + ['shell', 'pidof', package], capture_output=True, text=True)
    values = result.stdout.split()
    return values[0] if result.returncode == 0 and values and values[0].isdigit() else None
def stop_stream():
    global process
    if process is not None:
        selector.unregister(process.stdout)
        process.terminate()
        try: process.wait(timeout=5)
        except subprocess.TimeoutExpired: process.kill(); process.wait()
        process.stdout.close()
        process = None
try:
    while time.monotonic() < deadline:
        observed = current_pid()
        if observed != pid:
            stop_stream()
            pid = observed
            buffer = b''
            booted = False
            turn_done = False
            if pid:
                process = subprocess.Popen(adb + ['logcat', '--pid=' + pid, '-s', 'Unity', '-v', 'brief'], stdout=subprocess.PIPE)
                selector.register(process.stdout, selectors.EVENT_READ)
            else:
                pid_deadline = time.monotonic() + 20
        if not pid:
            if time.monotonic() >= pid_deadline:
                print('RPS_SMOKE error: app process not found within 20 seconds', flush=True)
                sys.exit(1)
            time.sleep(0.25)
            continue
        if not selector.select(timeout=1):
            if process.poll() is not None:
                print('RPS_SMOKE error: logcat stopped', flush=True)
                sys.exit(1)
            continue
        chunk = os.read(process.stdout.fileno(), 65536)
        if not chunk:
            print('RPS_SMOKE error: logcat closed', flush=True)
            sys.exit(1)
        buffer += chunk
        while b'\n' in buffer:
            raw, buffer = buffer.split(b'\n', 1)
            line = raw.decode('utf-8', errors='replace').rstrip('\r')
            if 'RPS_' not in line: continue
            print(line, flush=True)
            if 'RPS_BOOT fail' in line or 'RPS_BOOT step=7 fail' in line or 'RPS_TURN failed' in line: sys.exit(1)
            if 'RPS_BOOT step=7 ok' in line: booted = True
            if 'RPS_TURN status=' in line:
                if 'RPS_TURN status=completed ' not in line: sys.exit(1)
                turn_done = True
            if booted and turn_done:
                if current_pid() != pid:
                    break
                dump = subprocess.run(adb + ['shell', 'dumpsys', 'activity', 'activities'], text=True, capture_output=True, check=True).stdout
                top = next((item.strip() for item in dump.splitlines() if 'topResumedActivity=' in item), 'not found')
                print('dumpsys_top=' + top, flush=True)
                if not re.search(r'topResumedActivity=.*?\b' + re.escape(package) + r'/', top):
                    print('RPS_SMOKE error: target activity is not resumed', flush=True)
                    sys.exit(1)
                print(f'launch_to_done_seconds={time.monotonic() - launch:.3f}', flush=True)
                screenshot = os.environ.get('RPS_SCREENSHOT_PATH')
                if screenshot:
                    time.sleep(2)
                    with open(screenshot, 'wb') as output:
                        subprocess.run(adb + ['exec-out', 'screencap', '-p'], stdout=output, check=True)
                sys.exit(0)
    print('RPS_TURN timeout: no boot step 7 and completed turn within 180 seconds', flush=True)
finally:
    stop_stream()
    selector.close()
sys.exit(1)
PY
