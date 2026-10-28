# Unity Quest shell

Unity 6000.6.5f1 with Android Build Support (SDK, NDK and OpenJDK), .NET SDK,
`uv`, and Android Platform Tools are required. OpenXR 1.19.0 and Meta XR Core
207.0.1 are pinned in the package manifest. No hand-tracking feature is enabled;
the manifest permits hands as an optional input so the shell can launch when
controllers are asleep.

From the repository root on macOS:

```bash
export BERMUDA_THREAD_AGENT=quest-builder
bermuda thread with quest --ttl 20m --why 'Android build' -- \
  bermuda thread with android-build --ttl 20m --why 'Android build' -- \
  bash tools/unity/build-android.sh
```

The sync script publishes the unchanged netstandard2.1 engine and copies its
explicit runtime dependency list. A tool pinned to Mono.Cecil 0.11.6 renames
the identities of dependencies Unity also supplies and updates references among
the copied DLLs. Their namespaces and types are unchanged. The Unity runtime
assembly explicitly references these DLLs so other Unity packages keep using
Unity's copies.
The build configures IL2CPP, ARM64, low managed stripping, Android API 32 minimum,
the highest installed target API and `com.supercapybara.redpandasensei`.
Output: `unity/RedPandaSensei/Build/RedPandaSensei.apk`.
Override the editor executable with `UNITY_EDITOR` if needed.

Keep the headset connected and authorize USB debugging. `push-env.sh` reads only
`[openrouter_deepseek].api_key` from `$HOME/KEYS.toml` and creates a private temporary
file, pushes it to the application's external files directory, then deletes it.
Never commit `sensei.env`, include it in an APK, or print its contents.

```bash
export BERMUDA_THREAD_AGENT=quest-tester
bermuda thread with quest --ttl 15m --why 'u0 install' -- \
  bash -c 'bash tools/unity/push-env.sh && bash tools/unity/run-quest.sh'
```

Set `QUEST_SERIAL` when more than one device is connected; otherwise `adb` uses
the sole connected device. The run script resolves
the installed launcher activity, installs the APK, clears logcat, starts the app
and prints `RPS_` messages for up to 180 seconds. It succeeds after boot step 7
and a completed session-start turn. The status label is hidden after boot. Speech
is logged until the separate voice slice supplies the real speech port.

For developer-mode desk testing, the run script simulates the proximity sensor
and temporarily pauses the guardian boundary. It restores the physical sensor and
boundary on exit. Set `RPS_SCREENSHOT_PATH` to save a screenshot before restoration.
If a Quest launch-check or guardian dialog remains, it restarts the Quest shell
and retries the launch once.

The boot scene contains the Meta camera rig and a passthrough underlay. The
session-start turn greets the learner through the engine. Scene changes are
rendered from engine events; engine exceptions are reported in logcat.

## Development scene check

To exercise a scene change after the greeting, provision an optional test turn and tell the runner to wait for it. The turn uses the normal engine conversation path; the app does not log its text. This is for development checks only.

```bash
export RPS_EXPECT_TEST_TURN=1
bash tools/unity/push-env.sh --test-turn "Please show me an apple, a cup and a box."
bash tools/unity/run-quest.sh
```

Run the commands under the shared Quest lease as described above. Provision without `--test-turn` to remove the test instruction for ordinary use.
