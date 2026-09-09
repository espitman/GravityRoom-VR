# Phase 2 — grab and throw practice

Status: implementation and editor/physics validation complete. APK build verification is recorded below. Headset acceptance and final tuning are pending; the user is using the headset for other activities, so Phase 2 has not been installed or launched on it.

This phase adds one reusable ball, a reachable pedestal, and one target gate to a copy of the validated Phase 1 room. The accepted forearm-only black and white gloves are preserved. Gravity changes and full game rounds belong to later phases.

## Build and deploy

```bash
./scripts/unity.sh setup2
./scripts/unity.sh validate2
./scripts/unity.sh build2
ADB=/Users/espitman/Library/Android/sdk/platform-tools/adb ./scripts/deploy-quest.sh Builds/Android/GravityRoom-Phase2.apk
```

`setup2` regenerates PhaseTwo from the PhaseOne baseline; edit the generator for persistent scene changes. `build2` packages only PhaseTwo. The original `setup`, `validate`, and `build` commands retain their PhaseOne behavior.

## Acceptance on Quest

- Grab and release with the left hand, then the right hand; repeat with Touch controllers and natural hands where available.
- Hold the ball with one hand while reaching with the other. Only one hand may control it at a time.
- Throw forward through the opening several times. Each released passage should produce one success and return one ball to the pedestal.
- Carry the ball through the opening without releasing; hit the frame; throw outside the opening; cross backwards. None should count as a valid throw.
- Drop the ball on the floor or throw it out of reach. It should return without restarting the application.
- Hold the ball beyond the normal reset delay. It must remain held; release near a pending reset and check that it does not teleport during a new grab.
- Check that floor, walls and frame stop the ball, including faster throws.

No headset result is implied by editor validation. Device test results will be recorded when the headset is available again.

## Implementation and limits

- Sphere radius 0.10 m, mass 0.35 kg, ContinuousDynamic collision and interpolation. Pedestal top 0.85 m at z=0.65 m; initial ball center y=0.96 m.
- Meta `HandGrabInteractable` and `GrabInteractable` share one `Grabbable`, with SDK throwing and kinematic selection. One grab point and `TransferOnSecondSelection` support taking the ball with the other hand.
- Gate center (0, 1.4, 2.6), clear opening 1.0 × 1.2 m, solid frame. The sphere must be released in front of the gate, then its center must cross forward inside the opening reduced by its radius. The Unselect pose starts the sweep; cancellation does not arm a throw.
- Success turns the ball green and schedules a 0.9 s reset. Escaped balls, balls settled out of reach for 1.5 s, and balls left unheld for 12 s also reset. B/Y requests a manual reset. Selection cancels a previously pending reset; no reset executes while selected. The same sphere is moved back, not destroyed or duplicated.
- PhaseOne scene and glove geometry code are unchanged. Unity serialized existing shader defaults into glove material assets; this does not change their appearance.
- Physics checks simulate a floor drop, a 15 m/s opening shot followed by back-wall contact, and a 15 m/s rail impact in a temporary editor scene, restoring the source scene and simulation mode afterward. Gate/reset rule checks run in the same validation entry point. These checks do not emulate SDK hand tracking or establish on-device performance.
- Root review fixed destruction of a diagnostics root during scene generation, the pedestal's bottom offset, selection transfer, release-event capture, cancellation, and reset cancellation on re-grab.

## Model accounting

Sol (`gpt-5.6-sol`, Codex CLI) implemented the scene generator and runtime: 1,464,561 input + 21,536 output = 1,486,097 tokens, shared across Phase 2 implementation tasks. agy (`gemini-3.8-flash-medium`) supplied acceptance-test advice: 20,269 input + 2,560 output = 22,829 reported tokens. Root GPT-6 exact identity and task token totals are unavailable. See `model-usage.json`; cache and reasoning subsets are not added again.

## Verified build — 2026-09-10 (local date)

- `setup2`: passed; generated PhaseTwo from the unchanged PhaseOne scene.
- `validate2`: scene references, room collision, Meta interaction wiring, and gate/reset rules passed.
- `build2`: passed the above checks plus isolated PhysX floor/fast-throw/rail checks, then produced the development APK.
- Unity build: succeeded, 0 errors, 8 warnings, 80.1 seconds (21:37:07–21:38:27 UTC on 2026-09-09). Compiler warnings included IL2CPP/TMP split-method notices and an unused clang language argument.
- APK: `Builds/Android/GravityRoom-Phase2.apk`, approximately 104 MiB; SHA-256 `21b35abbc0b3159c778b474114a720f3d0af107843c7b81c80c771de2ee18e9e`. APK and detailed logs remain local and ignored by Git.
- `git diff --check`, shell syntax and accounting JSON parsing passed. Unity upgraded the serialized physics settings to its current schema while retaining FixedUpdate simulation and existing gravity.
- The physics test harness initially failed because scene creation and simulation mode needed editor-specific handling; both were corrected before the successful build.
- **Not yet verified:** installation/launch of PhaseTwo on Quest, natural-hand/controller throws, inter-hand transfers and reset races on hardware, reachability/throw tuning, performance and visual acceptance.
