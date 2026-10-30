import math
import struct
import unittest

from sensei_server.speech import FakeSpeech, JapaneseSpeech, VoiceUnavailable, sentences


class SpeechTests(unittest.TestCase):
    def test_fake_pcm(self):
        pcm = FakeSpeech().synthesize("hello", "fake", "normal")
        self.assertEqual(len(pcm), 24000)
        samples = struct.unpack("<12000h", pcm)
        self.assertAlmostEqual(max(samples) / 32767, 0.3, places=3)
        self.assertAlmostEqual(math.sqrt(sum(x * x for x in samples) / len(samples)) / 32767,
                               0.3 / math.sqrt(2), places=3)

    def test_sentence_split_and_japanese_error(self):
        self.assertEqual(sentences("こんにちは。 元気ですか？ Yes!"), ["こんにちは。", "元気ですか？", "Yes!"])
        with self.assertRaises(VoiceUnavailable) as failure:
            JapaneseSpeech().synthesize("こんにちは。", "ja", "normal")
        self.assertEqual(failure.exception.code, "tts_voice_unavailable")
