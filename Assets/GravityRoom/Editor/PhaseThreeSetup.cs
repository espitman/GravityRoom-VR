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
        private const float GravityAcceleration = 9.81f;
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
                    text.text = "Gravity pauses while held and resumes on release.\nA / X selects Down, Left, or Right.";
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
            PhaseThreeGravityController controller = gravityRoot.AddComponent<PhaseThreeGravityController>();
            controller.Configure(gravityBody, modeText);
            EditorUtility.SetDirty(controller);
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
                    failures.Add("The orb must start in Down mode at 9.81 m/s².");
                if (!GravityRules.ShouldApply(gravity.Body, false) || GravityRules.ShouldApply(gravity.Body, true))
                    failures.Add("Gravity must apply while free and be suppressed while selected.");
            }
            if (selectors.Length == 1 &&
                (selectors[0].GravityBody != gravity || selectors[0].ModeText == null))
                failures.Add("The manual gravity selector references are incomplete.");

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
