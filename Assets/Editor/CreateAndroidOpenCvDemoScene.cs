using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.XR.CoreUtils;
using UnityEngine.Rendering.Universal;
using OpenCVForAndroidCustom;

public static class CreateAndroidOpenCvDemoScene
{
    private const string SCENE_PATH = "Assets/OpenCV/Scenes/AndroidOpenCvDemoScene.unity";

    [MenuItem("XREAL/Create Android OpenCV Demo Scene")]
    public static void GenerateScene()
    {
        Debug.Log("=== Generating Hands-Free Dual-Window OpenCV Demo Scene for XREAL ===");

        // 1. Ensure Directories
        string sceneDir = "Assets/OpenCV/Scenes";
        string matDir = "Assets/Materials";
        if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);
        if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);

        // 2. Prepare Materials
        Material matCyan = GetOrCreateUnlitMaterial(Path.Combine(matDir, "AR_Cyan.mat"), new Color(0f, 1f, 1f, 1f));
        Material matGreen = GetOrCreateUnlitMaterial(Path.Combine(matDir, "AR_Green.mat"), new Color(0.2f, 1f, 0.2f, 1f));

        // 3. Create fresh empty scene
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 4. Directional Light
        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 1.0f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 5. XR Origin Hierarchy (AR Setup)
        GameObject xrOriginGo = new GameObject("XR Origin (XR Rig)");
        XROrigin xrOrigin = xrOriginGo.AddComponent<XROrigin>();

        GameObject cameraOffsetGo = new GameObject("Camera Offset");
        cameraOffsetGo.transform.SetParent(xrOriginGo.transform, false);

        GameObject mainCameraGo = new GameObject("Main Camera");
        mainCameraGo.transform.SetParent(cameraOffsetGo.transform, false);
        mainCameraGo.tag = "MainCamera";

        Camera camera = mainCameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // Transparent for AR see-through
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 1000f;
        camera.cullingMask = -1;

        mainCameraGo.AddComponent<AudioListener>();
        mainCameraGo.AddComponent<UniversalAdditionalCameraData>();
        mainCameraGo.AddComponent<XREALCameraPoseTracker>();

        xrOrigin.Camera = camera;
        xrOrigin.CameraFloorOffsetObject = cameraOffsetGo;
        xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

        // 6. EventSystem
        GameObject eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<StandaloneInputModule>();

        // 7. AR Depth Indicator (Rotating Cyan Diamond above canvas)
        GameObject indicatorCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        indicatorCube.name = "AR_Depth_Indicator";
        indicatorCube.transform.position = new Vector3(0f, 0.45f, 1.1f);
        indicatorCube.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
        indicatorCube.GetComponent<Renderer>().sharedMaterial = matCyan;
        indicatorCube.AddComponent<SimpleARDiagnostics>();

        // 8. World Space AR Canvas (Dual-Window Floating Panel at 1.1m in front of eyes)
        GameObject canvasGo = new GameObject("OpenCV_DualWindow_Canvas");
        canvasGo.transform.position = new Vector3(0f, 0f, 1.1f);
        canvasGo.transform.rotation = Quaternion.identity;
        canvasGo.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f); // 1000px = 1m

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;

        RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1280f, 720f);

        canvasGo.AddComponent<CanvasScaler>();
        canvasGo.AddComponent<GraphicRaycaster>();

        // Controller component on Canvas
        AndroidOpenCvDemoController controller = canvasGo.AddComponent<AndroidOpenCvDemoController>();

        // Panel Background
        GameObject bgGo = new GameObject("PanelBackground");
        bgGo.transform.SetParent(canvasGo.transform, false);
        Image bgImg = bgGo.AddComponent<Image>();
        bgImg.color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
        RectTransform bgRect = bgGo.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;

        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // ----------------- TOP HEADER AREA -----------------
        // Title Text
        GameObject titleGo = new GameObject("MainTitleText");
        titleGo.transform.SetParent(canvasGo.transform, false);
        Text titleText = titleGo.AddComponent<Text>();
        titleText.font = defaultFont;
        titleText.fontSize = 26;
        titleText.fontStyle = FontStyle.Bold;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.color = Color.white;
        titleText.text = "XREAL One Pro - OpenCV Real-Time Dual Display";
        RectTransform titleRect = titleGo.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.02f, 0.92f);
        titleRect.anchorMax = new Vector2(0.98f, 0.98f);
        titleRect.anchoredPosition = Vector2.zero;
        titleRect.sizeDelta = Vector2.zero;
        controller.mainTitleText = titleText;

        // FPS & Latency Status Summary Text
        GameObject summaryGo = new GameObject("StatusSummaryText");
        summaryGo.transform.SetParent(canvasGo.transform, false);
        Text summaryText = summaryGo.AddComponent<Text>();
        summaryText.font = defaultFont;
        summaryText.fontSize = 20;
        summaryText.fontStyle = FontStyle.Bold;
        summaryText.alignment = TextAnchor.MiddleCenter;
        summaryText.color = new Color(0.2f, 1f, 0.5f);
        summaryText.text = "FPS: 60   |   OpenCV Latency: 2.1 ms   |   Processed Frames: 0";
        RectTransform summaryRect = summaryGo.GetComponent<RectTransform>();
        summaryRect.anchorMin = new Vector2(0.02f, 0.85f);
        summaryRect.anchorMax = new Vector2(0.98f, 0.92f);
        summaryRect.anchoredPosition = Vector2.zero;
        summaryRect.sizeDelta = Vector2.zero;
        controller.statusSummaryText = summaryText;

        // Backend Version Text
        GameObject verGo = new GameObject("BackendVersionText");
        verGo.transform.SetParent(canvasGo.transform, false);
        Text verText = verGo.AddComponent<Text>();
        verText.font = defaultFont;
        verText.fontSize = 14;
        verText.alignment = TextAnchor.MiddleCenter;
        verText.color = new Color(0.4f, 0.85f, 1f);
        verText.text = "Backend: Native libOthelloCvPlugin.so (ARM64 Native) | Hands-Free Auto Stream";
        RectTransform verRect = verGo.GetComponent<RectTransform>();
        verRect.anchorMin = new Vector2(0.02f, 0.80f);
        verRect.anchorMax = new Vector2(0.98f, 0.85f);
        verRect.anchoredPosition = Vector2.zero;
        verRect.sizeDelta = Vector2.zero;
        controller.backendVersionText = verText;

        // Detailed Stats Text
        GameObject detailsGo = new GameObject("DetailedStatsText");
        detailsGo.transform.SetParent(canvasGo.transform, false);
        Text detailsText = detailsGo.AddComponent<Text>();
        detailsText.font = defaultFont;
        detailsText.fontSize = 14;
        detailsText.alignment = TextAnchor.MiddleCenter;
        detailsText.color = new Color(0.9f, 0.9f, 0.9f);
        detailsText.text = "Source: Initializing... | Status: [SEARCHING / EDGE STREAM]\nDiscs: Initializing OpenCV...";
        RectTransform detailsRect = detailsGo.GetComponent<RectTransform>();
        detailsRect.anchorMin = new Vector2(0.02f, 0.72f);
        detailsRect.anchorMax = new Vector2(0.98f, 0.80f);
        detailsRect.anchoredPosition = Vector2.zero;
        detailsRect.sizeDelta = Vector2.zero;
        controller.detailedStatsText = detailsText;

        // ----------------- WINDOW 1: RAW CAMERA FEED (LEFT) -----------------
        GameObject win1Go = new GameObject("Window1_RawCamera");
        win1Go.transform.SetParent(canvasGo.transform, false);
        RectTransform win1Rect = win1Go.AddComponent<RectTransform>();
        win1Rect.anchorMin = new Vector2(0.03f, 0.08f);
        win1Rect.anchorMax = new Vector2(0.49f, 0.71f);
        win1Rect.anchoredPosition = Vector2.zero;
        win1Rect.sizeDelta = Vector2.zero;

        Image win1Bg = win1Go.AddComponent<Image>();
        win1Bg.color = new Color(0.12f, 0.15f, 0.20f, 1f);

        // Window 1 Title
        GameObject win1TitleGo = new GameObject("Win1Title");
        win1TitleGo.transform.SetParent(win1Go.transform, false);
        Text win1Title = win1TitleGo.AddComponent<Text>();
        win1Title.font = defaultFont;
        win1Title.fontSize = 17;
        win1Title.fontStyle = FontStyle.Bold;
        win1Title.alignment = TextAnchor.MiddleCenter;
        win1Title.color = new Color(0.4f, 0.85f, 1f);
        win1Title.text = "[ WINDOW 1: RAW CAMERA FEED ]";
        RectTransform win1TitleRect = win1TitleGo.GetComponent<RectTransform>();
        win1TitleRect.anchorMin = new Vector2(0.02f, 0.91f);
        win1TitleRect.anchorMax = new Vector2(0.98f, 0.99f);
        win1TitleRect.anchoredPosition = Vector2.zero;
        win1TitleRect.sizeDelta = Vector2.zero;
        controller.window1TitleText = win1Title;

        // Window 1 Display RawImage
        GameObject rawImg1Go = new GameObject("RawImageDisplay");
        rawImg1Go.transform.SetParent(win1Go.transform, false);
        RawImage rawImg1 = rawImg1Go.AddComponent<RawImage>();
        RectTransform rawImg1Rect = rawImg1Go.GetComponent<RectTransform>();
        rawImg1Rect.anchorMin = new Vector2(0.03f, 0.10f);
        rawImg1Rect.anchorMax = new Vector2(0.97f, 0.90f);
        rawImg1Rect.anchoredPosition = Vector2.zero;
        rawImg1Rect.sizeDelta = Vector2.zero;
        controller.rawCameraDisplay = rawImg1;

        // Window 1 Subtitle
        GameObject win1SubGo = new GameObject("Win1Subtitle");
        win1SubGo.transform.SetParent(win1Go.transform, false);
        Text win1Sub = win1SubGo.AddComponent<Text>();
        win1Sub.font = defaultFont;
        win1Sub.fontSize = 13;
        win1Sub.alignment = TextAnchor.MiddleCenter;
        win1Sub.color = new Color(0.8f, 0.85f, 0.9f);
        win1Sub.text = "Direct Camera Feed (XREAL One Pro)";
        RectTransform win1SubRect = win1SubGo.GetComponent<RectTransform>();
        win1SubRect.anchorMin = new Vector2(0.02f, 0.01f);
        win1SubRect.anchorMax = new Vector2(0.98f, 0.09f);
        win1SubRect.anchoredPosition = Vector2.zero;
        win1SubRect.sizeDelta = Vector2.zero;
        controller.window1SubtitleText = win1Sub;

        // ----------------- WINDOW 2: OPENCV PROCESSED FEED (RIGHT) -----------------
        GameObject win2Go = new GameObject("Window2_OpenCvProcessed");
        win2Go.transform.SetParent(canvasGo.transform, false);
        RectTransform win2Rect = win2Go.AddComponent<RectTransform>();
        win2Rect.anchorMin = new Vector2(0.51f, 0.08f);
        win2Rect.anchorMax = new Vector2(0.97f, 0.71f);
        win2Rect.anchoredPosition = Vector2.zero;
        win2Rect.sizeDelta = Vector2.zero;

        Image win2Bg = win2Go.AddComponent<Image>();
        win2Bg.color = new Color(0.12f, 0.15f, 0.20f, 1f);

        // Window 2 Title
        GameObject win2TitleGo = new GameObject("Win2Title");
        win2TitleGo.transform.SetParent(win2Go.transform, false);
        Text win2Title = win2TitleGo.AddComponent<Text>();
        win2Title.font = defaultFont;
        win2Title.fontSize = 17;
        win2Title.fontStyle = FontStyle.Bold;
        win2Title.alignment = TextAnchor.MiddleCenter;
        win2Title.color = new Color(0.2f, 1f, 0.5f);
        win2Title.text = "[ WINDOW 2: OPENCV PROCESSED FEED ]";
        RectTransform win2TitleRect = win2TitleGo.GetComponent<RectTransform>();
        win2TitleRect.anchorMin = new Vector2(0.02f, 0.91f);
        win2TitleRect.anchorMax = new Vector2(0.98f, 0.99f);
        win2TitleRect.anchoredPosition = Vector2.zero;
        win2TitleRect.sizeDelta = Vector2.zero;
        controller.window2TitleText = win2Title;

        // Window 2 Display RawImage
        GameObject rawImg2Go = new GameObject("ProcessedImageDisplay");
        rawImg2Go.transform.SetParent(win2Go.transform, false);
        RawImage rawImg2 = rawImg2Go.AddComponent<RawImage>();
        RectTransform rawImg2Rect = rawImg2Go.GetComponent<RectTransform>();
        rawImg2Rect.anchorMin = new Vector2(0.03f, 0.10f);
        rawImg2Rect.anchorMax = new Vector2(0.97f, 0.90f);
        rawImg2Rect.anchoredPosition = Vector2.zero;
        rawImg2Rect.sizeDelta = Vector2.zero;
        controller.processedCameraDisplay = rawImg2;

        // Window 2 Subtitle
        GameObject win2SubGo = new GameObject("Win2Subtitle");
        win2SubGo.transform.SetParent(win2Go.transform, false);
        Text win2Sub = win2SubGo.AddComponent<Text>();
        win2Sub.font = defaultFont;
        win2Sub.fontSize = 13;
        win2Sub.alignment = TextAnchor.MiddleCenter;
        win2Sub.color = new Color(0.8f, 0.85f, 0.9f);
        win2Sub.text = "OpenCV: Real-Time Edge & Contour Detection Active";
        RectTransform win2SubRect = win2SubGo.GetComponent<RectTransform>();
        win2SubRect.anchorMin = new Vector2(0.02f, 0.01f);
        win2SubRect.anchorMax = new Vector2(0.98f, 0.09f);
        win2SubRect.anchoredPosition = Vector2.zero;
        win2SubRect.sizeDelta = Vector2.zero;
        controller.window2SubtitleText = win2Sub;

        // ----------------- BOTTOM FOOTER AREA -----------------
        GameObject footerGo = new GameObject("FooterText");
        footerGo.transform.SetParent(canvasGo.transform, false);
        Text footerText = footerGo.AddComponent<Text>();
        footerText.font = defaultFont;
        footerText.fontSize = 13;
        footerText.fontStyle = FontStyle.Bold;
        footerText.alignment = TextAnchor.MiddleCenter;
        footerText.color = new Color(0.6f, 0.75f, 0.9f);
        footerText.text = "✦ HANDS-FREE REAL-TIME STREAMING (NO CONTROLLER REQUIRED) ✦";
        RectTransform footerRect = footerGo.GetComponent<RectTransform>();
        footerRect.anchorMin = new Vector2(0.02f, 0.01f);
        footerRect.anchorMax = new Vector2(0.98f, 0.06f);
        footerRect.anchoredPosition = Vector2.zero;
        footerRect.sizeDelta = Vector2.zero;

        // 9. Save scene
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        Debug.Log($"SUCCESS: Dual-Window Android OpenCV Demo Scene saved at {SCENE_PATH}");

        // 10. Update EditorBuildSettings
        EditorBuildSettingsScene[] buildScenes = new EditorBuildSettingsScene[]
        {
            new EditorBuildSettingsScene(SCENE_PATH, true)
        };
        EditorBuildSettings.scenes = buildScenes;
        Debug.Log($"EditorBuildSettings updated with scene: {SCENE_PATH}");

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
