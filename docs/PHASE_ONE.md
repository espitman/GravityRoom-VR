# Phase 1: Quest foundation

## Toolchain

- Unity Editor: **6000.0.83f1** (see `ProjectSettings/ProjectVersion.txt`).
- Universal Render Pipeline: **17.0.4**.
- Meta XR Core, Interaction Essentials and OVR integration: **205.0.0**.
- Unity OpenXR: **1.16.1**.
- Android build support: SDK platform **35**, NDK **27.2.12479018 (r27c)**, **Java 17**, **Gradle 8.13**, and Android Gradle Plugin **8.10.0**.
- Target hardware: Meta Quest 3, connected with USB debugging authorized.

Direct dependencies are pinned in `Packages/manifest.json`; Unity records resolved transitive versions in `Packages/packages-lock.json`. Meta packages come from the official Meta registry. SDK code stays in the package cache, outside version control.

## Commands

Close the Unity Editor for this project before running batch commands. On macOS, the scripts locate the version recorded in `ProjectVersion.txt`. On another installation, set `UNITY_EDITOR` to the editor executable.

```bash
# Configure the project and create the test scene if it does not exist.
./scripts/unity.sh setup

# Check project, XR and scene configuration.
./scripts/unity.sh validate

# Produce a development APK.
./scripts/unity.sh build

# Install and launch on the connected Quest.
./scripts/deploy-quest.sh
```

Build output: `Builds/Android/GravityRoom-Phase1.apk`. Editor logs: `Logs/`. Both are ignored by Git. If more than one Android device is connected, set `QUEST_SERIAL` explicitly. Set `ADB` if adb is not on PATH.

`UNITY_JDK_ROOT`, `UNITY_SDK_ROOT`, `UNITY_NDK_ROOT`, and `UNITY_GRADLE_ROOT` optionally select custom Android tool installations. The shell wrapper also loads `UserSettings/android-tools.env` when present; this file is local and ignored by Git. Otherwise the setup uses existing Unity External Tools paths, falling back to bundled directories. Machine-specific paths are not committed.

The local editor installation contained mismatched Java 21, NDK r27b and Gradle 9.1 tools. Phase 1 selects compatible Java 17 and NDK r27c installations and downloads Gradle 8.13. The project pins AGP 8.10.0 in its own base Gradle template so a modified machine-wide template cannot silently switch it to AGP 9.

## Headset acceptance check

The test scene is a stationary room at real-world scale. It contains a one-meter reference, virtual hands, instructions, and a live tracking/input panel. It does not yet implement grabbing, throwing, scoring or changing gravity.

Confirm on Quest:

1. The room appears in stereo and follows head movement normally.
2. Floor height and the one-meter reference feel correct.
3. Both virtual hands follow the controllers.
4. The status panel shows XR running and tracked head/controllers.
5. Squeezing each trigger and grip changes the corresponding value.
6. Taking off and putting on the headset resumes rendering without a crash.

Runtime diagnostics are local logs prefixed with `[GravityRoom]`. They report XR startup, tracking state, trigger/grip transitions, and OVR hand data validity. They do not transmit data.

The comprehensive Meta rig uses `ConformingToController` hand poses. The project must also select `ControllersAndHands`: the OVRHand data path requires the hand-tracking manifest feature and permission even when controllers drive the finger poses. Setup enforces this setting and validation rejects `ControllersOnly`.

## Reference documentation

- [Meta Interaction SDK setup](https://developers.meta.com/horizon/documentation/unity/unity-isdk-setup/)
- [Meta package registry](https://developers.meta.com/horizon/documentation/unity/unity-package-manager/)
- [Unity OpenXR 1.16](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.16/manual/index.html)
- [Unity URP for untethered XR](https://docs.unity3d.com/6000.0/Documentation/Manual/xr-untethered-device-optimization.html)
- [Unity and Gradle version compatibility](https://docs.unity3d.com/6000.0/Documentation/Manual/android-gradle-version-compatibility.html)

## Validation results

- 2026-09-09: Unity configuration and build validation passed; Android development build succeeded with zero errors and eight warnings.
- APK built with Java 17, NDK r27c, Gradle 8.13 and AGP 8.10.0, then installed and launched on a USB-connected Quest 3.
- Initial device run confirmed OpenXR running and head/both controller tracking. The user reported invisible hands in that initial build.
- Follow-up build enables the missing hand-tracking manifest declarations, reduces overlapping room text and logs input transitions/OVR hand data. Installation succeeded. Subsequent on-device logs show both OVRHands tracked with valid, high-confidence data and trigger/grip transitions from both controllers. A live stereo capture confirms both translucent virtual hand meshes around controller models and legible room labels. Tracking/rendering resumed after removing and wearing the headset.
- Follow-up APK SHA-256: `1dd4fbfb93b5875c20ddc54c9b0eae5c4161d2f7b015dc07c26994b2812a5e06`.
- Performance and comfort qualification are deferred to phase 6; setting a 90 Hz target is not proof of sustained frame rate.

## Hand appearance follow-up

The phase-one translucent hands were replaced by a reference-inspired opaque glove prototype. The user then requested arms through the upper arm; that build is installed. See [HAND_VISUALS.md](HAND_VISUALS.md) for the approximate arm model, build evidence and outstanding visual acceptance. This follow-up does not imply that the final reference appearance is approved.
