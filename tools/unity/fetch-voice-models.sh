#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
CACHE="${RPS_VOICE_CACHE:-$HOME/scratchpad/rps-voice-downloads}"
DEST="$ROOT/unity/RedPandaSensei/Assets/StreamingAssets"
mkdir -p "$CACHE" "$DEST/supertonic3" "$DEST/sensevoice-nano"
fetch() {
  local name="$1" url="$2" expected="$3" actual
  if [[ ! -f "$CACHE/$name" ]]; then
    curl --fail --location --connect-timeout 15 --max-time 180 --retry 2 --retry-max-time 240 "$url" -o "$CACHE/$name.part" || { rm -f "$CACHE/$name.part"; return 1; }
    mv "$CACHE/$name.part" "$CACHE/$name"
  fi
  actual=$(shasum -a 256 "$CACHE/$name" | cut -d ' ' -f 1)
  [[ "$actual" == "$expected" ]] || { echo "SHA256 mismatch: $name" >&2; return 1; }
  tar -xjf "$CACHE/$name" -C "$CACHE"
}
TTS=sherpa-onnx-supertonic-3-tts-int8-2026-05-11
# Orchestrator decision 2026-10-28: ship SenseVoice-nano only; the classic model is not bundled.
NANO=sherpa-onnx-sense-voice-funasr-nano-int8-2025-12-17
fetch "$TTS.tar.bz2" "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/$TTS.tar.bz2" 82fa96f91c4ef8abaae3a14a3f4153facf88bed821d1f7331cec2700f432c427
fetch "$NANO.tar.bz2" "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/$NANO.tar.bz2" 257936ea9a64cbe33200274e6367fc26d373ff6ca58b996b15108ffd6b9f6148
for name in duration_predictor.int8.onnx text_encoder.int8.onnx vector_estimator.int8.onnx vocoder.int8.onnx tts.json unicode_indexer.bin voice.bin; do
  cp "$CACHE/$TTS/$name" "$DEST/supertonic3/$name"
done
cp "$CACHE/$NANO/model.int8.onnx" "$CACHE/$NANO/tokens.txt" "$DEST/sensevoice-nano/"
cp "$ROOT/unity/packages/com.supercapybara.sensei.voice/THIRD_PARTY.md" "$DEST/supertonic3/THIRD_PARTY.md"
cp "$ROOT/unity/packages/com.supercapybara.sensei.voice/THIRD_PARTY.md" "$DEST/sensevoice-nano/THIRD_PARTY.md"
echo 'Voice models verified and installed in StreamingAssets.'
