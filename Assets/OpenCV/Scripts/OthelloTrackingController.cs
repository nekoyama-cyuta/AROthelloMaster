using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using OpenCvSharp;

namespace OpenCVForUnityCustom
{
    public class OthelloTrackingController : MonoBehaviour
    {
        [Header("Debug Settings")]
        [SerializeField] private bool showDebugUI = true;
        [SerializeField] private RawImage targetRawImage;
        [SerializeField] private Text targetStatusText;
        [SerializeField] private int device_num = 0; // 使用するカメラのデバイス番号

        private WebCamTexture _webCamTexture;
        private Mat _frameMat;
        private Mat _debugMat;
        private Texture2D _displayTexture;
        private OthelloBoardRecognizer _recognizer;
        
        private readonly DiscColor[,] _boardState = new DiscColor[8, 8];
        private Stopwatch _stopwatch;

        private void Awake()
        {
            OpenCvUtils.InitializeNativeDll(); // ネイティブDLLの初期化

            if (showDebugUI && targetRawImage == null)
            {
                BuildDebugUI(); // UIがなければ自動作成
            }
        }

        private void Start()
        {
            _frameMat = new Mat();
            _debugMat = new Mat();
            _recognizer = new OthelloBoardRecognizer(warpSize: 400);
            _stopwatch = new Stopwatch();

            // WebCam 開始 (640x480)
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices != null && devices.Length > 0)
            {
                _webCamTexture = new WebCamTexture(devices[device_num].name, 640, 480, 30);
                _webCamTexture.Play();
            }
            else
            {
                UnityEngine.Debug.LogWarning("[OthelloTracker] カメラが見つかりません。");
            }
        }

        private void Update()
        {
            if (_webCamTexture == null || !_webCamTexture.didUpdateThisFrame) return;

            // テクスチャの初期化（初回フレーム時）
            if (_displayTexture == null || _displayTexture.width != _webCamTexture.width || _displayTexture.height != _webCamTexture.height)
            {
                _displayTexture = new Texture2D(_webCamTexture.width, _webCamTexture.height, TextureFormat.RGBA32, false);
                if (targetRawImage != null) targetRawImage.texture = _displayTexture;
            }

            _stopwatch.Restart();

            // WebCam から Mat に変換
            OpenCvUtils.WebCamTextureToMat(_webCamTexture, _frameMat);

            // 盤面認識 (デバッグ表示が有効なら debugMat に描画結果を格納)
            bool isDetected = _recognizer.TryRecognizeBoard(_frameMat, _boardState, showDebugUI ? _debugMat : null);

            _stopwatch.Stop();

            // Update メソッド内のテキスト更新部分を少しリッチに修正
            if (showDebugUI)
            {
                OpenCvUtils.MatToTexture2D(_debugMat, _displayTexture);

                if (targetStatusText != null)
                {
                    if (isDetected)
                    {
                        int blackCount = 0;
                        int whiteCount = 0;
                        for (int y = 0; y < 8; y++)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                if (_boardState[y, x] == DiscColor.Black) blackCount++;
                                else if (_boardState[y, x] == DiscColor.White) whiteCount++;
                            }
                        }

                        targetStatusText.text = $"Status: <color=#00FF00>Detected</color> | Discs: <color=#333333>● {blackCount}</color> / <color=#FFFFFF>○ {whiteCount}</color> | Latency: {_stopwatch.Elapsed.TotalMilliseconds:F1} ms";
                    }
                    else
                    {
                        targetStatusText.text = $"Status: <color=#FF4444>Searching Board...</color> | Latency: {_stopwatch.Elapsed.TotalMilliseconds:F1} ms";
                    }
                }
            }

            if (isDetected)
            {
                OnBoardUpdated(_boardState);
            }
        }

        private void OnBoardUpdated(DiscColor[,] board)
        {
            // ここに合法手計算やXRIハイライトの更新ロジックを呼ぶ
        }

        private void BuildDebugUI()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject eventSystemObj = new GameObject("EventSystem");
                eventSystemObj.AddComponent<EventSystem>();
                eventSystemObj.AddComponent<StandaloneInputModule>();
            }

            GameObject canvasObj = new GameObject("Debug_Canvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasObj.AddComponent<GraphicRaycaster>();

            // 画面表示用 RawImage
            GameObject rawImgObj = new GameObject("DebugRawImage");
            rawImgObj.transform.SetParent(canvasObj.transform, false);
            targetRawImage = rawImgObj.AddComponent<RawImage>();
            RectTransform imgRect = rawImgObj.GetComponent<RectTransform>();
            imgRect.anchorMin = new Vector2(0.05f, 0.05f);
            imgRect.anchorMax = new Vector2(0.95f, 0.85f);
            imgRect.sizeDelta = Vector2.zero;

            // ステータス表示 Text
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            GameObject textObj = new GameObject("StatusText");
            textObj.transform.SetParent(canvasObj.transform, false);
            targetStatusText = textObj.AddComponent<Text>();
            targetStatusText.font = font;
            targetStatusText.fontSize = 22;
            targetStatusText.color = Color.white;
            targetStatusText.text = "Initializing Camera...";
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.05f, 0.88f);
            textRect.anchorMax = new Vector2(0.95f, 0.98f);
            textRect.sizeDelta = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (_webCamTexture != null && _webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
            }

            _frameMat?.Dispose();
            _debugMat?.Dispose();
            _recognizer?.Dispose();
        }
    }
}