# Sensei web server

Requires Python 3.12 or newer. Install dependencies with `uv sync`, then run
`uv run python -m sensei_server`. `PORT` controls the listening port (default
8000), `SENSEI_PROFILE_DIR` controls profile storage, and `SENSEI_CLIENT_DIR`
can point to a built client directory. The server caches the SenseVoice model
outside the repository and downloads it on first use.

Set `SENSEI_VOICE_REF` to the path of a reference WAV for Qwen3-TTS. The
server loads and warms the voice before accepting connections; model weights
are cached by Hugging Face outside the repository. Set `SENSEI_TTS=off` to
skip voice initialization. If initialization fails, captions remain available
and `welcome.voice` reports `unavailable`.

Set `SENSEI_LLM_API_KEY` in the process environment for the real model.
`SENSEI_LLM_MODEL`, `SENSEI_LLM_BASE_URL`, `SENSEI_LLM_REASONING_EFFORT`,
and `SENSEI_LLM_MAX_OUTPUT_TOKENS` override the defaults.

For deterministic integration testing only, set both `SENSEI_FAKE_LLM=1` and
`SENSEI_FAKE_ASR=1` with no LLM API key in the environment. Fake mode exposes
`POST /__fake__/transcript` to set the next transcript.

Run the server tests with `uv run pytest tests`.
