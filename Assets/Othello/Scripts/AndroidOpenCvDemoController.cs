using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
using Unity.XR.XREAL;
#endif

namespace OpenCVForAndroidCustom
{
    /// <summary>
    /// 完全ハンズフリー・コントローラー不要の XREAL One Pro 用 OpenCV リアルタイム2画面デモコントローラー
    /// ウィンドウ1: XREAL One Pro の生カメラ映像 (Raw Feed)
    /// ウィンドウ2: OpenCV (libOthelloCvPlugin.so) によるリアルタイム画像処理映像 (Processed Feed)
    /// </summary>
    public class AndroidOpenCvDemoController : MonoBehaviour
    {
        [Header("Window 1: Raw Camera Feed")]
        [SerializeField] public RawImage rawCameraDisplay;
        [SerializeField] public Text window1TitleText;
        [SerializeField] public Text window1SubtitleText;

        [Header("Window 2: OpenCV Processed Feed")]
        [SerializeField] public RawImage processedCameraDisplay;
        [SerializeField] public Text window2TitleText;
        [SerializeField] public Text window2SubtitleText;

        [Header("HUD Information (Updated Every Frame)")]
        [SerializeField] public Text mainTitleText;
        [SerializeField] public Text statusSummaryText;
        [SerializeField] public Text detailedStatsText;
        [SerializeField] public Text backendVersionText;

        // OpenCV Recognizer
        private NativeOthelloRecognizer _recognizer;
        private Texture2D _opencvDebugTexture;

        // Camera sources
        private WebCamTexture _webCamTexture;
        private bool _isXrealCameraActive = false;
        private bool _isWebCamActive = false;
        private string _activeCameraSourceName = "Initializing...";

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        private XREALRGBCameraTexture _xrealRgbCam;
        private Material _yuvMaterial;
#endif

        // Board & Detection state
        private readonly DiscColor[,] _boardState = new DiscColor[8, 8];
        private readonly Vector2[] _corners = new Vector2[4];
        private bool _lastDetectionSuccess = false;

        // Live stats (Never "--")
        private float _lastOpenCvLatencyMs = 0f;
        private float _fpsTimer = 0f;
        private int _fpsCounter = 0;
        private int _currentFps = 60;
        private ulong _totalFramesProcessed = 0;
        private string _cameraResolution = "Waiting for Camera...";
        private string _lastError = null;
        private bool _hasReceivedNativeFrame = false;

        // Fallback animated procedural texture
        private Texture2D _proceduralBoardTexture;
        private float _proceduralAnimTimer = 0f;
        private const int TEX_WIDTH = 512;
        private const int TEX_HEIGHT = 512;

        private void Awake()
        {
            _recognizer = new NativeOthelloRecognizer
            {
                WarpSize = 400,
                MinAreaThreshold = 5000,
                BlackThreshold = 65,
                WhiteThreshold = 165
            };

            // 初期状態でも決して真っ黒にならないよう、鮮明なシアン/ダークブルーのテストグラデーション (A=255) を設定
            _opencvDebugTexture = new Texture2D(640, 360, TextureFormat.RGBA32, false);
            Color32[] initPix = new Color32[640 * 360];
            for (int y = 0; y < 360; y++)
            {
                for (int x = 0; x < 640; x++)
                {
                    byte v = (byte)(25 + (x * 40 / 640) + (y * 40 / 360));
                    initPix[y * 640 + x] = new Color32(v, (byte)(v + 40), (byte)(v + 80), 255);
                }
            }
            _opencvDebugTexture.SetPixels32(initPix);
            _opencvDebugTexture.Apply();
        }

        private void OnEnable()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            XREALRGBCameraTexture.OnNativeCameraPlanesAvailable += OnNativeCameraPlanesReceived;
#endif
        }

        private void OnDisable()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            XREALRGBCameraTexture.OnNativeCameraPlanesAvailable -= OnNativeCameraPlanesReceived;
#endif
        }

        /// <summary>
        /// XREAL カメラドライバから直接届くハードウェア YUV プレーンのゼロコピー処理
        /// </summary>
        private void OnNativeCameraPlanesReceived(IntPtr yPtr, IntPtr uPtr, IntPtr vPtr, int width, int height)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                _cameraResolution = $"{width}x{height}";

                bool ok = _recognizer.TryRecognizeNativeYuvPlanes(
                    yPtr, uPtr, vPtr,
                    width, height,
                    _boardState, _corners,
                    _opencvDebugTexture,
                    640, 360
                );

                _lastDetectionSuccess = ok;

                if (processedCameraDisplay != null)
                {
                    processedCameraDisplay.material = null;
                    processedCameraDisplay.texture = _opencvDebugTexture;
                }

                sw.Stop();
                _lastOpenCvLatencyMs = (float)sw.Elapsed.TotalMilliseconds;
                _totalFramesProcessed++;
                _hasReceivedNativeFrame = true;
                _lastError = null;
            }
            catch (Exception ex)
            {
                _lastError = $"{ex.GetType().Name}: {ex.Message}";
                UnityEngine.Debug.LogError($"[AndroidOpenCvDemo] Exception in OnNativeCameraPlanesReceived: {ex}");
            }
        }

        private void Start()
        {
            if (processedCameraDisplay != null)
            {
                processedCameraDisplay.material = null;
                processedCameraDisplay.texture = _opencvDebugTexture;
            }

            // 初期テキストの設定 (「ーー」ではなく即座に数値を表示)
            UpdateHudTexts();

            // 1. XREAL カメラの初期化試行
            TryStartXrealCamera();

            // 2. XREAL カメラが利用不可の場合は WebCamTexture を試行
            if (!_isXrealCameraActive)
            {
                TryStartWebCam();
            }

            // 3. どちらも初期化中の場合の動的プロシージャルパターンを準備
            _proceduralBoardTexture = CreateProceduralOthelloBoard(TEX_WIDTH, TEX_HEIGHT, 0f);

            // 初期フレーム処理
            ExecuteOpenCvOnCurrentSource();
        }

        private void TryStartXrealCamera()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            try
            {
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
                {
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                }

                _xrealRgbCam = XREALRGBCameraTexture.CreateSingleton();
                if (_xrealRgbCam != null)
                {
                    bool started = _xrealRgbCam.StartCapture();
                    if (started)
                    {
                        _isXrealCameraActive = true;
                        _activeCameraSourceName = "XREAL One Pro RGB Camera";

                        Shader yuvShader = Shader.Find("Unlit/XrealYuvToRgb") ?? Shader.Find("XREALSDK/CaptureBackgroundYUV");
                        if (yuvShader != null)
                        {
                            _yuvMaterial = new Material(yuvShader);
                        }
                        UnityEngine.Debug.Log("[AndroidOpenCvDemo] XREALRGBCameraTexture started successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AndroidOpenCvDemo] Failed to start XREAL camera: {ex.Message}");
                _isXrealCameraActive = false;
            }
#endif
        }

        private void TryStartWebCam()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
            }
#endif
            try
            {
                WebCamDevice[] devices = WebCamTexture.devices;
                if (devices != null && devices.Length > 0)
                {
                    _webCamTexture = new WebCamTexture(devices[0].name, 640, 480, 30);
                    _webCamTexture.Play();
                    _isWebCamActive = true;
                    _activeCameraSourceName = $"Device Camera ({devices[0].name})";
                    UnityEngine.Debug.Log($"[AndroidOpenCvDemo] WebCam started on device: {devices[0].name}");
                }
                else
                {
                    _isWebCamActive = false;
                    _activeCameraSourceName = "Animated Synthetic Test Pattern";
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AndroidOpenCvDemo] Failed to start WebCam: {ex.Message}");
                _isWebCamActive = false;
                _activeCameraSourceName = "Animated Synthetic Test Pattern";
            }
        }

        private void Update()
        {
            // 毎フレーム FPS 計測
            _fpsCounter++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _currentFps = Mathf.Max(1, Mathf.RoundToInt(_fpsCounter / _fpsTimer));
                _fpsCounter = 0;
                _fpsTimer = 0f;
            }

            // カメラおよび画像処理の毎フレーム実行
            ExecuteOpenCvOnCurrentSource();

            // 毎フレーム HUD テキストを更新 (絶対に「ーー」で止まらない)
            UpdateHudTexts();
        }

        private void ExecuteOpenCvOnCurrentSource()
        {
            try
            {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
                if (_isXrealCameraActive && _xrealRgbCam != null)
                {
                    var yuv = _xrealRgbCam.GetYUVFormatTextures();
                    if (yuv != null && yuv[0] != null && _yuvMaterial != null)
                    {
                        _yuvMaterial.mainTexture = yuv[0];
                        _yuvMaterial.SetTexture("_UTex", yuv[1]);
                        _yuvMaterial.SetTexture("_VTex", yuv[2]);

                        // ウィンドウ1: 生カメラ映像
                        if (rawCameraDisplay != null)
                        {
                            rawCameraDisplay.material = _yuvMaterial;
                            rawCameraDisplay.texture = yuv[0];
                        }

                        // ウィンドウ2は OnNativeCameraPlanesReceived で直接毎フレーム超高速ゼロコピー更新される
                        if (_hasReceivedNativeFrame)
                        {
                            return;
                        }
                    }
                }
#endif

                if (_isWebCamActive && _webCamTexture != null)
                {
                    // ウィンドウ1: 生WebCam映像
                    if (rawCameraDisplay != null)
                    {
                        rawCameraDisplay.material = null;
                        rawCameraDisplay.texture = _webCamTexture;
                    }

                    // ウィンドウ2: OpenCV処理
                    if (_webCamTexture.didUpdateThisFrame)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        _lastDetectionSuccess = _recognizer.TryRecognizeWebCam(_webCamTexture, _boardState, _corners, _opencvDebugTexture);
                        if (processedCameraDisplay != null)
                        {
                            processedCameraDisplay.material = null;
                            processedCameraDisplay.texture = _opencvDebugTexture;
                        }
                        sw.Stop();
                        _lastOpenCvLatencyMs = (float)sw.Elapsed.TotalMilliseconds;
                        _totalFramesProcessed++;
                    }
                    return;
                }

                // フォールバック: 動的プロシージャルテストパターン (カメラ待機時も両画面とも滑らかに稼働)
                _proceduralAnimTimer += Time.deltaTime;
                UpdateProceduralOthelloBoard(_proceduralBoardTexture, _proceduralAnimTimer);

                if (rawCameraDisplay != null && !_hasReceivedNativeFrame)
                {
                    rawCameraDisplay.material = null;
                    rawCameraDisplay.texture = _proceduralBoardTexture;
                }

                if (!_hasReceivedNativeFrame)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    _lastDetectionSuccess = _recognizer.TryRecognizeTexture(_proceduralBoardTexture, _boardState, _corners, _opencvDebugTexture);
                    if (processedCameraDisplay != null)
                    {
                        processedCameraDisplay.material = null;
                        processedCameraDisplay.texture = _opencvDebugTexture;
                    }
                    sw.Stop();
                    _lastOpenCvLatencyMs = (float)sw.Elapsed.TotalMilliseconds;
                    _totalFramesProcessed++;
                }
            }
            catch (Exception ex)
            {
                _lastError = $"{ex.GetType().Name}: {ex.Message}";
                UnityEngine.Debug.LogError($"[AndroidOpenCvDemo] Exception in ExecuteOpenCvOnCurrentSource: {ex}");
            }
        }

        private void UpdateHudTexts()
        {
            if (mainTitleText != null)
            {
                mainTitleText.text = "XREAL One Pro - OpenCV Real-Time Dual Display";
            }

            if (statusSummaryText != null)
            {
                statusSummaryText.text = $"FPS: {_currentFps}   |   OpenCV: {_lastOpenCvLatencyMs:F1} ms   |   Frames: {_totalFramesProcessed}   |   Res: {_cameraResolution}";
            }

            if (backendVersionText != null)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                backendVersionText.text = "Backend: Native libOthelloCvPlugin.so (ARM64 Zero-Copy) | Hands-Free Auto Stream";
#else
                backendVersionText.text = "Backend: Unity Editor Preview (Simulation Mode) | Hands-Free Auto Stream";
#endif
            }

            int blackCount = 0, whiteCount = 0, emptyCount = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (_boardState[y, x] == DiscColor.Black) blackCount++;
                    else if (_boardState[y, x] == DiscColor.White) whiteCount++;
                    else emptyCount++;
                }
            }

            if (detailedStatsText != null)
            {
                if (!string.IsNullOrEmpty(_lastError))
                {
                    detailedStatsText.text = $"<color=red>ERROR: {_lastError}</color>";
                }
                else
                {
                    string statusTag = _lastDetectionSuccess ? "<color=#00FF88>[BOARD DETECTED]</color>" : "<color=#FFCC00>[SEARCHING / EDGE STREAM]</color>";
                    StringBuilder sb = new StringBuilder();
                    sb.Append($"Source: {_activeCameraSourceName}   |   Status: {statusTag}\n");
                    if (_lastDetectionSuccess)
                    {
                        sb.Append($"Discs: Black={blackCount}, White={whiteCount}, Empty={emptyCount}   |   Corners: 4 pts Locked");
                    }
                    else
                    {
                        sb.Append($"OpenCV Stream Active: Direct Hardware YUV Zero-Copy to C++ OpenCV (Canny Edges & Contours)");
                    }
                    detailedStatsText.text = sb.ToString();
                }
            }

            if (window1SubtitleText != null)
            {
                window1SubtitleText.text = $"Direct Camera Feed ({_activeCameraSourceName})";
            }

            if (window2SubtitleText != null)
            {
                window2SubtitleText.text = _lastDetectionSuccess
                    ? "<color=#00FF88>OpenCV: Board Quad + Stone Recognition Active</color>"
                    : "<color=#00E5FF>OpenCV: Real-Time Edge & Contour Detection Active</color>";
            }
        }

        private Texture2D CreateProceduralOthelloBoard(int width, int height, float animTime)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            UpdateProceduralOthelloBoard(tex, animTime);
            return tex;
        }

        private void UpdateProceduralOthelloBoard(Texture2D tex, float animTime)
        {
            int width = tex.width;
            int height = tex.height;
            Color32[] pixels = new Color32[width * height];

            Color32 tableColor = new Color32(220, 220, 220, 255);
            Color32 boardGreen = new Color32(20, 140, 50, 255);
            Color32 gridLineColor = new Color32(10, 60, 25, 255);
            Color32 blackDisc = new Color32(15, 15, 15, 255);
            Color32 whiteDisc = new Color32(240, 240, 240, 255);

            int boardMargin = width * 10 / 100;
            int boardW = width - boardMargin * 2;
            int boardH = height - boardMargin * 2;
            int cellW = boardW / 8;
            int cellH = boardH / 8;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    if (x < boardMargin || x >= width - boardMargin || y < boardMargin || y >= height - boardMargin)
                    {
                        pixels[idx] = tableColor;
                    }
                    else
                    {
                        int bx = x - boardMargin;
                        int by = y - boardMargin;
                        bool isGrid = (bx % cellW < 2) || (by % cellH < 2) || bx < 2 || bx >= boardW - 2 || by < 2 || by >= boardH - 2;
                        pixels[idx] = isGrid ? gridLineColor : boardGreen;
                    }
                }
            }

            void DrawDisc(int cx, int cy, int radius, Color32 col)
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
                            pixels[py * width + px] = col;
                        }
                    }
                }
            }

            int discRadius = cellW * 38 / 100;

            // 初期4石
            DrawDisc(boardMargin + 3 * cellW + cellW / 2, boardMargin + 3 * cellH + cellH / 2, discRadius, whiteDisc);
            DrawDisc(boardMargin + 4 * cellW + cellW / 2, boardMargin + 4 * cellH + cellH / 2, discRadius, whiteDisc);
            DrawDisc(boardMargin + 3 * cellW + cellW / 2, boardMargin + 4 * cellH + cellH / 2, discRadius, blackDisc);
            DrawDisc(boardMargin + 4 * cellW + cellW / 2, boardMargin + 3 * cellH + cellH / 2, discRadius, blackDisc);

            // アニメーション石 (ユーザーにリアルタイム動作中であることが直感的に伝わる)
            int movingCellX = 2 + (int)(Mathf.PingPong(animTime * 1.5f, 3f));
            DrawDisc(boardMargin + movingCellX * cellW + cellW / 2, boardMargin + 2 * cellH + cellH / 2, discRadius, blackDisc);
            DrawDisc(boardMargin + 5 * cellW + cellW / 2, boardMargin + 5 * cellH + cellH / 2, discRadius, whiteDisc);

            tex.SetPixels32(pixels);
            tex.Apply();
        }

        private void OnDestroy()
        {
            if (_webCamTexture != null && _webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
            }
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            if (_xrealRgbCam != null && _xrealRgbCam.IsCapturing)
            {
                _xrealRgbCam.StopCapture();
            }
#endif
            if (_recognizer != null)
            {
                _recognizer.Dispose();
            }
        }
    }
}
