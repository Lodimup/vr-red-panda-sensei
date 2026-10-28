#!/bin/bash
set -eu
repo=${REVIEW_REPO:-$(cd "$(dirname "$0")/../../.." && pwd)}
work=$(mktemp -d "${TMPDIR:-/tmp}/rps-prompt.XXXXXX")
trap 'rm -rf "$work"' EXIT
mkdir "$work/bin" "$work/home"
printf "[openrouter_deepseek]\napi_key = 'unrequested-fake-key'\n" > "$work/home/KEYS.toml"
cat > "$work/bin/adb" <<'ADB'
#!/bin/bash
if [[ "$*" == *'cat >'* ]]; then cat > "$RPS_CAPTURE"; fi
ADB
chmod +x "$work/bin/adb"
export RPS_CAPTURE="$work/captured" RPS_REVIEW_ROOT="$repo" RPS_REVIEW_HOME="$work/home" RPS_REVIEW_BIN="$work/bin"
uv run --no-project python - <<'PY'
import os, pty, select, subprocess, time
master, slave = pty.openpty()
env = dict(os.environ, HOME=os.environ['RPS_REVIEW_HOME'], PATH=os.environ['RPS_REVIEW_BIN'] + ':' + os.environ['PATH'])
env.pop('SENSEI_LLM_API_KEY', None)
p = subprocess.Popen(['bash',os.environ['RPS_REVIEW_ROOT']+'/tools/unity/push-env.sh'], stdin=slave, stdout=slave, stderr=slave, env=env)
os.close(slave)
out=b''
while p.poll() is None:
    r,_,_=select.select([master],[],[],0.1)
    if r:
        try: out+=os.read(master,4096)
        except OSError: break
    if b'OpenRouter API key:' in out: os.write(master,b'entered-fake-key\n')
    if len(out)>10000: break
p.wait(timeout=30)
try: out+=os.read(master,4096)
except OSError: pass
os.close(master)
captured=open(os.environ['RPS_CAPTURE']).read() if os.path.exists(os.environ['RPS_CAPTURE']) else ''
print('prompt shown:', b'OpenRouter API key:' in out)
print('unrequested KEYS.toml key sent:', 'unrequested-fake-key' in captured)
if b'OpenRouter API key:' not in out or 'entered-fake-key' not in captured:
    print('FAIL: README promises a prompt when the environment variable is unset')
    raise SystemExit(1)
PY
