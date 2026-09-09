using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GravityRoom;
using Meta.XR;
using Oculus.Interaction.OVR.Editor.QuickActions;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using Object = UnityEngine.Object;

namespace GravityRoom.Editor
{
    /// <summary>Repeatable editor entry points for the Phase 1 Quest smoke-test scene.</summary>
    public static class PhaseOneSetup
    {
        private const string ScenePath = "Assets/GravityRoom/Scenes/PhaseOne.unity";
        private const string MobilePipelinePath = "Assets/Settings/Mobile_RPAsset.asset";
        private const string MaterialFolder = "Assets/GravityRoom/Materials";
        private const string XrSettingsFolder = "Assets/XR";
        private const string XrSettingsPath = XrSettingsFolder + "/XRGeneralSettingsPerBuildTarget.asset";
        private const string ApkPath = "Builds/Android/GravityRoom-Phase1.apk";
        private const string BuildLogPath = "Logs/GravityRoom-Phase1-build.txt";
        private const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";

        private static readonly string[] RequiredFeatureIds =
        {
            MetaQuestFeature.featureId,
            OculusTouchControllerProfile.featureId,
            MetaQuestTouchPlusControllerProfile.featureId,
            MetaXRFeature.featureId
        };

        /// <summary>Configures rendering, Android, XR, and creates the Phase 1 scene when absent.</summary>
        public static void Configure()
        {
            ConfigureRendering();
            ConfigureAndroid();
            ConfigureXr();
            // The comprehensive rig obtains controller-driven fingers through OVRHand.
            // That path still requires the hand-tracking manifest feature and permission.
            var metaConfig = OVRProjectConfig.CachedProjectConfig;
            metaConfig.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            OVRProjectConfig.CommitProjectConfig(metaConfig);

            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                CreateScene();
            else
            {
                Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                DisableLocomotionObjects();
                ConfigureDiagnosticLabels();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }

            EnsureSceneIsEnabled();
            AssetDatabase.SaveAssets();
            Debug.Log("[GravityRoom] Phase 1 configuration complete.");
        }

        /// <summary>Validates all Phase 1 build settings and scene invariants.</summary>
        public static void Validate()
        {
            var failures = new List<string>();
            ValidateRendering(failures);
            ValidateAndroid(failures);
            ValidateXr(failures);
            ValidateScene(failures);

            if (failures.Count > 0)
                throw new BuildFailedException("GravityRoom Phase 1 validation failed:\n - " +
                                               string.Join("\n - ", failures));

            Debug.Log("[GravityRoom] Phase 1 validation passed.");
        }

        /// <summary>Validates and creates a debug-signed development APK for Android.</summary>
        public static void BuildAndroid()
        {
            ConfigureBundledAndroidTools();
            Validate();
            PlayerSettings.Android.useCustomKeystore = false;

            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath) ?? "Builds/Android");
            Directory.CreateDirectory(Path.GetDirectoryName(BuildLogPath) ?? "Logs");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            string text = $"GravityRoom Phase 1 Android build{Environment.NewLine}" +
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
                throw new BuildFailedException($"Android build {summary.result} with {summary.totalErrors} error(s). " +
                                               $"See {BuildLogPath}.");

            Debug.Log($"[GravityRoom] Development APK built at {ApkPath}. Summary: {BuildLogPath}");
        }

        private static void ConfigureRendering()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobilePipelinePath);
            if (!pipeline)
                throw new BuildFailedException($"Mobile URP asset was not found at {MobilePipelinePath}.");

            GraphicsSettings.defaultRenderPipeline = pipeline;
            int originalQuality = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(originalQuality, false);
        }

        private static void ConfigureAndroid()
        {
            ConfigureBundledAndroidTools();
            PlayerSettings.companyName = "espitman";
            PlayerSettings.productName = "Gravity Room";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.espitman.gravityroom");
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.Android.useCustomKeystore = false;

            Object playerSettings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            var serializedSettings = new SerializedObject(playerSettings);
            SerializedProperty inputHandler = serializedSettings.FindProperty("activeInputHandler");
            if (inputHandler == null)
                throw new BuildFailedException("PlayerSettings.activeInputHandler could not be serialized.");
            inputHandler.intValue = 1; // Input System Package (New)
            serializedSettings.FindProperty("useCustomBaseGradleTemplate").boolValue = true;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureBundledAndroidTools()
        {
            string root = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/AndroidPlayer");
            AndroidExternalToolsSettings.jdkRootPath = ResolveTool("UNITY_JDK_ROOT", AndroidExternalToolsSettings.jdkRootPath, Path.Combine(root, "OpenJDK"));
            AndroidExternalToolsSettings.sdkRootPath = ResolveTool("UNITY_SDK_ROOT", AndroidExternalToolsSettings.sdkRootPath, Path.Combine(root, "SDK"));
            AndroidExternalToolsSettings.ndkRootPath = ResolveTool("UNITY_NDK_ROOT", AndroidExternalToolsSettings.ndkRootPath, Path.Combine(root, "NDK"));
            AndroidExternalToolsSettings.gradlePath = ResolveTool("UNITY_GRADLE_ROOT", AndroidExternalToolsSettings.gradlePath, Path.Combine(root, "Tools/gradle"));
        }

        private static string ResolveTool(string variable, string configuredPath, string bundledPath)
        {
            string path = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(path)) path = Directory.Exists(configuredPath) ? configuredPath : bundledPath;
            if (!Directory.Exists(path)) throw new BuildFailedException($"Android tool missing: set {variable} to a valid installation.");
            return path;
        }

        private static void ConfigureXr()
        {
            XRGeneralSettings general = EnsureAndroidXrSettings();
            XRManagerSettings manager = general.AssignedSettings;
            if (!XRPackageMetadataStore.AssignLoader(manager, OpenXrLoaderType, BuildTargetGroup.Android))
                throw new BuildFailedException("Could not assign the Android OpenXR loader.");

            XRLoader openXrLoader = manager.activeLoaders.FirstOrDefault(loader =>
                loader != null && loader.GetType().FullName == OpenXrLoaderType);
            if (!openXrLoader || !manager.TrySetLoaders(new List<XRLoader> { openXrLoader }))
                throw new BuildFailedException("Could not make OpenXR the sole Android XR loader.");

            general.InitManagerOnStart = true;
            manager.automaticLoading = true;
            manager.automaticRunning = true;
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(manager);

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            foreach (string featureId in RequiredFeatureIds)
            {
                OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
                if (!feature)
                    throw new BuildFailedException($"Required Android OpenXR feature is unavailable: {featureId}. " +
                                                   "Wait for the Meta XR packages to finish importing, then run Configure again.");
                feature.enabled = true;
                EditorUtility.SetDirty(feature);
            }
        }

        private static XRGeneralSettings EnsureAndroidXrSettings()
        {
            XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (general != null && general.AssignedSettings != null)
                return general;

            EnsureAssetFolder(XrSettingsFolder);
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perTarget);
            if (!perTarget)
                perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
            if (!perTarget)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, XrSettingsPath);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);

            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            return perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        }

        private static void CreateScene()
        {
            EnsureAssetFolder("Assets/GravityRoom/Scenes");
            EnsureAssetFolder(MaterialFolder);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material floor = GetOrCreateMaterial("Floor", new Color(0.16f, 0.19f, 0.23f));
            Material wall = GetOrCreateMaterial("Walls", new Color(0.72f, 0.76f, 0.82f));
            Material reference = GetOrCreateMaterial("ReferenceCube", new Color(0.08f, 0.55f, 0.82f));

            CreateBox("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(6f, 0.1f, 6f), floor);
            CreateBox("Wall North", new Vector3(0f, 1.25f, 3f), new Vector3(6f, 2.5f, 0.1f), wall);
            CreateBox("Wall South", new Vector3(0f, 1.25f, -3f), new Vector3(6f, 2.5f, 0.1f), wall);
            CreateBox("Wall East", new Vector3(3f, 1.25f, 0f), new Vector3(0.1f, 2.5f, 6f), wall);
            CreateBox("Wall West", new Vector3(-3f, 1.25f, 0f), new Vector3(0.1f, 2.5f, 6f), wall);
            CreateBox("Standing Reference (1m)", new Vector3(1.35f, 0.5f, 1.25f), Vector3.one, reference);

            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            OVRQuickActionsAPI.AddOVRInteractionRig(false);
            DisableLocomotionObjects();

            CreateText("Title", "GRAVITY ROOM — PHASE 1", new Vector3(0f, 2.05f, 2.88f), 0.025f);
            CreateText("Instructions", "Stationary test — stay at the room center.\nMove your head and both Touch controllers.",
                new Vector3(0f, 1.65f, 2.88f), 0.012f);
            TextMesh status = CreateText("Live XR Status", "XR: WAITING\nHead: NOT TRACKED\nLeft: NOT TRACKED\nRight: NOT TRACKED",
                new Vector3(0f, 1.08f, 2.88f), 0.012f);
            PhaseOneDiagnostics diagnostics = status.gameObject.AddComponent<PhaseOneDiagnostics>();
            diagnostics.Configure(status);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new BuildFailedException($"Could not save {ScenePath}.");
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader)
                throw new BuildFailedException("Built-in URP/Lit shader was not found.");
            material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateBox(string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetPositionAndRotation(position, Quaternion.identity);
            box.transform.localScale = scale;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
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

        private static void DisableLocomotionObjects()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (IsLocomotionObject(child.name))
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                foreach (MonoBehaviour behaviour in child.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && IsLocomotionBehaviour(behaviour))
                        behaviour.enabled = false;
                }
            }
        }

        private static void ConfigureDiagnosticLabels()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (TextMesh text in root.GetComponentsInChildren<TextMesh>(true))
            {
                if (text.name == "Title") text.characterSize = 0.025f;
                if (text.name == "Instructions" || text.name == "Live XR Status") text.characterSize = 0.012f;
            }
        }

        private static void EnsureSceneIsEnabled()
        {
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static bool IsLocomotionObject(string name) =>
            name.IndexOf("Locomotion", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.Equals("Locomotor", StringComparison.OrdinalIgnoreCase);

        private static void ValidateRendering(ICollection<string> failures)
        {
            RenderPipelineAsset expected = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(MobilePipelinePath);
            if (!expected)
                failures.Add($"Mobile URP asset is missing at {MobilePipelinePath}.");
            else
            {
                if (GraphicsSettings.defaultRenderPipeline != expected)
                    failures.Add("GraphicsSettings.defaultRenderPipeline is not the mobile URP asset.");
                int originalQuality = QualitySettings.GetQualityLevel();
                for (int i = 0; i < QualitySettings.names.Length; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    if (QualitySettings.renderPipeline != expected)
                        failures.Add($"Quality level '{QualitySettings.names[i]}' does not use the mobile URP asset.");
                }
                QualitySettings.SetQualityLevel(originalQuality, false);
            }
        }

        private static void ValidateAndroid(ICollection<string> failures)
        {
            if (PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) != "com.espitman.gravityroom")
                failures.Add("Android application identifier must match the deployment script.");
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) != ScriptingImplementation.IL2CPP)
                failures.Add("Android scripting backend is not IL2CPP.");
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                failures.Add("Android architecture is not ARM64-only.");
            if (PlayerSettings.Android.minSdkVersion != AndroidSdkVersions.AndroidApiLevel29)
                failures.Add("Android minimum API is not 29.");
            if (PlayerSettings.Android.targetSdkVersion != AndroidSdkVersions.AndroidApiLevel35)
                failures.Add("Android target API is not 35.");
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                failures.Add("Color space is not Linear.");
            GraphicsDeviceType[] graphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) ||
                graphicsApis.Length != 1 || graphicsApis[0] != GraphicsDeviceType.Vulkan)
                failures.Add("Android graphics API is not Vulkan-only.");

            var serializedSettings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            SerializedProperty inputHandler = serializedSettings.FindProperty("activeInputHandler");
            if (inputHandler == null || inputHandler.intValue != 1)
                failures.Add("Active input handling is not Input System Package (New).");
        }

        private static void ValidateXr(ICollection<string> failures)
        {
            if (OVRProjectConfig.CachedProjectConfig.handTrackingSupport !=
                OVRProjectConfig.HandTrackingSupport.ControllersAndHands)
                failures.Add("Controller-driven hands require ControllersAndHands manifest support.");
            XRGeneralSettings general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            XRManagerSettings manager = general != null ? general.AssignedSettings : null;
            if (general == null || manager == null)
            {
                failures.Add("Android XR Management settings are missing.");
                return;
            }

            int openXrLoaders = manager.activeLoaders.Count(loader =>
                loader != null && loader.GetType().FullName == OpenXrLoaderType);
            if (openXrLoaders != 1 || manager.activeLoaders.Count != 1)
                failures.Add("OpenXR is not the sole active Android XR loader.");
            if (!general.InitManagerOnStart || !manager.automaticLoading || !manager.automaticRunning)
                failures.Add("Android XR automatic initialization and startup are not enabled.");

            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (!settings)
            {
                failures.Add("Android OpenXR settings are missing.");
                return;
            }
            foreach (string featureId in RequiredFeatureIds)
            {
                OpenXRFeature feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);
                if (!feature || !feature.enabled)
                    failures.Add($"Required Android OpenXR feature is unavailable or disabled: {featureId}.");
            }
        }

        private static void ValidateScene(ICollection<string> failures)
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
            {
                failures.Add($"Phase 1 scene is missing at {ScenePath}.");
                return;
            }
            if (!EditorBuildSettings.scenes.Any(scene => scene.enabled && scene.path == ScenePath))
                failures.Add("Phase 1 scene is not enabled in Build Settings.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject[] roots = scene.GetRootGameObjects();
            int cameraRigs = roots.Sum(root => root.GetComponentsInChildren<OVRCameraRig>(true).Length);
            if (cameraRigs != 1)
                failures.Add($"Phase 1 scene contains {cameraRigs} OVRCameraRig components; expected exactly one.");

            PhaseOneDiagnostics[] diagnosticComponents = roots
                .SelectMany(root => root.GetComponentsInChildren<PhaseOneDiagnostics>(true)).ToArray();
            int diagnostics = diagnosticComponents.Length;
            if (diagnostics != 1)
                failures.Add($"Phase 1 scene contains {diagnostics} PhaseOneDiagnostics components; expected exactly one.");
            else
            {
                var serializedDiagnostics = new SerializedObject(diagnosticComponents[0]);
                SerializedProperty statusText = serializedDiagnostics.FindProperty("statusText");
                if (statusText == null || statusText.objectReferenceValue == null)
                    failures.Add("PhaseOneDiagnostics does not have its status TextMesh assigned.");
            }

            foreach (GameObject root in roots)
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.GetComponents<Component>().Any(component => component == null))
                    failures.Add($"Missing script found on '{GetHierarchyPath(child)}'.");
                if (child.gameObject.activeInHierarchy &&
                    IsLocomotionObject(child.name))
                    failures.Add($"Locomotion object is active: '{GetHierarchyPath(child)}'.");
                foreach (MonoBehaviour behaviour in child.GetComponents<MonoBehaviour>())
                {
                    if (behaviour != null && behaviour.enabled && child.gameObject.activeInHierarchy &&
                        IsLocomotionBehaviour(behaviour))
                        failures.Add($"Locomotion behaviour is enabled: '{GetHierarchyPath(child)}/{behaviour.GetType().Name}'.");
                }
            }
        }

        private static bool IsLocomotionBehaviour(MonoBehaviour behaviour)
        {
            string typeNamespace = behaviour.GetType().Namespace;
            return typeNamespace != null &&
                   (typeNamespace == "Oculus.Interaction.Locomotion" ||
                    typeNamespace.StartsWith("Oculus.Interaction.Locomotion.", StringComparison.Ordinal));
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static void EnsureAssetFolder(string path)
        {
            string current = "Assets";
            foreach (string part in path.Split('/').Skip(1))
            {
                string next = current + "/" + part;
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, part);
                current = next;
            }
        }
    }
}
