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
    /// XREAL One Pro 用 AR オセロ盤面判定テストコントローラー
    /// 完全ハンズフリー動作: 現実のオセロ盤上に OpenCV の黄色枠線と丸判定結果を透過重ね合わせ (AR Overlay) 表示
    /// </summary>
    public class AROthelloBoardTestController : MonoBehaviour
    {
        [Header("Primary AR Overlay (Full-screen Transparent)")]
        [Tooltip("現実のオセロ盤上に黄色い線と丸を直接重ねる全画面透過 RawImage")]
        [SerializeField] public RawImage overlayRawImage;

        [Header("Mini Preview Monitor (Corner Camera View)")]
        [Tooltip("カメラの向きと捕捉状態を確認するための右下ミニモニター")]
        [SerializeField] public RawImage miniCameraRawImage;

        [Header("AR HUD Displays")]
        [SerializeField] public Text hudStatusText;
        [SerializeField] public Text hudStoneCountText;
        [SerializeField] public Text hudStatsText;

        [Header("AR Alignment Tuning (Parallax / FOV Adjustment)")]
        [Tooltip("カメラとグラスの視野角・視差の微調整用スケール")]
        [SerializeField] public Vector2 overlayScale = new Vector2(1.0f, 1.0f);
        [Tooltip("カメラとグラスの視差の微調整用オフセット (ピクセル単位)")]
        [SerializeField] public Vector2 overlayOffset = Vector2.zero;

        // OpenCV Recognizer
        private NativeOthelloRecognizer _recognizer;
        private Texture2D _opencvOverlayTexture;

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
        private bool _isBoardDetected = false;
        private int _blackCount = 0;
        private int _whiteCount = 0;

        // Performance & Stats
        private float _lastOpenCvLatencyMs = 0f;
        private float _fpsTimer = 0f;
        private int _fpsCounter = 0;
        private int _currentFps = 60;
        private ulong _totalFramesProcessed = 0;
        private bool _hasReceivedNativeFrame = false;

        // Fallback procedural animation
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
                WhiteThreshold = 165,
                TransparentBackground = true // AR透過モード
            };

            // 640x360 (16:9) 透過テクスチャを生成
            _opencvOverlayTexture = new Texture2D(640, 360, TextureFormat.RGBA32, false);
            Color32[] clearPix = new Color32[640 * 360];
            _opencvOverlayTexture.SetPixels32(clearPix);
            _opencvOverlayTexture.Apply();

            if (overlayRawImage != null)
            {
                overlayRawImage.texture = _opencvOverlayTexture;
                overlayRawImage.color = Color.white;
            }
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

        private void Start()
        {
            UpdateHudTexts();

            // 1. XREAL カメラの初期化試行
            TryStartXrealCamera();

            // 2. XREAL カメラが利用不可の場合は WebCamTexture を試行
            if (!_isXrealCameraActive)
            {
                TryStartWebCam();
            }

            // 3. 待機時用プロシージャルパターン
            _proceduralBoardTexture = CreateProceduralOthelloBoard(TEX_WIDTH, TEX_HEIGHT, 0f);

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
                        UnityEngine.Debug.Log("[AROthelloBoardTest] XREALRGBCameraTexture started successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AROthelloBoardTest] Failed to start XREAL camera: {ex.Message}");
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
                    _activeCameraSourceName = $"Camera ({devices[0].name})";
                }
                else
                {
                    _isWebCamActive = false;
                    _activeCameraSourceName = "Editor Simulation Pattern";
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[AROthelloBoardTest] Failed to start WebCam: {ex.Message}");
                _isWebCamActive = false;
                _activeCameraSourceName = "Editor Simulation Pattern";
            }
        }

        /// <summary>
        /// XREAL カメラドライバからのハードウェア YUV プレーン受信コールバック (超高速ゼロコピー)
        /// </summary>
        private void OnNativeCameraPlanesReceived(IntPtr yPtr, IntPtr uPtr, IntPtr vPtr, int width, int height)
        {
            try
            {
                Stopwatch sw = Stopwatch.StartNew();

                bool ok = _recognizer.TryRecognizeNativeYuvPlanes(
                    yPtr, uPtr, vPtr,
                    width, height,
                    _boardState, _corners,
                    _opencvOverlayTexture,
                    640, 360
                );

                _isBoardDetected = ok;
                CountDiscs();

                sw.Stop();
                _lastOpenCvLatencyMs = (float)sw.Elapsed.TotalMilliseconds;
                _totalFramesProcessed++;
                _hasReceivedNativeFrame = true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[AROthelloBoardTest] Error in OnNativeCameraPlanesReceived: {ex}");
            }
        }

        private void Update()
        {
            _fpsCounter++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer >= 0.5f)
            {
                _currentFps = Mathf.Max(1, Mathf.RoundToInt(_fpsCounter / _fpsTimer));
                _fpsCounter = 0;
                _fpsTimer = 0f;
            }

            // 微調整スケール・オフセットのリアルタイム適用
            if (overlayRawImage != null)
            {
                overlayRawImage.rectTransform.localScale = new Vector3(overlayScale.x, overlayScale.y, 1f);
                overlayRawImage.rectTransform.anchoredPosition = overlayOffset;
            }

            ExecuteOpenCvOnCurrentSource();
            UpdateHudTexts();
        }

        private void ExecuteOpenCvOnCurrentSource()
        {
            try
            {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
                if (_isXrealCameraActive && _xrealRgbCam != null)
                {
                    // 右下のミニモニターに生カメラ映像をレンダリング (GPU YUV Shader)
                    if (miniCameraRawImage != null && _yuvMaterial != null)
                    {
                        var yuv = _xrealRgbCam.GetYUVFormatTextures();
                        if (yuv != null && yuv[0] != null)
                        {
                            _yuvMaterial.mainTexture = yuv[0];
                            _yuvMaterial.SetTexture("_UTex", yuv[1]);
                            _yuvMaterial.SetTexture("_VTex", yuv[2]);
                            miniCameraRawImage.material = _yuvMaterial;
                            miniCameraRawImage.texture = yuv[0];
                        }
                    }

                    if (_hasReceivedNativeFrame) return;
                }
#endif

                if (_isWebCamActive && _webCamTexture != null)
                {
                    if (miniCameraRawImage != null)
                    {
                        miniCameraRawImage.material = null;
                        miniCameraRawImage.texture = _webCamTexture;
                    }

                    if (_webCamTexture.didUpdateThisFrame)
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        _isBoardDetected = _recognizer.TryRecognizeWebCam(_webCamTexture, _boardState, _corners, _opencvOverlayTexture);
                        CountDiscs();
                        sw.Stop();
                        _lastOpenCvLatencyMs = (float)sw.Elapsed.TotalMilliseconds;
                        _totalFramesProcessed++;
                    }
                    return;
                }

                // フォールバック: プロシージャルパターン
                _proceduralAnimTimer += Time.deltaTime;
                UpdateProceduralOthelloBoard(_proceduralBoardTexture, _proceduralAnimTimer);

                if (miniCameraRawImage != null && !_hasReceivedNativeFrame)
                {
                    miniCameraRawImage.material = null;
                    miniCameraRawImage.texture = _proceduralBoardTexture;
                }

                if (!_hasReceivedNativeFrame)
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    _isBoardDetected = _recognizer.TryRecognizeTexture(_proceduralBoardTexture, _boardState, _corners, _opencvOverlayTexture);
                    CountDiscs();
                    sw.Stop();
                    _lastOpenCvLatencyMs = (float)sw.Elapsed.TotalMilliseconds;
                    _totalFramesProcessed++;
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[AROthelloBoardTest] Error in ExecuteOpenCvOnCurrentSource: {ex}");
            }
        }

        private void CountDiscs()
        {
            int black = 0;
            int white = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    if (_boardState[y, x] == DiscColor.Black) black++;
                    else if (_boardState[y, x] == DiscColor.White) white++;
                }
            }
            _blackCount = black;
            _whiteCount = white;
        }

        private void UpdateHudTexts()
        {
            if (hudStatusText != null)
            {
                hudStatusText.text = _isBoardDetected
                    ? "<color=#00FF88>● 盤面捕捉中 (DETECTED)</color>"
                    : "<color=#FFCC00>○ 盤面探索中 (SEARCHING...)</color>";
            }

            if (hudStoneCountText != null)
            {
                int total = _blackCount + _whiteCount;
                hudStoneCountText.text = $"石の数:  <color=#333333>● 黒 {_blackCount}個</color>  |  <color=#FFFFFF>○ 白 {_whiteCount}個</color>  (合計 {total}個)";
            }

            if (hudStatsText != null)
            {
                hudStatsText.text = $"処理時間: {_lastOpenCvLatencyMs:F1} ms  |  {_currentFps} FPS  |  Frames: {_totalFramesProcessed}";
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

            Color32 tableColor = new Color32(200, 200, 200, 255);
            Color32 boardGreen = new Color32(25, 145, 55, 255);
            Color32 gridLineColor = new Color32(10, 60, 25, 255);
            Color32 blackDisc = new Color32(15, 15, 15, 255);
            Color32 whiteDisc = new Color32(240, 240, 240, 255);

            int boardMargin = width * 12 / 100;
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
            DrawDisc(boardMargin + 3 * cellW + cellW / 2, boardMargin + 3 * cellH + cellH / 2, discRadius, whiteDisc);
            DrawDisc(boardMargin + 4 * cellW + cellW / 2, boardMargin + 4 * cellH + cellH / 2, discRadius, whiteDisc);
            DrawDisc(boardMargin + 3 * cellW + cellW / 2, boardMargin + 4 * cellH + cellH / 2, discRadius, blackDisc);
            DrawDisc(boardMargin + 4 * cellW + cellW / 2, boardMargin + 3 * cellH + cellH / 2, discRadius, blackDisc);

            int movingX = 2 + (int)(Mathf.PingPong(animTime * 1.5f, 3f));
            DrawDisc(boardMargin + movingX * cellW + cellW / 2, boardMargin + 2 * cellH + cellH / 2, discRadius, blackDisc);

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
