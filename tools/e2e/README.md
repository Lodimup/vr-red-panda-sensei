# Web E2E harness

Headed Chromium, Playwright, and Meta's pinned [IWER](https://github.com/meta-quest/immersive-web-emulation-runtime) emulate the desktop and Quest input paths. The harness is for local mocks only. HTTP requests are restricted to loopback and WebSocket connections must match `MOCK_WS_URL`; it must not be used with a production server or real LLM.

## Run

```sh
npm ci
npx playwright install chromium
npm run build
npm run run
```

With neither endpoint set, the runner launches the clearly labelled static fixture and deterministic mock on an ephemeral loopback port. To point at an existing local client and mock, set both `CLIENT_URL=http://127.0.0.1:...` and `MOCK_WS_URL=ws://127.0.0.1:.../ws`. The runner redirects the client's same-origin `/ws` connection to the configured mock; it also supplies `window.__MOCK_WS_URL` for the fixture. The client adapter uses the current client's button labels, status and canvas geometry, so changes to its UI or mock script may require updating the scenario. A run against the fixture is not a product acceptance result. In particular, the fixture's synthetic tone is not TTS and its simple card is not a WebXR scene.

Optional: `CHROMIUM_PATH` selects an installed Chromium-compatible binary; `MIC_WAV` selects a Chromium-compatible WAV instead of the generated mono 16 kHz tone. `E2E_OUTPUT` changes the output directory. Each scenario gets numbered screenshots for every step, `console.log`, `wire.json` (JSON messages and binary byte counts only), and a top-level `summary.json`; a failure returns nonzero. Browser contexts, browser and fixture server close after the run. The generated WAV is a deterministic audio signal, not intelligible speech.

`src/helpers.ts` exposes AR session start/end, head/controller/hand pose, controller button and hand pinch controls. `audioMark` and `assertAudio` sample destination-bound WebAudio in a bounded interval; mark before the action that starts each line, then assert its window. The tap records peak amplitude, not a recording or an assertion that a physical speaker produced sound. A10, A11, H1 and C1 are explicit TODO skeletons under `scenarios/`; they are not run or counted as passed.

## Test plan

- A01: connect and welcome; start the session and greeting; assert non-silent output; independently start and end an emulated AR session; exercise head pose, all face buttons, triggers and grips, and both hand pinches; verify XR select and squeeze events; assert mock messages and no unexpected console errors.
- D1: connect and start; select and drag a card; use R, H, M and L; hold Space with fake microphone and verify a nonzero PCM upload; assert expected mock protocol messages and no unexpected console errors.
- Every listed step gets a screenshot, including a failed step. Scenarios use isolated browser contexts. The fixture mock does not implement profile persistence or LLM behavior. The client mock also emits tones rather than synthesized speech. The separate emulated XR session validates harness input events, not the client’s AR interactions; those remain for H1 and C1.
