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

public static class CreateAROthelloBoardTestScene
{
    private const string SCENE_PATH = "Assets/OpenCV/Scenes/AROthelloBoardTestScene.unity";

    [MenuItem("XREAL/Create AR Othello Board Test Scene")]
    public static void GenerateScene()
    {
        Debug.Log("=== Generating Hands-Free AR Othello Board Test Scene for XREAL ===");

        // 1. Ensure Directories
        string sceneDir = "Assets/OpenCV/Scenes";
        string matDir = "Assets/Materials";
        if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);
        if (!Directory.Exists(matDir)) Directory.CreateDirectory(matDir);

        // 2. Create fresh empty scene
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 3. Directional Light
        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = Color.white;
        light.intensity = 1.0f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // 4. XR Origin Hierarchy (AR Setup)
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

        // 5. EventSystem
        GameObject eventSystemGo = new GameObject("EventSystem");
        eventSystemGo.AddComponent<EventSystem>();
        eventSystemGo.AddComponent<StandaloneInputModule>();

        // 6. ScreenSpace - Camera AR Canvas (Rigidly locked to head display, completely transparent)
        GameObject canvasGo = new GameObject("AR_Overlay_Canvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1.0f;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        // Controller component on Canvas
        AROthelloBoardTestController controller = canvasGo.AddComponent<AROthelloBoardTestController>();

        Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

        // ----------------- 1. PRIMARY AR OVERLAY (FULL SCREEN TRANSPARENT) -----------------
        // 現実のオセロ盤上に黄色枠線と丸を直接重ね合わせるための全画面透過 RawImage
        GameObject overlayGo = new GameObject("FullAROverlayRawImage");
        overlayGo.transform.SetParent(canvasGo.transform, false);
        RawImage overlayImg = overlayGo.AddComponent<RawImage>();
        overlayImg.color = Color.white;
        overlayImg.raycastTarget = false;

        RectTransform overlayRect = overlayGo.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        controller.overlayRawImage = overlayImg;

        // ----------------- 2. TOP HUD (FLOATING BAR) -----------------
        // 視界の上部に浮かぶコンパクトな半透明ステータスバー
        GameObject hudPanelGo = new GameObject("TopHUD_Panel");
        hudPanelGo.transform.SetParent(canvasGo.transform, false);
        Image hudBg = hudPanelGo.AddComponent<Image>();
        hudBg.color = new Color(0.04f, 0.08f, 0.12f, 0.65f); // 半透明ダークグラス
        hudBg.raycastTarget = false;

        RectTransform hudPanelRect = hudPanelGo.GetComponent<RectTransform>();
        hudPanelRect.anchorMin = new Vector2(0.15f, 0.88f);
        hudPanelRect.anchorMax = new Vector2(0.85f, 0.98f);
        hudPanelRect.offsetMin = Vector2.zero;
        hudPanelRect.offsetMax = Vector2.zero;

        // Status Text (【盤面捕捉中】/【盤面探索中】)
        GameObject statusTextGo = new GameObject("HUD_StatusText");
        statusTextGo.transform.SetParent(hudPanelGo.transform, false);
        Text statusText = statusTextGo.AddComponent<Text>();
        statusText.font = defaultFont;
        statusText.fontSize = 24;
        statusText.fontStyle = FontStyle.Bold;
        statusText.alignment = TextAnchor.MiddleCenter;
        statusText.color = Color.green;
        statusText.raycastTarget = false;
        RectTransform statusRect = statusTextGo.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.02f, 0.5f);
        statusRect.anchorMax = new Vector2(0.35f, 0.95f);
        statusRect.offsetMin = Vector2.zero;
        statusRect.offsetMax = Vector2.zero;
        controller.hudStatusText = statusText;

        // Stone Count Text (● 黒 X個 | ○ 白 Y個)
        GameObject countTextGo = new GameObject("HUD_StoneCountText");
        countTextGo.transform.SetParent(hudPanelGo.transform, false);
        Text countText = countTextGo.AddComponent<Text>();
        countText.font = defaultFont;
        countText.fontSize = 22;
        countText.fontStyle = FontStyle.Bold;
        countText.alignment = TextAnchor.MiddleCenter;
        countText.color = Color.white;
        countText.raycastTarget = false;
        RectTransform countRect = countTextGo.GetComponent<RectTransform>();
        countRect.anchorMin = new Vector2(0.35f, 0.5f);
        countRect.anchorMax = new Vector2(0.98f, 0.95f);
        countRect.offsetMin = Vector2.zero;
        countRect.offsetMax = Vector2.zero;
        controller.hudStoneCountText = countText;

        // Performance Stats Text (処理時間: XX.X ms | XX FPS)
        GameObject statsTextGo = new GameObject("HUD_StatsText");
        statsTextGo.transform.SetParent(hudPanelGo.transform, false);
        Text statsText = statsTextGo.AddComponent<Text>();
        statsText.font = defaultFont;
        statsText.fontSize = 17;
        statsText.alignment = TextAnchor.MiddleCenter;
        statsText.color = new Color(0f, 0.9f, 1f, 1f); // Cyan
        statsText.raycastTarget = false;
        RectTransform statsRect = statsTextGo.GetComponent<RectTransform>();
        statsRect.anchorMin = new Vector2(0.02f, 0.05f);
        statsRect.anchorMax = new Vector2(0.98f, 0.5f);
        statsRect.offsetMin = Vector2.zero;
        statsRect.offsetMax = Vector2.zero;
        controller.hudStatsText = statsText;

        // ----------------- 3. MINI CAMERA MONITOR (BOTTOM-RIGHT) -----------------
        // カメラの向きと画角を確認するための右下小型モニター (180x101)
        GameObject miniPanelGo = new GameObject("MiniCamera_Panel");
        miniPanelGo.transform.SetParent(canvasGo.transform, false);
        Image miniBorder = miniPanelGo.AddComponent<Image>();
        miniBorder.color = new Color(0f, 0.8f, 1f, 0.6f); // シアンの縁取り
        miniBorder.raycastTarget = false;

        RectTransform miniPanelRect = miniPanelGo.GetComponent<RectTransform>();
        miniPanelRect.anchorMin = new Vector2(0.82f, 0.03f);
        miniPanelRect.anchorMax = new Vector2(0.98f, 0.20f);
        miniPanelRect.offsetMin = Vector2.zero;
        miniPanelRect.offsetMax = Vector2.zero;

        // Mini Camera RawImage (縁取りの内側)
        GameObject miniImgGo = new GameObject("MiniCameraRawImage");
        miniImgGo.transform.SetParent(miniPanelGo.transform, false);
        RawImage miniImg = miniImgGo.AddComponent<RawImage>();
        miniImg.color = Color.white;
        miniImg.raycastTarget = false;

        RectTransform miniImgRect = miniImgGo.GetComponent<RectTransform>();
        miniImgRect.anchorMin = new Vector2(0.02f, 0.02f);
        miniImgRect.anchorMax = new Vector2(0.98f, 0.82f);
        miniImgRect.offsetMin = Vector2.zero;
        miniImgRect.offsetMax = Vector2.zero;

        controller.miniCameraRawImage = miniImg;

        // Mini Label
        GameObject miniLabelGo = new GameObject("MiniCameraLabel");
        miniLabelGo.transform.SetParent(miniPanelGo.transform, false);
        Text miniLabel = miniLabelGo.AddComponent<Text>();
        miniLabel.font = defaultFont;
        miniLabel.fontSize = 14;
        miniLabel.fontStyle = FontStyle.Bold;
        miniLabel.alignment = TextAnchor.MiddleCenter;
        miniLabel.text = "Camera Preview";
        miniLabel.color = Color.white;
        miniLabel.raycastTarget = false;

        RectTransform miniLabelRect = miniLabelGo.GetComponent<RectTransform>();
        miniLabelRect.anchorMin = new Vector2(0f, 0.82f);
        miniLabelRect.anchorMax = new Vector2(1f, 1f);
        miniLabelRect.offsetMin = Vector2.zero;
        miniLabelRect.offsetMax = Vector2.zero;

        // Save Scene
        EditorSceneManager.SaveScene(scene, SCENE_PATH);
        Debug.Log($"SUCCESS: Generated AR Othello Board Test Scene at: {SCENE_PATH}");
    }
}
