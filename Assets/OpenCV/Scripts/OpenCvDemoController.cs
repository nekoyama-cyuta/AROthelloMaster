using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using OpenCvSharp;

namespace OpenCVForUnityCustom
{
    public enum OpenCVFilterMode
    {
        Original,
        Grayscale,
        GaussianBlur,
        CannyEdge,
        Threshold,
        Contours,
        CircleDetection,
        BitwiseNot
    }

    /// <summary>
    /// OpenCV の主要な機能を Unity 上で検証・体験するためのデモコントローラー
    /// UI がインスペクターで指定されていない場合でも、自動的に Canvas とボタンを動的生成します。
    /// </summary>
    public class OpenCvDemoController : MonoBehaviour
    {
        [Header("UI References (Optional - Auto generated if null)")]
        [SerializeField] private RawImage displayImage;
        [SerializeField] private Text statusText;
        [SerializeField] private Text versionText;
        [SerializeField] private Button originalButton;
        [SerializeField] private Button grayButton;
        [SerializeField] private Button blurButton;
        [SerializeField] private Button cannyButton;
        [SerializeField] private Button thresholdButton;
        [SerializeField] private Button contoursButton;
        [SerializeField] private Button circlesButton;
        [SerializeField] private Button invertButton;
        [SerializeField] private Button toggleWebCamButton;

        [Header("Settings")]
        [SerializeField] private int testImageWidth = 512;
        [SerializeField] private int testImageHeight = 512;

        private Texture2D _sourceTexture;
        private Texture2D _outputTexture;
        private WebCamTexture _webCamTexture;
        private OpenCVFilterMode _currentMode = OpenCVFilterMode.Original;
        private bool _useWebCam = false;
        private Mat _reusableWebCamMat;

        private void Awake()
        {
            // ネイティブ DLL の探索パスを初期化
            OpenCvUtils.InitializeNativeDll();

            // UI がインスペクターで未設定の場合は自動生成
            if (displayImage == null)
            {
                BuildRuntimeUI();
            }
        }

        private void Start()
        {
            // OpenCV バージョン取得
            try
            {
                string cvVersion = Cv2.GetVersionString();
                if (versionText != null)
                {
                    versionText.text = $"OpenCV Version: {cvVersion} (OpenCvSharp4)";
                }
                UnityEngine.Debug.Log($"[OpenCvDemo] OpenCV Loaded Successfully. Version: {cvVersion}");
            }
            catch (Exception ex)
            {
                string errMsg = $"[OpenCvDemo] Failed to load OpenCV native library: {ex.Message}";
                UnityEngine.Debug.LogError(errMsg);
                if (statusText != null)
                {
                    statusText.text = errMsg;
                }
                return;
            }

            // テスト用画像の生成 (幾何学図形やオセロ盤風の模様)
            _sourceTexture = CreateProceduralTestTexture(testImageWidth, testImageHeight);
            _outputTexture = new Texture2D(testImageWidth, testImageHeight, TextureFormat.RGBA32, false);

            if (displayImage != null)
            {
                displayImage.texture = _outputTexture;
            }

            // UIボタンのイベント登録
            SetupButtonListeners();

            // 初期フィルター適用
            ApplyFilter(_currentMode);
        }

        private void SetupButtonListeners()
        {
            if (originalButton != null) originalButton.onClick.AddListener(() => SetFilterMode(OpenCVFilterMode.Original));
            if (grayButton != null) grayButton.onClick.AddListener(() => SetFilterMode(OpenCVFilterMode.Grayscale));
            if (blurButton != null) blurButton.onClick.AddListener(() => SetFilterMode(OpenCVFilterMode.GaussianBlur));
            if (cannyButton != null) cannyButton.onClick.AddListener(() => SetFilterMode(OpenCVFilterMode.CannyEdge));
            if (thresholdButton != null) thresholdButton.onClick.AddListener(() => SetFilterMode(OpenCVFilterMode.Threshold));
            if (contoursButton != null) contoursButton.onClick.AddListener(() => SetFilterMode(OpenCVFilterMode.Contours));
            if (circlesButton != null) circlesButton.onClick.AddListener(() => SetFilterMode(OpenCVFilterMode.CircleDetection));
            if (invertButton != null) invertButton.onClick.AddListener(() => SetFilterMode(OpenCVFilterMode.BitwiseNot));
            if (toggleWebCamButton != null) toggleWebCamButton.onClick.AddListener(ToggleWebCam);
        }

        public void SetFilterMode(OpenCVFilterMode mode)
        {
            _currentMode = mode;
            if (!_useWebCam)
            {
                ApplyFilter(_currentMode);
            }
        }

        public void ToggleWebCam()
        {
            if (_useWebCam)
            {
                StopWebCam();
            }
            else
            {
                StartWebCam();
            }
        }

        private void StartWebCam()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices == null || devices.Length == 0)
            {
                if (statusText != null)
                {
                    statusText.text = "No WebCam found. Using procedural test pattern.";
                }
                UnityEngine.Debug.LogWarning("[OpenCvDemo] WebCam device not found.");
                return;
            }

            if (_webCamTexture == null)
            {
                _webCamTexture = new WebCamTexture(devices[0].name, 640, 480, 30);
            }

            _webCamTexture.Play();
            _useWebCam = true;

            if (toggleWebCamButton != null)
            {
                Text btnText = toggleWebCamButton.GetComponentInChildren<Text>();
                if (btnText != null) btnText.text = "Camera: ON";
            }
        }

        private void StopWebCam()
        {
            if (_webCamTexture != null && _webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
            }
            _useWebCam = false;

            if (toggleWebCamButton != null)
            {
                Text btnText = toggleWebCamButton.GetComponentInChildren<Text>();
                if (btnText != null) btnText.text = "Camera: OFF";
            }

            ApplyFilter(_currentMode);
        }

        private void Update()
        {
            if (_useWebCam && _webCamTexture != null && _webCamTexture.didUpdateThisFrame)
            {
                ProcessWebCamFrame();
            }
        }

        private void ProcessWebCamFrame()
        {
            if (_reusableWebCamMat == null)
            {
                _reusableWebCamMat = new Mat();
            }

            OpenCvUtils.WebCamTextureToMat(_webCamTexture, _reusableWebCamMat);
            ProcessAndDisplayMat(_reusableWebCamMat, _currentMode);
        }

        private void ApplyFilter(OpenCVFilterMode mode)
        {
            if (_sourceTexture == null) return;

            using (Mat srcMat = _sourceTexture.ToMat())
            {
                ProcessAndDisplayMat(srcMat, mode);
            }
        }

        private void ProcessAndDisplayMat(Mat srcMat, OpenCVFilterMode mode)
        {
            Stopwatch sw = Stopwatch.StartNew();
            using (Mat dstMat = new Mat())
            {
                switch (mode)
                {
                    case OpenCVFilterMode.Original:
                        srcMat.CopyTo(dstMat);
                        break;

                    case OpenCVFilterMode.Grayscale:
                        Cv2.CvtColor(srcMat, dstMat, ColorConversionCodes.BGRA2GRAY);
                        break;

                    case OpenCVFilterMode.GaussianBlur:
                        Cv2.GaussianBlur(srcMat, dstMat, new OpenCvSharp.Size(15, 15), 0);
                        break;

                    case OpenCVFilterMode.CannyEdge:
                        using (Mat gray = new Mat())
                        {
                            Cv2.CvtColor(srcMat, gray, ColorConversionCodes.BGRA2GRAY);
                            Cv2.Canny(gray, dstMat, 50, 150);
                        }
                        break;

                    case OpenCVFilterMode.Threshold:
                        using (Mat gray = new Mat())
                        {
                            Cv2.CvtColor(srcMat, gray, ColorConversionCodes.BGRA2GRAY);
                            Cv2.Threshold(gray, dstMat, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary);
                        }
                        break;

                    case OpenCVFilterMode.Contours:
                        srcMat.CopyTo(dstMat);
                        using (Mat gray = new Mat())
                        using (Mat binary = new Mat())
                        {
                            Cv2.CvtColor(srcMat, gray, ColorConversionCodes.BGRA2GRAY);
                            Cv2.Threshold(gray, binary, 100, 255, ThresholdTypes.Binary);
                            Cv2.FindContours(binary, out Point[][] contours, out HierarchyIndex[] hierarchy,
                                RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

                            Cv2.DrawContours(dstMat, contours, -1, new Scalar(0, 255, 0, 255), 2);
                        }
                        break;

                    case OpenCVFilterMode.CircleDetection:
                        srcMat.CopyTo(dstMat);
                        using (Mat gray = new Mat())
                        {
                            Cv2.CvtColor(srcMat, gray, ColorConversionCodes.BGRA2GRAY);
                            Cv2.GaussianBlur(gray, gray, new OpenCvSharp.Size(9, 9), 2);

                            CircleSegment[] circles = Cv2.HoughCircles(
                                gray,
                                HoughModes.Gradient,
                                dp: 1.2,
                                minDist: 30,
                                param1: 100,
                                param2: 30,
                                minRadius: 10,
                                maxRadius: 120
                            );

                            foreach (var circle in circles)
                            {
                                Point center = new Point((int)circle.Center.X, (int)circle.Center.Y);
                                int radius = (int)circle.Radius;
                                Cv2.Circle(dstMat, center, radius, new Scalar(0, 0, 255, 255), 3);
                                Cv2.Circle(dstMat, center, 3, new Scalar(255, 0, 0, 255), -1);
                            }
                        }
                        break;

                    case OpenCVFilterMode.BitwiseNot:
                        Cv2.BitwiseNot(srcMat, dstMat);
                        break;
                }

                sw.Stop();

                OpenCvUtils.MatToTexture2D(dstMat, _outputTexture);

                if (statusText != null)
                {
                    statusText.text = $"Mode: {mode} | Size: {dstMat.Width}x{dstMat.Height} | Time: {sw.Elapsed.TotalMilliseconds:F2} ms";
                }
            }
        }

        private Texture2D CreateProceduralTestTexture(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color32 greenBoard = new Color32(28, 128, 48, 255);
            Color32 gridLine = new Color32(10, 50, 18, 255);
            Color32 blackPiece = new Color32(20, 20, 20, 255);
            Color32 whitePiece = new Color32(235, 235, 235, 255);
            Color32 redAccent = new Color32(220, 40, 40, 255);

            Color32[] pixels = new Color32[width * height];
            int cellW = width / 8;
            int cellH = height / 8;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isBorder = (x % cellW < 2) || (y % cellH < 2) || x < 2 || x >= width - 2 || y < 2 || y >= height - 2;
                    pixels[y * width + x] = isBorder ? gridLine : greenBoard;
                }
            }

            void DrawDisk(int cx, int cy, int radius, Color32 color)
            {
                int r2 = radius * radius;
                for (int dy = -radius; dy <= radius; dy++)
                {
                    int py = cy + dy;
                    if (py < 0 || py >= height) continue;
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int px = cx + dx;
                        if (px < 0 || px >= width) continue;
                        if (dx * dx + dy * dy <= r2)
                        {
                            pixels[py * width + px] = color;
                        }
                    }
                }
            }

            int pieceR = cellW * 38 / 100;
            DrawDisk(3 * cellW + cellW / 2, 3 * cellH + cellH / 2, pieceR, whitePiece);
            DrawDisk(4 * cellW + cellW / 2, 4 * cellH + cellH / 2, pieceR, whitePiece);
            DrawDisk(3 * cellW + cellW / 2, 4 * cellH + cellH / 2, pieceR, blackPiece);
            DrawDisk(4 * cellW + cellW / 2, 3 * cellH + cellH / 2, pieceR, blackPiece);

            DrawDisk(1 * cellW + cellW / 2, 1 * cellH + cellH / 2, pieceR, blackPiece);
            DrawDisk(6 * cellW + cellW / 2, 6 * cellH + cellH / 2, pieceR, whitePiece);
            DrawDisk(1 * cellW + cellW / 2, 6 * cellH + cellH / 2, pieceR / 2, redAccent);

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// ランタイムで完全に独立した UI を構築します
        /// </summary>
        private void BuildRuntimeUI()
        {
            // EventSystem が無ければ作成
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject eventSystemObj = new GameObject("EventSystem");
                eventSystemObj.AddComponent<EventSystem>();
                eventSystemObj.AddComponent<StandaloneInputModule>();
            }

            // Canvas 作成
            GameObject canvasObj = new GameObject("OpenCV_Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasObj.AddComponent<GraphicRaycaster>();

            // 背景パネル
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(canvasObj.transform, false);
            Image bgImage = bgObj.AddComponent<Image>();
            bgImage.color = new Color(0.12f, 0.12f, 0.14f, 1f);
            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            // タイトル & バージョン テキスト
            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (defaultFont == null)
            {
                defaultFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(canvasObj.transform, false);
            Text title = titleObj.AddComponent<Text>();
            title.font = defaultFont;
            title.fontSize = 26;
            title.fontStyle = FontStyle.Bold;
            title.color = Color.white;
            title.text = "OpenCV for Unity Demo (OpenCvSharp4)";
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(30f, -20f);
            titleRect.sizeDelta = new Vector2(-60f, 40f);

            // バージョン テキスト
            GameObject verObj = new GameObject("VersionText");
            verObj.transform.SetParent(canvasObj.transform, false);
            versionText = verObj.AddComponent<Text>();
            versionText.font = defaultFont;
            versionText.fontSize = 16;
            versionText.color = new Color(0.7f, 0.9f, 1f);
            versionText.text = "OpenCV Version: Initializing...";
            RectTransform verRect = verObj.GetComponent<RectTransform>();
            verRect.anchorMin = new Vector2(0f, 1f);
            verRect.anchorMax = new Vector2(1f, 1f);
            verRect.pivot = new Vector2(0.5f, 1f);
            verRect.anchoredPosition = new Vector2(30f, -60f);
            verRect.sizeDelta = new Vector2(-60f, 30f);

            // ステータス テキスト
            GameObject statusObj = new GameObject("StatusText");
            statusObj.transform.SetParent(canvasObj.transform, false);
            statusText = statusObj.AddComponent<Text>();
            statusText.font = defaultFont;
            statusText.fontSize = 18;
            statusText.color = Color.yellow;
            statusText.text = "Mode: Original";
            RectTransform statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.anchoredPosition = new Vector2(30f, -90f);
            statusRect.sizeDelta = new Vector2(-60f, 30f);

            // 画像表示領域 (RawImage)
            GameObject rawImgObj = new GameObject("DisplayImage");
            rawImgObj.transform.SetParent(canvasObj.transform, false);
            displayImage = rawImgObj.AddComponent<RawImage>();
            RectTransform imgRect = rawImgObj.GetComponent<RectTransform>();
            imgRect.anchorMin = new Vector2(0.35f, 0.05f);
            imgRect.anchorMax = new Vector2(0.95f, 0.85f);
            imgRect.anchoredPosition = Vector2.zero;
            imgRect.sizeDelta = Vector2.zero;

            // ボタン配置用コンテナ
            GameObject btnPanel = new GameObject("ButtonPanel");
            btnPanel.transform.SetParent(canvasObj.transform, false);
            RectTransform panelRect = btnPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.05f, 0.05f);
            panelRect.anchorMax = new Vector2(0.30f, 0.85f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = Vector2.zero;

            VerticalLayoutGroup layout = btnPanel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childForceExpandHeight = false;
            layout.childControlHeight = true;

            // ボタン生成ヘルパー
            Button CreateBtn(string label, Color normalCol)
            {
                GameObject bObj = new GameObject($"Btn_{label}");
                bObj.transform.SetParent(btnPanel.transform, false);
                Image bImg = bObj.AddComponent<Image>();
                bImg.color = normalCol;
                Button btn = bObj.AddComponent<Button>();

                LayoutElement le = bObj.AddComponent<LayoutElement>();
                le.minHeight = 44f;
                le.preferredHeight = 44f;

                GameObject tObj = new GameObject("Text");
                tObj.transform.SetParent(bObj.transform, false);
                Text bTxt = tObj.AddComponent<Text>();
                bTxt.font = defaultFont;
                bTxt.fontSize = 16;
                bTxt.fontStyle = FontStyle.Bold;
                bTxt.alignment = TextAnchor.MiddleCenter;
                bTxt.color = Color.white;
                bTxt.text = label;
                RectTransform tRect = tObj.GetComponent<RectTransform>();
                tRect.anchorMin = Vector2.zero;
                tRect.anchorMax = Vector2.one;
                tRect.sizeDelta = Vector2.zero;

                return btn;
            }

            Color primaryColor = new Color(0.2f, 0.45f, 0.8f);
            Color accentColor = new Color(0.7f, 0.3f, 0.2f);
            Color camColor = new Color(0.2f, 0.6f, 0.35f);

            originalButton = CreateBtn("1. Original (元画像)", primaryColor);
            grayButton = CreateBtn("2. Grayscale (灰度化)", primaryColor);
            blurButton = CreateBtn("3. Gaussian Blur (ぼかし)", primaryColor);
            cannyButton = CreateBtn("4. Canny Edge (エッジ)", primaryColor);
            thresholdButton = CreateBtn("5. Threshold (二値化)", primaryColor);
            contoursButton = CreateBtn("6. Contours (輪郭抽出)", primaryColor);
            circlesButton = CreateBtn("7. Circles (円・石検出)", primaryColor);
            invertButton = CreateBtn("8. Invert (色反転)", primaryColor);
            toggleWebCamButton = CreateBtn("Camera: OFF (Click to ON)", camColor);
        }

        private void OnDestroy()
        {
            if (_webCamTexture != null && _webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
            }
            if (_reusableWebCamMat != null && !_reusableWebCamMat.IsDisposed)
            {
                _reusableWebCamMat.Dispose();
            }
        }
    }
}
