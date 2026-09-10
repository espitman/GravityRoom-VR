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
- The practice orb starts in Down mode at 2.5 m/s². It cycles deterministically Down → Left → Right every 12 seconds. A selects the next mode and X selects the previous mode; either manual action restarts the interval.
- The orb remains stationary and reachable on its accepted PhaseTwo pedestal until a genuine grab and release. Gravity is suppressed while selected, activates only after Select → terminal Unselect, and stays disabled after Cancel. Every ball reset disarms gravity again. On release, the Meta Interaction SDK throw velocity is retained and directional acceleration begins on the next physics step.
- The room colliders and `OVRCameraRig` are copied unchanged from PhaseTwo and never receive the gravity component.
- Existing hand/controller interaction, gate pass logic, delayed floor reset and player start alignment remain active.
- A distant unheld orb can be recalled without controllers: hold either tracked palm upward and maintain an index-thumb pinch for one second. The gesture is disabled near the orb and while it is grabbed, and tracking loss, low confidence, palm rotation, system gestures or a broken pinch cancel its progress.
- A collider-free three-piece arrow shows the active world-space direction. It turns orange while the wall text counts 3, 2, 1 before a change.
- A spatial warning chirp plays once when the countdown begins. The clip is synthesized once at startup, so no third-party audio asset is stored and no clip is allocated per frame.
- Game audio is temporarily muted for headset testing at the user's request; the warning logic remains wired for later re-enabling.

## Verification

- `setup3`: passed and generated `Assets/GravityRoom/Scenes/PhaseThree.unity`.
- `validate3`: passed direction mapping, cycling/countdown rules, arrow/audio wiring, opt-in wiring, fixed-room/rig comparisons, held suppression, first-release-step velocity and Down/Left/Right collision checks. `validate2` also passed after moving the floor reset component to a stable standalone Unity script asset.
- `build3`: succeeded with 0 errors and 8 warnings in 58.4 seconds.
- APK: 108,733,034 bytes; SHA-256 `1b9a4fde95afc6657ca18bc6b0ac48eb37bec4f3ff8c678f6c63a91d0b9aa0c9`.
- The APK installed successfully on Quest 3 serial `2G0YC5ZHBJ02WW` and cold-launched. Physical feel and visual acceptance are still part of the remaining PhaseThree device test.
- Device logs reached focused OpenXR rendering and player alignment with no Unity or Android runtime exception after launch.
- After the first headset pass showed that the waiting orb escaped and 9.81 m/s² made target throws impractical, the release-armed lifecycle and 2.5 m/s² tuning candidate passed validation and build. The revised APK is 108,729,169 bytes with SHA-256 `c19555d7bf2107a528cc3140bf9cc744ba1f6c8c18c2e0c3e1c5320629999953` and was installed and cold-launched on the same Quest 3.
- The natural-hand recall build passed PhaseThree validation and Android packaging with 0 errors and 8 warnings. Installation was deferred because no Quest appeared in `adb devices`; the APK was also copied to the desktop for sideloading.

## Model accounting

Sol (`gpt-5.6-sol`, Codex CLI) implemented the core runtime, scene tooling and physics checks. GPT-6 with an unavailable exact identifier reviewed the change, fixed the test-scene lifetime bug, ran Unity validation/build and deployed it. The Sol run was interrupted while Unity was blocked by its workspace sandbox and emitted no `turn.completed` usage event; root task usage is also unavailable. Both counts are recorded as unknown rather than estimated.

The feedback/timing milestone was implemented by Sol (`gpt-5.6-sol`, Codex CLI): 1,005,751 input + 17,419 output = 1,023,170 total tokens. Cached input (945,152) and reasoning output (4,888) are subsets. GPT-6 reviewed it, fixed the unstable multi-class Unity script serialization uncovered by a clean reload, regenerated the scenes, validated both phases, built and deployed; root task usage is unavailable. See `model-usage.json`.

The reachability and playable-strength correction was implemented by Sol (`gpt-5.6-sol`, Codex CLI): 991,304 input + 11,780 output = 1,003,084 total tokens. Cached input (928,512) and reasoning output (5,310) are subsets. GPT-6 reviewed, ran the expanded release-lifecycle and directional physics validation, built and installed the APK; root task usage is unavailable.

The controller-free recall gesture was implemented by Sol (`gpt-5.6-sol`, Codex CLI): 3,081,692 input + 17,793 output = 3,099,485 total tokens. Cached input (2,973,184) and reasoning output (7,428) are subsets. GPT-6 reviewed, validated and built it; root task usage is unavailable.
