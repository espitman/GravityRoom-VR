# Phase 3 — directional gravity

Status: directional gravity and its visual/audio change feedback are implemented, validated, built, installed and launched on Quest 3. Final device tuning and repeated throw-path acceptance remain open.

## Build and deploy

```bash
./scripts/unity.sh setup3
./scripts/unity.sh validate3
./scripts/unity.sh build3
ADB=/Users/espitman/Library/Android/sdk/platform-tools/adb ./scripts/deploy-quest.sh Builds/Android/GravityRoom-Phase3.apk
```

`setup3` regenerates PhaseThree from the accepted PhaseTwo scene. PhaseTwo runtime behavior remains unchanged. The PhaseThree APK contains only the PhaseThree scene.

## Core behavior

- `DirectionalGravityBody` opts one Rigidbody into room-relative gravity without changing `Physics.gravity`.
- The practice orb starts in Down mode at 9.81 m/s². It cycles deterministically Down → Left → Right every 12 seconds. A selects the next mode and X selects the previous mode; either manual action restarts the interval.
- Gravity is suppressed while the orb is selected. On release, the Meta Interaction SDK throw velocity is retained and directional acceleration begins on the next physics step.
- The room colliders and `OVRCameraRig` are copied unchanged from PhaseTwo and never receive the gravity component.
- Existing hand/controller interaction, gate pass logic, delayed floor reset and player start alignment remain active.
- A collider-free three-piece arrow shows the active world-space direction. It turns orange while the wall text counts 3, 2, 1 before a change.
- A spatial warning chirp plays once when the countdown begins. The clip is synthesized once at startup, so no third-party audio asset is stored and no clip is allocated per frame.

## Verification

- `setup3`: passed and generated `Assets/GravityRoom/Scenes/PhaseThree.unity`.
- `validate3`: passed direction mapping, cycling/countdown rules, arrow/audio wiring, opt-in wiring, fixed-room/rig comparisons, held suppression, first-release-step velocity and Down/Left/Right collision checks. `validate2` also passed after moving the floor reset component to a stable standalone Unity script asset.
- `build3`: succeeded with 0 errors and 8 warnings in 58.4 seconds.
- APK: 108,733,034 bytes; SHA-256 `1b9a4fde95afc6657ca18bc6b0ac48eb37bec4f3ff8c678f6c63a91d0b9aa0c9`.
- The APK installed successfully on Quest 3 serial `2G0YC5ZHBJ02WW` and cold-launched. Physical feel and visual acceptance are still part of the remaining PhaseThree device test.
- Device logs reached focused OpenXR rendering and player alignment with no Unity or Android runtime exception after launch.

## Model accounting

Sol (`gpt-5.6-sol`, Codex CLI) implemented the core runtime, scene tooling and physics checks. GPT-6 with an unavailable exact identifier reviewed the change, fixed the test-scene lifetime bug, ran Unity validation/build and deployed it. The Sol run was interrupted while Unity was blocked by its workspace sandbox and emitted no `turn.completed` usage event; root task usage is also unavailable. Both counts are recorded as unknown rather than estimated.

The feedback/timing milestone was implemented by Sol (`gpt-5.6-sol`, Codex CLI): 1,005,751 input + 17,419 output = 1,023,170 total tokens. Cached input (945,152) and reasoning output (4,888) are subsets. GPT-6 reviewed it, fixed the unstable multi-class Unity script serialization uncovered by a clean reload, regenerated the scenes, validated both phases, built and deployed; root task usage is unavailable. See `model-usage.json`.
