using System;
using System.IO;
using System.Linq;
using Sensei.Unity;
using Sensei.App;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Sensei.Editor
{
    public static class BuildScript
    {
        public static void BuildAndroid()
        {
            try
            {
                ConfigurePlayer();
                ConfigureXr();
                EnsureMaterials();
                EnsureBootScene();
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory("Build");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/Boot.unity" },
                    locationPathName = "Build/RedPandaSensei.apk",
                    target = BuildTarget.Android,
                    options = BuildOptions.Development
                });
                if (report.summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"Android build: {report.summary.result}, errors={report.summary.totalErrors}");
                Debug.Log($"RPS_BUILD succeeded size={report.summary.totalSize} duration={report.summary.totalTime.TotalSeconds:F1}s");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Supercapybara";
            PlayerSettings.productName = "Red Panda Sensei";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.supercapybara.redpandasensei");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.runInBackground = true;
            PlayerSettings.assemblyVersionValidation = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = settings.FindProperty("activeInputHandler");
            if (input != null) { input.intValue = 1; settings.ApplyModifiedPropertiesWithoutUndo(); }
        }

        private static void ConfigureXr()
        {
            Directory.CreateDirectory("Assets/XR");
            AssetDatabase.Refresh();
            const string path = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
            var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, path);
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
            if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = true;
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android))
                throw new BuildFailedException("Could not assign Android OpenXR loader.");
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            OpenXRFeatureSetManager.InitializeFeatureSets();
            var meta = OpenXRFeatureSetManager.GetFeatureSetWithId(BuildTargetGroup.Android, "com.meta.openxr.featureset.metaxr");
            if (meta == null) throw new BuildFailedException("Meta XR feature group not found.");
            meta.isEnabled = true;
            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(BuildTargetGroup.Android);
            var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var quest = xr.GetFeature<MetaQuestFeature>();
            if (quest == null) throw new BuildFailedException("Meta Quest support feature not found.");
            quest.enabled = true;
            var touch = xr.GetFeature<OculusTouchControllerProfile>();
            if (touch == null) throw new BuildFailedException("Oculus Touch interaction profile not found.");
            touch.enabled = true;
            EditorUtility.SetDirty(touch);
            xr.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            var config = OVRProjectConfig.CachedProjectConfig;
            // RULING(Handler 2026-10-28): allow launch with sleeping controllers; hand tracking is optional.
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
            config.targetDeviceTypes = new System.Collections.Generic.List<OVRProjectConfig.DeviceType> { OVRProjectConfig.DeviceType.Quest3 };
            OVRProjectConfig.CommitProjectConfig(config);
            EditorUtility.SetDirty(xr);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureMaterials()
        {
            Directory.CreateDirectory("Assets/Art/Resources");
            AssetDatabase.Refresh();
            foreach (var entry in new[] { ("SenseiGhost", "Sensei/GhostFade"), ("SenseiCard", "Sensei/Card"), ("SenseiLine", "Unlit/Color") })
            {
                var path = "Assets/Art/Resources/" + entry.Item1 + ".mat";
                var shader = Shader.Find(entry.Item2);
                if (shader == null) throw new BuildFailedException("Missing shader " + entry.Item2);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) AssetDatabase.CreateAsset(new Material(shader), path);
                else if (material.shader != shader) { material.shader = shader; EditorUtility.SetDirty(material); }
            }
        }

        private static void EnsureBootScene()
        {
            const string scenePath = "Assets/Scenes/Boot.unity";
            if (File.Exists(scenePath))
            {
                EditorSceneManager.OpenScene(scenePath);
            }
            else
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var root = new GameObject("XR Origin");
                var rig = root.AddComponent<OVRCameraRig>();
                var manager = root.AddComponent<OVRManager>();
                manager.isInsightPassthroughEnabled = true;
                manager.trackingOriginType = OVRManager.TrackingOrigin.EyeLevel;
                root.AddComponent<OVRPassthroughLayer>(); // Meta Core creates an underlay.
                foreach (var camera in root.GetComponentsInChildren<Camera>())
                {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.clear;
                    camera.nearClipPlane = 0.1f;
                    camera.farClipPlane = 100f;
                }
                rig.centerEyeAnchor.GetComponent<Camera>().tag = "MainCamera";
                var textObject = new GameObject("Smoke Status", typeof(TextMeshPro));
                textObject.transform.SetParent(rig.centerEyeAnchor, false);
                textObject.transform.localPosition = new Vector3(0, 0, 1.5f);
                var text = textObject.GetComponent<TextMeshPro>();
                text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
                if (text.font == null) throw new BuildFailedException("TMP essential font was not imported.");
                text.rectTransform.sizeDelta = new Vector2(1.65f, 0.6f);
                text.fontSize = 1.2f;
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.white;
                text.outlineColor = Color.black;
                text.outlineWidth = 0.3f;
                text.text = "RPS_SMOKE starting";
                var bootstrap = new GameObject("Smoke Bootstrap").AddComponent<SmokeBootstrap>();
                var serialized = new SerializedObject(bootstrap);
                serialized.FindProperty("label").objectReferenceValue = text;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Directory.CreateDirectory("Assets/Scenes");
                EditorSceneManager.SaveScene(scene, scenePath);
            }
            var smoke = UnityEngine.Object.FindFirstObjectByType<SmokeBootstrap>();
            if (smoke != null) UnityEngine.Object.DestroyImmediate(smoke.gameObject);
            if (UnityEngine.Object.FindFirstObjectByType<EngineHost>() == null)
                new GameObject("Engine Host").AddComponent<EngineHost>();
            var sceneRig = UnityEngine.Object.FindFirstObjectByType<OVRCameraRig>();
            if (sceneRig != null) sceneRig.GetComponent<OVRManager>().trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        }
    }
}
