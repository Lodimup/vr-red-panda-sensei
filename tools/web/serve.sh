#!/usr/bin/env bash
# Run and publish the Red Panda Sensei WebXR server on this machine. Called by the root Makefile.
#
#   serve.sh run      foreground server
#   serve.sh start    background server; waits until /healthz answers
#   serve.sh tunnel   publish http://127.0.0.1:$PORT on this machine's Tailscale HTTPS name
#   serve.sh url      print the HTTPS URL
#   serve.sh untunnel remove the Tailscale route
#   serve.sh stop     stop the background server
#   serve.sh status   health and routes
#
# Environment: PORT (8000), HTTPS_PORT (443), VOICE_REF (reference WAV; unset = captions only),
# PROFILE_DIR (.run/profiles), HEALTH_TIMEOUT seconds (900), SENSEI_LLM_API_KEY (asked for when unset
# and stdin is a terminal; passed to the server environment only, never written to disk).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
PORT="${PORT:-8000}"
HTTPS_PORT="${HTTPS_PORT:-443}"
RUN_DIR="$ROOT/.run"
PROFILE_DIR="${PROFILE_DIR:-$RUN_DIR/profiles}"
PID_FILE="$RUN_DIR/server-$PORT.pid"
LOG_FILE="$RUN_DIR/server-$PORT.log"
HEALTH_TIMEOUT="${HEALTH_TIMEOUT:-900}"
for v in PORT HTTPS_PORT; do [[ "${!v}" =~ ^[0-9]+$ ]] || { echo "$v must be a number, got '${!v}'" >&2; exit 2; }; done
TAILSCALE="${TAILSCALE:-$(command -v tailscale 2>/dev/null || echo /Applications/Tailscale.app/Contents/MacOS/Tailscale)}"

server_env() {
  if [ -z "${SENSEI_LLM_API_KEY:-}" ]; then
    [ -t 0 ] || { echo "SENSEI_LLM_API_KEY is not set" >&2; exit 1; }
    read -rsp "OpenRouter API key: " SENSEI_LLM_API_KEY; echo
  fi
  export SENSEI_LLM_API_KEY PORT
  export SENSEI_CLIENT_DIR="$ROOT/client/dist" SENSEI_PROFILE_DIR="$PROFILE_DIR"
  [ -f "$ROOT/client/dist/index.html" ] || { echo "client/dist missing: run make build" >&2; exit 1; }
  if [ -n "${VOICE_REF:-}" ]; then
    [ -f "$VOICE_REF" ] || { echo "VOICE_REF not found: $VOICE_REF" >&2; exit 1; }
    export SENSEI_VOICE_REF="$(cd "$(dirname "$VOICE_REF")" && pwd)/$(basename "$VOICE_REF")"
  else
    echo "VOICE_REF not set: captions only (SENSEI_TTS=off)"
    export SENSEI_TTS=off
  fi
  mkdir -p "$RUN_DIR" "$PROFILE_DIR"
}

kill_tree() { local c; for c in $(pgrep -P "$1" 2>/dev/null); do kill_tree "$c"; done; kill "$1" 2>/dev/null || true; }

running() { [ -f "$PID_FILE" ] && kill -0 "$(cat "$PID_FILE")" 2>/dev/null; }

url() {
  local host
  host="$("$TAILSCALE" status --json | uv run --no-project python -c 'import json,sys; print(json.load(sys.stdin)["Self"]["DNSName"].rstrip("."))')"
  if [ "$HTTPS_PORT" = 443 ]; then echo "open https://$host/"; else echo "open https://$host:$HTTPS_PORT/"; fi
}

case "${1:-}" in
  run)
    server_env
    cd "$ROOT/server" && exec uv run python -m sensei_server ;;
  start)
    if running; then echo "already running on port $PORT (pid $(cat "$PID_FILE"))"; exit 0; fi
    server_env
    cd "$ROOT/server"
    nohup uv run python -m sensei_server >"$LOG_FILE" 2>&1 </dev/null &
    echo $! >"$PID_FILE"
    cd "$ROOT"
    echo "server pid $(cat "$PID_FILE"), log $LOG_FILE"
    echo "waiting for http://127.0.0.1:$PORT/healthz (the voice model downloads and warms up on first run)"
    for i in $(seq "$HEALTH_TIMEOUT"); do
      if curl -fsS "http://127.0.0.1:$PORT/healthz" >/dev/null 2>&1; then echo "healthy after ${i}s"; exit 0; fi
      running || { echo "server exited; last log lines:" >&2; tail -20 "$LOG_FILE" >&2; rm -f "$PID_FILE"; exit 1; }
      sleep 1
    done
    echo "not healthy after ${HEALTH_TIMEOUT}s; see $LOG_FILE" >&2; exit 1 ;;
  tunnel)
    "$TAILSCALE" serve --bg --https="$HTTPS_PORT" "http://127.0.0.1:$PORT"
    url ;;
  url) url ;;
  untunnel) "$TAILSCALE" serve --https="$HTTPS_PORT" off || true ;;
  stop)
    if running; then
      pid="$(cat "$PID_FILE")"; kill_tree "$pid"
      echo "stopped $pid"
    else echo "not running on port $PORT"; fi
    rm -f "$PID_FILE" ;;
  status)
    curl -fsS "http://127.0.0.1:$PORT/healthz" && echo || echo "server not answering on port $PORT"
    "$TAILSCALE" serve status || true ;;
  *) sed -n '2,15p' "$0"; exit 1 ;;
esac
