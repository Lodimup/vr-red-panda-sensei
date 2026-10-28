#!/usr/bin/env bash
# Live evaluation: plays the fixed scripted conversations (live-eval-scripts.txt) against the real LLM provider through
# the real engine and writes live-eval.json (per turn: wall ms, time to first speak, rounds, tool calls, status, speak
# calls, duplicate spoken lines, record_attempt calls; per conversation: labels and evidence at the end).
# It is not part of `dotnet test` (no network in tests).
#
#   export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH   # if the .NET SDK is user-local
#   sensei-engine/scripts/live-eval.sh [--out DIR] [--only ID[,ID]] [--parallel N] [--label TEXT]
#
# The key is read from ~/KEYS.toml, table [openrouter_deepseek], field api_key, and exported as SENSEI_LLM_API_KEY for
# the runner only. It is never printed and never written to a file. The other SENSEI_LLM_* variables
# (PROVIDER, MODEL, BASE_URL, REASONING_EFFORT, MAX_OUTPUT_TOKENS) pass through when set.
#
# Output (DIR defaults to a new directory under $TMPDIR): live-eval.json, console.log (the transcript), traces/, profiles/.
# Exit code: 0 all measured targets hold, 3 a target is missed, 2 a provider error voided the run, 1 usage error.
set -u

out=""
only=""
parallel="1"
label=""
while [ $# -gt 0 ]; do
  case "$1" in
    --out) out="${2:-}"; shift 2 ;;
    --only) only="${2:-}"; shift 2 ;;
    --parallel) parallel="${2:-}"; shift 2 ;;
    --label) label="${2:-}"; shift 2 ;;
    -h|--help) sed -n '2,15p' "$0"; exit 0 ;;
    *) echo "live-eval: unknown option $1" >&2; exit 1 ;;
  esac
done

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project="$here/../src/Sensei.Engine.Eval/Sensei.Engine.Eval.csproj"
scripts="$here/live-eval-scripts.txt"
if [ -z "$out" ]; then
  out="$(mktemp -d "${TMPDIR:-/tmp}/sensei-live-eval.XXXXXX")"
else
  mkdir -p "$out"
fi

# The key: tomllib reads it inside the process that exports it; nothing echoes it.
read_key() {
  local reader='import os, sys, tomllib
with open(os.path.expanduser("~/KEYS.toml"), "rb") as f:
    sys.stdout.write(tomllib.load(f)["openrouter_deepseek"]["api_key"])'
  if command -v uv >/dev/null 2>&1; then
    uv run --no-project python -c "$reader"
  else
    python3 -c "$reader"
  fi
}
if [ -f "$HOME/KEYS.toml" ]; then
  key="$(read_key 2>/dev/null)" || key=""
  [ -n "$key" ] && export SENSEI_LLM_API_KEY="$key"
  unset key
fi
if [ -z "${SENSEI_LLM_API_KEY:-}" ]; then
  echo "live-eval: no key: ~/KEYS.toml [openrouter_deepseek] api_key is unreadable and SENSEI_LLM_API_KEY is not set" >&2
  exit 1
fi

sha="$(git -C "$here" rev-parse --short HEAD 2>/dev/null || echo unknown)"
if ! git -C "$here" diff --quiet HEAD -- 2>/dev/null; then sha="$sha+dirty"; fi

dotnet build "$project" --configuration Release --nologo -v quiet >"$out/build.log" 2>&1 || { cat "$out/build.log" >&2; echo "live-eval: build failed" >&2; exit 1; }

args=(--scripts "$scripts" --out "$out" --parallel "$parallel" --label "$label" --git-sha "$sha")
[ -n "$only" ] && args+=(--only "$only")

dotnet run --project "$project" --configuration Release --no-build -- "${args[@]}" 2>&1 | tee "$out/console.log"
code=${PIPESTATUS[0]}

echo
echo "live-eval: output in $out (live-eval.json, console.log, traces/, profiles/); exit code $code (0 targets hold, 3 target missed, 2 provider error)"
exit "$code"
