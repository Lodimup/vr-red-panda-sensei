#!/usr/bin/env bash
set -euo pipefail
serial=${QUEST_SERIAL:-${ANDROID_SERIAL:-}}
adb_device=(adb)
if [[ -n "$serial" ]]; then adb_device+=(-s "$serial"); fi
# RULING(Handler 2026-10-28): read only this key; never echo or bundle it.
api_key=$(uv run --no-project python -c 'import pathlib,tomllib; value=tomllib.loads((pathlib.Path.home()/"KEYS.toml").read_text())["openrouter_deepseek"]["api_key"]; assert value and "\n" not in value and "\r" not in value; print(value,end="")')
umask 077
mkdir -p "$HOME/scratchpad"
temporary=$(mktemp "$HOME/scratchpad/rps-env.XXXXXX")
trap 'rm -f "$temporary"; unset api_key' EXIT
printf 'SENSEI_LLM_PROVIDER=openrouter\nSENSEI_LLM_API_KEY=%s\n' "$api_key" > "$temporary"
destination=/sdcard/Android/data/com.supercapybara.redpandasensei/files
"${adb_device[@]}" shell mkdir -p "$destination"
"${adb_device[@]}" push "$temporary" "$destination/sensei.env" >/dev/null 2>&1
echo 'pushed sensei.env (key set)'
