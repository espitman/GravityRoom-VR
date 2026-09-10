# Phase 3 — directional gravity

Status: the core gravity milestone is implemented, validated, built, installed and launched on Quest 3. Direction indicators, timed changes, warning audio and final device tuning remain open.

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
- The practice orb starts in Down mode at 9.81 m/s². A selects the next mode and X selects the previous mode across Down, Left and Right during this manual-test milestone.
- Gravity is suppressed while the orb is selected. On release, the Meta Interaction SDK throw velocity is retained and directional acceleration begins on the next physics step.
- The room colliders and `OVRCameraRig` are copied unchanged from PhaseTwo and never receive the gravity component.
- Existing hand/controller interaction, gate pass logic, delayed floor reset and player start alignment remain active.

## Verification

- `setup3`: passed and generated `Assets/GravityRoom/Scenes/PhaseThree.unity`.
- `validate3`: passed direction mapping, opt-in wiring, fixed-room/rig comparisons, held suppression, first-release-step velocity and Down/Left/Right collision checks.
- `build3`: succeeded with 0 errors and 8 warnings in 60.2 seconds.
- APK: 108,725,695 bytes; SHA-256 `ab98ee3aac7c90dc7769bf6622e031d644468a57c7248c193a6cb13d1551b2e3`.
- The APK installed successfully on Quest 3 serial `2G0YC5ZHBJ02WW` and cold-launched. Physical feel and visual acceptance are still part of the remaining PhaseThree device test.

## Model accounting

Sol (`gpt-5.6-sol`, Codex CLI) implemented the core runtime, scene tooling and physics checks. GPT-6 with an unavailable exact identifier reviewed the change, fixed the test-scene lifetime bug, ran Unity validation/build and deployed it. The Sol run was interrupted while Unity was blocked by its workspace sandbox and emitted no `turn.completed` usage event; root task usage is also unavailable. Both counts are recorded as unknown rather than estimated.
