using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildScript
{
    [MenuItem("Build/Build Android (XREAL)")]
    public static void BuildAndroid()
    {
        Debug.Log("=== Starting Android Build for XREAL Sample ===");

        string simpleARScenePath = "Assets/Scenes/SimpleARScene.unity";
        Debug.Log("Regenerating SimpleARScene.unity to ensure latest components...");
        CreateSimpleARScene.GenerateScene();

        string[] scenes = new string[] { simpleARScenePath };
        Debug.Log("Scenes to build: " + string.Join(", ", scenes));

        string outputDir = "Builds/Android";
        string outputPath = Path.Combine(outputDir, "AROthelloMaster.apk");
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        PerformAndroidBuild(scenes, outputPath);
    }

    [MenuItem("Build/Build Android (OpenCV Demo)")]
    public static void BuildAndroidOpenCvDemo()
    {
        Debug.Log("=== Starting Android Build for OpenCV Demo (XREAL) ===");

        string openCvScenePath = "Assets/OpenCV/Scenes/AndroidOpenCvDemoScene.unity";
        Debug.Log("Regenerating AndroidOpenCvDemoScene.unity...");
        CreateAndroidOpenCvDemoScene.GenerateScene();

        string[] scenes = new string[] { openCvScenePath };
        Debug.Log("Scenes to build: " + string.Join(", ", scenes));

        string outputDir = "Builds/Android";
        string outputPath = Path.Combine(outputDir, "AROthelloMaster-OpenCVDemo.apk");
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        PerformAndroidBuild(scenes, outputPath);
    }

    [MenuItem("Build/Build Android (AR Board Test)")]
    public static void BuildAndroidARBoardTest()
    {
        Debug.Log("=== Starting Android Build for AR Board Test (XREAL) ===");

        string arBoardTestScenePath = "Assets/OpenCV/Scenes/AROthelloBoardTestScene.unity";
        Debug.Log("Regenerating AROthelloBoardTestScene.unity...");
        CreateAROthelloBoardTestScene.GenerateScene();

        string[] scenes = new string[] { arBoardTestScenePath };
        Debug.Log("Scenes to build: " + string.Join(", ", scenes));

        string outputDir = "Builds/Android";
        string outputPath = Path.Combine(outputDir, "AROthelloMaster-ARBoardTest.apk");
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        PerformAndroidBuild(scenes, outputPath);
    }

    [MenuItem("Build/Build Android (Othello Auto Tracker)")]
    public static void BuildAndroidAutoTracker()
    {
        Debug.Log("=== Starting Android Build for Othello Auto Tracker (XREAL) ===");

        string autoTrackerScenePath = "Assets/Othello/Scenes/OthelloAutoTrackerScene.unity";
        Debug.Log("Regenerating OthelloAutoTrackerScene.unity...");
        CreateOthelloAutoTrackerScene.GenerateScene();

        string[] scenes = new string[] { autoTrackerScenePath };
        Debug.Log("Scenes to build: " + string.Join(", ", scenes));

        string outputDir = "Builds/Android";
        string outputPath = Path.Combine(outputDir, "AROthelloMaster-AutoTracker.apk");
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        PerformAndroidBuild(scenes, outputPath);
    }

    private static void PerformAndroidBuild(string[] scenes, string outputPath)
    {
        // Configure Android Player Settings for XREAL One Pro
#if UNITY_6000_0_OR_NEWER
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
#else
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
#endif
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
#if UNITY_6000_0_OR_NEWER
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
#endif

        // Ensure Graphics API is set to OpenGLES3
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new UnityEngine.Rendering.GraphicsDeviceType[]
        {
            UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3
        });

        // Ensure XREAL Settings
        var xrealSettings = Unity.XR.XREAL.XREALSettings.GetSettings();
        if (xrealSettings != null)
        {
            // Enable SupportMultiResume for dual-screen independent display (MRSpace)
            xrealSettings.SupportMultiResume = true;
            xrealSettings.StereoRendering = Unity.XR.XREAL.StereoRenderingMode.MultiPass;
            if (!xrealSettings.AddtionalPermissions.Contains("CAMERA"))
            {
                xrealSettings.AddtionalPermissions.Add("CAMERA");
            }
            EditorUtility.SetDirty(xrealSettings);
            Debug.Log("[BuildScript] XREALSettings SupportMultiResume=true, StereoRendering=MultiPass, CAMERA permission added.");
        }

        // Ensure XR General Settings
        var xrSettings = UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (xrSettings != null && xrSettings.Manager != null)
        {
            xrSettings.InitManagerOnStart = true;
            xrSettings.Manager.automaticLoading = true;
            xrSettings.Manager.automaticRunning = true;
            EditorUtility.SetDirty(xrSettings);
            EditorUtility.SetDirty(xrSettings.Manager);
            Debug.Log("[BuildScript] XRManagerSettings automaticLoading and automaticRunning set to true.");
        }

        AssetDatabase.SaveAssets();

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        Debug.Log($"=== Build Finished. Result: {summary.result}, Total Time: {summary.totalTime.TotalSeconds:F1}s, Output Size: {summary.totalSize} bytes ===");

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"SUCCESS: APK created at {outputPath}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        else
        {
            Debug.LogError($"FAILED: Build ended with result {summary.result}. Total Errors: {summary.totalErrors}");
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                    {
                        Debug.LogError($"[BuildStep: {step.name}] {msg.content}");
                    }
                }
            }
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
