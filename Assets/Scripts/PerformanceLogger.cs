using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AROthello
{
    /// <summary>
    /// 【XREAL Beam Pro / ARプロジェクト向け性能検証データ自動測定・CSV一括エクスポートスクリプト】
    /// 
    /// 要件:
    /// 1. 13項目の測定・記録 (Timestamp, FrameCount, CurrentFPS, Latencies, Head Metrics, Tracking Offset, Move_Flicker_Count_1s, Algorithm_Accuracy_Flag)
    /// 2. 合法手判定の直近1秒間チャタリング検知
    /// 3. Beam Pro画面タップでToggleLogging() & UI更新 (待機中: START / 記録中: REC [STOP & SAVE])、Spaceキー対応
    /// 4. Stopwatch高精度計測、メモリバッファリング、persistentDataPath / Exports への UTF-8 CSV 自動エクスポート
    /// </summary>
    [DisallowMultipleComponent]
    public class PerformanceLogger : MonoBehaviour
    {
        public static PerformanceLogger Instance { get; private set; }

        [Header("UI References (Beam Pro Touch Screen)")]
        [Tooltip("Beam Pro 画面上のロギング開始/停止ボタン")]
        [SerializeField] private Button logToggleButton;
        [SerializeField] private Text logButtonText;

        [Header("Target References (Optional / Auto-detected)")]
        [SerializeField] private Transform arCamera;
        [SerializeField] private Transform boardTransform;

        [Header("Status")]
        [SerializeField] private bool isLogging = false;

        public bool IsLogging => isLogging;

        // CSV Header (13項目)
        private const string CSV_HEADER =
            "Timestamp,FrameCount,CurrentFPS,Latency_Total_ms,Latency_Capture_ms,Latency_Recognition_ms," +
            "Latency_Algorithm_ms,Latency_Render_ms,Head_Distance_cm,Head_Angle_deg,Tracking_Offset_mm," +
            "Move_Flicker_Count_1s,Algorithm_Accuracy_Flag";

        // メモリ内バッファ (フレームごとのディスクIOを完全回避)
        private readonly List<string> _logBuffer = new List<string>(8192);

        // チャタリング (合法手判定の揺れ) 検知用
        private ulong _prevLegalMovesBitmask = 0UL;
        private bool _hasPrevLegalMoves = false;
        private readonly Queue<float> _flickerTimestamps = new Queue<float>(64);

        // FPS 計算用 (直近1秒間の平均FPS)
        private int _fpsAccumFrameCount = 0;
        private float _fpsAccumTime = 0f;
        private float _currentFps = 60.0f;

        // パイプライン計測値 (ミリ秒)
        private float _latencyCaptureMs = 0f;
        private float _latencyRecognitionMs = 0f;
        private float _latencyAlgorithmMs = 0f;
        private float _latencyRenderMs = 0f;
        private float _latencyTotalMs = 0f;

        // 姿勢・トラッキング指標
        private float _headDistanceCm = 0f;
        private float _headAngleDeg = 0f;
        private float _trackingOffsetMm = 0f;

        // 記録セッション状態
        private float _loggingStartTime = 0f;
        private int _recordedFrameCountInSession = 0;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            if (arCamera == null && Camera.main != null)
            {
                arCamera = Camera.main.transform;
            }
        }

        private void Start()
        {
            BindUIControls();
            UpdateUIState();
        }

        private void Update()
        {
            // 1. 直近1秒間の平均FPS計測
            _fpsAccumFrameCount++;
            _fpsAccumTime += Time.unscaledDeltaTime;
            if (_fpsAccumTime >= 1.0f)
            {
                _currentFps = _fpsAccumFrameCount / _fpsAccumTime;
                _fpsAccumFrameCount = 0;
                _fpsAccumTime = 0f;
            }

            // 2. エディタ実行時等のキーボード操作 (Spaceキーでトグル)
            HandleSpaceInput();

            // 3. チャタリングキューの期限切れ破棄 (直近1.0秒外の履歴を削除)
            float now = Time.unscaledTime;
            while (_flickerTimestamps.Count > 0 && (now - _flickerTimestamps.Peek()) > 1.0f)
            {
                _flickerTimestamps.Dequeue();
            }

            // 4. 記録中であればフレームデータをメモリバッファへ追加
            if (isLogging)
            {
                RecordCurrentFrame();
                UpdateRecordingUI();
            }
        }

        private void HandleSpaceInput()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                ToggleLogging();
            }
#else
            if (Input.GetKeyDown(KeyCode.Space))
            {
                ToggleLogging();
            }
#endif
        }

        private void OnApplicationQuit()
        {
            if (isLogging)
            {
                StopLoggingAndSave();
            }
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && isLogging)
            {
                // アプリがバックグラウンドに移行した際に自動退避
                StopLoggingAndSave();
            }
        }

        #region Public Recording API (Called by OthelloAutoTracker / Pipeline)

        /// <summary>
        /// パイプライン各ステージのレイテンシ (ms) を登録
        /// </summary>
        public void SetPipelineLatencies(float captureMs, float recognitionMs, float algorithmMs, float renderMs, float totalMs)
        {
            _latencyCaptureMs = captureMs;
            _latencyRecognitionMs = recognitionMs;
            _latencyAlgorithmMs = algorithmMs;
            _latencyRenderMs = renderMs;
            _latencyTotalMs = totalMs;
        }

        /// <summary>
        /// トラッキング幾何指標を登録
        /// </summary>
        public void SetTrackingMetrics(float distanceCm, float angleDeg, float offsetMm)
        {
            _headDistanceCm = distanceCm;
            _headAngleDeg = angleDeg;
            _trackingOffsetMm = offsetMm;
        }

        /// <summary>
        /// 8x8 = 64マスの合法手判定結果 (ulong ビットマスク) を登録し、チャタリングを検知
        /// </summary>
        public void SetLegalMovesBitmask(ulong legalMovesBitmask)
        {
            if (_hasPrevLegalMoves)
            {
                if (legalMovesBitmask != _prevLegalMovesBitmask)
                {
                    // 合法手判定が前フレームから変化した瞬間のタイムスタンプをキューに登録
                    _flickerTimestamps.Enqueue(Time.unscaledTime);
                }
            }
            else
            {
                _hasPrevLegalMoves = true;
            }

            _prevLegalMovesBitmask = legalMovesBitmask;
        }

        #endregion

        #region Logging Control & File Export

        /// <summary>
        /// Beam Pro タッチ画面または外部からのロギングトグル
        /// </summary>
        public void ToggleLogging()
        {
            if (isLogging)
            {
                StopLoggingAndSave();
            }
            else
            {
                StartLogging();
            }
        }

        public void StartLogging()
        {
            _logBuffer.Clear();
            _logBuffer.Add(CSV_HEADER);
            _loggingStartTime = Time.time;
            _recordedFrameCountInSession = 0;
            _flickerTimestamps.Clear();
            _hasPrevLegalMoves = false;

            isLogging = true;
            UpdateUIState();

            CustomScreenLogger.Log("<color=#FF0055>[PERF] 性能データ測定を開始しました (REC)</color>");
            Debug.Log("[PerformanceLogger] Performance logging started.");
        }

        public void StopLoggingAndSave()
        {
            if (!isLogging && _logBuffer.Count <= 1) return;

            isLogging = false;
            UpdateUIState();

            // 保存先ディレクトリの決定 (Android: persistentDataPath, Editor: Exports/)
            string exportDir;
#if UNITY_EDITOR
            exportDir = Path.Combine(Directory.GetCurrentDirectory(), "Exports");
#elif UNITY_ANDROID
            exportDir = Application.persistentDataPath;
#else
            exportDir = Application.persistentDataPath;
#endif

            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            string timestampStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string filePath = Path.Combine(exportDir, $"performance_log_{timestampStr}.csv");

            try
            {
                File.WriteAllLines(filePath, _logBuffer, Encoding.UTF8);
                string msg = $"<color=#00FF88>[PERF] CSVエクスポート完了 ({_recordedFrameCountInSession} frames):</color>\n{filePath}";
                CustomScreenLogger.Log(msg);
                Debug.Log($"[PerformanceLogger] Successfully exported CSV to: {filePath}");
            }
            catch (Exception ex)
            {
                string errMsg = $"[PERF] CSV保存エラー: {ex.Message}";
                CustomScreenLogger.LogError(errMsg);
                Debug.LogError($"[PerformanceLogger] Failed to save CSV: {ex.Message}");
            }
        }

        private void RecordCurrentFrame()
        {
            _recordedFrameCountInSession++;

            // 1. Timestamp (ISO 8601 UTC)
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

            // 2. FrameCount
            int frameCount = Time.frameCount;

            // 3. CurrentFPS
            float fps = _currentFps;

            // 4..8. Latencies (ms)
            float latTotal = _latencyTotalMs;
            float latCap = _latencyCaptureMs;
            float latRec = _latencyRecognitionMs;
            float latAlg = _latencyAlgorithmMs;
            float latRen = _latencyRenderMs;

            // 9..11. Tracking Metrics
            float distCm = _headDistanceCm;
            float angleDeg = _headAngleDeg;
            float offsetMm = _trackingOffsetMm;

            // 12. Move_Flicker_Count_1s
            int flickerCount = _flickerTimestamps.Count;

            // 13. Algorithm_Accuracy_Flag (目視確認用の手動入力欄のため常に空文字 "")
            string accuracyFlag = "";

            // CSV 1行の生成 (文化依存の小数点を防ぐため InvariantCulture)
            string csvLine = string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2:F1},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F1},{9:F1},{10:F1},{11},{12}",
                timestamp, frameCount, fps, latTotal, latCap, latRec, latAlg, latRen,
                distCm, angleDeg, offsetMm, flickerCount, accuracyFlag);

            _logBuffer.Add(csvLine);
        }

        #endregion

        #region UI Binding & Updates

        public void BindUIControls()
        {
            if (logToggleButton == null)
            {
                logToggleButton = GameObject.Find("Btn_Toggle_PerfLog")?.GetComponent<Button>();
            }

            if (logToggleButton != null)
            {
                if (logButtonText == null)
                {
                    logButtonText = logToggleButton.GetComponentInChildren<Text>();
                }

                logToggleButton.onClick.RemoveAllListeners();
                logToggleButton.onClick.AddListener(ToggleLogging);
            }
        }

        private void UpdateUIState()
        {
            if (logButtonText != null)
            {
                logButtonText.text = isLogging ? "REC [STOP & SAVE]" : "LOG: START";
            }

            if (logToggleButton != null)
            {
                var img = logToggleButton.GetComponent<Image>();
                if (img != null)
                {
                    img.color = isLogging
                        ? new Color(0.9f, 0.1f, 0.2f, 0.95f) // 記録中: 鮮やかな赤
                        : new Color(0.15f, 0.45f, 0.85f, 0.95f); // 待機中: ブルー
                }
            }
        }

        private void UpdateRecordingUI()
        {
            if (logButtonText != null)
            {
                float elapsed = Time.time - _loggingStartTime;
                int minutes = (int)(elapsed / 60f);
                int seconds = (int)(elapsed % 60f);
                bool blink = ((int)(elapsed * 2f) % 2) == 0;
                string dot = blink ? "● " : "○ ";
                logButtonText.text = $"{dot}REC {minutes:D2}:{seconds:D2} [STOP]";
            }
        }

        #endregion
    }
}
