import json, os, re, sys
from pathlib import Path
out = Path(os.environ['E2E_OUT'])
scenario = os.environ['E2E_SCENARIO']
log = (out / 'logcat.txt').read_text(errors='replace')
checks = {'launch': os.environ['E2E_LAUNCH_STATUS'] == '0', 'script_pass': 'RPS_E2E done pass' in log, 'no_key_prefix': 'sk-or-' not in log}
turns = re.findall(r'RPS_TURN status=(\w+).*?tools=([^\r\n]*)', log)
if scenario == 'a01':
    checks.update(completed_turns=sum(status == 'completed' for status, _ in turns) >= 3,
                  mixed_language_speak=any('ja' in m.split(',') and 'en' in m.split(',') for m in re.findall(r'RPS_SPEAK_GROUP generation=\d+ langs=([a-z,]+)', log)),
                  challenge=any('start_challenge' in tools for _, tools in turns),
                  attempt=any('record_attempt' in tools for _, tools in turns),
                  no_failed_turn=not re.search(r'RPS_TURN (?:status=failed|failed)', log))
elif scenario == 'a10':
    lines = log.splitlines(); presses = [i for i, line in enumerate(lines) if 'RPS_PTT start' in line]
    press = presses[-1] if presses else -1
    before = [re.search(r'RPS_PLAY generation=(\d+)', line) for line in lines[:max(press, 0)]]
    old = next((m.group(1) for m in reversed(before) if m), None)
    stop = next((re.search(r'RPS_INTERRUPT was_playing=true stopped=true stopped_ms=(\d+)', line) for line in lines[press+1:] if 'RPS_INTERRUPT' in line), None) if press >= 0 else None
    checks.update(interrupt_stopped=bool(stop and int(stop.group(1)) <= 300), no_old_playback=bool(old and not any(f'RPS_PLAY generation={old}' in line for line in lines[press+1:])), new_turn_completed=bool(press >= 0 and any('RPS_TURN status=completed' in line for line in lines[press+1:])))
else:
    sessions = re.search(r'RPS_PROFILE sessions=(\d+)', log)
    texts = re.findall(r'RPS_SPEAK generation=\d+ lang=(?:ja|en) text=([^\r\n]*)', log)
    checks.update(existing_profile=bool(sessions and int(sessions.group(1)) >= 2), greeting_spoken=bool(texts),
                  no_onboarding_question=not any(re.search(r'(?:what|how much|any|知っ|日本語).*(?:japanese|日本語|know|勉強)', t, re.I) and ('?' in t or '？' in t) for t in texts),
                  returning_greeting=any(re.search(r'\b(back|again|continue|return|welcome back|previous|review)\b|おかえり|前回|続き|復習', t, re.I) for t in texts))
capture = json.loads((out / 'capture.json').read_text())
checks['video_recorded'] = (out / 'clip.mp4').stat().st_size > 0
summary = {'capture': f"frame capture, ~{capture['fps']:.1f} fps", 'scenario': scenario, 'pass': all(checks.values()), 'checks': checks, 'completed_turns': sum(s == 'completed' for s, _ in turns)}
(out / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n')
print(json.dumps(summary, indent=2))
sys.exit(0 if summary['pass'] else 1)
