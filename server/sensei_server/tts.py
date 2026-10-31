"""Resident, serialized speech synthesis and sentence splitting."""
from __future__ import annotations

import math
import re
import threading

import numpy as np


class CachedSynthesizer:
    def __init__(self):
        self.cache = {}
        self.lock = threading.Lock()

    def synthesize(self, text: str, lang: str) -> bytes:
        # A single resident voice: text uniquely identifies a cached result.
        with self.lock:
            if text not in self.cache:
                self.cache[text] = self.generate(text, lang)
            return self.cache[text]


class FakeSynthesizer(CachedSynthesizer):
    def generate(self, text: str, lang: str) -> bytes:
        samples = np.arange(12000, dtype=np.float64)
        return (np.sin(2 * math.pi * 440 * samples / 24000) * 0.3 * 32767).astype('<i2').tobytes()


class QwenSynthesizer(CachedSynthesizer):
    def __init__(self, reference: str):
        super().__init__()
        import torch
        from qwen_tts import Qwen3TTSModel

        device = 'mps' if torch.backends.mps.is_available() else ('cuda' if torch.cuda.is_available() else 'cpu')
        self.model = Qwen3TTSModel.from_pretrained(
            'Qwen/Qwen3-TTS-12Hz-1.7B-Base', device_map=device,
            dtype=torch.bfloat16 if device != 'cpu' else torch.float32,
            attn_implementation='sdpa',
        )
        self.prompt = self.model.create_voice_clone_prompt(
            ref_audio=reference, ref_text=None, x_vector_only_mode=True,
        )
        # RULING(Handler 2026-10-31): warm the model before accepting connections.
        self.generate('こんにちは。', 'ja')

    def generate(self, text: str, lang: str) -> bytes:
        waves, rate = self.model.generate_voice_clone(
            text=text, language={'ja': 'Japanese', 'en': 'English'}[lang],
            voice_clone_prompt=self.prompt,
        )
        if rate != 24000:
            raise ValueError('Unexpected TTS sample rate')
        samples = np.asarray(waves[0], dtype=np.float32)
        return (np.clip(samples, -1, 1) * 32767).astype('<i2').tobytes()


def sentences(segments):
    """Return (segment index, sentence text, segment) in speech order."""
    result = []
    for index, segment in enumerate(segments):
        for part in re.split(r'(?<=[。！？.!?])\s*', segment['text']):
            if part.strip():
                result.append((index, part.strip(), segment))
    return result
