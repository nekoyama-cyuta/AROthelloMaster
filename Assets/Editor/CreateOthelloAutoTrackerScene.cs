using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using AROthelloEditor;

public static class CreateOthelloAutoTrackerScene
{
    public const string SCENE_PATH = "Assets/Othello/Scenes/OthelloAutoTrackerScene.unity";

    [MenuItem("XREAL/Create Othello Auto Tracker Scene")]
    public static void GenerateScene()
    {
        Debug.Log("=== Generating Dedicated Othello Auto Tracker AR Scene ===");

        // 1. Ensure Directory
        string sceneDir = Path.GetDirectoryName(SCENE_PATH);
        if (!Directory.Exists(sceneDir))
        {
            Directory.CreateDirectory(sceneDir);
        }

        // 2. Create fresh empty scene
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 3. Directional Light
        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 1.0f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 4. Run standard setup to create XR Origin, Target, Tracker, Canvas, Logger, and Preview
        OthelloARDebugSetup.SetupScene();

        // 5. Save Scene
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        Debug.Log($"<color=#00FF88>SUCCESS: Generated and saved Othello Auto Tracker Scene at: {SCENE_PATH}</color>");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
