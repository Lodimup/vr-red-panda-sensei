#!/usr/bin/env bash
set -euo pipefail
ROOT=$(cd "$(dirname "$0")/../.." && pwd)
export RPS_CORPUS_DIR="$ROOT/unity/spikes/VoiceSpike/Assets/StreamingAssets/asr-corpus"
uv run --no-project python - <<'PY'
import os, re, subprocess, tempfile, wave
from pathlib import Path

root = Path(os.environ['RPS_CORPUS_DIR'])
root.mkdir(parents=True, exist_ok=True)
lines = {
    'ja': ['りんごをください', '水はどこですか', '本を箱に入れてください', 'ペンを右に置いて', 'おにぎりを二つください', 'かばんの中に鍵があります', '傘はどれですか', 'お茶をお願いします', 'バナナを左に動かしてください', 'コップはテーブルの上です', 'わかりません', 'もう一度お願いします'],
    'en': ["I don't know any Japanese", 'Can you say that again slowly', 'What does kaban mean', 'Give me a hint please', 'I want to practice ordering food', 'Put the apple in the box'],
    'mixed': ['Can I have the りんご', 'How do I say 傘', 'Is this 本 or ノート', "I think it's 水"],
}
voices = {'ja': 'Kyoko (Japanese (Japan))', 'en': 'Samantha (English (US))'}
manifest = []
# Temporary intermediates stay outside the repository; only PCM16 WAVs survive.
scratch = Path.home() / 'scratchpad'
scratch.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='rps-corpus-', dir=scratch) as tmp:
    for group, phrases in lines.items():
        for index, text in enumerate(phrases, 1):
            segments = re.findall(r'[\u3040-\u30ff\u3400-\u9fff]+|[^\u3040-\u30ff\u3400-\u9fff]+', text) if group == 'mixed' else [text]
            chunks = []
            for part, segment in enumerate(segments):
                lang = ('ja' if re.search(r'[\u3040-\u30ff\u3400-\u9fff]', segment) else 'en') if group == 'mixed' else group
                audio = Path(tmp) / f'{part}.wav'
                subprocess.run(['say', '-v', voices[lang], '-r', '160', '-o', str(audio), '--file-format=WAVE', '--data-format=LEI16@16000', '--channels=1', segment.strip()], check=True)
                with wave.open(str(audio), 'rb') as wav:
                    assert wav.getframerate() == 16000 and wav.getnchannels() == 1 and wav.getsampwidth() == 2
                    chunks.append(wav.readframes(wav.getnframes()))
            filename = f'{group}-{index:02}.wav'
            with wave.open(str(root / filename), 'wb') as output:
                output.setparams((1, 2, 16000, 0, 'NONE', 'not compressed'))
                output.writeframes(b''.join(chunks))
            manifest.append(f'{filename}\t{text}')
(root / 'corpus.tsv').write_text('\n'.join(manifest) + '\n', encoding='utf-8')
print(f'Generated {len(manifest)} mono PCM16 16 kHz corpus files.')
PY
