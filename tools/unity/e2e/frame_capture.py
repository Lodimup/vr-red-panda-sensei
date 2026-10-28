#!/usr/bin/env python3
"""Capture Quest frames with host monotonic timestamps, then encode variable durations."""
import json
import signal
import subprocess
import sys
import time
from pathlib import Path

mode, out_arg = sys.argv[1:3]
out = Path(out_arg).resolve()
frames = out / 'frames'
manifest = out / 'frames.json'
if mode == 'capture':
    frames.mkdir(parents=True, exist_ok=True)
    for path in frames.glob('frame-*.png'):
        path.unlink()
    samples = []
    stopping = False
    def stop(_signum, _frame):
        global stopping
        stopping = True
    signal.signal(signal.SIGTERM, stop)
    signal.signal(signal.SIGINT, stop)
    while not stopping:
        stamp = time.monotonic_ns() // 1_000_000
        try:
            result = subprocess.run(['adb', '-s', '2G0YC5ZF8402ZP', 'exec-out', 'screencap', '-p'], capture_output=True, timeout=30)
        except subprocess.TimeoutExpired:
            print('screencap timed out', file=sys.stderr, flush=True)
            break
        if result.returncode or not result.stdout.startswith(b'\x89PNG\r\n\x1a\n'):
            print('screencap failed: ' + result.stderr.decode(errors='replace'), file=sys.stderr, flush=True)
            break
        name = f'frame-{len(samples):06d}.png'
        (frames / name).write_bytes(result.stdout)
        samples.append({'file': name, 'host_ms': stamp})
        manifest.write_text(json.dumps(samples))
    sys.exit(0 if stopping and len(samples) >= 2 else 1)
elif mode == 'encode':
    samples = json.loads(manifest.read_text())
    if len(samples) < 2: raise SystemExit('fewer than two captured frames')
    concat = out / 'frames.ffconcat'
    lines = ['ffconcat version 1.0']
    for i, sample in enumerate(samples):
        path = (frames / sample['file']).as_posix().replace("'", "'\\''")
        duration = (samples[i + 1]['host_ms'] - sample['host_ms']) / 1000 if i + 1 < len(samples) else (samples[-1]['host_ms'] - samples[-2]['host_ms']) / 1000
        if duration <= 0: raise SystemExit('non-increasing frame timestamps')
        lines += [f"file '{path}'", f'duration {duration:.3f}']
    lines.append(f"file '{(frames / samples[-1]['file']).as_posix().replace(chr(39), chr(39) + chr(92) + chr(39) + chr(39))}'")
    concat.write_text('\n'.join(lines) + '\n')
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-safe', '0', '-f', 'concat', '-i', str(concat), '-vf', 'scale=1280:-2,format=yuv420p', '-fps_mode', 'vfr', '-c:v', 'libx264', str(out / 'clip.mp4')], check=True)
    seconds = (samples[-1]['host_ms'] - samples[0]['host_ms']) / 1000
    (out / 'capture.json').write_text(json.dumps({'mode': 'frame capture', 'frames': len(samples), 'seconds': seconds, 'fps': (len(samples) - 1) / seconds}, indent=2) + '\n')
else:
    raise SystemExit('unknown mode')
