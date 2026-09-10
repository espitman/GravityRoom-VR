using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GravityRoom;
using Oculus.Interaction;
using Oculus.Interaction.Editor.QuickActions;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;
using Oculus.Interaction.Surfaces;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GravityRoom.Editor
{
    /// <summary>Repeatable Phase 2 scene creation, validation, and Android build entry points.</summary>
    public static class PhaseTwoSetup
    {
        private const string PhaseOneScenePath = "Assets/GravityRoom/Scenes/PhaseOne.unity";
        private const string ScenePath = "Assets/GravityRoom/Scenes/PhaseTwo.unity";
        private const string MaterialFolder = "Assets/GravityRoom/Materials";
        private const string ApkPath = "Builds/Android/GravityRoom-Phase2.apk";
        private const string ReleaseApkPath = "Builds/Android/GravityRoom-Phase2-Release.apk";
        private const string BuildLogPath = "Logs/GravityRoom-Phase2-build.txt";
        private const string ReleaseBuildLogPath = "Logs/GravityRoom-Phase2-release-build.txt";
        private const float BallRadius = 0.1f;
        private static readonly Vector2 ApertureHalfSize = new(0.5f, 0.6f);

        public static void Configure()
        {
            PhaseOneSetup.Configure();
            PhaseOneSetup.Validate();

            // Rebase every run on the currently validated Phase 1 artifact. The Phase 1 scene is never edited here.
            File.Copy(PhaseOneScenePath, ScenePath, true);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemovePhaseOneOnlyObjects(scene);
            ConfigureCopiedLabels(scene);
            CreatePractice(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new BuildFailedException($"Could not save {ScenePath}.");
            EnsureBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[GravityRoom] Phase 2 configuration complete.");
        }

        public static void Validate()
        {
            // Phase 1 owns shared Android/XR/rendering and accepted-rig invariants.
            PhaseOneSetup.Validate();
            var failures = new List<string>();
            ValidatePassRules(failures);
            ValidateScene(failures);
            if (failures.Count == 0) PhaseTwoPhysicsChecks.Validate(SceneManager.GetActiveScene(), failures);
            if (failures.Count > 0)
                throw new BuildFailedException("GravityRoom Phase 2 validation failed:\n - " +
                                               string.Join("\n - ", failures));
            Debug.Log("[GravityRoom] Phase 2 validation passed, including gate-rule checks.");
        }

        public static void BuildAndroid()
        {
            BuildAndroid(false);
        }

        public static void BuildReleaseAndroid()
        {
            BuildAndroid(true);
        }

        private static void BuildAndroid(bool release)
        {
            PhaseOneSetup.ConfigureBundledAndroidTools();
            Validate();
            PlayerSettings.Android.useCustomKeystore = false;
            string apkPath = release ? ReleaseApkPath : ApkPath;
            string buildLogPath = release ? ReleaseBuildLogPath : BuildLogPath;
            string buildType = release ? "Release" : "Development";
            Directory.CreateDirectory(Path.GetDirectoryName(apkPath) ?? "Builds/Android");
            Directory.CreateDirectory(Path.GetDirectoryName(buildLogPath) ?? "Logs");

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = release ? BuildOptions.None : BuildOptions.Development
            });
            BuildSummary summary = report.summary;
            string text = $"GravityRoom Phase 2 Android {buildType} build{Environment.NewLine}" +
                          $"Result: {summary.result}{Environment.NewLine}" +
                          $"Output: {summary.outputPath}{Environment.NewLine}" +
                          $"Started: {summary.buildStartedAt:O}{Environment.NewLine}" +
                          $"Ended: {summary.buildEndedAt:O}{Environment.NewLine}" +
                          $"Duration: {summary.totalTime}{Environment.NewLine}" +
                          $"Size: {summary.totalSize} bytes{Environment.NewLine}" +
                          $"Warnings: {summary.totalWarnings}{Environment.NewLine}" +
                          $"Errors: {summary.totalErrors}{Environment.NewLine}";
            File.WriteAllText(buildLogPath, text);
            AssetDatabase.Refresh();
            if (summary.result != BuildResult.Succeeded || summary.totalErrors > 0)
                throw new BuildFailedException($"Android {buildType} build {summary.result} with " +
                                               $"{summary.totalErrors} error(s). See {buildLogPath}.");
            Debug.Log($"[GravityRoom] {buildType} APK built at {apkPath}. Summary: {buildLogPath}");
        }

        private static void RemovePhaseOneOnlyObjects(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PhaseOneDiagnostics diagnostics in root.GetComponentsInChildren<PhaseOneDiagnostics>(true))
                    Object.DestroyImmediate(diagnostics.gameObject);
                if (!root) continue;
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                    if (transform != null && transform.name == "Standing Reference (1m)")
                        Object.DestroyImmediate(transform.gameObject);
            }
        }

        private static void ConfigureCopiedLabels(Scene scene)
        {
            foreach (TextMesh text in scene.GetRootGameObjects()
                         .SelectMany(root => root.GetComponentsInChildren<TextMesh>(true)))
            {
                if (text.name == "Title")
                {
                    text.text = "GRAVITY ROOM — PHASE 2";
                    text.transform.position = new Vector3(0f, 2.35f, 2.88f);
                }
                else if (text.name == "Instructions")
                {
                    text.text = "Grab the orb with either hand.\nFar orb: palm up + index pinch for 1 second.";
                    text.transform.position = new Vector3(0f, 2.08f, 2.88f);
                }
                EditorUtility.SetDirty(text);
            }
        }

        private static void CreatePractice(Scene scene)
        {
            GameObject oldRoot = scene.GetRootGameObjects().FirstOrDefault(root => root.name == "Phase 2 Practice");
            if (oldRoot) Object.DestroyImmediate(oldRoot);

            Material pedestalMaterial = GetOrCreateMaterial("Phase2Pedestal", new Color(0.18f, 0.24f, 0.31f));
            Material gateMaterial = GetOrCreateMaterial("Phase2Gate", new Color(0.08f, 0.64f, 0.78f));
            Material ballMaterial = GetOrCreateMaterial("Phase2Orb", new Color(0.95f, 0.38f, 0.08f));
            Material successMaterial = GetOrCreateMaterial("Phase2OrbSuccess", new Color(0.12f, 0.9f, 0.3f));

            var root = new GameObject("Phase 2 Practice");
            GameObject pedestal = CreateBox("Pedestal", root.transform, new Vector3(0f, 0.425f, 0.65f),
                new Vector3(0.55f, 0.85f, 0.55f), pedestalMaterial);
            CreatePedestalHandSurface(root.transform, pedestal);

            var gate = new GameObject("Gate Center");
            gate.transform.SetParent(root.transform, false);
            gate.transform.SetPositionAndRotation(new Vector3(0f, 1.4f, 2.6f), Quaternion.identity);
            CreateBox("Gate Left", gate.transform, new Vector3(-0.575f, 0f, 0f),
                new Vector3(0.15f, 1.5f, 0.16f), gateMaterial, true);
            CreateBox("Gate Right", gate.transform, new Vector3(0.575f, 0f, 0f),
                new Vector3(0.15f, 1.5f, 0.16f), gateMaterial, true);
            CreateBox("Gate Top", gate.transform, new Vector3(0f, 0.675f, 0f),
                new Vector3(1.3f, 0.15f, 0.16f), gateMaterial, true);
            CreateBox("Gate Bottom", gate.transform, new Vector3(0f, -0.675f, 0f),
                new Vector3(1.3f, 0.15f, 0.16f), gateMaterial, true);

            GameObject ballObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ballObject.name = "Practice Orb";
            ballObject.transform.SetParent(root.transform, false);
            ballObject.transform.position = new Vector3(0f, 0.96f, 0.65f);
            ballObject.transform.localScale = Vector3.one * (BallRadius * 2f);
            ballObject.GetComponent<MeshRenderer>().sharedMaterial = ballMaterial;
            Rigidbody rigidbody = ballObject.AddComponent<Rigidbody>();
            rigidbody.mass = 0.35f;
            rigidbody.useGravity = true;
            rigidbody.isKinematic = false;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            QuickActionsAPI.AddGrabInteraction(ballObject);
            Grabbable grabbable = ballObject.GetComponent<Grabbable>();
            if (!grabbable)
                throw new BuildFailedException("Meta Quick Actions did not create a Grabbable on the practice orb.");
            grabbable.InjectOptionalRigidbody(rigidbody);
            grabbable.InjectOptionalThrowWhenUnselected(true);
            grabbable.InjectOptionalKinematicWhileSelected(true);
            grabbable.MaxGrabPoints = 1;
            grabbable.TransferOnSecondSelection = true;
            EditorUtility.SetDirty(grabbable);

            TextMesh status = CreateText("Practice Status", "GRAB THE ORB, THEN THROW IT THROUGH THE GATE\nRESET: B / Y OR PALM-UP PINCH",
                new Vector3(0f, 1.78f, 2.88f), 0.011f);
            status.transform.SetParent(root.transform, true);
            Shader textShader = Shader.Find("GravityRoom/DepthTestedText");
            if (!textShader) throw new BuildFailedException("Depth-tested room text shader is missing.");
            DepthTestedRoomText depthText = status.gameObject.AddComponent<DepthTestedRoomText>();
            depthText.Configure(textShader);

            PhaseTwoPracticeController controller = root.AddComponent<PhaseTwoPracticeController>();
            controller.Configure(rigidbody, grabbable, gate.transform, status, ballMaterial, successMaterial,
                BallRadius, ApertureHalfSize);
            EditorUtility.SetDirty(controller);
            PhaseTwoFloorReset floorReset = ballObject.AddComponent<PhaseTwoFloorReset>();
            floorReset.Configure(controller);
            EditorUtility.SetDirty(floorReset);
        }

        private static void CreatePedestalHandSurface(Transform parent, GameObject pedestal)
        {
            var surfaceObject = new GameObject("Pedestal Hand Surface");
            surfaceObject.transform.SetParent(parent, false);
            Bounds bounds = pedestal.GetComponent<Collider>().bounds;
            surfaceObject.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.max.y + 0.001f, bounds.center.z),
                Quaternion.Euler(-90f, 0f, 0f));

            PlaneSurface plane = surfaceObject.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Forward, false);
            BoundsClipper clipper = surfaceObject.AddComponent<BoundsClipper>();
            clipper.Position = Vector3.zero;
            clipper.Size = new Vector3(bounds.size.x, bounds.size.z, 0.02f);
            ClippedPlaneSurface clippedPlane = surfaceObject.AddComponent<ClippedPlaneSurface>();
            clippedPlane.InjectAllClippedPlaneSurface(plane, new IBoundsClipper[] { clipper });
            PokeInteractable interactable = surfaceObject.AddComponent<PokeInteractable>();
            interactable.InjectAllPokeInteractable(clippedPlane);
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale,
            Material material, bool local = false)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            if (local) box.transform.localPosition = position;
            else box.transform.position = position;
            box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = scale;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box;
        }

        private static TextMesh CreateText(string name, string content, Vector3 position, float characterSize)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetPositionAndRotation(position, Quaternion.identity);
            TextMesh text = gameObject.AddComponent<TextMesh>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 64;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.black;
            text.richText = false;
            text.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
            return text;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) throw new BuildFailedException("Built-in URP/Lit shader was not found.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes.Where(item => item.path != ScenePath).ToList();
            int phaseOneIndex = scenes.FindIndex(item => item.path == PhaseOneScenePath);
            int insertion = phaseOneIndex >= 0 ? phaseOneIndex + 1 : scenes.Count;
            scenes.Insert(insertion, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void ValidatePassRules(ICollection<string> failures)
        {
            Vector3 center = new(0f, 1.4f, 2.6f);
            Quaternion rotation = Quaternion.identity;
            bool validSweep = PhaseTwoPassLogic.CrossedFrontToBack(
                new Vector3(0.35f, 1.85f, 1.8f), new Vector3(0.35f, 1.85f, 3.2f),
                center, rotation, ApertureHalfSize, BallRadius);
            bool reverseSweep = PhaseTwoPassLogic.CrossedFrontToBack(
                new Vector3(0f, 1.4f, 3f), new Vector3(0f, 1.4f, 2f),
                center, rotation, ApertureHalfSize, BallRadius);
            bool clippedSide = PhaseTwoPassLogic.CrossedFrontToBack(
                new Vector3(0.41f, 1.4f, 2f), new Vector3(0.41f, 1.4f, 3f),
                center, rotation, ApertureHalfSize, BallRadius);
            bool clippedTop = PhaseTwoPassLogic.CrossedFrontToBack(
                new Vector3(0f, 1.91f, 2f), new Vector3(0f, 1.91f, 3f),
                center, rotation, ApertureHalfSize, BallRadius);
            bool noCrossing = PhaseTwoPassLogic.CrossedFrontToBack(
                new Vector3(0f, 1.4f, 2f), new Vector3(0f, 1.4f, 2.5f),
                center, rotation, ApertureHalfSize, BallRadius);
            if (!validSweep) failures.Add("Gate rule rejected a fast, fully contained front-to-back sweep.");
            if (reverseSweep) failures.Add("Gate rule accepted a back-to-front sweep.");
            if (clippedSide) failures.Add("Gate rule accepted a sphere clipping the aperture side.");
            if (clippedTop) failures.Add("Gate rule accepted a sphere clipping the aperture top.");
            if (noCrossing) failures.Add("Gate rule accepted a segment that never reached the gate plane.");

            bool reachableIdleReset = PhaseTwoPassLogic.ShouldAutoReset(
                new Vector3(0f, 0.96f, 0.65f), 2f, 0f, 12f);
            bool escapedReset = PhaseTwoPassLogic.ShouldAutoReset(
                new Vector3(3.5f, 1f, 0f), 0.1f, 0f, 12f);
            bool timedOutReset = PhaseTwoPassLogic.ShouldAutoReset(
                new Vector3(0f, 1f, 0f), 12f, 0f, 12f);
            bool movingCornerSettled = PhaseTwoPassLogic.IsSettledAndUnreachable(
                new Vector3(2.5f, 0.2f, 0f), new Vector3(1f, 0f, 0f), Vector3.zero, false);
            bool stoppedCornerSettled = PhaseTwoPassLogic.IsSettledAndUnreachable(
                new Vector3(2.5f, 0.2f, 0f), Vector3.zero, Vector3.zero, false);
            bool settledReset = PhaseTwoPassLogic.ShouldAutoReset(
                new Vector3(2.5f, 0.2f, 0f), 2f, 1.5f, 12f);
            if (reachableIdleReset) failures.Add("Reset rule accepted a normal reachable practice state.");
            if (!escapedReset) failures.Add("Reset rule did not recover an escaped orb immediately.");
            if (!timedOutReset) failures.Add("Reset rule did not recover an orb at the unheld timeout.");
            if (movingCornerSettled || !stoppedCornerSettled || !settledReset)
                failures.Add("Reset rule does not distinguish a moving orb from one settled out of reach.");
            if (!PhaseTwoPassLogic.HasReachedFloor(new Vector3(0f, BallRadius, 0f), BallRadius) ||
                PhaseTwoPassLogic.HasReachedFloor(new Vector3(0f, 0.3f, 0f), BallRadius))
                failures.Add("Floor-contact reset rule does not reset immediately at floor height.");

            const float gestureDistance = 0.9f;
            bool distantFromBothHands = PhaseTwoResetGestureLogic.IsOrbMeaningfullyDistant(
                Vector3.zero, new Vector3(1f, 0f, 0f), true,
                new Vector3(-1f, 0f, 0f), true, gestureDistance);
            bool nearEitherHand = PhaseTwoResetGestureLogic.IsOrbMeaningfullyDistant(
                Vector3.zero, new Vector3(1f, 0f, 0f), true,
                new Vector3(0.2f, 0f, 0f), true, gestureDistance);
            if (!distantFromBothHands || nearEitherHand)
                failures.Add("Hand reset distance rule does not protect a near-orb hand interaction.");

            bool validPalmPinch = PhaseTwoResetGestureLogic.AreHandConditionsMet(
                true, false, true, 0.7f, 0.65f);
            bool lowConfidencePinch = PhaseTwoResetGestureLogic.AreHandConditionsMet(
                false, false, true, 0.7f, 0.65f);
            bool tiltedPalmPinch = PhaseTwoResetGestureLogic.AreHandConditionsMet(
                true, false, true, 0.5f, 0.65f);
            if (!validPalmPinch || lowConfidencePinch || tiltedPalmPinch)
                failures.Add("Hand reset rule does not require confident palm-up tracking and a pinch.");

            float partialHold = PhaseTwoResetGestureLogic.UpdateHoldDuration(0f, true, 0.6f, 1f);
            float resetHold = PhaseTwoResetGestureLogic.UpdateHoldDuration(partialHold, false, 0.1f, 1f);
            float completedHold = PhaseTwoResetGestureLogic.UpdateHoldDuration(partialHold, true, 0.4f, 1f);
            if (resetHold != 0f ||
                !PhaseTwoResetGestureLogic.CompletedThisFrame(partialHold, completedHold, 1f))
                failures.Add("Hand reset hold rule does not require one uninterrupted gesture.");
        }

        private static void ValidateScene(ICollection<string> failures)
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            {
                failures.Add($"Phase 2 scene is missing at {ScenePath}.");
                return;
            }
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == PhaseOneScenePath))
                failures.Add("Phase 1 must remain enabled in Build Settings.");
            if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == ScenePath))
                failures.Add("Phase 2 scene is not enabled in Build Settings.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            int rigs = roots.Sum(root => root.GetComponentsInChildren<OVRCameraRig>(true).Length);
            if (rigs != 1) failures.Add($"Phase 2 contains {rigs} OVRCameraRig components; expected one.");
            if (roots.SelectMany(root => root.GetComponentsInChildren<PhaseOneDiagnostics>(true)).Any())
                failures.Add("Phase 1 diagnostics remain in the Phase 2 scene.");
            if (roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Any(transform => transform.name == "Standing Reference (1m)"))
                failures.Add("The Phase 1 reference cube remains in Phase 2.");

            foreach (string roomPart in new[] { "Floor", "Wall North", "Wall South", "Wall East", "Wall West" })
            {
                Transform part = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                    .FirstOrDefault(transform => transform.name == roomPart);
                if (!part || !part.TryGetComponent(out Collider collider) || collider.isTrigger)
                    failures.Add($"Room collision is missing or non-solid on '{roomPart}'.");
            }

            PhaseTwoPracticeController[] controllers = roots
                .SelectMany(root => root.GetComponentsInChildren<PhaseTwoPracticeController>(true)).ToArray();
            if (controllers.Length != 1)
            {
                failures.Add($"Phase 2 contains {controllers.Length} practice controllers; expected one.");
                return;
            }
            PhaseTwoPracticeController controller = controllers[0];
            Rigidbody ball = controller.Ball;
            Grabbable grabbable = controller.Grabbable;
            if (!ball || !grabbable || !controller.GateCenter || !controller.StatusText)
                failures.Add("Practice controller references are incomplete.");
            if (!ball) return;

            SphereCollider sphere = ball.GetComponent<SphereCollider>();
            float worldRadius = sphere ? sphere.radius * ball.transform.lossyScale.x : -1f;
            if (!sphere || sphere.isTrigger || Mathf.Abs(worldRadius - BallRadius) > 0.005f)
                failures.Add("Practice orb must have a solid sphere collider with an approximately 0.10 m radius.");
            if (Mathf.Abs(ball.mass - 0.35f) > 0.001f)
                failures.Add("Practice orb mass is not 0.35 kg.");
            if (ball.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic ||
                ball.interpolation != RigidbodyInterpolation.Interpolate)
                failures.Add("Practice orb physics must use ContinuousDynamic collision and interpolation.");
            if (grabbable && (grabbable.MaxGrabPoints != 1 || !grabbable.TransferOnSecondSelection))
                failures.Add("Practice orb Grabbable must allow exactly one grab point.");
            if (grabbable)
            {
                var serialized = new SerializedObject(grabbable);
                if (!serialized.FindProperty("_throwWhenUnselected").boolValue ||
                    !serialized.FindProperty("_kinematicWhileSelected").boolValue ||
                    serialized.FindProperty("_rigidbody").objectReferenceValue != ball)
                    failures.Add("Practice orb Grabbable throw/kinematic/Rigidbody injection is incomplete.");
            }
            PhaseTwoFloorReset floorReset = ball.GetComponent<PhaseTwoFloorReset>();
            if (!floorReset || floorReset.Controller != controller)
                failures.Add("Practice orb immediate floor-contact reset is missing or not wired.");
            int handInteractables = ball.GetComponentsInChildren<HandGrabInteractable>(true).Length;
            int controllerInteractables = ball.GetComponentsInChildren<GrabInteractable>(true).Length;
            if (handInteractables != 1 || controllerInteractables != 1)
                failures.Add($"Practice orb interaction graph has {handInteractables} hand and " +
                             $"{controllerInteractables} controller grab interactables; expected one each.");
            int handInteractors = roots.Sum(root => root.GetComponentsInChildren<HandGrabInteractor>(true).Length);
            int grabInteractors = roots.Sum(root => root.GetComponentsInChildren<GrabInteractor>(true).Length);
            if (handInteractors < 2 || grabInteractors < 2)
                failures.Add("The comprehensive rig does not expose grab interactors for both sides/devices.");
            OVRHand[] trackedHands = roots.SelectMany(root => root.GetComponentsInChildren<OVRHand>(true)).ToArray();
            bool hasLeftOvrHand = trackedHands.Any(hand =>
            {
                OVRSkeleton.SkeletonType type =
                    ((OVRSkeleton.IOVRSkeletonDataProvider)hand).GetSkeletonType();
                return type == OVRSkeleton.SkeletonType.HandLeft ||
                       type == OVRSkeleton.SkeletonType.XRHandLeft;
            });
            bool hasRightOvrHand = trackedHands.Any(hand =>
            {
                OVRSkeleton.SkeletonType type =
                    ((OVRSkeleton.IOVRSkeletonDataProvider)hand).GetSkeletonType();
                return type == OVRSkeleton.SkeletonType.HandRight ||
                       type == OVRSkeleton.SkeletonType.XRHandRight;
            });
            if (!hasLeftOvrHand || !hasRightOvrHand)
                failures.Add("The practice reset fallback requires existing left and right OVRHand sources.");
            Hand[] poseHands = roots.SelectMany(root => root.GetComponentsInChildren<Hand>(true)).ToArray();
            if (!poseHands.Any(hand => hand.Handedness == Handedness.Left) ||
                !poseHands.Any(hand => hand.Handedness == Handedness.Right))
                failures.Add("The practice reset fallback requires world-pose hands for both sides.");

            Transform gate = controller.GateCenter;
            if (gate && (Vector3.Distance(gate.position, new Vector3(0f, 1.4f, 2.6f)) > 0.01f ||
                         gate.childCount != 4))
                failures.Add("Gate frame position or four-piece structure is incorrect.");
            Transform pedestal = roots.SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(transform => transform.name == "Pedestal");
            if (!pedestal || !pedestal.TryGetComponent(out Collider pedestalCollider) || pedestalCollider.isTrigger)
                failures.Add("Reachable pedestal collision is missing.");
            if (pedestal)
            {
                PokeInteractable handSurface = pedestal.parent
                    .GetComponentsInChildren<PokeInteractable>(true)
                    .FirstOrDefault(item => item.name == "Pedestal Hand Surface");
                if (!handSurface || handSurface.GetComponent<ClippedPlaneSurface>() == null)
                    failures.Add("Pedestal hand-limiting surface is missing.");
            }
        }
    }
}
