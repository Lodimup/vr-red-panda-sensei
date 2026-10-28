#!/usr/bin/env bash
{ set +x; } 2>/dev/null
set -euo pipefail
test_script=
test_turn=
if [[ ${1:-} == --test-turn && $# == 2 ]]; then
  test_turn=$2
  [[ -n "$test_turn" && "$test_turn" != *$'\n'* && "$test_turn" != *$'\r'* ]] || { echo 'Invalid test turn' >&2; exit 2; }
elif [[ ${1:-} == --test-script && $# == 2 ]]; then
  test_script=$2
  [[ "$test_script" =~ ^[A-Za-z0-9_.-]+$ && "$test_script" != . && "$test_script" != .. ]] || { echo "Invalid test script" >&2; exit 2; }
elif (( $# != 0 )); then
  echo 'Usage: push-env.sh [--test-turn "text"]' >&2; exit 2
fi
serial=${QUEST_SERIAL:-${ANDROID_SERIAL:-}}
adb_device=(adb)
if [[ -n "$serial" ]]; then adb_device+=(-s "$serial"); fi
# Key source, first match: SENSEI_LLM_API_KEY in the environment; a prompt when no KEYS.toml exists and stdin is a terminal; else KEYS.toml.
if [[ -n ${SENSEI_LLM_API_KEY:-} ]]; then
api_key=$SENSEI_LLM_API_KEY
elif [[ ! -f "$HOME/KEYS.toml" && -t 0 ]]; then
read -rs -p 'OpenRouter API key: ' api_key; echo >&2
else
# RULING(Handler 2026-10-28): read only this key; never echo or bundle it.
api_key=$(uv run --no-project python -c 'import pathlib,tomllib; value=tomllib.loads((pathlib.Path.home()/"KEYS.toml").read_text())["openrouter_deepseek"]["api_key"]; assert value and "\n" not in value and "\r" not in value; print(value,end="")')
fi
[[ -n "$api_key" && "$api_key" != *$'\n'* && "$api_key" != *$'\r'* ]] || { echo 'Invalid or empty API key' >&2; exit 2; }
destination=/sdcard/Android/data/com.supercapybara.redpandasensei/files
"${adb_device[@]}" shell mkdir -p "$destination"
{ printf 'SENSEI_LLM_PROVIDER=openrouter\nSENSEI_LLM_API_KEY=%s\n' "$api_key"; if [[ -n "$test_script" ]]; then printf 'RPS_TEST_SCRIPT=%s\nRPS_DEBUG_TEXT=1\n' "$test_script"; fi; if [[ -n "$test_turn" ]]; then printf 'RPS_TEST_TURN=%s\n' "$test_turn"; fi; } | "${adb_device[@]}" shell "cat > $destination/sensei.env"
unset api_key
echo 'pushed sensei.env (key set)'
