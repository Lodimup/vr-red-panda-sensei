#!/usr/bin/env bash
# Live smoke test: runs the sensei CLI with --script on three learner lines against the real LLM provider and prints the result.
# It is not part of `dotnet test` (no network in tests) and does nothing without a key.
#
#   export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH   # if the .NET SDK is user-local
#   export SENSEI_LLM_API_KEY=...                               # required
#   # optional: SENSEI_LLM_PROVIDER, SENSEI_LLM_MODEL, SENSEI_LLM_BASE_URL (see SPEC section 2)
#   sensei-engine/scripts/live-smoke.sh
#
# Exit code: the CLI's script exit code (0 ok, 1 command error, 2 provider/LLM error); 0 when skipped for lack of a key.
set -u

if [ -z "${SENSEI_LLM_API_KEY:-}" ]; then
  echo "live-smoke: SENSEI_LLM_API_KEY is not set; nothing to do."
  exit 0
fi

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cli="$here/../src/Sensei.Engine.Cli/Sensei.Engine.Cli.csproj"
work="$(mktemp -d "${TMPDIR:-/tmp}/sensei-live-smoke.XXXXXX")"
trap 'rm -rf "$work"' EXIT

cat > "$work/script.txt" <<'LINES'
Hello! I'm a complete beginner. Can you show me something to drink?
みずをください。
Thank you! What should we do next?
/scene
/profile
LINES

echo "live-smoke: three learner lines, one provider call chain each (profile dir: throw-away)"
dotnet run --project "$cli" --configuration Release -- --profile-dir "$work/profile" --script "$work/script.txt" --trace
code=$?

echo
echo "live-smoke: CLI exit code $code (0 ok, 1 command error, 2 provider error)"
traces=$(cat "$work"/profile/traces/*.jsonl 2>/dev/null | wc -l | tr -d ' ')
echo "live-smoke: $traces tool calls traced"
exit "$code"
