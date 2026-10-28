#!/usr/bin/env bash
{ set +x; } 2>/dev/null
set -euo pipefail
test_turn=
if [[ ${1:-} == --test-turn && $# == 2 ]]; then
  test_turn=$2
  [[ -n "$test_turn" && "$test_turn" != *$'\n'* && "$test_turn" != *$'\r'* ]] || { echo 'Invalid test turn' >&2; exit 2; }
elif (( $# != 0 )); then
  echo 'Usage: push-env.sh [--test-turn "text"]' >&2; exit 2
fi
serial=${QUEST_SERIAL:-${ANDROID_SERIAL:-}}
adb_device=(adb)
if [[ -n "$serial" ]]; then adb_device+=(-s "$serial"); fi
# RULING(Handler 2026-10-28): read only this key; never echo or bundle it.
api_key=$(uv run --no-project python -c 'import pathlib,tomllib; value=tomllib.loads((pathlib.Path.home()/"KEYS.toml").read_text())["openrouter_deepseek"]["api_key"]; assert value and "\n" not in value and "\r" not in value; print(value,end="")')
destination=/sdcard/Android/data/com.supercapybara.redpandasensei/files
"${adb_device[@]}" shell mkdir -p "$destination"
{ printf 'SENSEI_LLM_PROVIDER=openrouter\nSENSEI_LLM_API_KEY=%s\n' "$api_key"; if [[ -n "$test_turn" ]]; then printf 'RPS_TEST_TURN=%s\n' "$test_turn"; fi; } | "${adb_device[@]}" shell "cat > $destination/sensei.env"
unset api_key
echo 'pushed sensei.env (key set)'
