#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
if [[ "${VOICESPIKE_CLAIM:-0}" != 1 ]]; then
  exec env BERMUDA_THREAD_AGENT=rps-u0v bermuda thread with quest --ttl 15m --wait 5m --why 'u0v voice bench' -- env VOICESPIKE_CLAIM=1 bash "$0" "$@"
fi
export RPS_VOICE_ROOT="$ROOT"
export RPS_QUEST_SERIAL="${RPS_QUEST_SERIAL:-2G0YC5ZF8402ZP}"
export RPS_VOICE_RUN_DIR="${RPS_VOICE_RUN_DIR:-$HOME/Projects/vr-red-panda-sensei_runs/unity}"
uv run --no-project python - <<'PY'
import json, os, pathlib, re, selectors, signal, subprocess, tempfile, time, tomllib

root = pathlib.Path(os.environ['RPS_VOICE_ROOT'])
run = pathlib.Path(os.environ['RPS_VOICE_RUN_DIR']); run.mkdir(parents=True, exist_ok=True)
adb = ['adb', '-s', os.environ['RPS_QUEST_SERIAL']]
package = 'com.supercapybara.voicespike'
files = f'/sdcard/Android/data/{package}/files'
def call(*args, **kwargs): return subprocess.run(adb + list(args), check=True, **kwargs)
def capture(*args): return subprocess.check_output(adb + list(args), text=True, errors='replace')
def terminate_process(process):
    process.terminate()
    try: process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5)
def blocking_dialog():
    state = capture('shell', 'dumpsys', 'activity', 'activities')
    return next((line.strip() for line in state.splitlines() if 'topResumedActivity=' in line and re.search(r'LaunchCheck|GuardianDialog', line, re.I)), None)
key = tomllib.loads((pathlib.Path.home() / 'KEYS.toml').read_text())['openrouter_deepseek']['api_key']
scratch = pathlib.Path.home() / 'scratchpad'; scratch.mkdir(exist_ok=True)
logcat = None
playback = None
volume_changed = False
guardian_paused = False
proximity_active = False
probe = os.environ.get('RPS_MIC_PROBE') == '1'
original_volume = int(subprocess.check_output(['osascript', '-e', 'output volume of (get volume settings)'], text=True).strip())
original_muted = subprocess.check_output(['osascript', '-e', 'output muted of (get volume settings)'], text=True).strip() == 'true'
corpus_dir = root / 'unity/spikes/VoiceSpike/Assets/StreamingAssets/asr-corpus'
corpus_files = [line.split('\t', 1)[0] for line in (corpus_dir / 'corpus.tsv').read_text(encoding='utf-8').splitlines() if line]
try:
    call('install', '-r', str(root / 'unity/spikes/VoiceSpike/Build/VoiceSpike.apk'))
    call('shell', 'pm', 'grant', package, 'android.permission.RECORD_AUDIO')
    # Cold install must measure extraction, not a prior run's marker.
    call('shell', 'am', 'force-stop', package)
    if probe:
        call('shell', 'rm', '-f', files + '/online.ready')
    else:
        call('shell', 'rm', '-rf', files + '/supertonic3', files + '/sensevoice', files + '/sensevoice-nano', files + '/tts-out', files + '/online.ready')
    call('shell', 'mkdir', '-p', files)
    path = None
    try:
        with tempfile.NamedTemporaryFile(mode='w', prefix='sensei-', suffix='.env', dir=scratch, delete=False) as temp:
            path = pathlib.Path(temp.name)
            temp.write('OPENROUTER_API_KEY=' + key + '\n')
            if probe: temp.write('RPS_MIC_PROBE=1\n')
            if os.environ.get('RPS_MIC_DEVICE'): temp.write('RPS_MIC_DEVICE=' + os.environ['RPS_MIC_DEVICE'] + '\n')
        call('push', str(path), files + '/sensei.env', stdout=subprocess.DEVNULL)
    finally:
        if path is not None: path.unlink(missing_ok=True)
    key = None
    call('shell', 'svc', 'wifi', 'disable')
    if 'disabled' not in capture('shell', 'cmd', 'wifi', 'status').lower(): raise RuntimeError('Could not confirm Wi-Fi disabled')
    # Orchestrator decision 2026-10-28: pause the dev Guardian prompt for the desk-mounted headset, then reset it in cleanup.
    guardian_paused = True; call('shell', 'setprop', 'debug.oculus.guardian_pause', '1')
    # Orchestrator decision 2026-10-28: keep the desk-mounted headset awake while recording.
    proximity_active = True; call('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.prox_close')
    (run / 'u0v-launch.txt').write_text('Guardian pause set to 1 before launch.\n')
    if probe:
        volume_changed = True
        subprocess.run(['osascript', '-e', 'set volume output volume 70'], check=True)
        subprocess.run(['osascript', '-e', 'set volume without output muted'], check=True)
    # Preserve shared device logs and start at the device's current epoch time.
    since = capture('shell', 'date', '+%s.%N').strip()
    logcat = subprocess.Popen(adb + ['logcat', '-T', since, '-v', 'raw', 'Unity:I', '*:S'], stdout=subprocess.PIPE, bufsize=0)
    call('shell', 'am', 'start', '-n', package + '/com.unity3d.player.UnityPlayerActivity')
    restarted_shell = False
    for _ in range(30):
        if subprocess.run(adb + ['shell', 'pidof', package], stdout=subprocess.DEVNULL).returncode == 0: break
        blocked = blocking_dialog()
        if blocked:
            if restarted_shell: raise RuntimeError('Quest launch remains blocked: ' + blocked)
            # Orchestrator decision 2026-10-28: restart vrshell once only for the observed launch-check or Guardian dialog.
            print('Quest dialog observed; restarting vrshell once: ' + blocked, flush=True)
            with (run / 'u0v-launch.txt').open('a') as launch_log: launch_log.write('Restarted vrshell after: ' + blocked + '\n')
            call('shell', 'am', 'force-stop', 'com.oculus.vrshell')
            time.sleep(12)
            call('shell', 'am', 'start', '-n', package + '/com.unity3d.player.UnityPlayerActivity')
            restarted_shell = True
        time.sleep(0.5)
    else:
        raise RuntimeError('VoiceSpike process did not start: ' + (blocking_dialog() or 'no launch-check activity visible'))
    for _ in range(20):
        power = capture('shell', 'dumpsys', 'power')
        activities = capture('shell', 'dumpsys', 'activity', 'activities')
        focused = next((line.strip() for line in activities.splitlines() if 'topResumedActivity=' in line and package in line), None)
        if 'mWakefulness=Awake' in power and focused: break
        blocked = next((line.strip() for line in activities.splitlines() if 'topResumedActivity=' in line and re.search(r'LaunchCheck|GuardianDialog', line, re.I)), None)
        if blocked:
            if restarted_shell: raise RuntimeError('Quest launch remains blocked: ' + blocked)
            print('Quest dialog observed; restarting vrshell once: ' + blocked, flush=True)
            with (run / 'u0v-launch.txt').open('a') as launch_log: launch_log.write('Restarted vrshell after: ' + blocked + '\n')
            call('shell', 'am', 'force-stop', 'com.oculus.vrshell')
            time.sleep(12)
            call('shell', 'am', 'start', '-n', package + '/com.unity3d.player.UnityPlayerActivity')
            restarted_shell = True
        time.sleep(0.5)
    else:
        top = next((line.strip() for line in activities.splitlines() if 'topResumedActivity=' in line), 'no resumed activity')
        raise RuntimeError('Quest not awake with VoiceSpike resumed: ' + top)
    with (run / 'u0v-launch.txt').open('a') as launch_log: launch_log.write('Wakefulness=Awake; ' + focused + '\n')
    print('Quest awake; VoiceSpike is top-resumed.', flush=True)
    selector = selectors.DefaultSelector(); selector.register(logcat.stdout, selectors.EVENT_READ)
    deadline = time.monotonic() + 600; done = False; online = False; pending = b''
    with (run / 'u0v-voice.log').open('w') as output:
        while time.monotonic() < deadline:
            for event, _ in selector.select(timeout=1):
                data = os.read(event.fd, 65536)
                if not data: raise RuntimeError('Device logcat stream closed')
                pending += data
                lines = pending.split(b'\n'); pending = lines.pop()
                for raw in lines:
                    line = raw.decode('utf-8', errors='replace').strip()
                    if 'RPS_VOICE ' not in line: continue
                    line = line[line.index('RPS_VOICE '):]
                    if 'sk-or-' in line: raise RuntimeError('Credential-shaped text in benchmark output; withheld')
                    print(line, flush=True); output.write(line + '\n'); output.flush()
                    if line == 'RPS_VOICE tts_complete' and not online:
                        (run / 'u0v-meminfo.txt').write_text(capture('shell', 'dumpsys', 'meminfo', package))
                        call('shell', 'svc', 'wifi', 'enable')
                        # Orchestrator decision 2026-10-28: play corpus through the Mac speakers at volume 70 for Quest mic tests.
                        volume_changed = True
                        subprocess.run(['osascript', '-e', 'set volume output volume 70'], check=True)
                        subprocess.run(['osascript', '-e', 'set volume without output muted'], check=True)
                        call('shell', 'touch', files + '/online.ready'); online = True
                    if line.startswith('RPS_VOICE mic_ready i=') or (probe and line.startswith('RPS_VOICE mic_probe_ready mode=')):
                        index = 0 if probe else int(line.rsplit('=', 1)[1])
                        if index < 0 or index >= len(corpus_files): raise RuntimeError('Invalid microphone corpus index')
                        if playback is not None:
                            if playback.poll() is None: raise RuntimeError('Prior corpus audio is still playing')
                            if playback.returncode: raise RuntimeError('Mac corpus playback failed')
                        playback = subprocess.Popen(['afplay', str(corpus_dir / corpus_files[index])])
                    if not probe and line.startswith('RPS_VOICE mic_capture '):
                        capture_result = json.loads(line[len('RPS_VOICE mic_capture '):])
                        if capture_result['rms'] == 0:
                            raise RuntimeError('Quest microphone captured silence for ' + capture_result['file'])
                    if line.startswith('RPS_VOICE error '): raise RuntimeError(line)
                    if line == ('RPS_VOICE mic_probe_done' if probe else 'RPS_VOICE done'): done = True; break
            if done: break
    if not done: raise TimeoutError('No RPS_VOICE completion within 600 seconds')
    if playback is not None and playback.wait(timeout=5): raise RuntimeError('Mac corpus playback failed')
    if probe:
        (run / 'u0v-audio-dumpsys.txt').write_text(capture('shell', 'dumpsys', 'audio'))
    else:
        (run / 'u0v-tts').mkdir(parents=True, exist_ok=True)
        call('pull', files + '/tts-out/.', str(run / 'u0v-tts'))
    # Print exactly the required secret-prefix count, never logcat itself.
    count = sum('sk-or-' in line for line in capture('logcat', '-d').splitlines())
    print(count)
    if count: raise RuntimeError('Credential prefix detected in device logcat')
except Exception:
    # Preserve any completed TTS evidence even if a later recognition step fails.
    (run / 'u0v-tts').mkdir(parents=True, exist_ok=True)
    subprocess.run(adb + ['pull', files + '/tts-out/.', str(run / 'u0v-tts')], check=False)
    raise
finally:
    # A second interrupt during cleanup must not leave audio unmuted or credentials on the device.
    signal.signal(signal.SIGINT, signal.SIG_IGN)
    try:
        try:
            if playback is not None and playback.poll() is None: terminate_process(playback)
        finally:
            try:
                if logcat is not None: terminate_process(logcat)
            finally:
                try:
                    call('shell', 'am', 'force-stop', package)
                finally:
                    try:
                        call('shell', 'svc', 'wifi', 'enable')
                    finally:
                        try:
                            call('shell', 'rm', '-f', files + '/sensei.env', files + '/online.ready')
                        finally:
                            try:
                                if guardian_paused:
                                    call('shell', 'setprop', 'debug.oculus.guardian_pause', '0')
                                    with (run / 'u0v-launch.txt').open('a') as launch_log: launch_log.write('Guardian pause reset to 0 after run.\n')
                            finally:
                                if proximity_active: call('shell', 'am', 'broadcast', '-a', 'com.oculus.vrpowermanager.automation_disable')
    finally:
        if volume_changed:
            subprocess.run(['osascript', '-e', 'set volume output volume ' + str(original_volume)], check=True)
            if original_muted: subprocess.run(['osascript', '-e', 'set volume with output muted'], check=True)
PY
