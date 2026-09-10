# Phase 2 — grab and throw practice

Status: complete and accepted on Quest 3 after iterative controller and natural-hand testing.

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

The checklist above was exercised through successive Quest builds. The user accepted the resulting interaction and asked to proceed to Phase 3.

## Implementation and limits

- Sphere radius 0.10 m, mass 0.35 kg, ContinuousDynamic collision and interpolation. Pedestal top 0.85 m at z=0.65 m; initial ball center y=0.96 m.
- Meta `HandGrabInteractable` and `GrabInteractable` share one `Grabbable`, with SDK throwing and kinematic selection. One grab point and `TransferOnSecondSelection` support taking the ball with the other hand. The experimental touch-hand grab path was removed because it anchored the ball unnaturally at the fingertips.
- Gate center (0, 1.4, 2.6), clear opening 1.0 × 1.2 m, solid frame. The sphere must be released in front of the gate, then its center must cross forward inside the opening reduced by its radius. The Unselect pose starts the sweep; cancellation does not arm a throw.
- Success turns the ball green and schedules a 0.9 s reset. A floor hit now waits 2 seconds before returning the ball to the pedestal. Escaped balls, balls settled out of reach, and balls left unheld for 12 seconds also reset. B/Y requests a manual reset. Selection cancels a previously pending reset; no reset executes while selected. The same sphere is moved back, not destroyed or duplicated.
- Meta hand-physics capsules stop the rendered hands at the ball. A clipped poke surface and hand poke limiters reduce visual penetration into the pedestal. The room remains the authoritative physical boundary.
- `PlayerStartAlignment` aligns the rig with the target wall shortly after startup and after recenter. Recenter also clears the glove body-direction cache so the left forearm does not invert.
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
- **Quest acceptance:** natural-hand/controller interaction, reachability, hand/orb collision, pedestal limiting, delayed floor reset, initial facing and recenter behavior were exercised in iterative device builds and accepted by the user.

## Natural-hand input correction — 2026-09-10

The Quest manifest permissions and OpenXR hand extensions were present, but the application initially entered controller-only input mode. The generated scenes now enable `SimultaneousHandsAndControllersEnabled` and `launchSimultaneousHandsControllersOnStartup` on their sole `OVRManager`; controller-driven hand poses and the accepted glove visuals remain unchanged. A development APK built successfully with 0 errors and 8 warnings, was installed on the connected Quest 3, and its device log confirmed `SimultaneousHandsAndControllersModeEnabled = true`. The user subsequently confirmed natural-hand interaction on hardware.

Sol (`gpt-5.6-sol`, Codex CLI) usage for the focused fix: 128,350 input + 1,291 output = 129,641 tokens. Root verification usage is unavailable. The separate agy diagnosis attempt timed out without a result and is recorded in `model-usage.json` without attribution for the fix.

## Release build for sideload testing

```bash
./scripts/unity.sh release2
```

This builds `Builds/Android/GravityRoom-Phase2-Release.apk` with `BuildOptions.None` (non-development player). It uses the same local debug signing certificate as earlier sideload builds for update compatibility; store signing is not configured. The existing `build2` command still produces the development APK.

Release build tooling: Sol (`gpt-5.6-sol`, Codex CLI), 82,744 input + 1,345 output = 84,089 tokens. Root build/verification/delivery usage is unavailable.

Verified 2026-09-10: Release build succeeded (0 errors, 8 warnings); APK 69,871,186 bytes, SHA-256 `f1cc09fe78aebef02232f5c1eaebbfdd60a5562ad4ea10a1affb3394abdb157d`. `apksigner verify` passed and the certificate matches the prior development APK. The manifest has no `android:debuggable` attribute (Android defaults it to false). An identical copy was delivered to the desktop as `GravityRoom-Phase2-Release.apk`.

## Final Quest tuning — 2026-09-10

The final development APK is 108,722,900 bytes with SHA-256 `be180bf4e9ffb1cc388bf0c2802c46ef44d9dc4c8ce8a5910f303cb725c8b0e1`. Unity validation and build passed with 0 errors and 8 warnings before installation. The user tested the build on Quest 3 and accepted it for progression to Phase 3.

GPT-6 with an unavailable exact identifier implemented, built, installed and iterated on the device; its task token count is unavailable. A focused `gpt-5.6-sol` SDK inspection was interrupted before a usage summary, and a `gemini-3.8-flash-high` agy attempt produced no usable usage report. Their counts are therefore recorded as unknown rather than estimated.
