using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GravityRoom;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GravityRoom.Editor
{
    /// <summary>Repeatable Phase 3 scene creation, validation, and Android build entry points.</summary>
    public static class PhaseThreeSetup
    {
        private const string PhaseOneScenePath = "Assets/GravityRoom/Scenes/PhaseOne.unity";
        private const string PhaseTwoScenePath = "Assets/GravityRoom/Scenes/PhaseTwo.unity";
        private const string ScenePath = "Assets/GravityRoom/Scenes/PhaseThree.unity";
        private const string ApkPath = "Builds/Android/GravityRoom-Phase3.apk";
        private const string BuildLogPath = "Logs/GravityRoom-Phase3-build.txt";
        private const float GravityAcceleration = 2.5f;
        private const float GravityChangeInterval = 12f;
        private const float GravityCountdownDuration = 3f;
        private static readonly string[] RoomPartNames =
            { "Floor", "Wall North", "Wall South", "Wall East", "Wall West" };

        public static void Configure()
        {
            // Phase 2 remains the source of truth for the accepted rig, interaction, gate, and reset behavior.
            PhaseTwoSetup.Configure();
            PhaseTwoSetup.Validate();

            File.Copy(PhaseTwoScenePath, ScenePath, true);
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ConfigureCopiedLabels(scene);
            AddDirectionalGravity(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new BuildFailedException($"Could not save {ScenePath}.");
            EnsureBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log("[GravityRoom] Phase 3 configuration complete; Phase 2 remains unchanged.");
        }

        public static void Validate()
        {
            // This also verifies the inherited hand/controller graph, gate logic, floor reset, and alignment.
            PhaseTwoSetup.Validate();
            FixedSceneSnapshot baseline = CaptureFixedScene(SceneManager.GetActiveScene());
            var failures = new List<string>();
            ValidateGravityRules(failures);
            ValidateScene(baseline, failures);
            if (failures.Count == 0)
                PhaseThreePhysicsChecks.Validate(SceneManager.GetActiveScene(), failures);
            if (failures.Count > 0)
                throw new BuildFailedException("GravityRoom Phase 3 validation failed:\n - " +
                                               string.Join("\n - ", failures));
            Debug.Log("[GravityRoom] Phase 3 validation passed, including selection/release and directional physics checks.");
        }

        public static void BuildAndroid()
        {
            PhaseOneSetup.ConfigureBundledAndroidTools();
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath) ?? "Builds/Android");
            Directory.CreateDirectory(Path.GetDirectoryName(BuildLogPath) ?? "Logs");
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            });
            BuildSummary summary = report.summary;
            string text = $"GravityRoom Phase 3 Android Development build{Environment.NewLine}" +
                          $"Result: {summary.result}{Environment.NewLine}" +
                          $"Output: {summary.outputPath}{Environment.NewLine}" +
                          $"Started: {summary.buildStartedAt:O}{Environment.NewLine}" +
                          $"Ended: {summary.buildEndedAt:O}{Environment.NewLine}" +
                          $"Duration: {summary.totalTime}{Environment.NewLine}" +
                          $"Size: {summary.totalSize} bytes{Environment.NewLine}" +
                          $"Warnings: {summary.totalWarnings}{Environment.NewLine}" +
                          $"Errors: {summary.totalErrors}{Environment.NewLine}";
            File.WriteAllText(BuildLogPath, text);
            AssetDatabase.Refresh();
            if (summary.result != BuildResult.Succeeded || summary.totalErrors > 0)
                throw new BuildFailedException($"Android Development build {summary.result} with " +
                                               $"{summary.totalErrors} error(s). See {BuildLogPath}.");
            Debug.Log($"[GravityRoom] Development APK built at {ApkPath}. Summary: {BuildLogPath}");
        }

        private static void ConfigureCopiedLabels(Scene scene)
        {
            foreach (TextMesh text in AllComponents<TextMesh>(scene))
            {
                if (text.name == "Title")
                    text.text = "GRAVITY ROOM — PHASE 3";
                else if (text.name == "Instructions")
                    text.text = "Follow the arrow; gravity changes automatically.\nFar orb: palm up + index pinch for 1 second.";
                EditorUtility.SetDirty(text);
            }

            Transform practiceRoot = AllComponents<Transform>(scene)
                .FirstOrDefault(item => item.name == "Phase 2 Practice");
            if (practiceRoot != null) practiceRoot.name = "Phase 3 Practice";
        }

        private static void AddDirectionalGravity(Scene scene)
        {
            PhaseTwoPracticeController practice = AllComponents<PhaseTwoPracticeController>(scene).Single();
            Rigidbody orb = practice.Ball;
            DirectionalGravityBody oldGravity = orb.GetComponent<DirectionalGravityBody>();
            if (oldGravity != null) UnityEngine.Object.DestroyImmediate(oldGravity);
            PhaseThreeGravityController oldController = AllComponents<PhaseThreeGravityController>(scene)
                .FirstOrDefault();
            if (oldController != null) UnityEngine.Object.DestroyImmediate(oldController.gameObject);

            DirectionalGravityBody gravityBody = orb.gameObject.AddComponent<DirectionalGravityBody>();
            gravityBody.Configure(orb, practice.Grabbable, GravityMode.Down, GravityAcceleration);
            EditorUtility.SetDirty(gravityBody);

            var gravityRoot = new GameObject("Phase 3 Gravity System");
            TextMesh modeText = CreateText("Gravity Status", "", new Vector3(0f, 1.55f, 2.88f), 0.01f);
            modeText.transform.SetParent(gravityRoot.transform, true);
            Shader textShader = Shader.Find("GravityRoom/DepthTestedText");
            if (!textShader) throw new BuildFailedException("Depth-tested room text shader is missing.");
            modeText.gameObject.AddComponent<DepthTestedRoomText>().Configure(textShader);

            Material arrowMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/GravityRoom/Materials/Phase2Gate.mat");
            if (!arrowMaterial) throw new BuildFailedException("Phase 2 gate material is missing.");
            Transform arrow = CreateDirectionArrow(gravityRoot.transform, arrowMaterial, out Renderer[] renderers);
            AudioSource warningAudio = gravityRoot.AddComponent<AudioSource>();
            warningAudio.playOnAwake = false;
            warningAudio.loop = false;
            warningAudio.mute = true;
            warningAudio.spatialBlend = 1f;
            warningAudio.rolloffMode = AudioRolloffMode.Linear;
            warningAudio.minDistance = 0.75f;
            warningAudio.maxDistance = 8f;
            warningAudio.volume = 0.8f;
            PhaseThreeGravityController controller = gravityRoot.AddComponent<PhaseThreeGravityController>();
            controller.Configure(gravityBody, modeText, arrow, renderers, warningAudio,
                GravityAcceleration, GravityChangeInterval, GravityCountdownDuration);
            EditorUtility.SetDirty(controller);
        }

        private static Transform CreateDirectionArrow(Transform parent, Material material,
            out Renderer[] renderers)
        {
            var root = new GameObject("Gravity Direction Arrow");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(new Vector3(-1.75f, 1.42f, 2.84f), Quaternion.identity);

            Renderer shaft = CreateArrowPart("Arrow Shaft", root.transform,
                new Vector3(0f, 0.13f, 0f), new Vector3(0.14f, 0.5f, 0.055f), 0f, material);
            Renderer headLeft = CreateArrowPart("Arrow Head Left", root.transform,
                new Vector3(-0.12f, -0.22f, 0f), new Vector3(0.12f, 0.36f, 0.055f), -45f, material);
            Renderer headRight = CreateArrowPart("Arrow Head Right", root.transform,
                new Vector3(0.12f, -0.22f, 0f), new Vector3(0.12f, 0.36f, 0.055f), 45f, material);
            renderers = new[] { shaft, headLeft, headRight };
            return root.transform;
        }

        private static Renderer CreateArrowPart(string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, float zRotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(0f, 0f, zRotation);
            part.transform.localScale = localScale;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return renderer;
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

        private static void EnsureBuildScenes()
        {
            var scenes = EditorBuildSettings.scenes.Where(item => item.path != ScenePath).ToList();
            int phaseTwoIndex = scenes.FindIndex(item => item.path == PhaseTwoScenePath);
            int insertion = phaseTwoIndex >= 0 ? phaseTwoIndex + 1 : scenes.Count;
            scenes.Insert(insertion, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void ValidateGravityRules(ICollection<string> failures)
        {
            if (GravityRules.GetDirection(GravityMode.Down) != Vector3.down ||
                GravityRules.GetDirection(GravityMode.Left) != Vector3.left ||
                GravityRules.GetDirection(GravityMode.Right) != Vector3.right)
                failures.Add("Down/Left/Right gravity direction mapping is incorrect.");
            if (GravityRules.GetAcceleration(GravityMode.Left, -1f) != Vector3.zero)
                failures.Add("Gravity acceleration must not become negative.");
            if (GravityCycleRules.Next(GravityMode.Down) != GravityMode.Left ||
                GravityCycleRules.Next(GravityMode.Left) != GravityMode.Right ||
                GravityCycleRules.Next(GravityMode.Right) != GravityMode.Down ||
                GravityCycleRules.Previous(GravityMode.Down) != GravityMode.Right ||
                GravityCycleRules.Previous(GravityMode.Right) != GravityMode.Left ||
                GravityCycleRules.Previous(GravityMode.Left) != GravityMode.Down)
                failures.Add("Automatic/manual gravity cycling is not deterministic Down/Left/Right.");
            if (GravityCycleRules.CountdownNumber(3f, GravityCountdownDuration) != 3 ||
                GravityCycleRules.CountdownNumber(2f, GravityCountdownDuration) != 2 ||
                GravityCycleRules.CountdownNumber(1f, GravityCountdownDuration) != 1 ||
                GravityCycleRules.CountdownNumber(3.01f, GravityCountdownDuration) != 0)
                failures.Add("The visual warning must count down 3, 2, 1 before a change.");
            if (GravityCycleRules.ArrowAngle(GravityMode.Down) != 0f ||
                GravityCycleRules.ArrowAngle(GravityMode.Left) != -90f ||
                GravityCycleRules.ArrowAngle(GravityMode.Right) != 90f)
                failures.Add("The world-space arrow rotations do not match the gravity directions.");
        }

        private static void ValidateScene(FixedSceneSnapshot baseline, ICollection<string> failures)
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            {
                failures.Add($"Phase 3 scene is missing at {ScenePath}.");
                return;
            }
            foreach (string required in new[] { PhaseOneScenePath, PhaseTwoScenePath, ScenePath })
                if (!EditorBuildSettings.scenes.Any(item => item.enabled && item.path == required))
                    failures.Add($"Required scene is not enabled in Build Settings: {required}.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            FixedSceneSnapshot phaseThree = CaptureFixedScene(scene);
            baseline.CompareTo(phaseThree, failures);

            OVRCameraRig[] rigs = AllComponents<OVRCameraRig>(scene).ToArray();
            if (rigs.Length != 1)
                failures.Add($"Phase 3 contains {rigs.Length} OVRCameraRig components; expected one.");
            else if (rigs[0].GetComponentsInChildren<DirectionalGravityBody>(true).Length != 0)
                failures.Add("The OVRCameraRig hierarchy must never opt into object gravity.");

            DirectionalGravityBody[] gravityBodies = AllComponents<DirectionalGravityBody>(scene).ToArray();
            PhaseThreeGravityController[] selectors = AllComponents<PhaseThreeGravityController>(scene).ToArray();
            PhaseTwoPracticeController[] practices = AllComponents<PhaseTwoPracticeController>(scene).ToArray();
            if (gravityBodies.Length != 1)
                failures.Add($"Phase 3 contains {gravityBodies.Length} opted-in gravity bodies; expected one orb.");
            if (selectors.Length != 1)
                failures.Add($"Phase 3 contains {selectors.Length} gravity mode controllers; expected one.");
            if (practices.Length != 1)
            {
                failures.Add($"Phase 3 contains {practices.Length} inherited practice controllers; expected one.");
                return;
            }

            PhaseTwoPracticeController practice = practices[0];
            DirectionalGravityBody gravity = gravityBodies.FirstOrDefault();
            if (gravity == null || gravity.Body != practice.Ball || gravity.Grabbable != practice.Grabbable)
                failures.Add("The dedicated gravity component is not wired to the inherited orb and Grabbable.");
            else
            {
                if (gravity.Body.useGravity)
                    failures.Add("The opted-in orb still uses global Rigidbody gravity.");
                if (gravity.Mode != GravityMode.Down ||
                    Mathf.Abs(gravity.Acceleration - GravityAcceleration) > 0.001f)
                    failures.Add("The orb must start in Down mode at 2.5 m/s².");
                if (gravity.IsGravityArmed ||
                    GravityRules.ShouldApply(gravity.Body, false, gravity.IsGravityArmed) ||
                    GravityRules.ShouldApply(gravity.Body, true, true))
                    failures.Add("Gravity must wait for a release and remain suppressed while selected.");
            }
            if (selectors.Length == 1)
            {
                PhaseThreeGravityController controller = selectors[0];
                if (controller.GravityBody != gravity || controller.ModeText == null ||
                    controller.DirectionArrow == null || controller.WarningAudioSource == null)
                    failures.Add("The gravity controller feedback references are incomplete.");
                if (Mathf.Abs(controller.GravityStrength - GravityAcceleration) > 0.001f ||
                    Mathf.Abs(controller.ChangeInterval - GravityChangeInterval) > 0.001f ||
                    Mathf.Abs(controller.CountdownDuration - GravityCountdownDuration) > 0.001f)
                    failures.Add("Initial Quest gravity/timing settings do not match the Phase 3 defaults.");
                if (controller.DirectionArrow != null)
                {
                    if (Vector3.Distance(controller.DirectionArrow.position,
                            new Vector3(-1.75f, 1.42f, 2.84f)) > 0.0001f ||
                        Quaternion.Angle(controller.DirectionArrow.rotation, Quaternion.identity) > 0.001f)
                        failures.Add("The gravity arrow initial world-space placement is incorrect.");
                    if (controller.DirectionArrow.GetComponentsInChildren<Renderer>(true).Length != 3)
                        failures.Add("The world-space gravity arrow must contain exactly three visible parts.");
                    if (controller.DirectionArrow.GetComponentsInChildren<Collider>(true).Length != 0)
                        failures.Add("The gravity arrow must not add colliders to the room.");
                }
                AudioSource audio = controller.WarningAudioSource;
                if (audio != null && (audio.playOnAwake || audio.loop || !audio.mute || audio.spatialBlend < 0.99f ||
                                      audio.clip != null))
                    failures.Add("The muted procedural warning AudioSource settings are incorrect.");
            }

            foreach (string roomPartName in RoomPartNames)
            {
                Transform part = AllComponents<Transform>(scene)
                    .FirstOrDefault(item => item.name == roomPartName);
                if (part == null) continue;
                if (part.GetComponent<Rigidbody>() != null || part.GetComponent<DirectionalGravityBody>() != null)
                    failures.Add($"Fixed room part '{roomPartName}' must not be a gravity body.");
            }
        }

        private static FixedSceneSnapshot CaptureFixedScene(Scene scene)
        {
            var values = new Dictionary<string, TransformValue>();
            foreach (string name in RoomPartNames)
            {
                Transform item = AllComponents<Transform>(scene).FirstOrDefault(transform => transform.name == name);
                if (item != null) values[name] = new TransformValue(item);
            }
            OVRCameraRig rig = AllComponents<OVRCameraRig>(scene).SingleOrDefault();
            if (rig != null) values["OVRCameraRig"] = new TransformValue(rig.transform);
            return new FixedSceneSnapshot(values);
        }

        private static IEnumerable<T> AllComponents<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true));

        private readonly struct TransformValue
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public TransformValue(Transform transform)
            {
                Position = transform.position;
                Rotation = transform.rotation;
                Scale = transform.lossyScale;
            }
        }

        private sealed class FixedSceneSnapshot
        {
            private readonly IReadOnlyDictionary<string, TransformValue> values;

            public FixedSceneSnapshot(IReadOnlyDictionary<string, TransformValue> snapshotValues)
            {
                values = snapshotValues;
            }

            public void CompareTo(FixedSceneSnapshot other, ICollection<string> failures)
            {
                foreach (string name in RoomPartNames.Concat(new[] { "OVRCameraRig" }))
                {
                    if (!values.TryGetValue(name, out TransformValue expected) ||
                        !other.values.TryGetValue(name, out TransformValue actual))
                    {
                        failures.Add($"Fixed baseline object '{name}' is missing from Phase 2 or Phase 3.");
                        continue;
                    }
                    if (Vector3.Distance(expected.Position, actual.Position) > 0.0001f ||
                        Quaternion.Angle(expected.Rotation, actual.Rotation) > 0.001f ||
                        Vector3.Distance(expected.Scale, actual.Scale) > 0.0001f)
                        failures.Add($"Fixed baseline object '{name}' moved or changed scale in Phase 3.");
                }
            }
        }
    }
}
