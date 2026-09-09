# Reference hand appearance

The reference shows opaque white-and-black science-fiction gloves, a black wrist cuff, and a short white forearm sleeve. Controller geometry is invisible. The white back-of-hand panels contrast with dark palms, finger undersides, and flexible joint areas.

The previous phase-one translucent Meta hands were a tracking smoke test. They did not satisfy this appearance target. The hand appearance task has been brought forward from phase 5 at the user's request.

## Implementation constraints

- Preserve Meta's tracked hand skeleton, controller input, and interaction data.
- Reuse installed SDK meshes by package reference; keep original project visual code/materials separate.
- Hide controller rendering without disabling controller tracking or interaction objects.
- Keep glove and sleeve opaque on Quest under URP lighting.
- Hide attached geometry when hand tracking is invalid; restore it when tracking returns.
- Arms extend to estimated shoulders using two-bone inverse kinematics. Wrist positions remain tracked; shoulder and elbow positions are inferred, not measured body tracking.

## Acceptance

Check both hands on Quest: white/black glove appearance, cuff/sleeve attachment, no controller models, finger response to trigger/grip, no misplaced or floating geometry, and recovery after tracking loss. User approval of the visual result remains pending until the new build is viewed.

## Current implementation and testing

The initial glove build compiled and installed with zero errors. Live stereo captures confirmed opaque black palms, white dorsal plates, black cuffs and short white sleeves. On-device logs reported seven generated parts per tracked hand. The geometry is a simplified reference-inspired prototype; user acceptance is pending. The user subsequently requested extension to the upper arms, so the short-sleeve iteration is superseded. Shoulder/elbow positions must be estimated from the headset and tracked hands, not presented as measured body tracking.

The extended arm implementation uses a smoothed HMD-yaw shoulder estimate and analytical two-bone IK. Upper arm and forearm sleeves meet at a black elbow joint; the wrist remains at the tracked position. Slight reach accommodation is bounded, and incompatible arm estimates are hidden while gloves remain visible. This is an approximate avatar pose, especially when the head turns independently of the torso.

## Latest build

2026-09-10 (Asia/Tehran): the extended-arm build and depth-tested room text compiled successfully with zero errors and eight warnings, then installed and launched on Quest 3. APK SHA-256: `17999e18481352818ac8c1e79b42f5790fccccaa2227deb2a1bf0c0d796a5686`. The final arm appearance, text occlusion and controller-button acceptance remain pending the headset check.

Device runtime follow-up: both hands produced valid, high-confidence data and nine visible generated parts. Some poses triggered the right-arm reach guard (six remaining glove parts), so arm fit still needs user review. The headset then paused before a usable final stereo capture was obtained. No managed exception or assertion appeared in the inspected Unity log.

## User-requested forearm revision

The user preferred only the portion below the elbow and explicitly asked to keep the attractive white finger accents. Upper-arm geometry is now removed; the shoulder is retained only as an input to elbow estimation. Separate rigid finger plates were replaced with white vertex-color patches on a runtime clone of the SDK skinned hand mesh. The white accents now deform on the same surface as the black glove, eliminating interpenetrating finger geometry. No vendor mesh data is committed.

This revision built with zero errors and eight warnings and was installed on Quest. APK SHA-256: `c3e86dfe46e574dfe87b37e61184045b7dcea1cfac4d7b7031ccb6013933eed8`. Visual acceptance remains pending. Root model identity beyond GPT-6 and task token usage are unavailable; no additional Sol run was used for this revision.

Acceptance: the user confirmed the forearm-only revision and retained white finger accents with “خوب شد الان” (looks good now). Runtime logged white surface vertices on both hand meshes (right 74/1360, left 75/1360) with no managed exception or assertion in the inspected Unity log. This accepted prototype is the current visual baseline; it is not a claim of pixel-exact reference reproduction.
