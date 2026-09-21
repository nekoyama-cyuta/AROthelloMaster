using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 【実機・ARグラス（XREAL等）対応】
/// 性能検証データ自動常時測定・リアルタイムCSVエクスポートスクリプト
/// 
/// ・グラス装着中のためボタン操作不要（起動と同時に自動常時ロギング）
/// ・突発的なアプリ終了・ケーブル切断でもデータを失わない自動フラッシュ（定期追記）機構
/// ・Androidのライフサイクル（OnApplicationPause/Quit）に完全対応
/// </summary>
[DisallowMultipleComponent]
public class PerformanceLogger : MonoBehaviour
{
    public static PerformanceLogger Instance { get; private set; }

    [Header("Target References (Auto-assigned if null)")]
    [Tooltip("ARカメラのTransform (nullの場合はCamera.mainを自動参照)")]
    [SerializeField] private Transform arCamera;

    [Tooltip("オセロ盤オブジェクトのTransform")]
    [SerializeField] private Transform boardTransform;

    [Tooltip("物理盤面マーカーのTransform (Tracking_Offset計測用)")]
    [SerializeField] private Transform physicalMarkerTransform;

    [Tooltip("仮想ハイライト中心のTransform (Tracking_Offset計測用)")]
    [SerializeField] private Transform virtualHighlightTransform;

    [Header("Hands-Free & Real-Device Settings")]
    [Tooltip("ARグラス実機向け: 起動と同時に自動で常時ロギングを開始します（ボタン操作不要）")]
    [SerializeField] private bool autoStartLogging = true;

    [Tooltip("ディスクへの自動フラッシュ間隔（フレーム数）。定期的に追記書き込みを行い、クラッシュや強制終了時のデータ消失を防ぎます")]
    [SerializeField] private int autoFlushIntervalFrames = 60; // 60fps環境で約1秒ごと

    [Tooltip("保存先サブディレクトリ名 (実機ではApplication.persistentDataPath、エディタではプロジェクト直下に作成)")]
    [SerializeField] private string exportSubFolder = "Exports";

    [Tooltip("AR視界内にデバッグ用HUDを表示するかどうか")]
    [SerializeField] private bool showOnScreenHUD = true;

    // ロギング状態
    private bool _isLogging = false;
    public bool IsLogging => _isLogging;

    // ファイルストリーム管理（リアルタイム追記ストリーミング）
    private FileStream _fileStream;
    private StreamWriter _streamWriter;
    private string _currentLogFilePath = "";
    public string CurrentLogFilePath => _currentLogFilePath;

    private readonly StringBuilder _rowBuffer = new StringBuilder(4096);
    private int _pendingFramesCount = 0;
    private long _totalRecordedFrames = 0;
    public long TotalRecordedFrames => _totalRecordedFrames;

    // パイプラインごとのレイテンシ計測用 Stopwatch
    private readonly Stopwatch _swCapture = new Stopwatch();
    private readonly Stopwatch _swRecognition = new Stopwatch();
    private readonly Stopwatch _swAlgorithm = new Stopwatch();
    private readonly Stopwatch _swRender = new Stopwatch();
    private readonly Stopwatch _swFrameTotal = new Stopwatch();

    // 最新フレームのレイテンシ値 (ms)
    private double _currentLatencyCapture = 0.0;
    private double _currentLatencyRecognition = 0.0;
    private double _currentLatencyAlgorithm = 0.0;
    private double _currentLatencyRender = 0.0;
    private double _currentLatencyTotal = 0.0;

    // 外部から明示的にセットされたTracking Offset (mm) のオーバーライド用
    private float? _manualTrackingOffsetMm = null;

    // FPS算出用 (直近1秒間の平均FPS)
    private readonly Queue<float> _fpsTimeQueue = new Queue<float>(120);

    // チャタリング検知用 (直近1秒間の合法手反転回数)
    private ulong _previousLegalMovesMask = 0UL;
    private bool _hasPreviousLegalMoves = false;
    private readonly Queue<float> _flickerTimeQueue = new Queue<float>(64);
    private int _currentFlickerCount1s = 0;

    // CSVヘッダー行定義
    private const string CSV_HEADER = "Timestamp,FrameCount,CurrentFPS,Latency_Total_ms,Latency_Capture_ms,Latency_Recognition_ms,Latency_Algorithm_ms,Latency_Render_ms,Head_Distance_cm,Head_Angle_deg,Tracking_Offset_mm,Move_Flicker_Count_1s,Algorithm_Accuracy_Flag";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // ARカメラの自動検出
        if (arCamera == null && Camera.main != null)
        {
            arCamera = Camera.main.transform;
        }
    }

    private void Start()
    {
        // 実機向け: 起動時に自動で常時ロギングを開始
        if (autoStartLogging)
        {
            StartLogging();
        }
    }

    private void Update()
    {
        // 開発PCでの検証用: [Space] キーによる手動トグルも可能
        HandleSpaceInput();

        // 直近1秒間の平均FPS計測 (スライディングウィンドウ)
        UpdateFpsCalculation();

        // チャタリングキューの期限切れ破棄 (直近1秒外)
        CleanExpiredFlickers(Time.unscaledTime);

        // フレーム総処理時間の計測開始
        if (!_swFrameTotal.IsRunning)
        {
            _swFrameTotal.Restart();
        }
    }

    private void LateUpdate()
    {
        if (!_isLogging || _streamWriter == null) return;

        // 1. フレーム総処理時間 (ms) の確定
        _swFrameTotal.Stop();
        _currentLatencyTotal = _swFrameTotal.Elapsed.TotalMilliseconds;
        _swFrameTotal.Restart();

        // 2. カメラと盤面の距離・角度・オフセットの算出
        CalculateSpatialMetrics(out float distanceCm, out float angleDeg, out float offsetMm);

        // 3. CSV 1行分のデータをフォーマットして一時バッファに追記
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        float currentFps = CalculateCurrentFps();

        _rowBuffer.Append(timestamp).Append(',');
        _rowBuffer.Append(Time.frameCount).Append(',');
        _rowBuffer.Append(currentFps.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(_currentLatencyTotal.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(_currentLatencyCapture.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(_currentLatencyRecognition.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(_currentLatencyAlgorithm.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(_currentLatencyRender.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(distanceCm.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(angleDeg.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(offsetMm.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
        _rowBuffer.Append(_currentFlickerCount1s).Append(',');
        _rowBuffer.Append("\"\""); // Algorithm_Accuracy_Flag (目視確認用の手動入力欄として常に空文字)
        _rowBuffer.AppendLine();

        _pendingFramesCount++;
        _totalRecordedFrames++;

        // 4. 定期的にディスクへフラッシュ（長時間の常時ロギングでもメモリを消費せず、突発終了にも強い）
        if (_pendingFramesCount >= autoFlushIntervalFrames)
        {
            FlushBufferToDisk();
        }

        // フレーム毎の計測値をリセット (次回フレームで計測されない場合は0)
        _currentLatencyCapture = 0.0;
        _currentLatencyRecognition = 0.0;
        _currentLatencyAlgorithm = 0.0;
        _currentLatencyRender = 0.0;
        _manualTrackingOffsetMm = null;
    }

    #region Input Handling (Optional for PC Debug)

    private void HandleSpaceInput()
    {
        bool spacePressed = false;

#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            spacePressed = true;
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Space))
        {
            spacePressed = true;
        }
#endif

        if (spacePressed)
        {
            ToggleLogging();
        }
    }

    #endregion

    #region Public Control & State

    /// <summary>
    /// ロギングの「開始 / 停止」をトグルします。
    /// </summary>
    public void ToggleLogging()
    {
        if (_isLogging)
        {
            StopLogging();
        }
        else
        {
            StartLogging();
        }
    }

    /// <summary>
    /// 常時ロギングを開始し、新しいCSVファイルストリームを開きます。
    /// </summary>
    public void StartLogging()
    {
        if (_isLogging) return;

        try
        {
            // 保存先ディレクトリの決定 (実機: persistentDataPath / エディタ: プロジェクト直下のExports)
            string exportDir;
#if UNITY_EDITOR
            exportDir = Path.Combine(Directory.GetCurrentDirectory(), exportSubFolder);
#else
            exportDir = Path.Combine(Application.persistentDataPath, exportSubFolder);
#endif

            if (!Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            string timestampStr = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"performance_log_{timestampStr}.csv";
            _currentLogFilePath = Path.Combine(exportDir, fileName);

            // ファイルストリームを生成 (UTF-8 BOM付きでExcel文字化け防止)
            _fileStream = new FileStream(_currentLogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            _streamWriter = new StreamWriter(_fileStream, new UTF8Encoding(true));

            // CSVヘッダーの即時書き込み
            _streamWriter.WriteLine(CSV_HEADER);
            _streamWriter.Flush();

            _rowBuffer.Clear();
            _pendingFramesCount = 0;
            _totalRecordedFrames = 0;
            _flickerTimeQueue.Clear();
            _hasPreviousLegalMoves = false;
            _currentFlickerCount1s = 0;
            _swFrameTotal.Restart();

            _isLogging = true;
            Debug.Log($"[PerformanceLogger] >>> Real-time Logging STARTED. File: {_currentLogFilePath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PerformanceLogger] ロギング開始に失敗しました: {ex.Message}\n{ex.StackTrace}");
            CloseLogFile();
        }
    }

    /// <summary>
    /// ロギングを停止し、ファイルを確定してクローズします。
    /// </summary>
    public void StopLogging()
    {
        if (!_isLogging) return;

        _isLogging = false;
        _swFrameTotal.Reset();
        CloseLogFile();

        Debug.Log($"[PerformanceLogger] <<< Logging STOPPED. Output saved: {_currentLogFilePath} (Total Frames: {_totalRecordedFrames})");
    }

    /// <summary>
    /// バッファ内のデータをディスクにフラッシュ（即時書き込み）します。
    /// </summary>
    public void FlushBufferToDisk()
    {
        if (_streamWriter != null && _rowBuffer.Length > 0)
        {
            try
            {
                _streamWriter.Write(_rowBuffer.ToString());
                _streamWriter.Flush();
                _rowBuffer.Clear();
                _pendingFramesCount = 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PerformanceLogger] ファイルフラッシュ中にエラー: {ex.Message}");
            }
        }
    }

    private void CloseLogFile()
    {
        try
        {
            FlushBufferToDisk();

            if (_streamWriter != null)
            {
                _streamWriter.Dispose();
                _streamWriter = null;
            }

            if (_fileStream != null)
            {
                _fileStream.Dispose();
                _fileStream = null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PerformanceLogger] ファイルクローズ中にエラー: {ex.Message}");
        }
    }

    #endregion

    #region Spatial Metrics (Distance, Angle, Offset)

    private void CalculateSpatialMetrics(out float distanceCm, out float angleDeg, out float offsetMm)
    {
        distanceCm = 0f;
        angleDeg = 0f;
        offsetMm = 0f;

        // ARカメラが未設定なら再検索
        if (arCamera == null && Camera.main != null)
        {
            arCamera = Camera.main.transform;
        }

        // 1. ARカメラと盤面オブジェクトの直線距離 (cm) & 盤面法線に対する見下ろし角度 (deg)
        if (arCamera != null && boardTransform != null)
        {
            Vector3 camPos = arCamera.position;
            Vector3 boardPos = boardTransform.position;

            // 直線距離 (m -> cm)
            distanceCm = Vector3.Distance(camPos, boardPos) * 100f;

            // 盤面中心からカメラへの視線方向ベクトル
            Vector3 toCam = (camPos - boardPos).normalized;

            // 盤面の天面法線 (boardTransform.up) とのなす角 (真上=0度、真横=90度)
            angleDeg = Vector3.Angle(boardTransform.up, toCam);
        }

        // 2. 物理マーカーと仮想ハイライト中心のズレ幅 (mm)
        if (_manualTrackingOffsetMm.HasValue)
        {
            offsetMm = _manualTrackingOffsetMm.Value;
        }
        else if (physicalMarkerTransform != null && virtualHighlightTransform != null)
        {
            offsetMm = Vector3.Distance(physicalMarkerTransform.position, virtualHighlightTransform.position) * 1000f;
        }
    }

    /// <summary>
    /// 外部スクリプトからトラッキング誤差 (mm) を直接設定します。
    /// </summary>
    public void SetTrackingOffset(float offsetMm)
    {
        _manualTrackingOffsetMm = offsetMm;
    }

    /// <summary>
    /// 物理マーカー位置と仮想ハイライト位置からトラッキング誤差 (mm) を直接設定します。
    /// </summary>
    public void SetTrackingPositions(Vector3 physicalMarkerWorldPos, Vector3 virtualHighlightWorldPos)
    {
        _manualTrackingOffsetMm = Vector3.Distance(physicalMarkerWorldPos, virtualHighlightWorldPos) * 1000f;
    }

    #endregion

    #region Chattering / Flicker Detection Logic

    /// <summary>
    /// 64マスの合法手ビット列 (ulong) を更新し、直近1秒間の反転・変化回数 (チャタリング) を計算します。
    /// </summary>
    public void UpdateLegalMoves(ulong currentLegalMovesMask)
    {
        float now = Time.unscaledTime;

        if (_hasPreviousLegalMoves)
        {
            if (currentLegalMovesMask != _previousLegalMovesMask)
            {
                _flickerTimeQueue.Enqueue(now);
                _previousLegalMovesMask = currentLegalMovesMask;
            }
        }
        else
        {
            _previousLegalMovesMask = currentLegalMovesMask;
            _hasPreviousLegalMoves = true;
        }

        CleanExpiredFlickers(now);
    }

    /// <summary>
    /// 64要素のbool配列から合法手を更新します。
    /// </summary>
    public void UpdateLegalMoves(bool[] legalMoves64)
    {
        if (legalMoves64 == null) return;
        ulong mask = 0UL;
        int count = Math.Min(64, legalMoves64.Length);
        for (int i = 0; i < count; i++)
        {
            if (legalMoves64[i])
            {
                mask |= (1UL << i);
            }
        }
        UpdateLegalMoves(mask);
    }

    /// <summary>
    /// 8x8のbool配列から合法手を更新します。
    /// </summary>
    public void UpdateLegalMoves(bool[,] legalMoves8x8)
    {
        if (legalMoves8x8 == null) return;
        ulong mask = 0UL;
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                if (legalMoves8x8[y, x])
                {
                    int index = y * 8 + x;
                    mask |= (1UL << index);
                }
            }
        }
        UpdateLegalMoves(mask);
    }

    /// <summary>
    /// Vector2Intの合法手座標リスト (x: 0~7, y: 0~7) から合法手を更新します。
    /// </summary>
    public void UpdateLegalMoves(IEnumerable<Vector2Int> playablePositions)
    {
        if (playablePositions == null) return;
        ulong mask = 0UL;
        foreach (var pos in playablePositions)
        {
            if (pos.x >= 0 && pos.x < 8 && pos.y >= 0 && pos.y < 8)
            {
                int index = pos.y * 8 + pos.x;
                mask |= (1UL << index);
            }
        }
        UpdateLegalMoves(mask);
    }

    private void CleanExpiredFlickers(float now)
    {
        while (_flickerTimeQueue.Count > 0 && (now - _flickerTimeQueue.Peek() > 1.0f))
        {
            _flickerTimeQueue.Dequeue();
        }
        _currentFlickerCount1s = _flickerTimeQueue.Count;
    }

    #endregion

    #region Latency Measurement API (Stopwatch / Scope)

    public void RecordLatencyCapture(double ms) => _currentLatencyCapture = ms;
    public void RecordLatencyRecognition(double ms) => _currentLatencyRecognition = ms;
    public void RecordLatencyAlgorithm(double ms) => _currentLatencyAlgorithm = ms;
    public void RecordLatencyRender(double ms) => _currentLatencyRender = ms;

    public void BeginCapture() => _swCapture.Restart();
    public void EndCapture()
    {
        _swCapture.Stop();
        _currentLatencyCapture = _swCapture.Elapsed.TotalMilliseconds;
    }

    public void BeginRecognition() => _swRecognition.Restart();
    public void EndRecognition()
    {
        _swRecognition.Stop();
        _currentLatencyRecognition = _swRecognition.Elapsed.TotalMilliseconds;
    }

    public void BeginAlgorithm() => _swAlgorithm.Restart();
    public void EndAlgorithm()
    {
        _swAlgorithm.Stop();
        _currentLatencyAlgorithm = _swAlgorithm.Elapsed.TotalMilliseconds;
    }

    public void BeginRender() => _swRender.Restart();
    public void EndRender()
    {
        _swRender.Stop();
        _currentLatencyRender = _swRender.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// using ステートメントで各処理ブロックを高精度計測するための計測スコープ構造体
    /// </summary>
    public readonly struct MeasureScope : IDisposable
    {
        private readonly Stopwatch _sw;
        private readonly Action<double> _onComplete;

        public MeasureScope(Stopwatch sw, Action<double> onComplete)
        {
            _sw = sw;
            _onComplete = onComplete;
            _sw.Restart();
        }

        public void Dispose()
        {
            _sw.Stop();
            _onComplete?.Invoke(_sw.Elapsed.TotalMilliseconds);
        }
    }

    public MeasureScope MeasureCapture() => new MeasureScope(_swCapture, ms => _currentLatencyCapture = ms);
    public MeasureScope MeasureRecognition() => new MeasureScope(_swRecognition, ms => _currentLatencyRecognition = ms);
    public MeasureScope MeasureAlgorithm() => new MeasureScope(_swAlgorithm, ms => _currentLatencyAlgorithm = ms);
    public MeasureScope MeasureRender() => new MeasureScope(_swRender, ms => _currentLatencyRender = ms);

    #endregion

    #region FPS Calculation

    private void UpdateFpsCalculation()
    {
        float now = Time.unscaledTime;
        _fpsTimeQueue.Enqueue(now);

        while (_fpsTimeQueue.Count > 0 && (now - _fpsTimeQueue.Peek() > 1.0f))
        {
            _fpsTimeQueue.Dequeue();
        }
    }

    public float CalculateCurrentFps()
    {
        if (_fpsTimeQueue.Count <= 1)
        {
            return Time.unscaledDeltaTime > 0f ? (1f / Time.unscaledDeltaTime) : 0f;
        }

        float timeSpan = Time.unscaledTime - _fpsTimeQueue.Peek();
        if (timeSpan <= 0.0001f)
        {
            return _fpsTimeQueue.Count;
        }

        return (_fpsTimeQueue.Count - 1) / timeSpan;
    }

    #endregion

    #region Mobile / Android Lifecycle Handlers

    private void OnApplicationPause(bool pauseStatus)
    {
        // Android実機: ホーム画面移行やスリープ時に即座にディスクへ書き出す
        if (pauseStatus)
        {
            Debug.Log("[PerformanceLogger] Application Paused. Flushing buffer to CSV...");
            FlushBufferToDisk();
        }
    }

    private void OnApplicationQuit()
    {
        Debug.Log("[PerformanceLogger] Application Quitting. Closing CSV file...");
        CloseLogFile();
    }

    private void OnDestroy()
    {
        CloseLogFile();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    #endregion

    #region On-Screen AR HUD (Hands-Free Indicator)

    private void OnGUI()
    {
        if (!showOnScreenHUD) return;

        // グラス視界の邪魔にならないよう画面左上にコンパクトに表示
        GUILayout.BeginArea(new Rect(20, 20, 300, 110), GUI.skin.box);
        
        bool blink = ((int)(Time.unscaledTime * 2f) % 2 == 0);

        if (_isLogging)
        {
            GUI.color = blink ? Color.red : new Color(1f, 0.4f, 0.4f);
            GUILayout.Label($"<b>● REC [AUTO LOGGING]</b> ({_totalRecordedFrames} frames)");
        }
        else
        {
            GUI.color = Color.gray;
            GUILayout.Label("○ LOGGING IDLE");
        }

        GUI.color = Color.white;
        GUILayout.Label($"FPS: {CalculateCurrentFps():F1} | Latency: {_currentLatencyTotal:F1}ms");
        GUILayout.Label($"Flicker: {_currentFlickerCount1s}/s | Dist: {GetDisplayDistance()}cm");
        GUILayout.EndArea();
    }

    private string GetDisplayDistance()
    {
        if (arCamera != null && boardTransform != null)
        {
            return (Vector3.Distance(arCamera.position, boardTransform.position) * 100f).ToString("F0");
        }
        return "--";
    }

    #endregion
}
