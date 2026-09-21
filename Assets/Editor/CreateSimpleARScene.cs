using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;

public static class CreateSimpleARScene
{
    [MenuItem("XREAL/Create Simple AR Scene")]
    public static void GenerateScene()
    {
        Debug.Log("=== Generating Clean Simple AR Scene with Camera-Locked HUD for XREAL ===");

        // 1. Create Directories
        string sceneDir = "Assets/Scenes";
        string matDir = "Assets/Materials";
        if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);
        if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);

        string scenePath = Path.Combine(sceneDir, "SimpleARScene.unity");

        // 2. Prepare Solid Unlit Materials as persistent assets so they are guaranteed in the build
        Material matCyan = GetOrCreateUnlitMaterial(Path.Combine(matDir, "AR_Cyan.mat"), new Color(0f, 1f, 1f, 1f));
        Material matYellow = GetOrCreateUnlitMaterial(Path.Combine(matDir, "AR_Yellow.mat"), new Color(1f, 1f, 0f, 1f));
        Material matMagenta = GetOrCreateUnlitMaterial(Path.Combine(matDir, "AR_Magenta.mat"), new Color(1f, 0f, 1f, 1f));
        Material matGreen = GetOrCreateUnlitMaterial(Path.Combine(matDir, "AR_Green.mat"), new Color(0.2f, 1f, 0.2f, 1f));

        // 3. Create a fresh empty scene
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 4. Directional Light (neutral bright)
        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 1.0f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 5. Setup XR Origin (XR Rig) hierarchy
        GameObject xrOriginGo = new GameObject("XR Origin (XR Rig)");
        XROrigin xrOrigin = xrOriginGo.AddComponent<XROrigin>();

        GameObject cameraOffsetGo = new GameObject("Camera Offset");
        cameraOffsetGo.transform.SetParent(xrOriginGo.transform, false);

        GameObject mainCameraGo = new GameObject("Main Camera");
        mainCameraGo.transform.SetParent(cameraOffsetGo.transform, false);
        mainCameraGo.tag = "MainCamera";

        Camera camera = mainCameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // Transparent for AR
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 1000f;
        camera.cullingMask = -1; // Render everything

        mainCameraGo.AddComponent<AudioListener>();
        mainCameraGo.AddComponent<UniversalAdditionalCameraData>();

        // Add XREALCameraPoseTracker for direct XRNode tracking (eliminates unbound Input System issues)
        mainCameraGo.AddComponent<XREALCameraPoseTracker>();

        xrOrigin.Camera = camera;
        xrOrigin.CameraFloorOffsetObject = cameraOffsetGo;
        xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

        // 6. [CAMERA-LOCKED / HEAD-LOCKED HUD]
        // This is attached directly to the Main Camera, so it is IMPOSSIBLE to lose in field of view!
        GameObject hudRoot = new GameObject("HeadLocked_HUD");
        hudRoot.transform.SetParent(mainCameraGo.transform, false);
        hudRoot.transform.localPosition = new Vector3(0f, 0f, 0.75f); // 75cm directly in front of eyes
        hudRoot.transform.localRotation = Quaternion.identity;

        // Camera-locked Status Text
        GameObject hudTextGo = new GameObject("HUD_Status_Text");
        hudTextGo.transform.SetParent(hudRoot.transform, false);
        hudTextGo.transform.localPosition = new Vector3(0f, 0.12f, 0f);
        hudTextGo.transform.localScale = Vector3.one;

        TextMesh hudTextMesh = hudTextGo.AddComponent<TextMesh>();
        hudTextMesh.text = "[XREAL AR HUD Starting...]";
        hudTextMesh.fontSize = 28;
        hudTextMesh.characterSize = 0.004f;
        hudTextMesh.alignment = TextAlignment.Center;
        hudTextMesh.anchor = TextAnchor.MiddleCenter;
        hudTextMesh.color = Color.yellow;

        // Camera-locked Rotating Cyan Cube
        GameObject hudCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hudCube.name = "HUD_Rotating_Cube";
        hudCube.transform.SetParent(hudRoot.transform, false);
        hudCube.transform.localPosition = new Vector3(0f, -0.08f, 0f);
        hudCube.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
        hudCube.GetComponent<Renderer>().sharedMaterial = matCyan;

        SimpleARDiagnostics hudDiag = hudCube.AddComponent<SimpleARDiagnostics>();
        hudDiag.statusText = hudTextMesh;

        // Stereo depth spheres on HUD left/right
        GameObject hudSphereL = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hudSphereL.name = "HUD_Sphere_L";
        hudSphereL.transform.SetParent(hudRoot.transform, false);
        hudSphereL.transform.localPosition = new Vector3(-0.22f, -0.08f, 0f);
        hudSphereL.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
        hudSphereL.GetComponent<Renderer>().sharedMaterial = matGreen;

        GameObject hudSphereR = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        hudSphereR.name = "HUD_Sphere_R";
        hudSphereR.transform.SetParent(hudRoot.transform, false);
        hudSphereR.transform.localPosition = new Vector3(0.22f, -0.08f, 0f);
        hudSphereR.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
        hudSphereR.GetComponent<Renderer>().sharedMaterial = matMagenta;

        // 7. [WORLD-LOCKED AR OBJECT]
        // Placed in world space at 1.5m to demonstrate tracking and stereoscopic stability
        GameObject worldRoot = new GameObject("WorldLocked_AR_Anchor");
        worldRoot.transform.position = new Vector3(0f, 0f, 1.5f);

        GameObject worldCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        worldCube.name = "World_Cube";
        worldCube.transform.SetParent(worldRoot.transform, false);
        worldCube.transform.localPosition = Vector3.zero;
        worldCube.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        worldCube.GetComponent<Renderer>().sharedMaterial = matMagenta;

        // Attach rotating diagnostics to world cube too
        worldCube.AddComponent<SimpleARDiagnostics>();

        GameObject worldTextGo = new GameObject("World_Anchor_Text");
        worldTextGo.transform.SetParent(worldRoot.transform, false);
        worldTextGo.transform.localPosition = new Vector3(0f, 0.25f, 0f);
        TextMesh worldTextMesh = worldTextGo.AddComponent<TextMesh>();
        worldTextMesh.text = "[Fixed in World Space: 1.5m]";
        worldTextMesh.fontSize = 32;
        worldTextMesh.characterSize = 0.005f;
        worldTextMesh.alignment = TextAlignment.Center;
        worldTextMesh.anchor = TextAnchor.MiddleCenter;
        worldTextMesh.color = Color.green;

        // 8. Save the scene
        EditorSceneManager.SaveScene(scene, scenePath);
        Debug.Log($"SUCCESS: Simple AR Scene saved at {scenePath}");

        // 9. Update EditorBuildSettings to use this scene
        EditorBuildSettingsScene[] buildScenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene(scenePath, true)
        };
        EditorBuildSettings.scenes = buildScenes;
        Debug.Log($"EditorBuildSettings updated with single scene: {scenePath}");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static Material GetOrCreateUnlitMaterial(string assetPath, Color color)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") 
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Standard");
            mat = new Material(shader);
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, assetPath);
        }
        else
        {
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }
}
