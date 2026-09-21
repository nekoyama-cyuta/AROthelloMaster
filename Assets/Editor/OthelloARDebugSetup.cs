using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.XR.CoreUtils;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using AROthello;

namespace AROthelloEditor
{
    /// <summary>
    /// Tools > Setup Othello AR Auto Tracker メニュー拡張
    /// シーンに必要な 3D ターゲット、AutoTracker、グラス姿勢ビジュアライザー、
    /// 俯瞰カメラ (Overhead_Camera)、Dual Canvas を自動生成・バインド
    /// </summary>
    public static class OthelloARDebugSetup
    {
        [MenuItem("Tools/Setup Othello AR Auto Tracker")]
        public static void SetupScene()
        {
            Debug.Log("=== [OthelloARDebugSetup] Setting up Othello AR Auto Tracker ===");

            // クリーンアップ: 4点キャリブレーション、手動アンカー、不要パネルをシーンから完全排除
            string[] obsoleteNames = new string[]
            {
                "FourPointCalibrationManager",
                "SpatialCursor",
                "Calibration_Panel_BeamPro",
                "ARAnchor_OthelloBoard_Manual",
                "DummyBoard_VisualProof"
            };
            foreach (var name in obsoleteNames)
            {
                var go = GameObject.Find(name);
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }

            foreach (var am in UnityEngine.Object.FindObjectsByType<UnityEngine.XR.ARFoundation.ARAnchorManager>(FindObjectsSortMode.None))
            {
                if (am != null) UnityEngine.Object.DestroyImmediate(am);
            }

            // 0. レイヤー GlassesVisualizer (Layer 8) の確認と登録
            int glassesLayer = EnsureLayer("GlassesVisualizer", 8);

            // 1. Main Camera / XR Origin の確認と AR 設定 (透過背景 + 6DoFトラッカー)
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject xrOriginGo = new GameObject("XR Origin (XR Rig)");
                XROrigin xrOrigin = xrOriginGo.AddComponent<XROrigin>();

                GameObject cameraOffsetGo = new GameObject("Camera Offset");
                cameraOffsetGo.transform.SetParent(xrOriginGo.transform, false);

                GameObject mainCameraGo = new GameObject("Main Camera");
                mainCameraGo.transform.SetParent(cameraOffsetGo.transform, false);
                mainCameraGo.tag = "MainCamera";

                mainCam = mainCameraGo.AddComponent<Camera>();
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 透過背景 (シースルーAR用)
                mainCam.nearClipPlane = 0.05f;
                mainCam.farClipPlane = 1000f;
                mainCam.cullingMask = -1;

                mainCameraGo.AddComponent<AudioListener>();
                UniversalAdditionalCameraData uac = mainCameraGo.AddComponent<UniversalAdditionalCameraData>();
                uac.allowXRRendering = true;
                mainCameraGo.AddComponent<XREALCameraPoseTracker>();

                xrOrigin.Camera = mainCam;
                xrOrigin.CameraFloorOffsetObject = cameraOffsetGo;
                xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

                Undo.RegisterCreatedObjectUndo(xrOriginGo, "Create XR Origin");
            }
            else
            {
                if (mainCam.GetComponent<XREALCameraPoseTracker>() == null)
                {
                    mainCam.gameObject.AddComponent<XREALCameraPoseTracker>();
                }
                mainCam.clearFlags = CameraClearFlags.SolidColor;
                mainCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                UniversalAdditionalCameraData uac = mainCam.GetComponent<UniversalAdditionalCameraData>();
                if (uac == null) uac = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
                uac.allowXRRendering = true;
            }

            // グラス着用者 (Main Camera) からはグラスビジュアライザー (Layer 8) を非表示 (Cull) に設定
            if (glassesLayer != -1)
            {
                mainCam.cullingMask &= ~(1 << glassesLayer);
            }

            // 2. グラスの 3D 位置・姿勢ビジュアライザー (三軸矢印 + 視線コーン) の生成
            SetupGlassesVisualizer(mainCam, glassesLayer);

            // 3. 全体俯瞰カメラ (Overhead_Camera) の生成・設定
            Camera overheadCam = SetupOverheadCamera(glassesLayer);

            // 4. EventSystem の存在確認
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<EventSystem>();
                eventSystemGo.AddComponent<StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(eventSystemGo, "Create EventSystem");
            }

            // 4.5. OthelloLogic の存在確認と生成
            if (UnityEngine.Object.FindFirstObjectByType<OthelloLogic>() == null)
            {
                GameObject logicGo = new GameObject("OthelloLogic");
                logicGo.AddComponent<OthelloLogic>();
                Undo.RegisterCreatedObjectUndo(logicGo, "Create OthelloLogic");
            }

            // 5. 3D ターゲットオセロ盤 (実寸 22.8cm x 1.8cm x 22.8cm) の生成・ワイヤーフレームビジュアライザー設定
            GameObject targetBoard = GameObject.Find("OthelloBoard_AR_Target");

            if (targetBoard == null)
            {
                targetBoard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                targetBoard.name = "OthelloBoard_AR_Target";
                targetBoard.transform.position = new Vector3(0f, 0f, 0.6f); // 初期位置: カメラ前方60cm
                targetBoard.transform.rotation = Quaternion.identity;
                targetBoard.transform.localScale = new Vector3(0.228f, 0.018f, 0.228f);

                // コライダー削除
                UnityEngine.Object.DestroyImmediate(targetBoard.GetComponent<Collider>());
                Undo.RegisterCreatedObjectUndo(targetBoard, "Create OthelloBoard_AR_Target");
            }

            // 完全ワイヤーフレーム化のため、ソリッド直方体 MeshRenderer を無効化
            var boardMr = targetBoard.GetComponent<MeshRenderer>();
            if (boardMr != null)
            {
                boardMr.enabled = false;
            }

            // 既存の動的子オブジェクトがあればクリーンアップ
            string[] oldVisualizerChildren = new string[] { "GridLines_Container", "Cells_Container", "Highlights_Container", "Wireframe_Board_Mesh" };
            foreach (var childName in oldVisualizerChildren)
            {
                var childTr = targetBoard.transform.Find(childName);
                if (childTr != null) UnityEngine.Object.DestroyImmediate(childTr.gameObject);
            }

            // 前回のバーチャル盤で実証済みの確実なマテリアルをアサイン
            Material gridLineMat = GetOrCreateMaterial("Assets/Materials/AR_GridLine.mat", new Color(0.2f, 1.0f, 0.5f, 0.95f));
            Material whiteDiscMat = GetOrCreateMaterial("Assets/Materials/AR_WhiteMarker.mat", new Color(1.0f, 1.0f, 1.0f, 0.95f));
            Material blackDiscMat = GetOrCreateMaterial("Assets/Materials/AR_AxisBlue.mat", new Color(0.0f, 0.85f, 1.0f, 0.95f));
            Material highlightMat = GetOrCreateMaterial("Assets/Materials/AR_YellowMarker.mat", new Color(1.0f, 0.85f, 0.0f, 0.95f));

            // OthelloBoardVisualizer コンポーネントの設定
            OthelloBoardVisualizer visualizer = targetBoard.GetComponent<OthelloBoardVisualizer>();
            if (visualizer == null)
            {
                visualizer = targetBoard.AddComponent<OthelloBoardVisualizer>();
            }
            visualizer.TargetTeam = 1; // 白番固定
            visualizer.CustomBoardMaterial = gridLineMat;
            visualizer.CustomWhiteDiscMaterial = whiteDiscMat;
            visualizer.CustomBlackDiscMaterial = blackDiscMat;
            visualizer.CustomHighlightMaterial = highlightMat;

            // 背景透過を確保するため中心白丸は不要 (削除)
            Transform centerMarker = targetBoard.transform.Find("CenterIndicator");
            if (centerMarker != null)
            {
                UnityEngine.Object.DestroyImmediate(centerMarker.gameObject);
            }

            // 前方 (+Z) 方向を示す細い黄色ポインター (俯瞰での回転確認用)
            Transform forwardMarker = targetBoard.transform.Find("ForwardIndicator");
            if (forwardMarker == null)
            {
                GameObject fmGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fmGo.name = "ForwardIndicator";
                fmGo.transform.SetParent(targetBoard.transform, false);
                fmGo.transform.localPosition = new Vector3(0f, 0.515f, 0.40f);
                fmGo.transform.localScale = new Vector3(0.015f, 0.015f, 0.12f);
                Material yellowMat = GetOrCreateMaterial("Assets/Materials/AR_YellowMarker.mat", new Color(1f, 0.85f, 0f, 0.9f));
                if (yellowMat != null) fmGo.GetComponent<Renderer>().sharedMaterial = yellowMat;
                UnityEngine.Object.DestroyImmediate(fmGo.GetComponent<Collider>());
            }

            // 6. CustomScreenLogger の生成・設定
            CustomScreenLogger screenLogger = UnityEngine.Object.FindFirstObjectByType<CustomScreenLogger>();
            if (screenLogger == null)
            {
                GameObject loggerGo = new GameObject("CustomScreenLogger");
                screenLogger = loggerGo.AddComponent<CustomScreenLogger>();
                Undo.RegisterCreatedObjectUndo(loggerGo, "Create CustomScreenLogger");
            }

            // 7. OthelloAutoTracker の生成・設定
            OthelloAutoTracker tracker = UnityEngine.Object.FindFirstObjectByType<OthelloAutoTracker>();
            if (tracker == null)
            {
                GameObject trackerGo = new GameObject("OthelloAR_AutoTracker");
                tracker = trackerGo.AddComponent<OthelloAutoTracker>();
                Undo.RegisterCreatedObjectUndo(trackerGo, "Create OthelloAutoTracker");
            }

            tracker.TargetBoardTransform = targetBoard.transform;
            tracker.TrackingCamera = mainCam;

            // =========================================================================
            // 8. Canvas 1: XR グラス用 HUD (Screen Space - Camera)
            // =========================================================================
            GameObject canvasGlassesGo = GameObject.Find("Canvas_AR_Debug");
            if (canvasGlassesGo == null)
            {
                canvasGlassesGo = new GameObject("Canvas_AR_Debug");
                Undo.RegisterCreatedObjectUndo(canvasGlassesGo, "Create Canvas_AR_Debug");
            }

            Canvas canvasGlasses = canvasGlassesGo.GetComponent<Canvas>();
            if (canvasGlasses == null) canvasGlasses = canvasGlassesGo.AddComponent<Canvas>();
            canvasGlasses.renderMode = RenderMode.ScreenSpaceCamera;
            canvasGlasses.worldCamera = mainCam;
            canvasGlasses.planeDistance = 1.0f;
            canvasGlasses.sortingOrder = 100;

            CanvasScaler scalerGlasses = canvasGlassesGo.GetComponent<CanvasScaler>();
            if (scalerGlasses == null) scalerGlasses = canvasGlassesGo.AddComponent<CanvasScaler>();
            scalerGlasses.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scalerGlasses.referenceResolution = new Vector2(1920, 1080);
            scalerGlasses.matchWidthOrHeight = 0.5f;

            if (canvasGlassesGo.GetComponent<GraphicRaycaster>() == null)
            {
                canvasGlassesGo.AddComponent<GraphicRaycaster>();
            }

            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");

            // グラス用プレビューパネル (右上)
            GameObject glassesPreviewPanel = GameObject.Find("Preview_Panel_Glasses");
            RawImage glassesRawImage = null;
            if (glassesPreviewPanel == null)
            {
                glassesPreviewPanel = new GameObject("Preview_Panel_Glasses");
                glassesPreviewPanel.transform.SetParent(canvasGlassesGo.transform, false);

                Image borderImg = glassesPreviewPanel.AddComponent<Image>();
                borderImg.color = new Color(0f, 0.8f, 1f, 0.6f);

                RectTransform panelRect = glassesPreviewPanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.72f, 0.68f);
                panelRect.anchorMax = new Vector2(0.98f, 0.98f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;

                GameObject rawGo = new GameObject("RawImage_DebugPreview");
                rawGo.transform.SetParent(glassesPreviewPanel.transform, false);
                glassesRawImage = rawGo.AddComponent<RawImage>();
                glassesRawImage.color = Color.clear; // カメラ映像取得までは透明にし、白飛びを防止

                RectTransform rawRect = rawGo.GetComponent<RectTransform>();
                rawRect.anchorMin = new Vector2(0.02f, 0.02f);
                rawRect.anchorMax = new Vector2(0.98f, 0.88f);
                rawRect.offsetMin = Vector2.zero;
                rawRect.offsetMax = Vector2.zero;

                GameObject labelGo = new GameObject("Label_Preview");
                labelGo.transform.SetParent(glassesPreviewPanel.transform, false);
                Text label = labelGo.AddComponent<Text>();
                label.font = defaultFont;
                label.text = "HUD Camera Preview";
                label.fontSize = 16;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;

                RectTransform labelRect = labelGo.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 0.88f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }
            else
            {
                glassesRawImage = glassesPreviewPanel.GetComponentInChildren<RawImage>();
                if (glassesRawImage != null && glassesRawImage.texture == null) glassesRawImage.color = Color.clear;
            }

            // グラス用ログパネル (左下)
            GameObject glassesLogPanel = GameObject.Find("Log_Panel_Glasses");
            Text glassesLogText = null;
            if (glassesLogPanel == null)
            {
                glassesLogPanel = new GameObject("Log_Panel_Glasses");
                glassesLogPanel.transform.SetParent(canvasGlassesGo.transform, false);

                Image logBg = glassesLogPanel.AddComponent<Image>();
                logBg.color = new Color(0.03f, 0.05f, 0.08f, 0.70f);

                RectTransform logPanelRect = glassesLogPanel.GetComponent<RectTransform>();
                logPanelRect.anchorMin = new Vector2(0.02f, 0.02f);
                logPanelRect.anchorMax = new Vector2(0.65f, 0.35f);
                logPanelRect.offsetMin = Vector2.zero;
                logPanelRect.offsetMax = Vector2.zero;

                GameObject textGo = new GameObject("Text_GlassesLog");
                textGo.transform.SetParent(glassesLogPanel.transform, false);
                glassesLogText = textGo.AddComponent<Text>();
                glassesLogText.font = defaultFont;
                glassesLogText.fontSize = 17;
                glassesLogText.color = Color.white;
                glassesLogText.alignment = TextAnchor.LowerLeft;
                glassesLogText.supportRichText = true;
                glassesLogText.text = "<color=#00FF88>[Glasses HUD] Othello AR Tracker Ready.</color>";

                RectTransform textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0.03f, 0.03f);
                textRect.anchorMax = new Vector2(0.97f, 0.97f);
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
            }
            else
            {
                glassesLogText = glassesLogPanel.GetComponentInChildren<Text>();
            }

            // =========================================================================
            // 9. Canvas 2: 手元 Beam Pro 用デバッグ画面 (Screen Space - Overlay / Display 0)
            // =========================================================================
            GameObject canvasHandheldGo = GameObject.Find("Canvas_BeamPro_Handheld");
            if (canvasHandheldGo == null)
            {
                canvasHandheldGo = new GameObject("Canvas_BeamPro_Handheld");
                Undo.RegisterCreatedObjectUndo(canvasHandheldGo, "Create Canvas_BeamPro_Handheld");
            }

            Canvas canvasHandheld = canvasHandheldGo.GetComponent<Canvas>();
            if (canvasHandheld == null) canvasHandheld = canvasHandheldGo.AddComponent<Canvas>();
            canvasHandheld.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasHandheld.targetDisplay = 0; // 手元端末のメインタッチディスプレイ
            canvasHandheld.sortingOrder = 50;

            CanvasScaler scalerHandheld = canvasHandheldGo.GetComponent<CanvasScaler>();
            if (scalerHandheld == null) scalerHandheld = canvasHandheldGo.AddComponent<CanvasScaler>();
            scalerHandheld.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scalerHandheld.referenceResolution = new Vector2(1080, 1920);
            scalerHandheld.matchWidthOrHeight = 0.5f;

            if (canvasHandheldGo.GetComponent<GraphicRaycaster>() == null)
            {
                canvasHandheldGo.AddComponent<GraphicRaycaster>();
            }

            // 手元用ヘッダーバー
            GameObject headerPanel = GameObject.Find("Header_Panel_Handheld");
            if (headerPanel == null)
            {
                headerPanel = new GameObject("Header_Panel_Handheld");
                headerPanel.transform.SetParent(canvasHandheldGo.transform, false);

                Image headerBg = headerPanel.AddComponent<Image>();
                headerBg.color = new Color(0.05f, 0.10f, 0.18f, 0.95f);

                RectTransform headerRect = headerPanel.GetComponent<RectTransform>();
                headerRect.anchorMin = new Vector2(0f, 0.94f);
                headerRect.anchorMax = new Vector2(1f, 1f);
                headerRect.offsetMin = Vector2.zero;
                headerRect.offsetMax = Vector2.zero;

                GameObject titleGo = new GameObject("Text_Title");
                titleGo.transform.SetParent(headerPanel.transform, false);
                Text titleText = titleGo.AddComponent<Text>();
                titleText.font = defaultFont;
                titleText.text = "XREAL Beam Pro - AR Dual Monitor (OpenCV + 3D Overhead)";
                titleText.fontSize = 20;
                titleText.fontStyle = FontStyle.Bold;
                titleText.alignment = TextAnchor.MiddleCenter;
                titleText.color = new Color(0.0f, 0.95f, 1f);

                RectTransform titleRect = titleGo.GetComponent<RectTransform>();
                titleRect.anchorMin = Vector2.zero;
                titleRect.anchorMax = Vector2.one;
                titleRect.offsetMin = Vector2.zero;
                titleRect.offsetMax = Vector2.zero;
            }

            // 手元用カメラプレビュー (画面上半分・左側: OpenCV RGBカメラ映像)
            GameObject handheldPreviewPanel = GameObject.Find("Preview_Panel_Handheld");
            RawImage handheldRawImage = null;
            if (handheldPreviewPanel == null)
            {
                handheldPreviewPanel = new GameObject("Preview_Panel_Handheld");
                handheldPreviewPanel.transform.SetParent(canvasHandheldGo.transform, false);

                Image borderImg = handheldPreviewPanel.AddComponent<Image>();
                borderImg.color = new Color(0.1f, 0.15f, 0.22f, 0.95f);

                RectTransform panelRect = handheldPreviewPanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.02f, 0.48f);
                panelRect.anchorMax = new Vector2(0.49f, 0.93f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;

                GameObject rawGo = new GameObject("RawImage_HandheldPreview");
                rawGo.transform.SetParent(handheldPreviewPanel.transform, false);
                handheldRawImage = rawGo.AddComponent<RawImage>();
                handheldRawImage.color = Color.clear; // カメラ映像取得までは透明にし、白飛びを防止

                RectTransform rawRect = rawGo.GetComponent<RectTransform>();
                rawRect.anchorMin = new Vector2(0.01f, 0.01f);
                rawRect.anchorMax = new Vector2(0.99f, 0.90f);
                rawRect.offsetMin = Vector2.zero;
                rawRect.offsetMax = Vector2.zero;

                GameObject labelGo = new GameObject("Label_HandheldPreview");
                labelGo.transform.SetParent(handheldPreviewPanel.transform, false);
                Text label = labelGo.AddComponent<Text>();
                label.font = defaultFont;
                label.text = "OpenCV Camera (RGB)";
                label.fontSize = 18;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;

                RectTransform labelRect = labelGo.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 0.90f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }
            else
            {
                RectTransform panelRect = handheldPreviewPanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.02f, 0.48f);
                panelRect.anchorMax = new Vector2(0.49f, 0.93f);
                handheldRawImage = handheldPreviewPanel.GetComponentInChildren<RawImage>();
                if (handheldRawImage != null && handheldRawImage.texture == null) handheldRawImage.color = Color.clear;
            }

            // 手元用 3D 俯瞰ビューパネル (画面上半分・右側: Overhead 3D Camera)
            GameObject handheldOverheadPanel = GameObject.Find("Overhead_Panel_Handheld");
            RawImage overheadRawImage = null;
            if (handheldOverheadPanel == null)
            {
                handheldOverheadPanel = new GameObject("Overhead_Panel_Handheld");
                handheldOverheadPanel.transform.SetParent(canvasHandheldGo.transform, false);

                Image borderImg = handheldOverheadPanel.AddComponent<Image>();
                borderImg.color = new Color(0.1f, 0.15f, 0.22f, 0.95f);

                RectTransform panelRect = handheldOverheadPanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.51f, 0.48f);
                panelRect.anchorMax = new Vector2(0.98f, 0.93f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;

                GameObject rawGo = new GameObject("RawImage_OverheadView");
                rawGo.transform.SetParent(handheldOverheadPanel.transform, false);
                overheadRawImage = rawGo.AddComponent<RawImage>();
                overheadRawImage.color = Color.white;
                if (overheadCam != null && overheadCam.targetTexture != null)
                {
                    overheadRawImage.texture = overheadCam.targetTexture;
                }

                RectTransform rawRect = rawGo.GetComponent<RectTransform>();
                rawRect.anchorMin = new Vector2(0.01f, 0.01f);
                rawRect.anchorMax = new Vector2(0.99f, 0.90f);
                rawRect.offsetMin = Vector2.zero;
                rawRect.offsetMax = Vector2.zero;

                GameObject labelGo = new GameObject("Label_OverheadView");
                labelGo.transform.SetParent(handheldOverheadPanel.transform, false);
                Text label = labelGo.AddComponent<Text>();
                label.font = defaultFont;
                label.text = "Overhead 3D Bird's-Eye";
                label.fontSize = 18;
                label.fontStyle = FontStyle.Bold;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = new Color(0.0f, 0.95f, 1f);

                RectTransform labelRect = labelGo.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0f, 0.90f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }
            else
            {
                RectTransform panelRect = handheldOverheadPanel.GetComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.51f, 0.48f);
                panelRect.anchorMax = new Vector2(0.98f, 0.93f);
                overheadRawImage = handheldOverheadPanel.GetComponentInChildren<RawImage>();
                if (overheadRawImage != null && overheadCam != null && overheadCam.targetTexture != null)
                {
                    overheadRawImage.texture = overheadCam.targetTexture;
                }
            }

            // 手元用ログパネル (画面下半分・左側)
            GameObject handheldLogPanel = GameObject.Find("Log_Panel_Handheld");
            Text handheldLogText = null;
            if (handheldLogPanel == null)
            {
                handheldLogPanel = new GameObject("Log_Panel_Handheld");
                handheldLogPanel.transform.SetParent(canvasHandheldGo.transform, false);

                Image logBg = handheldLogPanel.AddComponent<Image>();
                logBg.color = new Color(0.02f, 0.03f, 0.05f, 0.95f);

                RectTransform logPanelRect = handheldLogPanel.GetComponent<RectTransform>();
                logPanelRect.anchorMin = new Vector2(0.02f, 0.02f);
                logPanelRect.anchorMax = new Vector2(0.66f, 0.40f);
                logPanelRect.offsetMin = Vector2.zero;
                logPanelRect.offsetMax = Vector2.zero;

                GameObject textGo = new GameObject("Text_HandheldLog");
                textGo.transform.SetParent(handheldLogPanel.transform, false);
                handheldLogText = textGo.AddComponent<Text>();
                handheldLogText.font = defaultFont;
                handheldLogText.fontSize = 18;
                handheldLogText.color = Color.white;
                handheldLogText.alignment = TextAnchor.LowerLeft;
                handheldLogText.supportRichText = true;
                handheldLogText.text = "<color=#00FF88>[Beam Pro] Console Initialized with 3D Overhead Monitor.</color>";

                RectTransform textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0.02f, 0.02f);
                textRect.anchorMax = new Vector2(0.98f, 0.98f);
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
            }
            else
            {
                RectTransform logPanelRect = handheldLogPanel.GetComponent<RectTransform>();
                logPanelRect.anchorMin = new Vector2(0.02f, 0.02f);
                logPanelRect.anchorMax = new Vector2(0.66f, 0.40f);
                handheldLogText = handheldLogPanel.GetComponentInChildren<Text>();
            }

            // 手元用 2D ミニ盤面パネル (画面下半分・右端: MiniBoard 2D Debug)
            GameObject handheldMiniBoardPanel = GameObject.Find("MiniBoard_Panel_Handheld");
            RawImage miniBoardRawImage = null;
            Text miniBoardStatusText = null;
            if (handheldMiniBoardPanel == null)
            {
                handheldMiniBoardPanel = new GameObject("MiniBoard_Panel_Handheld");
                handheldMiniBoardPanel.transform.SetParent(canvasHandheldGo.transform, false);

                Image mbBg = handheldMiniBoardPanel.AddComponent<Image>();
                mbBg.color = new Color(0.04f, 0.06f, 0.09f, 0.95f);

                RectTransform mbPanelRect = handheldMiniBoardPanel.GetComponent<RectTransform>();
                mbPanelRect.anchorMin = new Vector2(0.68f, 0.02f);
                mbPanelRect.anchorMax = new Vector2(0.98f, 0.40f);
                mbPanelRect.offsetMin = Vector2.zero;
                mbPanelRect.offsetMax = Vector2.zero;

                // タイトルラベル
                GameObject titleGo = new GameObject("Label_MiniBoard");
                titleGo.transform.SetParent(handheldMiniBoardPanel.transform, false);
                Text titleText = titleGo.AddComponent<Text>();
                titleText.font = defaultFont;
                titleText.text = "2D Board (White)";
                titleText.fontSize = 16;
                titleText.fontStyle = FontStyle.Bold;
                titleText.alignment = TextAnchor.MiddleCenter;
                titleText.color = new Color(0.9f, 0.95f, 1f);

                RectTransform titleRect = titleGo.GetComponent<RectTransform>();
                titleRect.anchorMin = new Vector2(0f, 0.88f);
                titleRect.anchorMax = new Vector2(1f, 1f);
                titleRect.offsetMin = Vector2.zero;
                titleRect.offsetMax = Vector2.zero;

                // 2D 盤面 RawImage
                GameObject mbRawGo = new GameObject("RawImage_MiniBoard");
                mbRawGo.transform.SetParent(handheldMiniBoardPanel.transform, false);
                miniBoardRawImage = mbRawGo.AddComponent<RawImage>();
                miniBoardRawImage.color = Color.white;

                RectTransform mbRawRect = mbRawGo.GetComponent<RectTransform>();
                mbRawRect.anchorMin = new Vector2(0.05f, 0.20f);
                mbRawRect.anchorMax = new Vector2(0.95f, 0.88f);
                mbRawRect.offsetMin = Vector2.zero;
                mbRawRect.offsetMax = Vector2.zero;

                // アスペクト比維持 (1:1 正方形)
                AspectRatioFitter arf = mbRawGo.AddComponent<AspectRatioFitter>();
                arf.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                arf.aspectRatio = 1.0f;

                // ステータステキスト (下部)
                GameObject statusGo = new GameObject("Text_MiniBoardStatus");
                statusGo.transform.SetParent(handheldMiniBoardPanel.transform, false);
                miniBoardStatusText = statusGo.AddComponent<Text>();
                miniBoardStatusText.font = defaultFont;
                miniBoardStatusText.text = "<color=#00D4FF>B:0</color> <color=#FFFFFF>W:0</color> | Legal: <color=#FFE600>0</color>";
                miniBoardStatusText.fontSize = 14;
                miniBoardStatusText.fontStyle = FontStyle.Bold;
                miniBoardStatusText.alignment = TextAnchor.MiddleCenter;
                miniBoardStatusText.supportRichText = true;
                miniBoardStatusText.color = Color.white;

                RectTransform statusRect = statusGo.GetComponent<RectTransform>();
                statusRect.anchorMin = new Vector2(0f, 0f);
                statusRect.anchorMax = new Vector2(1f, 0.20f);
                statusRect.offsetMin = Vector2.zero;
                statusRect.offsetMax = Vector2.zero;
            }
            else
            {
                RectTransform mbPanelRect = handheldMiniBoardPanel.GetComponent<RectTransform>();
                mbPanelRect.anchorMin = new Vector2(0.68f, 0.02f);
                mbPanelRect.anchorMax = new Vector2(0.98f, 0.40f);
                miniBoardRawImage = handheldMiniBoardPanel.transform.Find("RawImage_MiniBoard")?.GetComponent<RawImage>();
                miniBoardStatusText = handheldMiniBoardPanel.transform.Find("Text_MiniBoardStatus")?.GetComponent<Text>();
            }

            // Visualizer へ手元 UI 参照をバインド
            if (visualizer != null)
            {
                visualizer.MiniBoardRawImage = miniBoardRawImage;
                visualizer.MiniBoardStatusText = miniBoardStatusText;
                EditorUtility.SetDirty(visualizer);
            }

            // 手元用グラスローカル軸反転切り替えアクションバー (Y-Pos, Rot-X, Rot-Z, Rot-Y)
            // 手元用グラスローカル軸反転切り替え & 動的閾値再設定アクションバー
            GameObject actionBarGo = GameObject.Find("ActionBar_Handheld");
            if (actionBarGo == null)
            {
                actionBarGo = new GameObject("ActionBar_Handheld");
                actionBarGo.transform.SetParent(canvasHandheldGo.transform, false);

                Image barBg = actionBarGo.AddComponent<Image>();
                barBg.color = new Color(0.06f, 0.09f, 0.14f, 0.95f);

                RectTransform barRect = actionBarGo.GetComponent<RectTransform>();
                barRect.anchorMin = new Vector2(0.02f, 0.41f);
                barRect.anchorMax = new Vector2(0.98f, 0.47f);
                barRect.offsetMin = Vector2.zero;
                barRect.offsetMax = Vector2.zero;
            }

            // 既存のボタンをクリーンアップして 5 ボタンを再配置
            for (int b = actionBarGo.transform.childCount - 1; b >= 0; b--)
            {
                UnityEngine.Object.DestroyImmediate(actionBarGo.transform.GetChild(b).gameObject);
            }

            CreateAxisToggleButton(actionBarGo, "Btn_Toggle_YPos", "Y-Pos: INV", new Vector2(0.01f, 0.1f), new Vector2(0.19f, 0.9f), defaultFont, true);
            CreateAxisToggleButton(actionBarGo, "Btn_Toggle_RotX", "Rot-X: INV", new Vector2(0.21f, 0.1f), new Vector2(0.39f, 0.9f), defaultFont, true);
            CreateAxisToggleButton(actionBarGo, "Btn_Toggle_RotZ", "Rot-Z: INV", new Vector2(0.41f, 0.1f), new Vector2(0.59f, 0.9f), defaultFont, true);
            CreateAxisToggleButton(actionBarGo, "Btn_Toggle_RotY", "Rot-Y: NORM", new Vector2(0.61f, 0.1f), new Vector2(0.79f, 0.9f), defaultFont, false);
            CreateCustomButton(actionBarGo, "Btn_Recalibrate", "再設定", new Vector2(0.81f, 0.1f), new Vector2(0.99f, 0.9f), defaultFont, new Color(0.95f, 0.52f, 0.08f, 0.95f));

            tracker.BindAxisToggleButtons();

            // 10. AutoTracker & Logger への参照バインド
            tracker.PreviewRawImage = glassesRawImage;
            tracker.HandheldPreviewRawImage = handheldRawImage;
            tracker.BoardVisualizer = visualizer;
            tracker.enabled = true;

            screenLogger.LogTextComponent = glassesLogText;
            screenLogger.HandheldLogTextComponent = handheldLogText;

            // 変更をマーク & シーン保存
            EditorUtility.SetDirty(tracker);
            EditorUtility.SetDirty(screenLogger);
            Selection.activeGameObject = tracker.gameObject;

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("<color=#00FF88>SUCCESS: Othello AR Auto Tracker Setup Complete (Glasses Visualizer + Overhead Camera + Dual Monitor)!</color>");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("Setup Complete", 
                    "Othello AR Auto Tracker のセットアップが完了しました！\n\n" +
                    "・Glasses_Visualizer (Main Camera直下・Layer 8: GlassesVisualizer)\n" +
                    "  -> 赤(+X), 緑(+Y), 青(+Z)の3D三軸矢印と視線コーンにより向きと位置が一目で判明\n" +
                    "  -> Main Cameraからは除外(Cull)され、着用者の視界は遮りません\n" +
                    "・Overhead_Camera (全体を俯瞰するデフォルトカメラ)\n" +
                    "  -> 斜め上方からグラスとオセロ盤を常時監視・RT_OverheadViewへ描画\n" +
                    "・Canvas_BeamPro_Handheld (手元画面にOpenCV映像と3D俯瞰映像を左右並列表示)\n" +
                    "・OthelloBoard_AR_Target (半透明緑マテリアル + 前方黄色ポインター)\n\n" +
                    "すべて自動で生成・バインドされました。", "OK");
            }
        }

        /// <summary>
        /// 指定された名前のレイヤーが存在することを確認・無ければ空きスロットに設定
        /// </summary>
        private static int EnsureLayer(string layerName, int preferredIndex = 8)
        {
            try
            {
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                if (assets != null && assets.Length > 0)
                {
                    SerializedObject tagManager = new SerializedObject(assets[0]);
                    SerializedProperty layers = tagManager.FindProperty("layers");
                    if (layers != null && layers.isArray)
                    {
                        for (int i = 0; i < layers.arraySize; i++)
                        {
                            SerializedProperty elem = layers.GetArrayElementAtIndex(i);
                            if (elem.stringValue == layerName)
                            {
                                return i;
                            }
                        }

                        if (preferredIndex < layers.arraySize)
                        {
                            SerializedProperty elem = layers.GetArrayElementAtIndex(preferredIndex);
                            if (string.IsNullOrEmpty(elem.stringValue) || elem.stringValue == layerName)
                            {
                                elem.stringValue = layerName;
                                tagManager.ApplyModifiedProperties();
                                AssetDatabase.SaveAssets();
                                return preferredIndex;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OthelloARDebugSetup] TagManager inspection warning: {ex.Message}");
            }

            int id = LayerMask.NameToLayer(layerName);
            return id != -1 ? id : preferredIndex;
        }

        /// <summary>
        /// グラス (Main Camera) の 3D 位置・姿勢ビジュアライザーを生成
        /// </summary>
        private static GameObject SetupGlassesVisualizer(Camera mainCam, int layer)
        {
            Transform parent = mainCam.transform;
            Transform existing = parent.Find("Glasses_Visualizer");
            GameObject visualizerGo;
            if (existing != null)
            {
                visualizerGo = existing.gameObject;
            }
            else
            {
                visualizerGo = new GameObject("Glasses_Visualizer");
                visualizerGo.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(visualizerGo, "Create Glasses_Visualizer");
            }

            visualizerGo.transform.localPosition = Vector3.zero;
            visualizerGo.transform.localRotation = Quaternion.identity;
            visualizerGo.transform.localScale = Vector3.one;
            visualizerGo.layer = layer;

            // 既存の子オブジェクトを清掃して再生成
            for (int i = visualizerGo.transform.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(visualizerGo.transform.GetChild(i).gameObject);
            }

            Material frameMat = GetOrCreateMaterial("Assets/Materials/AR_GlassesFrame.mat", new Color(0.1f, 0.75f, 1f, 1f));
            Material redMat = GetOrCreateMaterial("Assets/Materials/AR_AxisRed.mat", new Color(1f, 0.2f, 0.2f, 1f));
            Material greenMat = GetOrCreateMaterial("Assets/Materials/AR_AxisGreen.mat", new Color(0.2f, 1f, 0.3f, 1f));
            Material blueMat = GetOrCreateMaterial("Assets/Materials/AR_AxisBlue.mat", new Color(0.1f, 0.5f, 1f, 1f));
            Material coneMat = GetOrCreateTransparentMaterial("Assets/Materials/AR_GazeCone.mat", new Color(0.1f, 0.85f, 1f, 0.3f));

            // A. グラス本体フレーム
            // フロントバー (左右 18cm)
            CreateVisualPart(visualizerGo, PrimitiveType.Cube, "FrontFrame", new Vector3(0f, 0f, 0f), new Vector3(0.18f, 0.03f, 0.025f), Quaternion.identity, frameMat, layer);
            // 左レンズリム
            CreateVisualPart(visualizerGo, PrimitiveType.Cube, "LeftRim", new Vector3(-0.045f, -0.012f, 0.012f), new Vector3(0.055f, 0.04f, 0.015f), Quaternion.identity, frameMat, layer);
            // 右レンズリム
            CreateVisualPart(visualizerGo, PrimitiveType.Cube, "RightRim", new Vector3(0.045f, -0.012f, 0.012f), new Vector3(0.055f, 0.04f, 0.015f), Quaternion.identity, frameMat, layer);
            // 左テンプル (つる、後方へ 12cm)
            CreateVisualPart(visualizerGo, PrimitiveType.Cube, "LeftTemple", new Vector3(-0.088f, 0f, -0.06f), new Vector3(0.01f, 0.015f, 0.12f), Quaternion.identity, frameMat, layer);
            // 右テンプル (つる、後方へ 12cm)
            CreateVisualPart(visualizerGo, PrimitiveType.Cube, "RightTemple", new Vector3(0.088f, 0f, -0.06f), new Vector3(0.01f, 0.015f, 0.12f), Quaternion.identity, frameMat, layer);

            // B. 3次元姿勢インジケーター (X:赤, Y:緑, Z:青)
            // Forward (+Z, 視線方向): 長さ 25cm の青い矢印
            CreateVisualPart(visualizerGo, PrimitiveType.Cylinder, "Axis_Forward_Z", new Vector3(0f, 0f, 0.13f), new Vector3(0.012f, 0.12f, 0.012f), Quaternion.Euler(90f, 0f, 0f), blueMat, layer);
            CreateVisualPart(visualizerGo, PrimitiveType.Sphere, "Tip_Z", new Vector3(0f, 0f, 0.25f), new Vector3(0.028f, 0.028f, 0.028f), Quaternion.identity, blueMat, layer);

            // Right (+X, グラス右方向): 長さ 15cm の赤い矢印
            CreateVisualPart(visualizerGo, PrimitiveType.Cylinder, "Axis_Right_X", new Vector3(0.08f, 0f, 0f), new Vector3(0.01f, 0.075f, 0.01f), Quaternion.Euler(0f, 0f, -90f), redMat, layer);
            CreateVisualPart(visualizerGo, PrimitiveType.Sphere, "Tip_X", new Vector3(0.16f, 0f, 0f), new Vector3(0.025f, 0.025f, 0.025f), Quaternion.identity, redMat, layer);

            // Up (+Y, グラス頭頂方向): 長さ 15cm の緑の矢印
            CreateVisualPart(visualizerGo, PrimitiveType.Cylinder, "Axis_Up_Y", new Vector3(0f, 0.08f, 0f), new Vector3(0.01f, 0.075f, 0.01f), Quaternion.identity, greenMat, layer);
            CreateVisualPart(visualizerGo, PrimitiveType.Sphere, "Tip_Y", new Vector3(0f, 0.16f, 0f), new Vector3(0.025f, 0.025f, 0.025f), Quaternion.identity, greenMat, layer);

            // C. 視錐台コーン (Gaze View Cone - 前方 40cm へ広がる視線インジケーター)
            CreateVisualPart(visualizerGo, PrimitiveType.Cylinder, "GazeCone", new Vector3(0f, 0f, 0.20f), new Vector3(0.005f, 0.20f, 0.005f), Quaternion.Euler(90f, 0f, 0f), coneMat, layer);

            // D. 3D Billboard Label
            GameObject labelGo = new GameObject("Label_Glasses_Head");
            labelGo.transform.SetParent(visualizerGo.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 0.20f, 0f);
            labelGo.layer = layer;
            TextMesh tm = labelGo.AddComponent<TextMesh>();
            tm.text = "XREAL Glasses (Head)";
            tm.fontSize = 24;
            tm.characterSize = 0.015f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0f, 0.95f, 1f, 1f);

            return visualizerGo;
        }

        /// <summary>
        /// 全体を俯瞰するデフォルトカメラ (Overhead_Camera) を生成・設定
        /// </summary>
        private static Camera SetupOverheadCamera(int glassesLayer)
        {
            GameObject camGo = GameObject.Find("Overhead_Camera");
            if (camGo == null)
            {
                camGo = new GameObject("Overhead_Camera");
                Undo.RegisterCreatedObjectUndo(camGo, "Create Overhead_Camera");
            }

            // アイソメトリック俯瞰位置 (X: 0.65m, Y: 2.1m, Z: -0.55m)
            // 角度: 下向き 55度、左向き 25度 (頭部とテーブルを斜め上方から捉える)
            camGo.transform.position = new Vector3(0.65f, 2.1f, -0.55f);
            camGo.transform.rotation = Quaternion.Euler(55f, -25f, 0f);

            Camera cam = camGo.GetComponent<Camera>();
            if (cam == null) cam = camGo.AddComponent<Camera>();

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.08f, 0.12f, 1.0f); // ダークネイビー
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.fieldOfView = 50f;
            cam.cullingMask = -1; // すべてのレイヤー (GlassesVisualizer含む) を描画
            cam.depth = 1;

            UniversalAdditionalCameraData uac = camGo.GetComponent<UniversalAdditionalCameraData>();
            if (uac == null) uac = camGo.AddComponent<UniversalAdditionalCameraData>();
            uac.allowXRRendering = false; // XR ステレオディスプレイには描画しない

            // 俯瞰用 RenderTexture の取得または作成
            string rtPath = "Assets/Materials/RT_OverheadView.renderTexture";
            RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
            if (rt == null)
            {
                rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32);
                rt.name = "RT_OverheadView";
                AssetDatabase.CreateAsset(rt, rtPath);
            }
            cam.targetTexture = rt;

            // 基準グリッドの生成
            SetupReferenceGrid();

            return cam;
        }

        /// <summary>
        /// 俯瞰カメラから空間スケールと移動量を即座に把握できる基準床・テーブルグリッド
        /// </summary>
        private static void SetupReferenceGrid()
        {
            GameObject gridRoot = GameObject.Find("Overhead_Reference_Grid");
            if (gridRoot == null)
            {
                gridRoot = new GameObject("Overhead_Reference_Grid");
                Undo.RegisterCreatedObjectUndo(gridRoot, "Create Overhead_Reference_Grid");
            }

            // 床面グリッド (Y = 0)
            Material gridMat = GetOrCreateTransparentMaterial("Assets/Materials/AR_GridFloor.mat", new Color(0.15f, 0.25f, 0.35f, 0.35f));
            Material originRed = GetOrCreateMaterial("Assets/Materials/AR_OriginRed.mat", new Color(1f, 0.1f, 0.2f, 0.8f));
            Material originBlue = GetOrCreateMaterial("Assets/Materials/AR_OriginBlue.mat", new Color(0.1f, 0.4f, 1f, 0.8f));

            // 床の 1m 四方薄型板 (スケール基準)
            Transform floorPlane = gridRoot.transform.Find("FloorScalePlane");
            if (floorPlane == null)
            {
                GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plane.name = "FloorScalePlane";
                plane.transform.SetParent(gridRoot.transform, false);
                plane.transform.position = new Vector3(0f, -0.005f, 0.5f);
                plane.transform.localScale = new Vector3(1.5f, 0.01f, 1.5f);
                if (gridMat != null) plane.GetComponent<Renderer>().sharedMaterial = gridMat;
                UnityEngine.Object.DestroyImmediate(plane.GetComponent<Collider>());
            }

            // 原点 X 軸 (赤ライン、長さ 1m)
            Transform axisX = gridRoot.transform.Find("World_Axis_X");
            if (axisX == null)
            {
                GameObject ax = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ax.name = "World_Axis_X";
                ax.transform.SetParent(gridRoot.transform, false);
                ax.transform.position = new Vector3(0.5f, 0.005f, 0f);
                ax.transform.localScale = new Vector3(0.01f, 0.5f, 0.01f);
                ax.transform.rotation = Quaternion.Euler(0f, 0f, -90f);
                if (originRed != null) ax.GetComponent<Renderer>().sharedMaterial = originRed;
                UnityEngine.Object.DestroyImmediate(ax.GetComponent<Collider>());
            }

            // 原点 Z 軸 (青ライン、長さ 1m)
            Transform axisZ = gridRoot.transform.Find("World_Axis_Z");
            if (axisZ == null)
            {
                GameObject az = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                az.name = "World_Axis_Z";
                az.transform.SetParent(gridRoot.transform, false);
                az.transform.position = new Vector3(0f, 0.005f, 0.5f);
                az.transform.localScale = new Vector3(0.01f, 0.5f, 0.01f);
                az.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                if (originBlue != null) az.GetComponent<Renderer>().sharedMaterial = originBlue;
                UnityEngine.Object.DestroyImmediate(az.GetComponent<Collider>());
            }
        }

        private static GameObject CreateVisualPart(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Quaternion rot, Material mat, int layer)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = rot;
            go.layer = layer;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            Collider col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);
            return go;
        }

        private static Material GetOrCreateMaterial(string path, Color color)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit") 
                             ?? Shader.Find("Mobile/Unlit (Supports Lightmap)") 
                             ?? Shader.Find("Unlit/Color");
                mat = new Material(shader) { color = color };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        private static Material GetOrCreateTransparentMaterial(string path, Color color)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") 
                         ?? Shader.Find("Mobile/Unlit (Supports Lightmap)") 
                         ?? Shader.Find("Unlit/Color");

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            // URP Transparent Unlit の設定
            mat.SetFloat("_Surface", 1.0f); // Transparent
            mat.SetFloat("_Blend", 0.0f);   // Alpha blending
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);       // ZWrite off
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static void CreateAxisToggleButton(
            GameObject parent,
            string btnName,
            string defaultLabel,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Font font,
            bool isInv
        ) {
            GameObject btnGo = new GameObject(btnName);
            btnGo.transform.SetParent(parent.transform, false);

            Image img = btnGo.AddComponent<Image>();
            img.color = isInv ? new Color(0.12f, 0.55f, 0.28f, 0.95f) : new Color(0.28f, 0.28f, 0.32f, 0.95f);

            Button btn = btnGo.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            btn.colors = cb;

            RectTransform rect = btnGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(btnGo.transform, false);
            Text txt = textGo.AddComponent<Text>();
            txt.font = font;
            txt.text = defaultLabel;
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;

            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private static void CreateCustomButton(
            GameObject parent,
            string btnName,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Font font,
            Color bgColor
        ) {
            GameObject btnGo = new GameObject(btnName);
            btnGo.transform.SetParent(parent.transform, false);

            Image img = btnGo.AddComponent<Image>();
            img.color = bgColor;

            Button btn = btnGo.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.9f, 0.9f, 0.9f);
            cb.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            btn.colors = cb;

            RectTransform rect = btnGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(btnGo.transform, false);
            Text txt = textGo.AddComponent<Text>();
            txt.font = font;
            txt.text = label;
            txt.fontSize = 15;
            txt.fontStyle = FontStyle.Bold;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;

            RectTransform textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }
    }
}
