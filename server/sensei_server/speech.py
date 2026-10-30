"""In-process sentence synthesis adapters and bounded audio cache."""
from __future__ import annotations

import asyncio
import math
import re
import struct
from collections import OrderedDict
from typing import Protocol


class VoiceUnavailable(Exception):
    code = "tts_voice_unavailable"


class SpeechAdapter(Protocol):
    def synthesize(self, text: str, voice: str, pace: str) -> bytes: ...


class FakeSpeech:
    def synthesize(self, text: str, voice: str, pace: str) -> bytes:
        return struct.pack("<12000h", *(round(32767 * 0.3 * math.sin(2 * math.pi * 440 * i / 24000))
                                       for i in range(12000)))


class JapaneseSpeech:
    def synthesize(self, text: str, voice: str, pace: str) -> bytes:
        raise VoiceUnavailable("Japanese voice has not been selected")


class KittenSpeech:
    def __init__(self) -> None:
        self._model = None

    def synthesize(self, text: str, voice: str, pace: str) -> bytes:
        from kittentts import KittenTTS
        import numpy as np

        if self._model is None:
            self._model = KittenTTS("KittenML/kitten-tts-mini-0.8")
        samples = self._model.generate(text, voice=voice, speed=0.8 if pace == "slow" else 1.0)
        return (np.clip(samples, -1, 1) * 32767).astype("<i2").tobytes()


def sentences(text: str) -> list[str]:
    return [s.strip() for s in re.split(r"(?<=[。！？.!?])\s*", text) if s.strip()]


class Speech:
    def __init__(self, *, fake: bool = False) -> None:
        self.adapters: dict[str, SpeechAdapter] = ({"ja": FakeSpeech(), "en": FakeSpeech()} if fake
                                                   else {"ja": JapaneseSpeech(), "en": KittenSpeech()})
        self.cache: OrderedDict[tuple[str, str, str], bytes] = OrderedDict()
        self._lock = asyncio.Lock()

    async def synthesize(self, lang: str, text: str, voice: str, pace: str) -> bytes:
        key = text, voice, pace
        async with self._lock:
            if key in self.cache:
                self.cache.move_to_end(key)
                return self.cache[key]
            audio = await asyncio.to_thread(self.adapters[lang].synthesize, text, voice, pace)
            self.cache[key] = audio
            while len(self.cache) > 256:
                self.cache.popitem(last=False)
            return audio
