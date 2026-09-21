using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
using Unity.XR.XREAL;
#endif

namespace AROthello
{
    /// <summary>
    /// オセロ盤の自動認識・3D姿勢推定 (SolvePnP) & AR自動追従トラッカー
    /// 1. NativeOthelloRecognizer で盤面の4隅を検出
    /// 2. SolvePnP (OpenCV ネイティブ または 平面ホモグラフィ分解) でカメラ相対 6DoF 位置姿勢を算出
    /// 3. 右手座標系から Unity の左手座標系へ変換し、ワールド座標を算出
    /// 4. 盤面の厚み (+0.018m) を加味し、Lerp / Slerp で平滑化してターゲット Transform を追従
    /// </summary>
    public class OthelloAutoTracker : MonoBehaviour
    {
        [Header("Target AR Object")]
        [Tooltip("実物のオセロ盤の上に追従させる 3D AR オブジェクトの Transform")]
        [SerializeField] private Transform targetBoardTransform;

        [Header("Camera & Tracking Settings")]
        [Tooltip("トラッキング基準となるカメラ (未設定時は Camera.main を使用)")]
        [SerializeField] private Camera trackingCamera;

        [Tooltip("デバッグ映像プレビュー用の RawImage (XR グラス HUD 用)")]
        [SerializeField] private RawImage previewRawImage;

        [Tooltip("手元 Beam Pro 画面プレビュー用の RawImage (手元操作用)")]
        [SerializeField] private RawImage handheldPreviewRawImage;

        [Tooltip("使用する WebCam デバイスのインデックス")]
        [SerializeField] private int webcamDeviceIndex = 0;

        [Tooltip("カメラキャプチャ解像度")]
        [SerializeField] private Vector2Int requestedResolution = new Vector2Int(640, 480);

        [Header("Board Physical Specifications")]
        [Tooltip("オセロ盤の幅 (22.8cm = 0.228m)")]
        [SerializeField] private float boardWidthMeters = 0.228f;

        [Tooltip("オセロ盤の奥行き (22.8cm = 0.228m)")]
        [SerializeField] private float boardDepthMeters = 0.228f;

        [Tooltip("オセロ盤の厚み (1.8cm = 0.018m)")]
        [SerializeField] private float boardThicknessMeters = 0.018f;

        [Header("Board Visualizer")]
        [Tooltip("3D ワイヤーフレーム盤・石・白石合法手ハイライト・手元 2D ミニ盤面を制御するビジュアライザー")]
        [SerializeField] private OthelloBoardVisualizer boardVisualizer;

        public OthelloBoardVisualizer BoardVisualizer
        {
            get => boardVisualizer;
            set => boardVisualizer = value;
        }

        [Header("Smoothing & Stability")]
        [Tooltip("位置のスムージング速度 (Lerp 係数)")]
        [SerializeField] private float positionLerpSpeed = 12.0f;

        [Tooltip("回転のスムージング速度 (Slerp 係数)")]
        [SerializeField] private float rotationSlerpSpeed = 12.0f;

        [Tooltip("トラッキング喪失と判定するまでの猶予時間 (秒)")]
        [SerializeField] private float trackingLostTimeout = 0.5f;

        [Header("Camera Intrinsics (0 = 自動推定)")]
        [Tooltip("焦点距離 Fx (0 の場合、画像幅と FOV から自動計算)")]
        [SerializeField] private float customFx = 0f;
        [Tooltip("焦点距離 Fy (0 の場合、画像幅と FOV から自動計算)")]
        [SerializeField] private float customFy = 0f;

        [Header("Local Axis Alignment (グラスローカル軸反転設定)")]
        [Tooltip("グラスローカル Y軸 (高さ) 移動を反転 (デフォルト: true)")]
        [SerializeField] private bool invertLocalY = true;

        [Tooltip("グラスローカル X軸 (水平) 回転 (Pitch) を反転 (デフォルト: true)")]
        [SerializeField] private bool invertLocalRotX = true;

        [Tooltip("グラスローカル Z軸 (奥行) 回転 (Roll) を反転 (デフォルト: true)")]
        [SerializeField] private bool invertLocalRotZ = true;

        [Tooltip("グラスローカル Y軸 (高さ) 回転 (Yaw) を反転 (デフォルト: false)")]
        [SerializeField] private bool invertLocalRotY = false;

        public bool InvertLocalY
        {
            get => invertLocalY;
            set { invertLocalY = value; UpdateAxisButtonLabels(); }
        }

        public bool InvertLocalRotX
        {
            get => invertLocalRotX;
            set { invertLocalRotX = value; UpdateAxisButtonLabels(); }
        }

        public bool InvertLocalRotZ
        {
            get => invertLocalRotZ;
            set { invertLocalRotZ = value; UpdateAxisButtonLabels(); }
        }

        public bool InvertLocalRotY
        {
            get => invertLocalRotY;
            set { invertLocalRotY = value; UpdateAxisButtonLabels(); }
        }

        [Header("Debug Log Settings")]
        [Tooltip("常時スクロールログを出力するインターバル (秒)")]
        [SerializeField] private float statusLogInterval = 0.5f;

        // Native Recognizer & Buffers
        private NativeOthelloRecognizer _recognizer;
        private WebCamTexture _webCamTexture;
        private Texture2D _debugTexture;

        private readonly DiscColor[,] _boardState = new DiscColor[8, 8];
        private readonly Vector2[] _corners = new Vector2[4];

        // 3D Local Corner Coordinates (Origin = Center of Board)
        // 0: Top-Left, 1: Top-Right, 2: Bottom-Right, 3: Bottom-Left
        private float[] _objectPoints3D;
        private float[] _imagePoints2D;
        private float[] _rvecBuffer = new float[3];
        private float[] _tvecBuffer = new float[3];

        // Tracking state
        private bool _isTracking = false;
        private bool _hasFirstPose = false;
        private float _timeSinceLastDetected = 999f;
        private Vector3 _smoothWorldPosition;
        private Quaternion _smoothWorldRotation;
        private Vector3 _lastEstimatedLocalPos;

        // Camera sources state
        private bool _isXrealCameraActive = false;
        private bool _isWebCamActive = false;
        private string _activeCameraName = "Initializing...";
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        private XREALRGBCameraTexture _xrealRgbCam;
        private float _xrealInitRetryTimer = 0f;
        private int _xrealInitRetryCount = 0;
        private const int MAX_XREAL_RETRIES = 5;
#endif

        // Periodic log & FPS state
        private float _statusLogTimer = 0f;
        private float _fpsMeasurementTimer = 0f;
        private int _fpsFrameCount = 0;
        private float _currentFps = 0f;

        public bool IsTracking => _isTracking;
        public Transform TargetBoardTransform
        {
            get => targetBoardTransform;
            set => targetBoardTransform = value;
        }
        public Camera TrackingCamera
        {
            get => trackingCamera != null ? trackingCamera : Camera.main;
            set => trackingCamera = value;
        }
        public RawImage PreviewRawImage
        {
            get => previewRawImage;
            set => previewRawImage = value;
        }
        public RawImage HandheldPreviewRawImage
        {
            get => handheldPreviewRawImage;
            set => handheldPreviewRawImage = value;
        }

        // Camera Intrinsics & Physical Calibration
        private float _effectiveFx = 0f;
        private float _effectiveFy = 0f;
        private float _effectiveCx = 0f;
        private float _effectiveCy = 0f;
        private Pose _rgbCameraPoseFromHead = Pose.identity;
        private bool _hasCameraIntrinsics = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string PLUGIN_NAME = "OthelloCvPlugin";

        [DllImport(PLUGIN_NAME, CallingConvention = CallingConvention.Cdecl)]
        private static extern int OthelloCv_SolvePnP(
            float[] objPts,
            float[] imgPts,
            float fx, float fy, float cx, float cy,
            [Out] float[] outRvec,
            [Out] float[] outTvec
        );
#endif

        private void Awake()
        {
            _recognizer = new NativeOthelloRecognizer
            {
                WarpSize = 400,
                MinAreaThreshold = 4000,
                BlackThreshold = 65,
                WhiteThreshold = 165,
                TransparentBackground = false
            };

            // オセロ盤の 3D ローカル座標 (中心原点、X=±0.114m, Y=±0.114m)
            float halfW = boardWidthMeters * 0.5f; // 0.114m
            float halfD = boardDepthMeters * 0.5f; // 0.114m

            // 4隅の 3D オブジェクト座標 (中心原点、X=±0.114m, Y=±0.114m)
            // OpenCV IPPE_SQUARE 仕様 & 画像座標系 (+Y=下/手前) との対応:
            // 0: Top-Left  (左奥)   -> [-halfW,  halfD, 0]
            // 1: Top-Right (右奥)   -> [ halfW,  halfD, 0]
            // 2: Bottom-Right (右手前) -> [ halfW, -halfD, 0]
            // 3: Bottom-Left  (左手前) -> [-halfW, -halfD, 0]
            // ※ +X: 右方向, +Y: 前方(奥)方向, +Z: 天面法線 (右手系 X x Y = +Z)
            _objectPoints3D = new float[12]
            {
                -halfW,  halfD, 0.0f, // 0: Top-Left (左奥)
                 halfW,  halfD, 0.0f, // 1: Top-Right (右奥)
                 halfW, -halfD, 0.0f, // 2: Bottom-Right (右手前)
                -halfW, -halfD, 0.0f  // 3: Bottom-Left (左手前)
            };

            _imagePoints2D = new float[8];

            if (trackingCamera == null)
            {
                trackingCamera = Camera.main;
            }

            if (boardVisualizer == null && targetBoardTransform != null)
            {
                boardVisualizer = targetBoardTransform.GetComponent<OthelloBoardVisualizer>();
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
            if (_isXrealCameraActive && _xrealRgbCam != null)
            {
                _xrealRgbCam.StopCapture();
                _isXrealCameraActive = false;
            }
#endif
            if (_isWebCamActive && _webCamTexture != null)
            {
                _webCamTexture.Stop();
                _isWebCamActive = false;
            }
        }

        private void Start()
        {
            CustomScreenLogger.Log("[OthelloAutoTracker] Initializing tracker. Board: 22.8x22.8x1.8cm");

            // 初期位置としてカメラ前方60cmに仮配置 (起動直後からXRグラス内に表示確認可能にする)
            if (targetBoardTransform != null && !_hasFirstPose)
            {
                Camera cam = trackingCamera != null ? trackingCamera : Camera.main;
                if (cam != null)
                {
                    targetBoardTransform.position = cam.transform.position + cam.transform.forward * 0.6f;
                    targetBoardTransform.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
                }
            }

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            Enable6DoFTracking();
            TryStartXrealCamera();
#endif
            if (!_isXrealCameraActive)
            {
                StartWebCamera();
            }

            BindAxisToggleButtons();
        }

        public void ToggleInvertLocalY()
        {
            invertLocalY = !invertLocalY;
            CustomScreenLogger.Log($"<color=#FFD700>[AXIS] InvertLocalY: {invertLocalY}</color>");
            UpdateAxisButtonLabels();
        }

        public void ToggleInvertLocalRotX()
        {
            invertLocalRotX = !invertLocalRotX;
            CustomScreenLogger.Log($"<color=#FFD700>[AXIS] InvertLocalRotX: {invertLocalRotX}</color>");
            UpdateAxisButtonLabels();
        }

        public void ToggleInvertLocalRotZ()
        {
            invertLocalRotZ = !invertLocalRotZ;
            CustomScreenLogger.Log($"<color=#FFD700>[AXIS] InvertLocalRotZ: {invertLocalRotZ}</color>");
            UpdateAxisButtonLabels();
        }

        public void ToggleInvertLocalRotY()
        {
            invertLocalRotY = !invertLocalRotY;
            CustomScreenLogger.Log($"<color=#FFD700>[AXIS] InvertLocalRotY: {invertLocalRotY}</color>");
            UpdateAxisButtonLabels();
        }

        public void BindAxisToggleButtons()
        {
            BindButton("Btn_Toggle_YPos", ToggleInvertLocalY);
            BindButton("Btn_Toggle_RotX", ToggleInvertLocalRotX);
            BindButton("Btn_Toggle_RotZ", ToggleInvertLocalRotZ);
            BindButton("Btn_Toggle_RotY", ToggleInvertLocalRotY);
            UpdateAxisButtonLabels();
        }

        private void BindButton(string goName, UnityEngine.Events.UnityAction action)
        {
            var btnGo = GameObject.Find(goName);
            if (btnGo != null)
            {
                var btn = btnGo.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(action);
                }
            }
        }

        public void UpdateAxisButtonLabels()
        {
            SetButtonText("Btn_Toggle_YPos", $"Y-Pos: {(invertLocalY ? "INV" : "NORM")}", invertLocalY);
            SetButtonText("Btn_Toggle_RotX", $"Rot-X: {(invertLocalRotX ? "INV" : "NORM")}", invertLocalRotX);
            SetButtonText("Btn_Toggle_RotZ", $"Rot-Z: {(invertLocalRotZ ? "INV" : "NORM")}", invertLocalRotZ);
            SetButtonText("Btn_Toggle_RotY", $"Rot-Y: {(invertLocalRotY ? "INV" : "NORM")}", invertLocalRotY);
        }

        private void SetButtonText(string goName, string text, bool isInv)
        {
            var btnGo = GameObject.Find(goName);
            if (btnGo != null)
            {
                var txt = btnGo.GetComponentInChildren<Text>();
                if (txt != null) txt.text = text;
                var img = btnGo.GetComponent<Image>();
                if (img != null)
                {
                    img.color = isInv ? new Color(0.12f, 0.55f, 0.28f, 0.95f) : new Color(0.28f, 0.28f, 0.32f, 0.95f);
                }
            }
        }

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
        private async void Enable6DoFTracking()
        {
            try
            {
                CustomScreenLogger.Log("<color=#00D4FF>[6DoF] Requesting 6DoF tracking mode...</color>");
                bool success = await XREALPlugin.SwitchTrackingTypeAsync(TrackingType.MODE_6DOF);
                if (success)
                {
                    CustomScreenLogger.Log("<color=#00FF88>[6DoF] Switched to 6DoF mode successfully.</color>");
                }
                else
                {
                    CustomScreenLogger.LogWarning("[6DoF] SwitchTrackingTypeAsync(6DoF) returned false, continuing in current mode.");
                }
            }
            catch (Exception ex)
            {
                CustomScreenLogger.LogWarning($"[6DoF] SwitchTrackingTypeAsync exception: {ex.Message}");
            }
        }

        private void InitializeXrealCameraParameters(int width, int height, int procW, int procH)
        {
            try
            {
                Vector2Int nativeRes = Vector2Int.zero;
                Vector2 focalLength = Vector2.zero;
                Vector2 principalPoint = Vector2.zero;

                bool gotRes = XREALPlugin.GetDeviceResolution(XREALComponent.XREAL_COMPONENT_RGB_CAMERA, ref nativeRes);
                bool gotIntrin = XREALPlugin.GetCameraIntrinsic(XREALComponent.XREAL_COMPONENT_RGB_CAMERA, ref focalLength, ref principalPoint);

                int baseW = (gotRes && nativeRes.x > 0) ? nativeRes.x : (width > 0 ? width : 1920);
                int baseH = (gotRes && nativeRes.y > 0) ? nativeRes.y : (height > 0 ? height : 1080);

                float scaleX = (float)procW / baseW;
                float scaleY = (float)procH / baseH;

                if (gotIntrin && focalLength.x > 10f && focalLength.y > 10f)
                {
                    _effectiveFx = focalLength.x * scaleX;
                    _effectiveFy = focalLength.y * scaleY;
                    _effectiveCx = principalPoint.x * scaleX;
                    _effectiveCy = principalPoint.y * scaleY;
                    CustomScreenLogger.Log($"<color=#00FF88>[Intrinsics] Native Calibrated:</color> fx={_effectiveFx:F1}, fy={_effectiveFy:F1}, cx={_effectiveCx:F1}, cy={_effectiveCy:F1} (from {baseW}x{baseH})");
                }
                else
                {
                    // XREAL One Pro RGB カメラの設計仕様キャリブレーション値 (垂直 FOV ~35.5度, 水平 FOV ~60度)
                    float fovVRad = 35.5f * Mathf.Deg2Rad;
                    _effectiveFy = (procH * 0.5f) / Mathf.Tan(fovVRad * 0.5f);
                    _effectiveFx = _effectiveFy;
                    _effectiveCx = procW * 0.5f;
                    _effectiveCy = procH * 0.5f;
                    CustomScreenLogger.Log($"<color=#FFAA00>[Intrinsics] Calibrated FOV=35.5deg fallback:</color> fx={_effectiveFx:F1}, fy={_effectiveFy:F1}, cx={_effectiveCx:F1}, cy={_effectiveCy:F1}");
                }

                // 頭部中心 (CenterEye) から RGB カメラへの 6DoF 物理オフセット取得
                Pose rgbPose = Pose.identity;
                if (XREALPlugin.GetDevicePoseFromHead(XREALComponent.XREAL_COMPONENT_RGB_CAMERA, ref rgbPose))
                {
                    _rgbCameraPoseFromHead = rgbPose;
                    CustomScreenLogger.Log($"<color=#00FF88>[CameraPose] RGB from Head:</color> pos={_rgbCameraPoseFromHead.position}, rot={_rgbCameraPoseFromHead.rotation.eulerAngles}");
                }
                else
                {
                    // グラスのノーズブリッジ中央（目の中央より約3cm前方、2cm上方）
                    _rgbCameraPoseFromHead = new Pose(new Vector3(0f, 0.02f, 0.03f), Quaternion.identity);
                    CustomScreenLogger.Log("<color=#FFAA00>[CameraPose] RGB from Head default offset (0, 0.02, 0.03).</color>");
                }

                _hasCameraIntrinsics = true;
            }
            catch (Exception ex)
            {
                CustomScreenLogger.LogError($"[OthelloAutoTracker] Intrinsics init error: {ex.Message}");
            }
        }

        private void TryStartXrealCamera()
        {
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
                        _activeCameraName = "XREAL One Pro RGB Camera";
                        CustomScreenLogger.Log("<color=#00FF88>[OthelloAutoTracker] XREAL One Pro RGB Camera started.</color>");
                        return;
                    }
                }
                CustomScreenLogger.LogWarning("[OthelloAutoTracker] XREAL camera StartCapture returned false.");
            }
            catch (Exception ex)
            {
                CustomScreenLogger.LogWarning($"[OthelloAutoTracker] XREAL camera init error: {ex.Message}");
            }
            _isXrealCameraActive = false;
        }

        /// <summary>
        /// XREAL One Pro グラス搭載の RGB カメラからゼロコピーで YUV プレーンを受信
        /// </summary>
        private void OnNativeCameraPlanesReceived(IntPtr yPtr, IntPtr uPtr, IntPtr vPtr, int width, int height)
        {
            try
            {
                const int procW = 640;
                const int procH = 360;

                if (!_hasCameraIntrinsics)
                {
                    InitializeXrealCameraParameters(width, height, procW, procH);
                }

                // デバッグテクスチャの準備 (640x360 16:9)
                if (_debugTexture == null || _debugTexture.width != procW || _debugTexture.height != procH)
                {
                    _debugTexture = new Texture2D(procW, procH, TextureFormat.RGBA32, false);
                }
                SyncPreviewTextures();

                bool detected = _recognizer.TryRecognizeNativeYuvPlanes(
                    yPtr, uPtr, vPtr,
                    width, height,
                    _boardState,
                    _corners,
                    _debugTexture,
                    procW, procH
                );

                if (detected)
                {
                    if (boardVisualizer != null)
                    {
                        boardVisualizer.UpdateBoard(_boardState);
                    }
                    if (_corners != null && _corners.Length >= 4)
                    {
                        UpdatePoseFromCorners(procW, procH, _corners);
                    }
                }
            }
            catch (Exception ex)
            {
                CustomScreenLogger.LogError($"[OthelloAutoTracker] YUV frame error: {ex.Message}");
            }
        }
#endif

        private void SyncPreviewTextures()
        {
            if (_debugTexture == null) return;
            if (previewRawImage != null)
            {
                if (previewRawImage.texture != _debugTexture)
                {
                    previewRawImage.texture = _debugTexture;
                }
                previewRawImage.color = Color.white;
            }
            if (handheldPreviewRawImage != null)
            {
                if (handheldPreviewRawImage.texture != _debugTexture)
                {
                    handheldPreviewRawImage.texture = _debugTexture;
                }
                handheldPreviewRawImage.color = Color.white;
            }
        }

        private void StartWebCamera()
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
                    int devIdx = Mathf.Clamp(webcamDeviceIndex, 0, devices.Length - 1);
                    _webCamTexture = new WebCamTexture(devices[devIdx].name, requestedResolution.x, requestedResolution.y, 30);
                    _webCamTexture.Play();
                    _isWebCamActive = true;
                    _activeCameraName = $"WebCam ({devices[devIdx].name})";
                    CustomScreenLogger.Log($"[OthelloAutoTracker] WebCam started: {devices[devIdx].name} ({requestedResolution.x}x{requestedResolution.y})");
                }
                else
                {
                    _isWebCamActive = false;
                    _activeCameraName = "Editor Simulation Pattern";
                    CustomScreenLogger.LogWarning("[OthelloAutoTracker] No camera device found.");
                }
            }
            catch (Exception ex)
            {
                _isWebCamActive = false;
                _activeCameraName = "Editor Simulation Pattern";
                CustomScreenLogger.LogError($"[OthelloAutoTracker] Failed to start WebCam: {ex.Message}");
            }
        }

        private void Update()
        {
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_IOS)
            // XREAL カメラ起動の再試行 (アクティビティ起動時の初期化遅延対策)
            if (!_isXrealCameraActive && _xrealInitRetryCount < MAX_XREAL_RETRIES)
            {
                _xrealInitRetryTimer += Time.deltaTime;
                if (_xrealInitRetryTimer >= 1.0f)
                {
                    _xrealInitRetryTimer = 0f;
                    _xrealInitRetryCount++;
                    TryStartXrealCamera();
                    if (_isXrealCameraActive && _isWebCamActive && _webCamTexture != null)
                    {
                        _webCamTexture.Stop();
                        _isWebCamActive = false;
                    }
                }
            }
#endif

            if (_isWebCamActive && _webCamTexture != null && _webCamTexture.isPlaying && _webCamTexture.didUpdateThisFrame)
            {
                ProcessCameraFrame();
            }

            // トラッキング状態の更新
            _timeSinceLastDetected += Time.deltaTime;
            if (_timeSinceLastDetected > trackingLostTimeout)
            {
                if (_isTracking)
                {
                    _isTracking = false;
                    CustomScreenLogger.LogWarning("[OthelloAutoTracker] Board lost.");
                }
            }

            // スムージングされた姿勢をターゲット Transform に適用
            if (_isTracking && targetBoardTransform != null)
            {
                float dt = Time.deltaTime;
                targetBoardTransform.position = Vector3.Lerp(targetBoardTransform.position, _smoothWorldPosition, dt * positionLerpSpeed);
                targetBoardTransform.rotation = Quaternion.Slerp(targetBoardTransform.rotation, _smoothWorldRotation, dt * rotationSlerpSpeed);
            }

            // FPS 計測 (0.5秒ごとに集計)
            _fpsFrameCount++;
            _fpsMeasurementTimer += Time.unscaledDeltaTime;
            if (_fpsMeasurementTimer >= 0.5f)
            {
                _currentFps = _fpsFrameCount / _fpsMeasurementTimer;
                _fpsFrameCount = 0;
                _fpsMeasurementTimer = 0f;
            }

            // 定期ステータスログ出力 (最大20行リングバッファへ常時スクロール表示)
            _statusLogTimer += Time.deltaTime;
            if (_statusLogTimer >= statusLogInterval)
            {
                _statusLogTimer = 0f;
                Transform camTr = trackingCamera != null ? trackingCamera.transform : transform;
                Vector3 cp = camTr.position;
                Vector3 cr = camTr.eulerAngles;

                if (_isTracking && targetBoardTransform != null)
                {
                    Vector3 bp = targetBoardTransform.position;
                    Vector3 br = targetBoardTransform.eulerAngles;
                    CustomScreenLogger.Log(
                        $"<color=#00FF88>[BOARD]</color> Pos:({bp.x:F2},{bp.y:F2},{bp.z:F2}) Rot:({br.x:F0},{br.y:F0},{br.z:F0}) Dist:{_lastEstimatedLocalPos.magnitude:F2}m\n" +
                        $"<color=#00CCFF>[HEAD]</color>  Pos:({cp.x:F2},{cp.y:F2},{cp.z:F2}) Rot:({cr.x:F0},{cr.y:F0},{cr.z:F0}) FPS:{_currentFps:F1} | Axes: Y:{(invertLocalY ? "INV" : "NORM")} Rx:{(invertLocalRotX ? "INV" : "NORM")} Rz:{(invertLocalRotZ ? "INV" : "NORM")}"
                    );
                }
                else
                {
                    CustomScreenLogger.Log(
                        $"<color=#FFAA00>[SEARCH]</color> FPS:{_currentFps:F1} | Head:({cp.x:F2},{cp.y:F2},{cp.z:F2}) Rot:({cr.x:F0},{cr.y:F0},{cr.z:F0}) | Searching..."
                    );
                }
            }
        }

        private void ProcessCameraFrame()
        {
            int imgW = _webCamTexture.width;
            int imgH = _webCamTexture.height;
            if (imgW <= 16 || imgH <= 16) return;

            // デバッグテクスチャの準備
            if (_debugTexture == null || _debugTexture.width != imgW || _debugTexture.height != imgH)
            {
                _debugTexture = new Texture2D(imgW, imgH, TextureFormat.RGBA32, false);
            }
            SyncPreviewTextures();

            // 1. NativeOthelloRecognizer で盤面検出
            bool detected = _recognizer.TryRecognizeWebCam(_webCamTexture, _boardState, _corners, _debugTexture);

            if (detected)
            {
                if (boardVisualizer != null)
                {
                    boardVisualizer.UpdateBoard(_boardState);
                }
                if (_corners != null && _corners.Length >= 4)
                {
                    UpdatePoseFromCorners(imgW, imgH, _corners);
                }
            }
        }

        /// <summary>
        /// 検出された4隅の2D画像座標から SolvePnP を実行し、Unityワールド空間の位置・姿勢を更新
        /// </summary>
        private void UpdatePoseFromCorners(int imgW, int imgH, Vector2[] corners)
        {
            if (EstimateBoardPose(imgW, imgH, corners, out Vector3 localPos, out Quaternion localRot))
            {
                Camera cam = trackingCamera != null ? trackingCamera : Camera.main;
                Transform camTransform = cam != null ? cam.transform : transform;

                // 1. RGB カメラ座標系から頭部中心 (XR Head / CenterEye) 座標系へ変換
                Vector3 headRelPos = _rgbCameraPoseFromHead.position + (_rgbCameraPoseFromHead.rotation * localPos);
                Quaternion headRelRot = _rgbCameraPoseFromHead.rotation * localRot;

                // 2. 頭部中心から Unity ワールド空間へ変換
                Vector3 rawWorldPos = camTransform.TransformPoint(headRelPos);
                Quaternion rawWorldRot = camTransform.rotation * headRelRot;

                // 3. 検出されたのは実機オセロ盤の「天面 (Top Surface)」
                // バーチャル盤 (厚さ 1.8cm の Cube) の中心は天面から 0.9cm 下方に存在するため、
                // 法線 (Up) の逆方向に厚みの半分 (0.009m) をオフセットして実機と体積を完全に一致させる
                Vector3 boardUp = rawWorldRot * Vector3.up;
                rawWorldPos -= boardUp * (boardThicknessMeters * 0.5f);

                if (!_hasFirstPose)
                {
                    _smoothWorldPosition = rawWorldPos;
                    _smoothWorldRotation = rawWorldRot;
                    if (targetBoardTransform != null)
                    {
                        targetBoardTransform.position = _smoothWorldPosition;
                        targetBoardTransform.rotation = _smoothWorldRotation;
                    }
                    _hasFirstPose = true;
                }
                else
                {
                    _smoothWorldPosition = rawWorldPos;
                    _smoothWorldRotation = rawWorldRot;
                }

                _lastEstimatedLocalPos = localPos;
                _timeSinceLastDetected = 0f;
                if (!_isTracking)
                {
                    _isTracking = true;
                    CustomScreenLogger.Log($"<color=#00FF88>[OthelloAutoTracker] Board detected!</color> Dist: {localPos.magnitude:F2}m (Z: {localPos.z:F2}m)");
                }
            }
        }

        /// <summary>
        /// 4隅の画像座標から SolvePnP (または平面ホモグラフィ分解) を用いてカメラに対する 3D 位置・姿勢を算出
        /// </summary>
        private bool EstimateBoardPose(int imgW, int imgH, Vector2[] corners, out Vector3 outLocalPos, out Quaternion outLocalRot)
        {
            outLocalPos = Vector3.zero;
            outLocalRot = Quaternion.identity;

            // カメラ内部パラメータの決定
            float fx, fy, cx, cy;

            if (customFx > 0f && customFy > 0f)
            {
                fx = customFx;
                fy = customFy;
                cx = imgW * 0.5f;
                cy = imgH * 0.5f;
            }
            else if (_hasCameraIntrinsics && _effectiveFx > 10f && _effectiveFy > 10f)
            {
                fx = _effectiveFx;
                fy = _effectiveFy;
                cx = _effectiveCx;
                cy = _effectiveCy;
            }
            else
            {
                // XREAL 環境または RGB カメラ稼働時は垂直 FOV ~35.5度を使用
                float defaultFovY = _isXrealCameraActive ? 35.5f : 55.0f;
                float fovRad = defaultFovY * Mathf.Deg2Rad;
                fy = (imgH * 0.5f) / Mathf.Tan(fovRad * 0.5f);
                fx = fy;
                cx = imgW * 0.5f;
                cy = imgH * 0.5f;
            }

            // 画像座標のフラット配列化
            for (int i = 0; i < 4; i++)
            {
                _imagePoints2D[i * 2 + 0] = corners[i].x;
                _imagePoints2D[i * 2 + 1] = corners[i].y;
            }

            bool pnpSuccess = false;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                int res = OthelloCv_SolvePnP(
                    _objectPoints3D,
                    _imagePoints2D,
                    fx, fy, cx, cy,
                    _rvecBuffer,
                    _tvecBuffer
                );
                pnpSuccess = (res == 1);
            }
            catch (Exception ex)
            {
                CustomScreenLogger.LogError($"[OthelloAutoTracker] Native SolvePnP failed: {ex.Message}");
                pnpSuccess = false;
            }
#endif

            // ネイティブが利用できない環境 (Editor / フォールバック) では高精度な平面ホモグラフィ PnP 解法を実行
            if (!pnpSuccess)
            {
                pnpSuccess = SolvePlanarHomographyPnP(
                    _objectPoints3D,
                    _imagePoints2D,
                    fx, fy, cx, cy,
                    _rvecBuffer,
                    _tvecBuffer
                );
            }

            if (!pnpSuccess)
            {
                return false;
            }

            // -------------------------------------------------------------
            // OpenCV (右手系) から Unity カメラローカル (左手系) への厳密な座標変換
            // OpenCV: +X=右, +Y=下, +Z=前
            // Unity:  +X=右, +Y=上, +Z=前
            // -------------------------------------------------------------
            float tx = _tvecBuffer[0];
            float ty = _tvecBuffer[1];
            float tz = _tvecBuffer[2];

            // カメラ前方に正しく存在するか検証 (tz > 0)
            if (tz <= 0.05f || tz > 5.0f)
            {
                return false;
            }

            // 平行移動ベクトル変換: X_unity = tx, Y_unity = (invertLocalY ? ty : -ty), Z_unity = tz
            // グラスローカル Y軸 (高さ) の移動方向
            float localY = invertLocalY ? ty : -ty;
            outLocalPos = new Vector3(tx, localY, tz);

            // 回転ベクトル (Rodrigues) を 3x3 回転行列 R に変換
            float rx = _rvecBuffer[0];
            float ry = _rvecBuffer[1];
            float rz = _rvecBuffer[2];
            Matrix3x3 R = RodriguesToMatrix(rx, ry, rz);

            // Unity カメラローカル座標系での基底ベクトル
            // OpenCV カメラ座標系: +X=右, +Y=下, +Z=前
            // Unity カメラ座標系:  +X=右, +Y=上, +Z=前 (Y軸のみ符号反転: y_u = -y_cv)
            // 盤面ローカル定義:    +X=右, +Y=奥(前), +Z=天面法線(上)
            // したがって回転行列 R の各列ベクトル (OpenCVカメラ系) を Unity カメラ系へ投影:
            // Column 0 (R00, R10, R20): 盤面右基底 -> Unity ( R00, -R10,  R20)
            // Column 1 (R01, R11, R21): 盤面奥基底 -> Unity ( R01, -R11,  R21)
            // Column 2 (R02, R12, R22): 盤面上基底 -> Unity ( R02, -R12,  R22)
            Vector3 forwardVec = new Vector3(R.m01, -R.m11, R.m21);
            Vector3 upVec = new Vector3(R.m02, -R.m12, R.m22);

            if (forwardVec.sqrMagnitude < 0.001f || upVec.sqrMagnitude < 0.001f)
            {
                return false;
            }

            outLocalRot = Quaternion.LookRotation(forwardVec.normalized, upVec.normalized);

            // グラスローカル軸回転の反転 (Quaternion の各軸成分反転)
            // localRot = (x, y, z, w) において、
            // x はローカル X軸 (水平) まわりの回転 (Pitch)
            // y はローカル Y軸 (高さ) まわりの回転 (Yaw)
            // z はローカル Z軸 (奥行) まわりの回転 (Roll)
            if (invertLocalRotX || invertLocalRotY || invertLocalRotZ)
            {
                outLocalRot = new Quaternion(
                    invertLocalRotX ? -outLocalRot.x : outLocalRot.x,
                    invertLocalRotY ? -outLocalRot.y : outLocalRot.y,
                    invertLocalRotZ ? -outLocalRot.z : outLocalRot.z,
                    outLocalRot.w
                );
            }

            return true;
        }

        /// <summary>
        /// 平面4点における Direct Linear Transform (DLT) & 極分解による PnP 姿勢推定器
        /// OpenCV ネイティブが存在しない Unity Editor でも全く同じ精度で 3D 姿勢を算出可能
        /// </summary>
        private static bool SolvePlanarHomographyPnP(
            float[] objPts,
            float[] imgPts,
            float fx, float fy, float cx, float cy,
            float[] outRvec,
            float[] outTvec
        ) {
            try
            {
                // 1. 正規化カメラ座標 (Normalized Camera Coordinates) に変換
                double[] x = new double[4];
                double[] y = new double[4];
                double[] X = new double[4];
                double[] Y = new double[4];

                for (int i = 0; i < 4; i++)
                {
                    x[i] = (imgPts[i * 2 + 0] - cx) / fx;
                    y[i] = (imgPts[i * 2 + 1] - cy) / fy;
                    X[i] = objPts[i * 3 + 0];
                    Y[i] = objPts[i * 3 + 1];
                }

                // 2. 8x8 連立方程式 A * h = b を解いてホモグラフィ H (h8=1) を算出
                double[,] A = new double[8, 8];
                double[] b = new double[8];

                for (int i = 0; i < 4; i++)
                {
                    int row0 = i * 2;
                    int row1 = i * 2 + 1;

                    A[row0, 0] = X[i];
                    A[row0, 1] = Y[i];
                    A[row0, 2] = 1.0;
                    A[row0, 3] = 0.0;
                    A[row0, 4] = 0.0;
                    A[row0, 5] = 0.0;
                    A[row0, 6] = -x[i] * X[i];
                    A[row0, 7] = -x[i] * Y[i];
                    b[row0] = x[i];

                    A[row1, 0] = 0.0;
                    A[row1, 1] = 0.0;
                    A[row1, 2] = 0.0;
                    A[row1, 3] = X[i];
                    A[row1, 4] = Y[i];
                    A[row1, 5] = 1.0;
                    A[row1, 6] = -y[i] * X[i];
                    A[row1, 7] = -y[i] * Y[i];
                    b[row1] = y[i];
                }

                double[] h = SolveLinearSystem8x8(A, b);
                if (h == null) return false;

                Vector3d h1 = new Vector3d(h[0], h[3], h[6]);
                Vector3d h2 = new Vector3d(h[1], h[4], h[7]);
                Vector3d h3 = new Vector3d(h[2], h[5], 1.0);

                double norm1 = h1.Magnitude();
                double norm2 = h2.Magnitude();
                if (norm1 < 1e-7 || norm2 < 1e-7) return false;

                double scale = 2.0 / (norm1 + norm2);
                if (h3.z < 0) scale = -scale;

                Vector3d r1 = h1 * scale;
                Vector3d r2 = h2 * scale;
                Vector3d t = h3 * scale;
                Vector3d r3 = Vector3d.Cross(r1, r2);

                // 直交正規化 (Gram-Schmidt)
                r1 = r1.Normalized();
                r2 = (r2 - r1 * Vector3d.Dot(r1, r2)).Normalized();
                r3 = Vector3d.Cross(r1, r2).Normalized();

                // 回転行列から回転ベクトル (Rodrigues) を算出
                Matrix3x3 Rmat;
                Rmat.m00 = (float)r1.x; Rmat.m01 = (float)r2.x; Rmat.m02 = (float)r3.x;
                Rmat.m10 = (float)r1.y; Rmat.m11 = (float)r2.y; Rmat.m12 = (float)r3.y;
                Rmat.m20 = (float)r1.z; Rmat.m21 = (float)r2.z; Rmat.m22 = (float)r3.z;

                Vector3 rvec = MatrixToRodrigues(Rmat);

                outRvec[0] = rvec.x;
                outRvec[1] = rvec.y;
                outRvec[2] = rvec.z;

                outTvec[0] = (float)t.x;
                outTvec[1] = (float)t.y;
                outTvec[2] = (float)t.z;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double[] SolveLinearSystem8x8(double[,] A, double[] b)
        {
            const int n = 8;
            double[,] M = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) M[i, j] = A[i, j];
                M[i, n] = b[i];
            }

            for (int p = 0; p < n; p++)
            {
                int maxRow = p;
                double maxVal = Math.Abs(M[p, p]);
                for (int i = p + 1; i < n; i++)
                {
                    double v = Math.Abs(M[i, p]);
                    if (v > maxVal) { maxVal = v; maxRow = i; }
                }
                if (maxVal < 1e-12) return null;

                if (maxRow != p)
                {
                    for (int j = p; j <= n; j++)
                    {
                        double tmp = M[p, j];
                        M[p, j] = M[maxRow, j];
                        M[maxRow, j] = tmp;
                    }
                }

                double pivot = M[p, p];
                for (int j = p; j <= n; j++) M[p, j] /= pivot;

                for (int i = 0; i < n; i++)
                {
                    if (i != p)
                    {
                        double factor = M[i, p];
                        for (int j = p; j <= n; j++)
                        {
                            M[i, j] -= factor * M[p, j];
                        }
                    }
                }
            }

            double[] x = new double[n];
            for (int i = 0; i < n; i++) x[i] = M[i, n];
            return x;
        }

        private struct Matrix3x3
        {
            public float m00, m01, m02;
            public float m10, m11, m12;
            public float m20, m21, m22;
        }

        private static Matrix3x3 RodriguesToMatrix(float rx, float ry, float rz)
        {
            float theta = Mathf.Sqrt(rx * rx + ry * ry + rz * rz);
            Matrix3x3 R;

            if (theta < 1e-6f)
            {
                R.m00 = 1f; R.m01 = 0f; R.m02 = 0f;
                R.m10 = 0f; R.m11 = 1f; R.m12 = 0f;
                R.m20 = 0f; R.m21 = 0f; R.m22 = 1f;
                return R;
            }

            float ux = rx / theta;
            float uy = ry / theta;
            float uz = rz / theta;

            float c = Mathf.Cos(theta);
            float s = Mathf.Sin(theta);
            float C = 1f - c;

            R.m00 = c + ux * ux * C;
            R.m01 = ux * uy * C - uz * s;
            R.m02 = ux * uz * C + uy * s;

            R.m10 = uy * ux * C + uz * s;
            R.m11 = c + uy * uy * C;
            R.m12 = uy * uz * C - ux * s;

            R.m20 = uz * ux * C - uy * s;
            R.m21 = uz * uy * C + ux * s;
            R.m22 = c + uz * uz * C;

            return R;
        }

        private static Vector3 MatrixToRodrigues(Matrix3x3 R)
        {
            float trace = R.m00 + R.m11 + R.m22;
            float cosTheta = Mathf.Clamp((trace - 1f) * 0.5f, -1f, 1f);
            float theta = Mathf.Acos(cosTheta);

            if (theta < 1e-5f)
            {
                return Vector3.zero;
            }

            float sinTheta = Mathf.Sin(theta);
            if (Mathf.Abs(sinTheta) < 1e-5f)
            {
                return new Vector3(Mathf.PI, 0, 0);
            }

            float factor = theta / (2f * sinTheta);
            float rx = (R.m21 - R.m12) * factor;
            float ry = (R.m02 - R.m20) * factor;
            float rz = (R.m10 - R.m01) * factor;

            return new Vector3(rx, ry, rz);
        }

        private struct Vector3d
        {
            public double x, y, z;
            public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            public double Magnitude() => Math.Sqrt(x * x + y * y + z * z);
            public Vector3d Normalized() { double m = Magnitude(); return m > 1e-12 ? new Vector3d(x / m, y / m, z / m) : new Vector3d(0, 0, 0); }
            public static Vector3d operator *(Vector3d v, double s) => new Vector3d(v.x * s, v.y * s, v.z * s);
            public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
            public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
            public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        private void OnDestroy()
        {
            if (_webCamTexture != null && _webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
            }
            if (_recognizer != null)
            {
                _recognizer.Dispose();
            }
        }
    }
}
