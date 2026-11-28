using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AROthello.Experiment
{
    /// <summary>
    /// 実験者用の手元画面 (Beam Pro)。uGUI を実行時に組み立てるので、シーンには本コンポーネントを置くだけでよい。
    ///
    /// 画面はいまの手順 (位置合わせ → 初期配置 → 実験ID → 試行 → 完了 → 評価・アンケート) を中心に置き、
    /// パターン・位置合わせの基準・グラスの動きの向き・記録などの設定は右上の「設定」の中にまとめる。
    /// 角丸のカード・ピル型のボタン・丸い十字キー・スイッチ・進行バーのモダンな見た目 (改善シート Version 7)。
    /// 既存の手元 UI (カメラ映像・俯瞰ビュー・認識した盤・ログ) は参照があれば新しい画面の中へ移して使い、
    /// 残りの既存パネルは実行時だけ非表示にする (シーンは変更しない。本コンポーネントを無効にすると元の画面に戻る)。
    ///
    /// キーボード (PC での確認用。入力欄の編集中は無効):
    ///   F1 設定 / Enter 次へ進む / PageUp・PageDown パターン切替 / D 四隅の印 / 矢印 位置合わせ
    ///   N 認識を待たず次へ / B 1 手戻る / Shift+R やり直す / 評価: S 成功・F 失敗 / アンケート: 1〜9
    ///   モック盤面使用時: P 正しく置く / W 違う色を置く / E 盤を空に / I 初期配置にする
    /// </summary>
    public class ExperimentOperatorPanel : MonoBehaviour
    {
        public const string RootName = "Experiment_Operator";

        [Header("References")]
        [SerializeField] private ExperimentController controller;
        [SerializeField] private ExperimentLogger logger;
        [SerializeField] private CalibrationDotsView calibrationDots;
        [Tooltip("IBoardOffsetAdjuster を実装したコンポーネント (任意)")]
        [SerializeField] private MonoBehaviour offsetAdjuster;
        [Tooltip("ITrackingSettings を実装したコンポーネント (任意。盤の認識状態・グラスの動きの向き・記録の設定)")]
        [SerializeField] private MonoBehaviour trackingSettings;
        [Tooltip("IGlassesRecorder を実装したコンポーネント (任意。試行中のグラス画面の録画。実機では XrealGlassesRecorder)")]
        [SerializeField] private MonoBehaviour glassesRecorder;
        [Tooltip("PC 確認用のモック盤面 (任意)")]
        [SerializeField] private MockBoardStateProvider mockProvider;
        [Tooltip("画面を置く Canvas (未設定なら専用の Screen Space - Overlay Canvas を作る)")]
        [SerializeField] private Canvas targetCanvas;

        [Header("既存の表示 (任意。新しい画面の中へ移して使う)")]
        [SerializeField] private RawImage cameraView;
        [SerializeField] private RawImage overheadView;
        [SerializeField] private RawImage boardView;
        [SerializeField] private Text boardStatusText;
        [SerializeField] private Text logText;
        [Tooltip("新しい画面に置き換える既存の手元 UI。実行時だけ非表示にする")]
        [SerializeField] private GameObject[] legacyObjects = new GameObject[0];

        [Header("Behaviour")]
        [Tooltip("起動時に設定を開いておく")]
        [SerializeField] private bool startOpen = false;
        [SerializeField] private bool enableKeyboard = true;
        [Tooltip("矢印 1 回分の移動量 (cm)")]
        [SerializeField, Min(0.1f)] private float nudgeStepCm = 0.5f;
        [Tooltip("試行完了ごとに順序 (試行番号) を 1 増やす (実験ID の手順では記録から数え直すので、ふつうは不要)")]
        [SerializeField] private bool autoIncrementOrder = false;
        [Tooltip("やり直し・被験者の削除は 2 回押しで実行 (誤操作防止)。この秒数以内の 2 回目で実行")]
        [SerializeField, Min(0.5f)] private float resetConfirmSeconds = 2.5f;
        [Tooltip("モック盤面の操作ボタンを実機でも出す (既定はエディタだけ)")]
        [SerializeField] private bool showMockOnDevice = false;
        [Tooltip("位置合わせを始めたら四隅の印 (紫) を自動で出し、試行を始めたら (または待機に戻ったら) 自動で消す")]
        [SerializeField] private bool autoCalibrationDots = true;

        [Header("実験ID・評価・アンケート")]
        [Tooltip("実験ID の手順で選べる条件")]
        [SerializeField] private string[] conditions = { "AR", "比較条件" };
        [Tooltip("完了の画面をこの秒数出したら自動で評価に進む (0 なら自動で進めない)")]
        [SerializeField, Min(0f)] private float autoEvaluateDelaySec = 1.5f;
        [Tooltip("失敗の理由の選択肢")]
        [SerializeField] private string[] failReasons = { "置き間違い", "認識の不具合", "途中で中止", "その他" };
        [Tooltip("直後アンケートの定義 (Resources 内の JSON)。ログのフォルダに同じ名前の .json があればそちらを優先する")]
        [SerializeField] private string questionnaireResource = "ExperimentQuestionnaire";

        // ---------------- 配色 (改善シートのモダン案) ----------------
        private static readonly Color ColBg = Hex("#0A0E15");
        private static readonly Color ColS1 = Hex("#121823");
        private static readonly Color ColS2 = Hex("#1A2230");
        private static readonly Color ColS3 = Hex("#252F40");
        private static readonly Color ColHero = Hex("#1A2640");
        private static readonly Color ColLine = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color ColText = Hex("#E9EEF6");
        private static readonly Color ColMuted = Hex("#8A95A8");
        private static readonly Color ColAccent = Hex("#5B8CFF");
        private static readonly Color ColAccent2 = Hex("#A9C1FF");
        private static readonly Color ColDone = Hex("#3A5AA8");
        private static readonly Color ColOk = Hex("#2FBF71");
        private static readonly Color ColWarn = Hex("#F2A33A");
        private static readonly Color ColNg = Hex("#EF5A5A");
        private static readonly Color ColYellow = Hex("#FFE08A");
        private static readonly Color ColPurple = Hex("#F0B4FF");
        private static readonly Color ColCyan = Hex("#38C6F4");
        private static readonly Color ColViolet = Hex("#A78BFA");
        private static readonly Color ColOff = Hex("#3A465C");
        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        private const float RCard = 28f;
        private const float RBtn = 20f;
        private const float Pill = -1f;

        private static readonly string[] BasisNames = { "自分から見た向き", "盤の向き", "部屋の向き" };
        private static readonly string[] StageNames = { "位置合わせ", "初期配置", "実験ID", "試行", "完了", "評価" };
        private static readonly (TrackingAxis axis, string label)[] AxisRows =
        {
            (TrackingAxis.Height, "上下に動かしたとき"),
            (TrackingAxis.Pitch, "上下を向いたとき"),
            (TrackingAxis.Yaw, "左右を向いたとき"),
            (TrackingAxis.Roll, "首をかしげたとき"),
        };
        private const int MaxScale = 11;
        private const int CheckRows = 5;

        private ExperimentOperator _operator;
        private IBoardOffsetAdjuster _adjuster;
        private ITrackingSettings _settings;
        private Font _font;
        private float _resetArmedUntil = -1f;
        private float _deleteArmedUntil = -1f;
        private float _statusTimer;
        private float _completedSince = -1f;
        private bool _refreshing;
        private int _viewIndex;
        private OperatorStep _lastStep = (OperatorStep)(-1);
        private TrialEvaluation _shownEvaluation;
        private string _subjectsSignature = "";
        private string _menuSubjectId;
        private readonly Dictionary<string, int> _trialCounts = new Dictionary<string, int>();

        // 画面の部品
        private RectTransform _root, _settingsSheet, _viewSlot, _boardSlot, _boardStatusSlot, _logSlot, _stepCard, _subjectList;
        private GameObject _boardCard, _primaryHolder, _overrideHolder, _errorBanner, _menuRoot, _dialogRoot;
        private RectTransform _dialogCard;
        private Image _stateDot, _trackDot, _nextStone, _runBarFill;
        private Text _stateText, _trackText, _viewTag, _viewEmptyText, _boardInfo, _headline, _stepName, _stepNext;
        private Text _idlePattern, _idleChip1, _idleChip2, _idleNote, _calibReadout, _checkCount, _checkNote;
        private Text _runProgress, _runNext, _runError, _runStat1, _runStat2, _doneTime, _doneErrors, _doneForced, _doneNote;
        private GameObject _recPill, _recRow;
        private Image _recPillBg, _recPillStroke, _recPillDot, _recRowBg, _recRowStroke;
        private Text _recPillText, _recRowText, _recHint, _recStoreText, _recFreeText;
        private float _freeSpaceTimer;
        private Text _trialNoText, _trialNoNote, _setupPattern, _expIdText, _evalSummary, _qNum, _qText, _scaleNow, _scaleMin, _scaleMid, _scaleMax;
        private Text _patternText, _detailText, _storePathText, _menuTitle;
        private Image _scaleNowBg;
        private CanvasGroup _reasonGroup;
        private InputField _memoField;
        private readonly List<Image> _bars = new List<Image>();
        private readonly List<(GameObject row, Text coord, Text text)> _checkRowsUi = new List<(GameObject, Text, Text)>();
        private readonly Dictionary<OperatorStep, GameObject> _bodies = new Dictionary<OperatorStep, GameObject>();
        private readonly Slider[] _sliders = new Slider[3];
        private readonly Text[] _sliderValues = new Text[3];
        private readonly Dictionary<string, Text> _labels = new Dictionary<string, Text>();
        private readonly Dictionary<string, RectTransform> _switchKnobs = new Dictionary<string, RectTransform>();
        private bool _legacyAdopted;

        public ExperimentOperator Operator => _operator;
        /// <summary>設定シートが開いているか</summary>
        public bool IsOpen => _settingsSheet != null && _settingsSheet.gameObject.activeSelf;
        /// <summary>被験者の ⋯ メニューが開いているか</summary>
        public bool IsMenuOpen => _menuRoot != null && _menuRoot.activeSelf;
        /// <summary>入力ダイアログが開いているか</summary>
        public bool IsDialogOpen => _dialogRoot != null && _dialogRoot.activeSelf;
        /// <summary>組み立てたボタン (ID → Button)。テスト・自動操作用。</summary>
        public Dictionary<string, Button> Buttons { get; } = new Dictionary<string, Button>();
        /// <summary>ボタンのラベル (ID → Text)。テスト用。</summary>
        public IReadOnlyDictionary<string, Text> Labels => _labels;
        /// <summary>入力欄 (ID → InputField)。テスト用。</summary>
        public Dictionary<string, InputField> Inputs { get; } = new Dictionary<string, InputField>();
        /// <summary>スイッチの状態 (ID → オン)。テスト用。</summary>
        public Dictionary<string, bool> Switches { get; } = new Dictionary<string, bool>();
        /// <summary>いま表示している手順の本体 (テスト用)。</summary>
        public OperatorStep ShownStep => _lastStep;

        private void Start()
        {
            if (controller == null) controller = FindFirstObjectByType<ExperimentController>();
            if (logger == null) logger = FindFirstObjectByType<ExperimentLogger>();
            if (calibrationDots == null) calibrationDots = FindFirstObjectByType<CalibrationDotsView>();
            if (controller == null)
            {
                Debug.LogWarning("[ExperimentOperatorPanel] ExperimentController was not found.");
                return;
            }

            InstructionLoader loader = controller.InstructionLoader;
            var op = new ExperimentOperator(
                controller.StateMachine,
                loader != null ? loader.Library : null,
                logger != null ? logger.Metadata : new TrialMetadata(),
                () => Time.realtimeSinceStartupAsDouble);
            if (logger != null)
            {
                op.Store = new ExperimentRecordStore(logger.OutputDirectory);
                op.Recorder = logger.Recorder;
                op.Questionnaire = LoadQuestionnaire(logger.OutputDirectory);

                IGlassesRecorder recorder = glassesRecorder as IGlassesRecorder;
                if (recorder == null)
                {
                    foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    {
                        if (mb is IGlassesRecorder r) { recorder = r; break; }
                    }
                }
                op.Recording = new ExperimentRecordingController(controller.StateMachine, recorder, logger.OutputDirectory,
                    () => op.Metadata, () => Time.realtimeSinceStartupAsDouble)
                {
                    Settings = LoadRecordingSettings()
                };
            }
            if (string.IsNullOrEmpty(op.Metadata.condition) && conditions != null && conditions.Length > 0) op.SetCondition(conditions[0]);
            Initialize(op);

            if (loader != null)
            {
                if (loader.IsLoaded) _operator.LoadSelectedPattern();
                loader.Loaded += lib => { _operator.LoadSelectedPattern(); RefreshAll(); };
            }
        }

        /// <summary>直後アンケートの定義を読む (ログのフォルダの JSON → Resources → 既定の Q1 の順)。</summary>
        private QuestionnaireDefinition LoadQuestionnaire(string outputDirectory)
        {
            Func<string, QuestionnaireDefinition> parse = JsonUtility.FromJson<QuestionnaireDefinition>;
            try
            {
                string path = Path.Combine(outputDirectory, questionnaireResource + ".json");
                if (File.Exists(path))
                {
                    QuestionnaireDefinition fromFile = QuestionnaireDefinition.FromJson(File.ReadAllText(path), parse);
                    if (fromFile != null) return fromFile;
                    Debug.LogWarning($"[ExperimentOperatorPanel] Questionnaire JSON could not be read: {path}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ExperimentOperatorPanel] {ex.Message}");
            }
            TextAsset asset = string.IsNullOrEmpty(questionnaireResource) ? null : Resources.Load<TextAsset>(questionnaireResource);
            return (asset != null ? QuestionnaireDefinition.FromJson(asset.text, parse) : null) ?? QuestionnaireDefinition.Default;
        }

        private void OnDestroy()
        {
            if (_operator != null) _operator.StateMachine.StateChanged -= OnStateChangedForDots;
            _operator?.Recording?.Dispose();
            _operator?.Dispose();
        }

        // ---------------- 録画の設定 (端末に保存) ----------------

        private const string PrefRecEnabled = "AROthello.Experiment.Recording.Enabled";
        private const string PrefRecSource = "AROthello.Experiment.Recording.Source";
        private const string PrefRecQuality = "AROthello.Experiment.Recording.Quality";

        private static RecordingSettings LoadRecordingSettings()
        {
            return new RecordingSettings
            {
                enabled = PlayerPrefs.GetInt(PrefRecEnabled, 1) == 1,
                source = (RecordingSource)Mathf.Clamp(PlayerPrefs.GetInt(PrefRecSource, 0), 0, 2),
                quality = (RecordingQuality)Mathf.Clamp(PlayerPrefs.GetInt(PrefRecQuality, 0), 0, 1)
            };
        }

        private void SaveRecordingSettings()
        {
            ExperimentRecordingController rc = _operator?.Recording;
            if (rc == null || !Application.isPlaying) return;
            PlayerPrefs.SetInt(PrefRecEnabled, rc.Settings.enabled ? 1 : 0);
            PlayerPrefs.SetInt(PrefRecSource, (int)rc.Settings.source);
            PlayerPrefs.SetInt(PrefRecQuality, (int)rc.Settings.quality);
            PlayerPrefs.Save();
        }

        public void OnToggleRecording()
        {
            ExperimentRecordingController rc = _operator?.Recording;
            if (rc == null) return;
            rc.Settings.enabled = !rc.Settings.enabled;
            SaveRecordingSettings();
            RefreshAll();
        }

        public void OnRecordingSource(int index)
        {
            ExperimentRecordingController rc = _operator?.Recording;
            if (rc == null) return;
            rc.Settings.source = (RecordingSource)Mathf.Clamp(index, 0, 2);
            SaveRecordingSettings();
            RefreshAll();
        }

        public void OnRecordingQuality(int index)
        {
            ExperimentRecordingController rc = _operator?.Recording;
            if (rc == null) return;
            rc.Settings.quality = (RecordingQuality)Mathf.Clamp(index, 0, 1);
            SaveRecordingSettings();
            RefreshAll();
        }

        private static string FormatDuration(double sec)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt((float)sec));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>四隅の印を差し替える (テスト用)。</summary>
        public void SetCalibrationDots(CalibrationDotsView dots) => calibrationDots = dots;

        /// <summary>位置合わせを始めたら四隅の印を出し、試行を始めたら・待機に戻ったら消す。</summary>
        private void OnStateChangedForDots(ExperimentState prev, ExperimentState next)
        {
            if (!autoCalibrationDots || calibrationDots == null) return;
            if (next == ExperimentState.Calibration) calibrationDots.SetVisible(true);
            else if (next == ExperimentState.Running || next == ExperimentState.Idle) calibrationDots.SetVisible(false);
        }

        /// <summary>操作ロジックを受け取り画面を組み立てる (Start から呼ばれる。テスト用に公開)。</summary>
        public void Initialize(ExperimentOperator op, Canvas canvas = null)
        {
            if (_operator != null) _operator.StateMachine.StateChanged -= OnStateChangedForDots;
            _operator = op ?? throw new ArgumentNullException(nameof(op));
            _operator.StateMachine.StateChanged += OnStateChangedForDots;
            _operator.AutoIncrementOrder = autoIncrementOrder;
            _operator.ActionLogged += msg => Debug.Log($"[Experiment] {msg}");
            _adjuster = offsetAdjuster as IBoardOffsetAdjuster;
            _settings = trackingSettings as ITrackingSettings;
            if (canvas != null) targetCanvas = canvas;
            if (string.IsNullOrEmpty(_operator.Metadata.condition) && conditions != null && conditions.Length > 0) _operator.SetCondition(conditions[0]);
            BuildUi();
            SetOpen(startOpen);
            RefreshAll();
        }

        /// <summary>トラッキング側の設定を差し替える (テスト用)。</summary>
        public void SetTrackingSettings(ITrackingSettings settings)
        {
            _settings = settings;
            if (_operator != null) { BuildUi(); RefreshAll(); }
        }

        /// <summary>条件の選択肢を差し替える (テスト用)。</summary>
        public void SetConditions(params string[] values)
        {
            conditions = values ?? new string[0];
            if (_operator != null) { BuildUi(); RefreshAll(); }
        }

        private void Update()
        {
            if (_operator == null) return;
            if (enableKeyboard) HandleKeyboard();

            float now = Time.unscaledTime;
            if (_resetArmedUntil > 0 && now > _resetArmedUntil) { _resetArmedUntil = -1f; RefreshAll(); }
            if (_deleteArmedUntil > 0 && now > _deleteArmedUntil) { _deleteArmedUntil = -1f; RefreshMenu(); }

            // 完了の画面を少し出したら評価へ
            if (_operator.Step == OperatorStep.Completed)
            {
                if (_completedSince < 0f) _completedSince = now;
                else if (autoEvaluateDelaySec > 0f && now - _completedSince >= autoEvaluateDelaySec)
                {
                    _operator.BeginEvaluation();
                    RefreshAll();
                }
            }
            else _completedSince = -1f;

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer = 0.2f;
                RefreshAll();
            }
        }

        private void LateUpdate()
        {
            FitAspect(cameraView);
            FitAspect(overheadView);
        }

        // ---------------- 操作 ----------------

        public void SetOpen(bool open)
        {
            if (_settingsSheet != null) _settingsSheet.gameObject.SetActive(open);
            if (open) CloseMenu();
            RefreshAll();
        }

        public void TogglePanel() => SetOpen(!IsOpen);

        public void OnPrimary() { _operator.PrimaryAction(); RefreshAll(); }
        public void OnForceAdvance() { _operator.ForceAdvance(); RefreshAll(); }
        public void OnStepBack() { _operator.StepBack(); RefreshAll(); }
        public void OnPrevPattern() { _operator.SelectPreviousPattern(); RefreshAll(); }
        public void OnNextPattern() { _operator.SelectNextPattern(); RefreshAll(); }
        public void OnToggleDots() { if (calibrationDots != null) calibrationDots.Toggle(); RefreshAll(); }

        /// <summary>1 回目で確認待ち、resetConfirmSeconds 以内の 2 回目でやり直し。</summary>
        public void OnReset()
        {
            if (_resetArmedUntil > 0 && Time.unscaledTime <= _resetArmedUntil)
            {
                _resetArmedUntil = -1f;
                _operator.ResetTrial();
            }
            else
            {
                _resetArmedUntil = Time.unscaledTime + resetConfirmSeconds;
            }
            RefreshAll();
        }

        public void OnNudge(int axis, float sign)
        {
            _adjuster?.Nudge(axis, sign * nudgeStepCm);
            RefreshAll();
        }

        public void OnResetOffset()
        {
            _adjuster?.ResetOffset();
            RefreshAll();
        }

        public void ShowView(int index)
        {
            _viewIndex = Mathf.Clamp(index, 0, 1);
            RefreshViews();
            RefreshAll();
        }

        public void OnOffsetBasis(int basis) { _settings?.SetOffsetBasis(basis); RefreshAll(); }
        public void OnTypicalOffset() { _settings?.ApplyTypicalOffset(); RefreshAll(); }
        public void OnAxis(TrackingAxis axis, bool inverted) { _settings?.SetAxisInverted(axis, inverted); RefreshAll(); }
        public void OnToggleAxis(TrackingAxis axis) { if (_settings != null) OnAxis(axis, !_settings.IsAxisInverted(axis)); }
        public void OnRecalibrate() { _settings?.RecalibrateStoneDetection(); RefreshAll(); }
        public void OnTogglePerformanceLog() { _settings?.TogglePerformanceLogging(); RefreshAll(); }

        // 実験ID
        public void OnSelectSubject(string id) { CloseMenu(); _operator.SelectSubject(id); RefreshAll(); }
        public void OnAddSubject() => OpenTextDialog("新しい被験者", "名前", "", "追加する", name =>
        {
            if (_operator.AddSubject(name) == null) return false;
            _subjectsSignature = "";
            return true;
        });
        public void OnTrialNo(int delta) { _operator.AdjustTrialNo(delta); RefreshAll(); }
        public void OnCondition(int index)
        {
            if (conditions == null || index < 0 || index >= conditions.Length) return;
            _operator.SetCondition(conditions[index]);
            RefreshAll();
        }

        public void OpenSubjectMenu(string id)
        {
            _menuSubjectId = id;
            _deleteArmedUntil = -1f;
            if (_menuRoot != null) _menuRoot.SetActive(true);
            RefreshMenu();
        }

        public void CloseMenu()
        {
            if (_menuRoot != null) _menuRoot.SetActive(false);
            _deleteArmedUntil = -1f;
        }

        private void OnMenuRename()
        {
            SubjectInfo s = _operator.Store?.Subjects.Find(_menuSubjectId);
            CloseMenu();
            if (s == null) return;
            OpenTextDialog("名前を変更", "名前", s.Name, "変更する", name =>
            {
                if (!_operator.RenameSubject(s.Id, name)) return false;
                _subjectsSignature = "";
                return true;
            });
        }

        private void OnMenuEdit()
        {
            SubjectInfo s = _operator.Store?.Subjects.Find(_menuSubjectId);
            CloseMenu();
            if (s != null) OpenInfoDialog(s);
        }

        private void OnMenuHistory()
        {
            SubjectInfo s = _operator.Store?.Subjects.Find(_menuSubjectId);
            CloseMenu();
            if (s != null) OpenHistoryDialog(s);
        }

        /// <summary>削除は 2 回押しで確定 (一覧から隠すだけで、記録は残す)。</summary>
        private void OnMenuDelete()
        {
            if (_deleteArmedUntil > 0 && Time.unscaledTime <= _deleteArmedUntil)
            {
                _operator.DeleteSubject(_menuSubjectId);
                _subjectsSignature = "";
                CloseMenu();
                RefreshAll();
                return;
            }
            _deleteArmedUntil = Time.unscaledTime + resetConfirmSeconds;
            RefreshMenu();
        }

        // 評価・アンケート
        public void OnResult(bool success) { _operator.SetResult(success); RefreshAll(); }
        public void OnFailReason(int index)
        {
            if (failReasons == null || index < 0 || index >= failReasons.Length) return;
            _operator.SetFailReason(failReasons[index]);
            RefreshAll();
        }
        public void OnAnswer(int value) { _operator.SetAnswer(value); RefreshAll(); }

        private void OnSlider(int axis, float value)
        {
            if (_refreshing || _adjuster == null) return;
            float target = Mathf.Round(value * 10f) / 10f;
            _adjuster.Nudge(axis, target - _adjuster.CurrentOffsetCm[axis]);
            RefreshAll();
        }

        // モック盤面 (PC 確認用)
        public void MockPlaceTarget()
        {
            if (mockProvider == null) return;
            mockProvider.Place(_operator.StateMachine.CurrentStep);
        }

        public void MockPlaceWrongColor()
        {
            InstructionStep s = _operator.StateMachine.CurrentStep;
            if (mockProvider == null || s == null) return;
            BoardCoord.TryParseColor(s.color, out StoneColor c);
            mockProvider.SetCell(s.col, s.row, c == StoneColor.Black ? StoneColor.White : StoneColor.Black);
        }

        public void MockClear() { if (mockProvider != null) mockProvider.ClearBoard(); }
        public void MockPlaceInitialStones() { if (mockProvider != null) mockProvider.PlaceInitialStones(_operator.StateMachine.Pattern); }

        private void HandleKeyboard()
        {
            EventSystem es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null)
            {
                InputField f = es.currentSelectedGameObject.GetComponent<InputField>();
                if (f != null && f.isFocused) return;
            }
            if (IsDialogOpen) return;

            if (Input.GetKeyDown(KeyCode.F1)) TogglePanel();
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnPrimary();
            if (Input.GetKeyDown(KeyCode.PageUp)) OnPrevPattern();
            if (Input.GetKeyDown(KeyCode.PageDown)) OnNextPattern();
            if (Input.GetKeyDown(KeyCode.D)) OnToggleDots();
            if (Input.GetKeyDown(KeyCode.N)) OnForceAdvance();
            if (Input.GetKeyDown(KeyCode.B)) OnStepBack();
            if (Input.GetKeyDown(KeyCode.UpArrow)) OnNudge(2, +1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) OnNudge(2, -1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) OnNudge(0, -1);
            if (Input.GetKeyDown(KeyCode.RightArrow)) OnNudge(0, +1);
            if (Input.GetKeyDown(KeyCode.R) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                _operator.ResetTrial();
                RefreshAll();
            }
            if (_operator.Step == OperatorStep.Evaluation)
            {
                if (Input.GetKeyDown(KeyCode.S)) OnResult(true);
                if (Input.GetKeyDown(KeyCode.F)) OnResult(false);
            }
            if (_operator.Step == OperatorStep.Survey)
            {
                for (int v = 1; v <= 9; v++)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha0 + v) || Input.GetKeyDown(KeyCode.Keypad0 + v)) OnAnswer(v);
                }
            }

            if (MockVisible)
            {
                if (Input.GetKeyDown(KeyCode.P)) MockPlaceTarget();
                if (Input.GetKeyDown(KeyCode.W)) MockPlaceWrongColor();
                if (Input.GetKeyDown(KeyCode.E)) MockClear();
                if (Input.GetKeyDown(KeyCode.I)) MockPlaceInitialStones();
            }
        }

        private bool MockVisible => mockProvider != null && (Application.isEditor || showMockOnDevice);

        // ---------------- 表示更新 ----------------

        public void RefreshAll()
        {
            if (_operator == null || _root == null) return;
            _refreshing = true;
            try
            {
                OperatorStep step = _operator.Step;
                ExperimentStateMachine sm = _operator.StateMachine;
                if (step != _lastStep)
                {
                    _lastStep = step;
                    if (step == OperatorStep.Setup) _subjectsSignature = "";
                    if (step != OperatorStep.Setup) CloseMenu();
                }
                RefreshTopBar(step);
                RefreshLayout(step);
                RefreshStages(step);
                RefreshStepCard(sm, step);
                RefreshSettings();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private static bool IsWide(OperatorStep s) => s == OperatorStep.Setup || s == OperatorStep.Evaluation || s == OperatorStep.Survey;

        private void RefreshLayout(OperatorStep step)
        {
            bool wide = IsWide(step);
            _stepCard.anchorMin = new Vector2(wide ? 0.03f : 0.405f, _stepCard.anchorMin.y);
            _boardCard.SetActive(!wide);
        }

        private void RefreshTopBar(OperatorStep step)
        {
            (string text, Color color) chip;
            switch (step)
            {
                case OperatorStep.Calibration: chip = ("位置合わせ中", ColCyan); break;
                case OperatorStep.InitialBoardCheck: chip = ("初期配置の確認中", ColWarn); break;
                case OperatorStep.Setup: chip = ("実験IDの設定中", ColViolet); break;
                case OperatorStep.Running: chip = ("試行中", ColOk); break;
                case OperatorStep.Completed: chip = ("完了", ColAccent); break;
                case OperatorStep.Evaluation: chip = ("評価中", ColAccent); break;
                case OperatorStep.Survey: chip = ("アンケート中", ColAccent); break;
                default: chip = ("待機中", ColMuted); break;
            }
            _stateText.text = chip.text;
            _stateDot.color = chip.color;

            bool recShown = RefreshRecordingPill(step);
            bool hasTracking = _settings != null;
            _trackDot.gameObject.SetActive(hasTracking);
            _trackText.gameObject.SetActive(hasTracking);
            _trackText.rectTransform.anchorMax = new Vector2(recShown ? 0.58f : 0.75f, 1f);
            if (hasTracking)
            {
                bool ok = _settings.IsTracking;
                _trackDot.color = ok ? ColOk : ColNg;
                _trackText.text = recShown ? (ok ? "認識中" : "見えていません") : (ok ? "盤を認識中" : "盤が見えていません");
            }
            SetButtonColor("settings", IsOpen ? ColAccent : ColS2);
            SetLabel("settings", IsOpen ? "閉じる" : "設定");

            InstructionPatternData p = _operator.Library.Current ?? _operator.StateMachine.Pattern;
            _boardInfo.text = p != null ? $"パターン  <b><color=#E9EEF6>{p.patternId}</color></b>\n{p.stepCount} 手・難易度 {p.difficulty}" : "パターンなし";
        }

        /// <summary>上部バーの「録画中」。試行中に録画している (または失敗した) ときだけ出す。</summary>
        private bool RefreshRecordingPill(OperatorStep step)
        {
            ExperimentRecordingController rc = _operator.Recording;
            bool show = rc != null && step == OperatorStep.Running && rc.Settings.enabled && rc.Status != RecordingStatus.Off;
            _recPill.SetActive(show);
            if (!show) return false;
            bool bad = rc.Status == RecordingStatus.Failed;
            Color c = bad ? ColWarn : ColNg;
            _recPillBg.color = new Color(c.r, c.g, c.b, 0.14f);
            _recPillStroke.color = new Color(c.r, c.g, c.b, 0.5f);
            _recPillDot.color = c;
            _recPillText.color = bad ? Hex("#FFD08F") : Hex("#FFB0B0");
            _recPillText.text = rc.Status == RecordingStatus.Recording ? $"録画中 {FormatDuration(rc.ElapsedSec)}"
                : rc.Status == RecordingStatus.Starting ? "録画の準備中"
                : bad ? "録画できません" : "録画を保存中";
            return true;
        }

        /// <summary>完了の画面の「録画を保存しました」。</summary>
        private void RefreshRecordingRow()
        {
            ExperimentRecordingController rc = _operator.Recording;
            if (rc == null) { _recRow.SetActive(false); return; }
            string id = _operator.LastTrialMetadata.experimentId;
            RecordingResult r = rc.LastResult;
            bool mine = r != null && !r.WasReset && r.ExperimentId == id;
            bool stopping = rc.Status == RecordingStatus.Stopping;
            bool failed = !mine && rc.Settings.enabled && rc.Status == RecordingStatus.Failed;
            bool show = mine || stopping || failed;
            _recRow.SetActive(show);
            if (!show) return;

            bool warn = failed || (mine && (!r.Success || r.FellBack));
            Color c = warn ? ColWarn : ColNg;
            _recRowBg.color = new Color(c.r, c.g, c.b, 0.08f);
            _recRowStroke.color = new Color(c.r, c.g, c.b, 0.35f);
            _recRowText.color = warn ? Hex("#FFD08F") : ColText;
            if (stopping) _recRowText.text = "録画を保存しています…";
            else if (failed) _recRowText.text = $"録画できませんでした: {rc.LastError}";
            else if (!r.Success) _recRowText.text = $"録画できませんでした: {r.Error}";
            else if (r.FellBack) _recRowText.text = $"カメラを開けず AR 表示だけで録画しました  <b><color=#A9C1FF>{r.FileName}</color></b>  {FormatDuration(r.DurationSec)}";
            else _recRowText.text = $"録画を保存しました  <b><color=#A9C1FF>{r.FileName}</color></b>  {FormatDuration(r.DurationSec)}";
        }

        private void RefreshRecordingSettings()
        {
            ExperimentRecordingController rc = _operator.Recording;
            if (rc == null || _recHint == null) return;
            SetSwitch("rec_enabled", rc.Settings.enabled);
            for (int i = 0; i < 3; i++) SetSegmentButton("rec_source_" + i, (int)rc.Settings.source == i);
            for (int i = 0; i < 2; i++) SetSegmentButton("rec_quality_" + i, (int)rc.Settings.quality == i);
            _recHint.text = "「見えている景色」はグラスのカメラ映像に AR 表示を重ねたもの。盤の認識と同じカメラを使うので、カメラを開けないときは自動で「AR 表示だけ」になります。被験者のグラスには録画中の表示を出しません。"
                + (rc.IsAvailable ? "" : "\n<color=#F2A33A>この端末 (エディタ) では録画できません。実機 (Beam Pro) で録画します。</color>");
            string parent = Path.GetFileName(Path.GetDirectoryName(rc.VideoDirectory) ?? "");
            _recStoreText.text = $"{parent}/{ExperimentRecordingController.VideoFolder}/実験ID.mp4";
            if (IsOpen && Time.unscaledTime >= _freeSpaceTimer)
            {
                _freeSpaceTimer = Time.unscaledTime + 2f;
                long bytes = StorageInfo.AvailableBytes(Application.persistentDataPath);
                if (bytes < 0) _recFreeText.text = "(分かりません)";
                else
                {
                    double hours = ExperimentRecordingController.EstimateHours(bytes);
                    string quality = rc.Settings.quality == RecordingQuality.High1080 ? "1080p" : "720p";
                    _recFreeText.text = $"{bytes / 1e9:0.0} GB（{quality} で約 {hours:0.#} 時間分）";
                    _recFreeText.color = hours < 1.0 ? ColWarn : ColMuted;
                }
            }
        }

        private void SetSegmentButton(string id, bool on)
        {
            SetButtonColor(id, on ? ColAccent : Clear);
            if (_labels.TryGetValue(id, out Text t)) t.color = on ? Color.white : ColMuted;
        }

        private void RefreshStages(OperatorStep step)
        {
            int current;
            switch (step)
            {
                case OperatorStep.Calibration: current = 0; break;
                case OperatorStep.InitialBoardCheck: current = 1; break;
                case OperatorStep.Setup: current = 2; break;
                case OperatorStep.Running: current = 3; break;
                case OperatorStep.Completed: current = 4; break;
                case OperatorStep.Evaluation:
                case OperatorStep.Survey: current = 5; break;
                default: current = -1; break;
            }
            for (int i = 0; i < _bars.Count; i++) _bars[i].color = i == current ? ColAccent : i < current ? ColDone : ColS3;
            _stepName.text = current < 0 ? "準備" : $"STEP <color=#A9C1FF>{current + 1}</color> / {StageNames.Length} ・ {StageNames[current]}";
            _stepNext.text = current < 0 ? "次: 位置合わせ"
                : current < StageNames.Length - 1 ? $"次: {StageNames[current + 1]}"
                : step == OperatorStep.Evaluation && _operator.Questionnaire != null && _operator.Questionnaire.Count > 0 ? "次: アンケート" : "次: 次の試行";
        }

        private void RefreshStepCard(ExperimentStateMachine sm, OperatorStep step)
        {
            foreach (var kv in _bodies) kv.Value.SetActive(kv.Key == step);

            bool trialActive = step == OperatorStep.InitialBoardCheck || step == OperatorStep.Running;
            _primaryHolder.SetActive(!trialActive);
            _overrideHolder.SetActive(trialActive);

            SetLabel("primary", _operator.PrimaryActionLabel);
            SetInteractable("primary", _operator.CanPrimaryAction);
            SetButtonColor("primary", _operator.CanPrimaryAction ? ColAccent : ColS3);

            bool armed = _resetArmedUntil > 0 && Time.unscaledTime <= _resetArmedUntil;
            SetLabel("reset", armed ? "もう一度押すとやり直し" : "最初からやり直す");
            SetButtonColor("reset", armed ? new Color(ColNg.r, ColNg.g, ColNg.b, 0.45f) : new Color(ColNg.r, ColNg.g, ColNg.b, 0.12f));
            SetLabel("force", step == OperatorStep.InitialBoardCheck ? "確認をとばして次へ" : "認識を待たず次へ");
            if (Buttons.TryGetValue("back", out Button back))
            {
                back.gameObject.SetActive(step == OperatorStep.Running);
                back.interactable = sm.CurrentStepIndex > 0;
            }
            if (Buttons.TryGetValue("force", out Button force))
            {
                // 初期配置の確認中は「1手戻る」が無いので左へ広げる
                ((RectTransform)force.transform).anchorMin = new Vector2(step == OperatorStep.Running ? 0.26f : 0f, 0f);
            }

            InstructionPatternData pattern = _operator.Library.Current ?? sm.Pattern;
            switch (step)
            {
                case OperatorStep.Idle:
                    _headline.text = "準備ができたら位置合わせを始めます";
                    _idlePattern.text = pattern != null ? pattern.patternId : "パターンなし";
                    _idleChip1.text = pattern != null ? $"{pattern.stepCount} 手" : "-";
                    _idleChip2.text = pattern != null ? $"難易度 {pattern.difficulty}" : "-";
                    _idleNote.text = pattern != null ? "被験者は「実験ID」の手順で選びます。" : "<color=#FF9A7A>パターンが読み込まれていません。「設定」で選んでください。</color>";
                    break;

                case OperatorStep.Calibration:
                    _headline.text = "グラスの表示を実物の盤に合わせます";
                    _calibReadout.text = BuildOffsetReadout();
                    bool dots = calibrationDots != null && calibrationDots.Visible;
                    SetLabel("dots", dots ? "● 四隅の印を消す" : "● 四隅の印を出す");
                    SetButtonColor("dots", dots ? new Color(0.94f, 0.7f, 1f, 0.22f) : ColS2);
                    break;

                case OperatorStep.InitialBoardCheck:
                    IReadOnlyList<CellMismatch> mm = sm.LastInitialMismatches;
                    bool empty = pattern == null || pattern.initialStones == null || pattern.initialStones.Length == 0;
                    _headline.text = empty ? "盤を空にしてください" : "盤を初期配置にしてください";
                    _checkCount.text = mm.Count == 0 ? "そろいました" : $"あと {mm.Count} マス";
                    _checkCount.color = mm.Count == 0 ? ColOk : ColWarn;
                    for (int i = 0; i < _checkRowsUi.Count; i++)
                    {
                        bool show = i < mm.Count;
                        _checkRowsUi[i].row.SetActive(show);
                        if (!show) continue;
                        _checkRowsUi[i].coord.text = mm[i].Coord;
                        _checkRowsUi[i].text.text = DescribeMismatch(mm[i]);
                    }
                    _checkNote.text = mm.Count > _checkRowsUi.Count ? $"ほか {mm.Count - _checkRowsUi.Count} マス。そろうと自動で「実験ID」に進みます。" : "そろうと自動で「実験ID」に進みます。";
                    break;

                case OperatorStep.Setup:
                    _headline.text = string.IsNullOrEmpty(_operator.Metadata.subjectId) ? "被験者を選んでください" : "被験者を選んで試行を始めます";
                    RefreshSetup(pattern);
                    break;

                case OperatorStep.Running:
                    _headline.text = "被験者が石を置いています";
                    _runProgress.text = $"<size=60><b><color=#E9EEF6>{sm.CurrentStepIndex + 1}</color></b></size>  / {sm.StepTotal} 手目";
                    _runBarFill.rectTransform.anchorMax = new Vector2(sm.StepTotal > 0 ? (float)sm.CurrentStepIndex / sm.StepTotal : 0f, 1f);
                    _runBarFill.gameObject.SetActive(sm.CurrentStepIndex > 0);
                    InstructionStep st = sm.CurrentStep;
                    if (st != null)
                    {
                        BoardCoord.TryParseColor(st.color, out StoneColor c);
                        _nextStone.color = c == StoneColor.White ? new Color(0.95f, 0.95f, 0.95f, 1f) : new Color(0.04f, 0.04f, 0.04f, 1f);
                        _runNext.text = $"{st.coord} に{ColorName(c)}";
                    }
                    _errorBanner.SetActive(sm.IsErrorActive);
                    if (sm.IsErrorActive) _runError.text = DescribeError(sm.ActiveError, st);
                    _runStat1.text = $"<size=44><b>{sm.CurrentStepErrorCount}</b></size> 回";
                    _runStat2.text = $"<size=44><b>{sm.TotalErrorCount}</b></size> 回";
                    break;

                case OperatorStep.Completed:
                    _headline.text = "試行が終わりました";
                    TrialResult r = _operator.LastTrial;
                    int forced = 0;
                    if (r != null) foreach (StepResult s in r.Steps) if (s.IsOverride) forced++;
                    _doneTime.text = r != null ? $"<size=40><b>{r.TotalTimeSec:0.0}</b></size> 秒" : "-";
                    _doneErrors.text = r != null ? $"<size=40><b>{r.TotalErrorCount}</b></size> 回" : "-";
                    _doneForced.text = r != null ? $"<size=40><b>{forced}</b></size> 手" : "-";
                    RefreshRecordingRow();
                    _doneNote.text = autoEvaluateDelaySec > 0f
                        ? "まもなく評価に進みます。成功か失敗かとメモ、続けて直後アンケートを記録します。"
                        : "「評価へ」で、成功か失敗かとメモ、続けて直後アンケートを記録します。";
                    break;

                case OperatorStep.Evaluation:
                    _headline.text = "この試行を評価します";
                    RefreshEvaluation();
                    break;

                case OperatorStep.Survey:
                    _headline.text = "直後アンケート";
                    RefreshSurvey();
                    break;
            }
        }

        private void RefreshSetup(InstructionPatternData pattern)
        {
            RebuildSubjectsIfNeeded();
            string selected = _operator.Metadata.subjectId;
            if (_operator.Store != null)
            {
                foreach (SubjectInfo s in _operator.Store.Subjects.Active)
                {
                    bool on = s.Id == selected;
                    SetButtonColor("subject_" + s.Id, on ? new Color(ColAccent.r, ColAccent.g, ColAccent.b, 0.16f) : ColS2);
                    if (Buttons.TryGetValue("subject_" + s.Id, out Button b))
                    {
                        Transform chk = b.transform.Find("Check");
                        if (chk != null) chk.gameObject.SetActive(on);
                        Transform ring = b.transform.Find("Ring");
                        if (ring != null) ring.gameObject.SetActive(on);
                        Transform ava = b.transform.Find("Avatar");
                        if (ava != null) ava.GetComponent<Image>().color = on ? ColAccent : Hex("#34405A");
                    }
                }
            }

            bool hasSubject = !string.IsNullOrEmpty(selected);
            _trialNoText.text = hasSubject ? _operator.Metadata.order.ToString() : "-";
            int done = hasSubject && _trialCounts.TryGetValue(selected, out int cached) ? cached : 0;
            _trialNoNote.text = !hasSubject ? "被験者を選ぶと記録から自動で入ります"
                : _operator.Metadata.order == done + 1 ? $"回目 ・ 記録 {done} 件から自動" : $"回目 ・ 手で変更 (記録は {done} 件)";
            SetInteractable("trial_minus", hasSubject && _operator.Metadata.order > 1);
            SetInteractable("trial_plus", hasSubject);

            for (int i = 0; conditions != null && i < conditions.Length; i++)
            {
                bool on = conditions[i] == _operator.Metadata.condition;
                SetButtonColor("cond_" + i, on ? ColAccent : Clear);
                if (_labels.TryGetValue("cond_" + i, out Text t)) t.color = on ? Color.white : ColMuted;
            }
            _setupPattern.text = pattern != null ? $"パターン  <b><color=#FFE08A>{pattern.patternId}</color></b>" : "パターンなし";
            _expIdText.text = hasSubject ? _operator.Metadata.experimentId : "-";
        }

        private void RefreshEvaluation()
        {
            TrialEvaluation e = _operator.Evaluation;
            if (e != _shownEvaluation)
            {
                _shownEvaluation = e;
                if (_memoField != null) _memoField.SetTextWithoutNotify(e.Memo ?? "");
            }
            TrialResult r = _operator.LastTrial;
            int forced = 0;
            if (r != null) foreach (StepResult s in r.Steps) if (s.IsOverride) forced++;
            string video = _operator.Recording != null ? _operator.Recording.VideoFileFor(_operator.LastTrialMetadata.experimentId) : "";
            _evalSummary.text = (r != null
                ? $"<color=#A9C1FF><b>{_operator.LastTrialMetadata.experimentId}</b></color>　　{r.TotalTimeSec:0.0} 秒　・　間違い {r.TotalErrorCount} 回　・　手動 {forced} 手"
                : _operator.LastTrialMetadata.experimentId)
                + (string.IsNullOrEmpty(video) ? "" : "　・　<color=#FFB0B0>● 録画あり</color>");

            bool ok = e.Success == true, ng = e.Success == false;
            SetTile("result_success", ok, ColOk);
            SetTile("result_fail", ng, ColNg);
            _reasonGroup.alpha = ng ? 1f : 0.45f;
            _reasonGroup.interactable = true;
            for (int i = 0; failReasons != null && i < failReasons.Length; i++)
            {
                bool on = ng && e.FailReason == failReasons[i];
                SetButtonColor("reason_" + i, on ? new Color(ColNg.r, ColNg.g, ColNg.b, 0.3f) : Clear);
            }
        }

        private void SetTile(string id, bool on, Color color)
        {
            SetButtonColor(id, on ? new Color(color.r, color.g, color.b, 0.16f) : ColS2);
            if (!Buttons.TryGetValue(id, out Button b)) return;
            Transform ring = b.transform.Find("Ring");
            if (ring != null) ring.GetComponent<Image>().color = on ? new Color(color.r, color.g, color.b, 0.75f) : ColLine;
            Transform icon = b.transform.Find("Icon");
            if (icon != null) icon.GetComponent<Image>().color = on ? color : ColS3;
            if (_labels.TryGetValue(id, out Text t)) t.color = on ? ColText : ColMuted;
        }

        private void RefreshSurvey()
        {
            QuestionnaireQuestion q = _operator.CurrentQuestion;
            if (q == null) return;
            int total = _operator.Questionnaire.Count;
            _qNum.text = $"{q.id}  ・  {_operator.SurveyIndex + 1} / {total}" + (string.IsNullOrEmpty(q.title) ? "" : $"  ・  {q.title}");
            _qText.text = Emphasize(q.text);
            int count = q.labels.Length;
            int? answer = _operator.CurrentAnswer;
            for (int i = 0; i < MaxScale; i++)
            {
                string id = "q_" + i;
                if (!Buttons.TryGetValue(id, out Button b)) continue;
                bool show = i < count;
                b.gameObject.SetActive(show);
                if (!show) continue;
                var rt = (RectTransform)b.transform;
                float w = 1f / count;
                rt.anchorMin = new Vector2(i * w + w * 0.06f, 0.3f);
                rt.anchorMax = new Vector2((i + 1) * w - w * 0.06f, 0.47f);
                int value = q.min + i;
                SetLabel(id, value.ToString());
                bool on = answer == value;
                SetButtonColor(id, on ? ColAccent : ColS2);
            }
            _scaleMin.text = q.LabelOf(q.min);
            _scaleMax.text = q.LabelOf(q.Max);
            _scaleMid.text = count % 2 == 1 ? q.LabelOf(q.min + count / 2) : "";
            _scaleNowBg.gameObject.SetActive(answer.HasValue);
            _scaleNow.text = answer.HasValue ? $"{answer.Value}：{q.LabelOf(answer.Value)}" : "";
        }

        /// <summary>**で囲んだ部分を太字・黄色にする。</summary>
        private static string Emphasize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            string[] parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                sb.Append(i % 2 == 1 ? $"<b><color=#FFE08A>{parts[i]}</color></b>" : parts[i]);
            }
            return sb.ToString();
        }

        private void RefreshSettings()
        {
            if (_detailText != null)
            {
                string dots = calibrationDots != null && calibrationDots.Visible ? "  [四隅の印を表示中]" : "";
                _detailText.text = _operator.BuildStatusText() + dots;
            }
            if (_patternText != null)
            {
                InstructionPatternData p = _operator.Library.Current;
                _patternText.text = p != null ? p.ToString() : "(パターンなし)";
            }
            SetInteractable("pattern_prev", !_operator.IsPatternLocked);
            SetInteractable("pattern_next", !_operator.IsPatternLocked);
            if (_storePathText != null) _storePathText.text = _operator.Store != null ? _operator.Store.Directory : "(記録の保存先は未接続)";

            Vector3 cm = _adjuster != null ? _adjuster.CurrentOffsetCm : Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                if (_sliders[i] != null) _sliders[i].SetValueWithoutNotify(cm[i]);
                if (_sliderValues[i] != null) _sliderValues[i].text = $"{cm[i]:+0.0;-0.0;0.0} cm";
            }

            RefreshRecordingSettings();
            if (_settings == null) return;
            for (int b = 0; b < BasisNames.Length; b++)
            {
                bool on = _settings.OffsetBasis == b;
                SetButtonColor("basis_" + b, on ? ColAccent : Clear);
                if (_labels.TryGetValue("basis_" + b, out Text t)) t.color = on ? Color.white : ColMuted;
            }
            foreach (var row in AxisRows) SetSwitch($"axis_{(int)row.axis}", _settings.IsAxisInverted(row.axis));
            SetSwitch("perf_log", _settings.IsPerformanceLogging);
        }

        private void SetSwitch(string id, bool on)
        {
            Switches[id] = on;
            SetButtonColor(id, on ? ColOk : ColOff);
            if (!_switchKnobs.TryGetValue(id, out RectTransform knob)) return;
            knob.anchorMin = knob.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
            knob.anchoredPosition = new Vector2(on ? -24f : 24f, 0f);
        }

        private string BuildOffsetReadout()
        {
            if (_adjuster == null) return "<color=#8A95A8>(位置合わせは未接続)</color>";
            Vector3 cm = _adjuster.CurrentOffsetCm;
            string basis = _settings != null ? BasisNames[Mathf.Clamp(_settings.OffsetBasis, 0, 2)] : _adjuster.ModeName;
            return $"左右 <color=#A9C1FF>{cm.x:+0.0;-0.0;0.0}</color>　奥行 <color=#A9C1FF>{cm.z:+0.0;-0.0;0.0}</color>　高さ <color=#A9C1FF>{cm.y:+0.0;-0.0;0.0}</color> cm　<color=#8A95A8>{basis}</color>";
        }

        private static string ColorName(StoneColor c) => c == StoneColor.White ? "白" : c == StoneColor.Black ? "黒" : "なし";

        private static string DescribeMismatch(CellMismatch m)
        {
            if (m.Expected == StoneColor.None) return "石をとる";
            if (m.Actual == StoneColor.None) return $"{ColorName(m.Expected)}を置く";
            return $"{ColorName(m.Expected)}にする";
        }

        private static string DescribeError(PlacementError e, InstructionStep step)
        {
            if (e == null || e.Mismatches.Count == 0) return "置き間違いがあります";
            CellMismatch m = e.Mismatches[0];
            StoneColor want = StoneColor.None;
            if (step != null) BoardCoord.TryParseColor(step.color, out want);
            switch (e.Type)
            {
                case PlacementErrorType.WrongColor:
                    return want != StoneColor.None
                        ? $"{m.Coord} に{ColorName(m.Actual)}が置かれています\n(正しくは{ColorName(want)})"
                        : $"{m.Coord} の色が違います";
                case PlacementErrorType.WrongPosition:
                    return step != null
                        ? $"{m.Coord} は指示と違うマスです\n(正しくは {step.coord})"
                        : $"{m.Coord} は指示と違うマスです";
                case PlacementErrorType.MissingStone: return $"{m.Coord} の石がなくなっています";
                default: return $"{m.Coord} の石の色が変わっています";
            }
        }

        private void SetLabel(string id, string text) { if (_labels.TryGetValue(id, out Text t)) t.text = text; }
        private void SetInteractable(string id, bool on) { if (Buttons.TryGetValue(id, out Button b)) b.interactable = on; }
        private void SetButtonColor(string id, Color c)
        {
            if (Buttons.TryGetValue(id, out Button b) && b.targetGraphic != null) b.targetGraphic.color = c;
        }

        private void RefreshViews()
        {
            if (cameraView != null && _legacyAdopted) cameraView.gameObject.SetActive(_viewIndex == 0);
            if (overheadView != null && _legacyAdopted) overheadView.gameObject.SetActive(_viewIndex == 1);
            RawImage shown = _viewIndex == 0 ? cameraView : overheadView;
            if (_viewEmptyText != null) _viewEmptyText.gameObject.SetActive(shown == null);
            if (_viewTag != null) _viewTag.text = _viewIndex == 0 ? "<color=#EF5A5A>●</color> LIVE　カメラの映像" : "上から見た配置";
            SetSegment("view_camera", _viewIndex == 0);
            SetSegment("view_overhead", _viewIndex == 1);
        }

        private void SetSegment(string id, bool on)
        {
            SetButtonColor(id, on ? ColS3 : Clear);
            if (_labels.TryGetValue(id, out Text t)) t.color = on ? ColText : ColMuted;
        }

        private static void FitAspect(RawImage img)
        {
            if (img == null || img.texture == null) return;
            AspectRatioFitter f = img.GetComponent<AspectRatioFitter>();
            if (f == null || img.texture.height <= 0) return;
            float a = (float)img.texture.width / img.texture.height;
            if (Mathf.Abs(f.aspectRatio - a) > 0.001f) f.aspectRatio = a;
        }

        // ---------------- 画面の組み立て ----------------

        private void BuildUi()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Buttons.Clear();
            Inputs.Clear();
            Switches.Clear();
            _labels.Clear();
            _bodies.Clear();
            _bars.Clear();
            _checkRowsUi.Clear();
            _switchKnobs.Clear();
            _subjectsSignature = "";
            _lastStep = (OperatorStep)(-1);
            _recHint = _recStoreText = _recFreeText = null;

            if (targetCanvas == null)
            {
                var canvasGo = new GameObject("ExperimentOperatorCanvas");
                canvasGo.transform.SetParent(transform, false);
                targetCanvas = canvasGo.AddComponent<Canvas>();
                targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                targetCanvas.sortingOrder = 200;
                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                scaler.matchWidthOrHeight = 0.5f;
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            Transform canvasRoot = targetCanvas.transform;
            ReleaseAdoptedViews();
            Transform old = canvasRoot.Find(RootName);
            if (old != null) DestroyObj(old.gameObject);

            _root = Img((RectTransform)canvasRoot, RootName, Vector2.zero, Vector2.one, ColBg).rectTransform;
            _root.SetAsLastSibling();

            BuildTopBar();
            BuildViewArea();
            BuildBoardCard();
            BuildStepCard();
            BuildSettingsSheet();
            BuildSubjectMenu();
            BuildDialog();
            _freeSpaceTimer = 0f;
            AdoptLegacyViews();
            RefreshViews();
        }

        private void BuildTopBar()
        {
            RectTransform bar = Rect(_root, "TopBar", new Vector2(0f, 0.94f), Vector2.one);
            Image pill = Img(bar, "StatePill", new Vector2(0.03f, 0.18f), new Vector2(0.42f, 0.82f), ColS2, Pill);
            AddStroke(pill.rectTransform, ColLine, Pill);
            _stateDot = FixedImg(pill.rectTransform, "StateDot", new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(20f, 20f), ColMuted, UiSprites.Circle);
            _stateText = Txt(pill.rectTransform, "StateText", "", 28, ColText, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, true);
            Pad(_stateText.rectTransform, 56, 12);

            _trackDot = FixedImg(bar, "TrackDot", new Vector2(0.45f, 0.5f), Vector2.zero, new Vector2(16f, 16f), ColMuted, UiSprites.Circle);
            _trackText = Txt(bar, "TrackText", "", 23, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.465f, 0f), new Vector2(0.75f, 1f));

            Button settings = Btn(bar, "settings", "設定", new Vector2(0.76f, 0.18f), new Vector2(0.97f, 0.82f), ColS2, ColText, 26, TogglePanel, Pill);
            AddStroke((RectTransform)settings.transform, ColLine, Pill);
            FixedImg((RectTransform)settings.transform, "Icon", new Vector2(0f, 0.5f), new Vector2(46f, 0f), new Vector2(30f, 30f), ColText, UiSprites.Sliders);
            Pad(_labels["settings"].rectTransform, 50, 8);

            // 録画中の表示 (試行中に録画しているときだけ)
            _recPillBg = Img(bar, "RecPill", new Vector2(0.585f, 0.2f), new Vector2(0.75f, 0.8f), new Color(ColNg.r, ColNg.g, ColNg.b, 0.14f), Pill);
            _recPillStroke = AddStroke(_recPillBg.rectTransform, new Color(ColNg.r, ColNg.g, ColNg.b, 0.5f), Pill);
            _recPillDot = FixedImg(_recPillBg.rectTransform, "Dot", new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(16f, 16f), ColNg, UiSprites.Circle);
            _recPillText = Txt(_recPillBg.rectTransform, "Text", "", 21, Hex("#FFB0B0"), TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, true);
            Pad(_recPillText.rectTransform, 42, 8);
            _recPill = _recPillBg.gameObject;
            _recPill.SetActive(false);
        }

        private void BuildViewArea()
        {
            Image track = Img(_root, "Tabs", new Vector2(0.03f, 0.895f), new Vector2(0.97f, 0.934f), ColS1, Pill);
            AddStroke(track.rectTransform, ColLine, Pill);
            Btn(track.rectTransform, "view_camera", "カメラの映像", new Vector2(0.006f, 0.1f), new Vector2(0.497f, 0.9f), ColS3, ColText, 24, () => ShowView(0), Pill);
            Btn(track.rectTransform, "view_overhead", "上から見た配置", new Vector2(0.503f, 0.1f), new Vector2(0.994f, 0.9f), Clear, ColMuted, 24, () => ShowView(1), Pill);

            Image frame = Img(_root, "View", new Vector2(0.03f, 0.575f), new Vector2(0.97f, 0.887f), Hex("#05070A"), RCard);
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            _viewSlot = Rect(frame.rectTransform, "Slot", Vector2.zero, Vector2.one);
            _viewEmptyText = Txt(frame.rectTransform, "Empty", "映像がありません", 26, ColMuted, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            Image tag = Img(frame.rectTransform, "Tag", new Vector2(0.025f, 0.05f), new Vector2(0.4f, 0.15f), new Color(0.04f, 0.055f, 0.08f, 0.75f), Pill);
            _viewTag = Txt(tag.rectTransform, "Text", "", 21, ColText, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            AddStroke(frame.rectTransform, ColLine, RCard);
        }

        private void BuildBoardCard()
        {
            Image card = Img(_root, "BoardCard", new Vector2(0.03f, 0.02f), new Vector2(0.385f, 0.56f), ColS1, RCard);
            AddStroke(card.rectTransform, ColLine, RCard);
            _boardCard = card.gameObject;
            Txt(card.rectTransform, "Title", "カメラが認識した盤", 21, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.08f, 0.9f), new Vector2(0.96f, 0.975f), true);
            _boardSlot = Rect(card.rectTransform, "BoardSlot", new Vector2(0.08f, 0.42f), new Vector2(0.92f, 0.89f));
            _boardStatusSlot = Rect(card.rectTransform, "StatusSlot", new Vector2(0.05f, 0.33f), new Vector2(0.95f, 0.41f));
            Img(card.rectTransform, "Divider", new Vector2(0.08f, 0.305f), new Vector2(0.92f, 0.307f), ColLine);
            _boardInfo = Txt(card.rectTransform, "Info", "", 22, ColMuted, TextAnchor.UpperLeft, new Vector2(0.08f, 0.05f), new Vector2(0.95f, 0.28f));
            _boardInfo.lineSpacing = 1.25f;
        }

        private void BuildStepCard()
        {
            Image card = Img(_root, "StepCard", new Vector2(0.405f, 0.02f), new Vector2(0.97f, 0.56f), ColS1, RCard);
            AddStroke(card.rectTransform, ColLine, RCard);
            _stepCard = card.rectTransform;

            // 手順の進み具合: 「STEP 3 / 6 ・ 実験ID」「次: 試行」と 6 分割の進行バー
            RectTransform prog = Rect(_stepCard, "Stages", new Vector2(0.045f, 0.884f), new Vector2(0.955f, 0.974f));
            _stepName = Txt(prog, "Name", "", 20, ColMuted, TextAnchor.MiddleLeft, new Vector2(0f, 0.45f), new Vector2(0.75f, 1f), true);
            _stepNext = Txt(prog, "Next", "", 20, ColMuted, TextAnchor.MiddleRight, new Vector2(0.5f, 0.45f), new Vector2(1f, 1f), true);
            for (int i = 0; i < StageNames.Length; i++)
            {
                float w = 1f / StageNames.Length;
                _bars.Add(Img(prog, "Bar" + i, new Vector2(i * w + 0.006f, 0.08f), new Vector2((i + 1) * w - 0.006f, 0.26f), ColS3, Pill));
            }

            _headline = Txt(_stepCard, "Headline", "", 34, ColText, TextAnchor.MiddleLeft, new Vector2(0.045f, 0.775f), new Vector2(0.955f, 0.875f), true);

            RectTransform body = Rect(_stepCard, "Body", new Vector2(0.045f, 0.18f), new Vector2(0.955f, 0.765f));
            BuildIdleBody(body);
            BuildCalibrationBody(body);
            BuildCheckBody(body);
            BuildSetupBody(body);
            BuildRunningBody(body);
            BuildCompletedBody(body);
            BuildEvaluationBody(body);
            BuildSurveyBody(body);

            RectTransform bottom = Rect(_stepCard, "Bottom", new Vector2(0.045f, 0.035f), new Vector2(0.955f, 0.155f));
            _primaryHolder = Rect(bottom, "Primary", Vector2.zero, Vector2.one).gameObject;
            Button primary = Btn((RectTransform)_primaryHolder.transform, "primary", "位置合わせを始める", Vector2.zero, Vector2.one, ColAccent, Color.white, 31, OnPrimary, RBtn);
            Txt((RectTransform)primary.transform, "Arrow", "→", 34, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleRight, new Vector2(0.8f, 0f), new Vector2(0.95f, 1f), true);

            RectTransform overrides = Rect(bottom, "Overrides", Vector2.zero, Vector2.one);
            _overrideHolder = overrides.gameObject;
            Button back = Btn(overrides, "back", "1手戻る", new Vector2(0f, 0f), new Vector2(0.24f, 1f), ColS2, ColText, 22, OnStepBack, RBtn);
            AddStroke((RectTransform)back.transform, ColLine, RBtn);
            Button force = Btn(overrides, "force", "認識を待たず次へ", new Vector2(0.26f, 0f), new Vector2(0.63f, 1f), new Color(ColWarn.r, ColWarn.g, ColWarn.b, 0.12f), Hex("#FFD08F"), 22, OnForceAdvance, RBtn);
            AddStroke((RectTransform)force.transform, new Color(ColWarn.r, ColWarn.g, ColWarn.b, 0.5f), RBtn);
            Button reset = Btn(overrides, "reset", "最初からやり直す", new Vector2(0.65f, 0f), new Vector2(1f, 1f), new Color(ColNg.r, ColNg.g, ColNg.b, 0.12f), Hex("#FFB0B0"), 22, OnReset, RBtn);
            AddStroke((RectTransform)reset.transform, new Color(ColNg.r, ColNg.g, ColNg.b, 0.5f), RBtn);
        }

        private RectTransform Body(RectTransform parent, OperatorStep step)
        {
            RectTransform b = Rect(parent, "Body_" + step, Vector2.zero, Vector2.one);
            _bodies[step] = b.gameObject;
            return b;
        }

        private void BuildIdleBody(RectTransform parent)
        {
            RectTransform b = Body(parent, OperatorStep.Idle);
            Image hero = Img(b, "Hero", new Vector2(0f, 0.5f), new Vector2(1f, 1f), ColHero, 24f);
            AddStroke(hero.rectTransform, ColLine, 24f);
            Txt(hero.rectTransform, "Label", "使うパターン", 20, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.07f, 0.74f), new Vector2(0.93f, 0.9f), true);
            _idlePattern = Txt(hero.rectTransform, "Pattern", "", 54, ColYellow, TextAnchor.MiddleLeft, new Vector2(0.07f, 0.36f), new Vector2(0.93f, 0.74f), true);
            _idleChip1 = Chip(hero.rectTransform, "Chip1", new Vector2(0.07f, 0.1f), new Vector2(0.3f, 0.3f));
            _idleChip2 = Chip(hero.rectTransform, "Chip2", new Vector2(0.33f, 0.1f), new Vector2(0.62f, 0.3f));
            _idleNote = Txt(b, "Note", "", 21, ColMuted, TextAnchor.UpperLeft, new Vector2(0f, 0.3f), new Vector2(1f, 0.45f));
        }

        private Text Chip(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            Image bg = Img(parent, name, min, max, ColS3, Pill);
            return Txt(bg.rectTransform, "Text", "", 21, ColText, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
        }

        private void BuildCalibrationBody(RectTransform parent)
        {
            RectTransform b = Body(parent, OperatorStep.Calibration);
            Txt(b, "Hint", "四隅の印が盤の角に重なるまで動かします (1 回 0.5cm)", 21, ColMuted, TextAnchor.UpperLeft, new Vector2(0f, 0.885f), new Vector2(1f, 1f));

            // 丸い十字キー (奥へ・左へ・右へ・手前へ)。真ん中は盤の絵
            RectTransform pad = Rect(b, "Pad", new Vector2(0f, 0.255f), new Vector2(0f, 0.875f));
            pad.pivot = new Vector2(0f, 0.5f);
            var fit = pad.gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            fit.aspectRatio = 1f;
            Image disk = Img(pad, "Disk", Vector2.zero, Vector2.one, ColS2);
            disk.sprite = UiSprites.Circle;
            ArrowButton(pad, "nudge_z+", "奥へ", 0f, () => OnNudge(2, +1), new Vector2(0.3f, 0.68f), new Vector2(0.7f, 0.98f));
            ArrowButton(pad, "nudge_x-", "左へ", 90f, () => OnNudge(0, -1), new Vector2(0.01f, 0.33f), new Vector2(0.34f, 0.67f));
            ArrowButton(pad, "nudge_x+", "右へ", -90f, () => OnNudge(0, +1), new Vector2(0.66f, 0.33f), new Vector2(0.99f, 0.67f));
            ArrowButton(pad, "nudge_z-", "手前へ", 180f, () => OnNudge(2, -1), new Vector2(0.3f, 0.02f), new Vector2(0.7f, 0.32f));
            Image board = Img(pad, "BoardIcon", new Vector2(0.35f, 0.35f), new Vector2(0.65f, 0.65f), Hex("#2B7A4F"), 14f);
            for (int i = 1; i < 4; i++)
            {
                Img(board.rectTransform, "V" + i, new Vector2(i / 4f - 0.008f, 0.04f), new Vector2(i / 4f + 0.008f, 0.96f), new Color(0f, 0f, 0f, 0.28f));
                Img(board.rectTransform, "H" + i, new Vector2(0.04f, i / 4f - 0.008f), new Vector2(0.96f, i / 4f + 0.008f), new Color(0f, 0f, 0f, 0.28f));
            }
            Txt(board.rectTransform, "Label", "盤", 22, Hex("#D9FFE6"), TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);

            // 高さ (縦長のピル)
            Image height = Img(b, "Height", new Vector2(0.78f, 0.255f), new Vector2(1f, 0.875f), ColS2, Pill);
            AddStroke(height.rectTransform, ColLine, Pill);
            ArrowButton(height.rectTransform, "nudge_y+", "上へ", 0f, () => OnNudge(1, +1), new Vector2(0f, 0.56f), new Vector2(1f, 0.98f));
            Txt(height.rectTransform, "Label", "高さ", 19, ColMuted, TextAnchor.MiddleCenter, new Vector2(0f, 0.45f), new Vector2(1f, 0.55f), true);
            ArrowButton(height.rectTransform, "nudge_y-", "下へ", 180f, () => OnNudge(1, -1), new Vector2(0f, 0.02f), new Vector2(1f, 0.44f));

            Image readout = Img(b, "Readout", new Vector2(0f, 0.145f), new Vector2(1f, 0.235f), ColS2, Pill);
            _calibReadout = Txt(readout.rectTransform, "Text", "", 20, ColText, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            Pad(_calibReadout.rectTransform, 14, 14);

            Button dots = Btn(b, "dots", "● 四隅の印を出す", new Vector2(0f, 0f), new Vector2(0.49f, 0.12f), ColS2, ColPurple, 22, OnToggleDots, RBtn);
            AddStroke((RectTransform)dots.transform, ColLine, RBtn);
            Button zero = Btn(b, "offset_reset", "ずれを 0 に戻す", new Vector2(0.51f, 0f), new Vector2(1f, 0.12f), ColS2, ColText, 22, OnResetOffset, RBtn);
            AddStroke((RectTransform)zero.transform, ColLine, RBtn);
        }

        private void ArrowButton(RectTransform parent, string id, string label, float angle, Action onClick, Vector2 min, Vector2 max)
        {
            Button b = Btn(parent, id, label, min, max, new Color(1f, 1f, 1f, 0.001f), ColText, 20, onClick, RBtn);
            var colors = b.colors;
            colors.pressedColor = new Color(80f, 80f, 80f, 120f); // 透明な背景を押したときだけ見えるように
            b.colors = colors;
            Text t = _labels[id];
            Stretch(t.rectTransform, new Vector2(0f, 0.04f), new Vector2(1f, 0.44f));
            Image arrow = FixedImg((RectTransform)b.transform, "Arrow", new Vector2(0.5f, 0.68f), Vector2.zero, new Vector2(36f, 36f), ColAccent2, UiSprites.Triangle);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void BuildCheckBody(RectTransform parent)
        {
            RectTransform b = Body(parent, OperatorStep.InitialBoardCheck);
            _checkCount = Txt(b, "Count", "", 48, ColWarn, TextAnchor.MiddleLeft, new Vector2(0f, 0.82f), new Vector2(1f, 1f), true);
            for (int i = 0; i < CheckRows; i++)
            {
                float top = 0.79f - i * 0.125f;
                Image row = Img(b, "Row" + i, new Vector2(0f, top - 0.105f), new Vector2(1f, top), ColS2, 16f);
                Image chip = Img(row.rectTransform, "Coord", new Vector2(0.03f, 0.18f), new Vector2(0.2f, 0.82f), ColS3, 10f);
                Text coord = Txt(chip.rectTransform, "Text", "", 22, ColAccent2, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
                Text text = Txt(row.rectTransform, "Text", "", 24, ColText, TextAnchor.MiddleLeft, new Vector2(0.25f, 0f), new Vector2(0.98f, 1f));
                _checkRowsUi.Add((row.gameObject, coord, text));
            }
            _checkNote = Txt(b, "Note", "", 20, ColMuted, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(1f, 0.1f));
        }

        private void BuildRunningBody(RectTransform parent)
        {
            RectTransform b = Body(parent, OperatorStep.Running);
            _runProgress = Txt(b, "Progress", "", 26, ColMuted, TextAnchor.MiddleLeft, new Vector2(0f, 0.86f), new Vector2(1f, 1f));
            Image barBg = Img(b, "Bar", new Vector2(0f, 0.825f), new Vector2(1f, 0.842f), ColS3, Pill);
            _runBarFill = Img(barBg.rectTransform, "Fill", Vector2.zero, new Vector2(0.2f, 1f), ColOk, Pill);

            Image next = Img(b, "Next", new Vector2(0f, 0.53f), new Vector2(1f, 0.79f), ColHero, 24f);
            AddStroke(next.rectTransform, ColLine, 24f);
            FixedImg(next.rectTransform, "StoneRim", new Vector2(0f, 0.5f), new Vector2(72f, 0f), new Vector2(92f, 92f), new Color(1f, 1f, 1f, 0.2f), UiSprites.Circle);
            _nextStone = FixedImg(next.rectTransform, "Stone", new Vector2(0f, 0.5f), new Vector2(72f, 0f), new Vector2(80f, 80f), Color.black, UiSprites.Circle);
            Txt(next.rectTransform, "Label", "次に置く場所", 20, ColMuted, TextAnchor.LowerLeft, new Vector2(0.3f, 0.55f), new Vector2(1f, 0.85f), true);
            _runNext = Txt(next.rectTransform, "Text", "", 44, ColText, TextAnchor.UpperLeft, new Vector2(0.3f, 0.1f), new Vector2(1f, 0.58f), true);

            Image banner = Img(b, "Error", new Vector2(0f, 0.27f), new Vector2(1f, 0.48f), new Color(ColNg.r, ColNg.g, ColNg.b, 0.14f), 18f);
            AddStroke(banner.rectTransform, new Color(ColNg.r, ColNg.g, ColNg.b, 0.55f), 18f);
            _errorBanner = banner.gameObject;
            Image ico = FixedImg(banner.rectTransform, "Icon", new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(44f, 44f), ColNg, UiSprites.Circle);
            Txt(ico.rectTransform, "Mark", "!", 28, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
            _runError = Txt(banner.rectTransform, "Text", "", 22, Hex("#FFD0D0"), TextAnchor.MiddleLeft, new Vector2(0.17f, 0f), new Vector2(0.97f, 1f), true);

            _runStat1 = StatTile(b, "StatStep", "この手の間違い", new Vector2(0f, 0f), new Vector2(0.49f, 0.22f));
            _runStat2 = StatTile(b, "StatTotal", "合計", new Vector2(0.51f, 0f), new Vector2(1f, 0.22f));
        }

        private Text StatTile(RectTransform parent, string name, string label, Vector2 min, Vector2 max)
        {
            Image tile = Img(parent, name, min, max, ColS2, 18f);
            Txt(tile.rectTransform, "Label", label, 19, ColMuted, TextAnchor.UpperLeft, new Vector2(0.08f, 0.55f), new Vector2(0.98f, 0.9f), true);
            return Txt(tile.rectTransform, "Value", "", 20, ColText, TextAnchor.LowerLeft, new Vector2(0.08f, 0.1f), new Vector2(0.98f, 0.62f));
        }

        private void BuildCompletedBody(RectTransform parent)
        {
            RectTransform b = Body(parent, OperatorStep.Completed);
            _doneTime = StatTile(b, "Time", "時間", new Vector2(0f, 0.7f), new Vector2(0.32f, 1f));
            _doneErrors = StatTile(b, "Errors", "間違い", new Vector2(0.34f, 0.7f), new Vector2(0.66f, 1f));
            _doneForced = StatTile(b, "Forced", "手動で進めた", new Vector2(0.68f, 0.7f), new Vector2(1f, 1f));
            _doneNote = Txt(b, "Note", "", 21, ColMuted, TextAnchor.UpperLeft, new Vector2(0f, 0.18f), new Vector2(1f, 0.44f));

            // 録画を保存しました (録画したときだけ)
            _recRowBg = Img(b, "Recording", new Vector2(0f, 0.5f), new Vector2(1f, 0.635f), new Color(ColNg.r, ColNg.g, ColNg.b, 0.08f), 16f);
            _recRowStroke = AddStroke(_recRowBg.rectTransform, new Color(ColNg.r, ColNg.g, ColNg.b, 0.35f), 16f);
            FixedImg(_recRowBg.rectTransform, "Dot", new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(16f, 16f), ColNg, UiSprites.Circle);
            _recRowText = Txt(_recRowBg.rectTransform, "Text", "", 20, ColText, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            Pad(_recRowText.rectTransform, 46, 14);
            _recRow = _recRowBg.gameObject;
            _recRow.SetActive(false);
        }

        // ---------------- 実験ID ----------------

        private void BuildSetupBody(RectTransform parent)
        {
            RectTransform b = Body(parent, OperatorStep.Setup);

            // 被験者の一覧 (スクロール)
            RectTransform listArea = Rect(b, "Subjects", new Vector2(0f, 0.405f), new Vector2(1f, 1f));
            RectTransform viewport = Img(listArea, "Viewport", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.001f)).rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            _subjectList = Rect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            _subjectList.pivot = new Vector2(0.5f, 1f);
            var vlg = _subjectList.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 10;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            _subjectList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = listArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = _subjectList;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // 試行番号
            Image trial = Img(b, "TrialNo", new Vector2(0f, 0.27f), new Vector2(1f, 0.385f), ColS2, 18f);
            Txt(trial.rectTransform, "Label", "試行番号", 20, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.025f, 0f), new Vector2(0.15f, 1f), true);
            _trialNoText = Txt(trial.rectTransform, "Value", "", 42, ColText, TextAnchor.MiddleLeft, new Vector2(0.15f, 0f), new Vector2(0.24f, 1f), true);
            _trialNoNote = Txt(trial.rectTransform, "Note", "", 20, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.24f, 0f), new Vector2(0.78f, 1f));
            Btn(trial.rectTransform, "trial_minus", "−", new Vector2(0.8f, 0.14f), new Vector2(0.885f, 0.86f), ColS3, ColText, 30, () => OnTrialNo(-1), Pill);
            Btn(trial.rectTransform, "trial_plus", "＋", new Vector2(0.9f, 0.14f), new Vector2(0.985f, 0.86f), ColS3, ColText, 28, () => OnTrialNo(+1), Pill);

            // 条件とパターン
            Image cond = Img(b, "Condition", new Vector2(0f, 0.14f), new Vector2(1f, 0.255f), ColS2, 18f);
            Txt(cond.rectTransform, "Label", "条件", 20, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.025f, 0f), new Vector2(0.15f, 1f), true);
            Image seg = Img(cond.rectTransform, "Seg", new Vector2(0.15f, 0.16f), new Vector2(0.52f, 0.84f), ColS1, Pill);
            int n = conditions != null ? Mathf.Min(conditions.Length, 4) : 0;
            for (int i = 0; i < n; i++)
            {
                int captured = i;
                float w = 1f / n;
                Btn(seg.rectTransform, "cond_" + i, conditions[i], new Vector2(i * w + 0.01f, 0.08f), new Vector2((i + 1) * w - 0.01f, 0.92f), Clear, ColMuted, 21, () => OnCondition(captured), Pill);
            }
            _setupPattern = Txt(cond.rectTransform, "Pattern", "", 21, ColMuted, TextAnchor.MiddleRight, new Vector2(0.53f, 0f), new Vector2(0.79f, 1f));
            Btn(cond.rectTransform, "setup_pattern_prev", "‹", new Vector2(0.8f, 0.14f), new Vector2(0.885f, 0.86f), ColS3, ColText, 32, OnPrevPattern, Pill);
            Btn(cond.rectTransform, "setup_pattern_next", "›", new Vector2(0.9f, 0.14f), new Vector2(0.985f, 0.86f), ColS3, ColText, 32, OnNextPattern, Pill);

            // 実験ID
            Image idRow = Img(b, "ExperimentId", new Vector2(0f, 0.01f), new Vector2(1f, 0.125f), ColS2, 18f);
            Txt(idRow.rectTransform, "Label", "実験ID", 20, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.025f, 0f), new Vector2(0.15f, 1f), true);
            Image chip = Img(idRow.rectTransform, "Chip", new Vector2(0.15f, 0.17f), new Vector2(0.45f, 0.83f), ColS3, 10f);
            _expIdText = Txt(chip.rectTransform, "Text", "", 27, ColAccent2, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
            Txt(idRow.rectTransform, "Note", "試行ログ・評価・アンケートの全行に記録", 20, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.47f, 0f), new Vector2(0.99f, 1f));
        }

        /// <summary>被験者の一覧を作り直す (追加・名前の変更・削除・記録の数が変わったときだけ)。</summary>
        private void RebuildSubjectsIfNeeded()
        {
            ExperimentRecordStore store = _operator.Store;
            List<SubjectInfo> list = store != null ? store.Subjects.Active : new List<SubjectInfo>();
            var sig = new StringBuilder("v|");
            foreach (SubjectInfo s in list) sig.Append(s.Id).Append(':').Append(s.Name).Append('|');
            if (sig.ToString() == _subjectsSignature && _subjectList.childCount > 0) return;
            _subjectsSignature = sig.ToString();

            // 記録の数はここ (一覧を作り直すとき) だけ CSV から数える
            _trialCounts.Clear();
            foreach (SubjectInfo s in list) _trialCounts[s.Id] = _operator.CountTrials(s.Id);
            Dictionary<string, int> counts = _trialCounts;

            var children = new List<GameObject>();
            foreach (Transform child in _subjectList) children.Add(child.gameObject);
            foreach (GameObject child in children) DestroyObj(child);
            var stale = new List<string>();
            foreach (string key in Buttons.Keys) if (key.StartsWith("subject_")) stale.Add(key);
            foreach (string key in stale) { Buttons.Remove(key); _labels.Remove(key); }

            foreach (SubjectInfo s in list)
            {
                string id = s.Id;
                Button row = Btn(_subjectList, "subject_" + id, "", Vector2.zero, Vector2.one, ColS2, ColText, 20, () => OnSelectSubject(id), 18f);
                row.GetComponent<LayoutElement>().preferredHeight = 80;
                var rrt = (RectTransform)row.transform;
                Image ring = AddStroke(rrt, new Color(ColAccent.r, ColAccent.g, ColAccent.b, 0.7f), 18f);
                ring.gameObject.name = "Ring";
                Image ava = FixedImg(rrt, "Avatar", new Vector2(0f, 0.5f), new Vector2(50f, 0f), new Vector2(56f, 56f), Hex("#34405A"), UiSprites.Circle);
                Txt(ava.rectTransform, "Initial", s.Initial, 24, Color.white, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
                Text name = Txt(rrt, "Name", s.DisplayName, 26, ColText, TextAnchor.LowerLeft, new Vector2(0f, 0.5f), new Vector2(0.8f, 0.95f), true);
                Pad(name.rectTransform, 96, 0);
                Text sub = Txt(rrt, "Sub", $"{s.Id}  ・  これまで {counts[id]} 回", 19, ColMuted, TextAnchor.UpperLeft, new Vector2(0f, 0.05f), new Vector2(0.8f, 0.5f));
                Pad(sub.rectTransform, 96, 0);
                Image chk = FixedImg(rrt, "Check", new Vector2(1f, 0.5f), new Vector2(-120f, 0f), new Vector2(40f, 40f), ColAccent, UiSprites.Circle);
                FixedImg(chk.rectTransform, "Mark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f), Color.white, UiSprites.Check);
                Btn(rrt, "subject_more_" + id, "…", new Vector2(0.92f, 0.12f), new Vector2(0.99f, 0.88f), Clear, ColMuted, 30, () => OpenSubjectMenu(id), Pill);
            }

            Button add = Btn(_subjectList, "subject_add", "＋ 新しい被験者を追加", Vector2.zero, Vector2.one, Clear, ColAccent2, 23, OnAddSubject, 18f);
            add.GetComponent<LayoutElement>().preferredHeight = 76;
            AddStroke((RectTransform)add.transform, ColOff, 18f);
            if (store == null) SetInteractable("subject_add", false);
        }

        private void BuildSubjectMenu()
        {
            Button blocker = Btn(_root, "menu_close", "", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.25f), ColText, 10, CloseMenu);
            _menuRoot = blocker.gameObject;
            Image panel = Img(blocker.GetComponent<RectTransform>(), "Panel", new Vector2(0.52f, 0.2f), new Vector2(0.95f, 0.43f), Hex("#222C3C"), 20f);
            AddStroke(panel.rectTransform, ColLine, 20f);
            _menuTitle = Txt(panel.rectTransform, "Title", "", 21, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.82f), new Vector2(0.96f, 0.97f), true);
            string[] ids = { "menu_rename", "menu_edit", "menu_history", "menu_delete" };
            string[] labels = { "名前を変更", "情報を編集 (年齢・利き目など)", "この人の記録を見る", "削除 (記録は残す)" };
            Action[] actions = { OnMenuRename, OnMenuEdit, OnMenuHistory, OnMenuDelete };
            for (int i = 0; i < ids.Length; i++)
            {
                float top = 0.8f - i * 0.195f;
                Button b = Btn(panel.rectTransform, ids[i], labels[i], new Vector2(0.03f, top - 0.18f), new Vector2(0.97f, top), Clear, i == 3 ? Hex("#FF9B9B") : ColText, 22, actions[i], 12f);
                _labels[ids[i]].alignment = TextAnchor.MiddleLeft;
                Pad(_labels[ids[i]].rectTransform, 18, 8);
            }
            _menuRoot.SetActive(false);
        }

        private void RefreshMenu()
        {
            if (!IsMenuOpen) return;
            SubjectInfo s = _operator.Store?.Subjects.Find(_menuSubjectId);
            _menuTitle.text = s != null ? $"{s.DisplayName}  ({s.Id})" : "";
            bool armed = _deleteArmedUntil > 0 && Time.unscaledTime <= _deleteArmedUntil;
            SetLabel("menu_delete", armed ? "もう一度押すと削除 (記録は残す)" : "削除 (記録は残す)");
            SetButtonColor("menu_delete", armed ? new Color(ColNg.r, ColNg.g, ColNg.b, 0.3f) : Clear);
        }

        // ---------------- ダイアログ (名前の入力・情報の編集・記録の一覧) ----------------

        private void BuildDialog()
        {
            Image dim = Img(_root, "Dialog", Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.62f));
            _dialogRoot = dim.gameObject;
            Image card = Img(dim.rectTransform, "Card", new Vector2(0.06f, 0.45f), new Vector2(0.94f, 0.92f), ColS1, RCard);
            AddStroke(card.rectTransform, ColLine, RCard);
            _dialogCard = card.rectTransform;
            _dialogRoot.SetActive(false);
        }

        private RectTransform BeginDialog(string title, float minY)
        {
            CloseMenu();
            var old = new List<GameObject>();
            foreach (Transform child in _dialogCard) if (child.name != "Stroke") old.Add(child.gameObject);
            foreach (GameObject child in old) DestroyObj(child);
            foreach (string key in new List<string>(Inputs.Keys)) if (key.StartsWith("dialog_") || key.StartsWith("info_")) Inputs.Remove(key);
            _dialogCard.anchorMin = new Vector2(0.06f, minY);
            Txt(_dialogCard, "Title", title, 32, ColText, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.88f), new Vector2(0.94f, 0.97f), true);
            _dialogRoot.SetActive(true);
            _dialogRoot.transform.SetAsLastSibling();
            return _dialogCard;
        }

        public void CloseDialog()
        {
            if (_dialogRoot != null) _dialogRoot.SetActive(false);
            RefreshAll();
        }

        private void DialogButtons(RectTransform card, string okLabel, Func<bool> onOk)
        {
            Button cancel = Btn(card, "dialog_cancel", "キャンセル", new Vector2(0.06f, 0.04f), new Vector2(0.38f, 0.13f), ColS2, ColText, 24, CloseDialog, RBtn);
            AddStroke((RectTransform)cancel.transform, ColLine, RBtn);
            if (onOk != null)
            {
                Btn(card, "dialog_ok", okLabel, new Vector2(0.42f, 0.04f), new Vector2(0.94f, 0.13f), ColAccent, Color.white, 26, () =>
                {
                    if (onOk()) CloseDialog();
                }, RBtn);
            }
            else SetLabel("dialog_cancel", "閉じる");
        }

        private void OpenTextDialog(string title, string fieldLabel, string initial, string okLabel, Func<string, bool> onOk)
        {
            RectTransform card = BeginDialog(title, 0.68f);
            Txt(card, "Label", fieldLabel, 22, ColMuted, TextAnchor.LowerLeft, new Vector2(0.06f, 0.62f), new Vector2(0.94f, 0.8f), true);
            InputField field = CreateInputField(card, "dialog_name", "名前を入力", new Vector2(0.06f, 0.3f), new Vector2(0.94f, 0.6f), false);
            field.text = initial ?? "";
            DialogButtons(card, okLabel, () => onOk(field.text));
            field.Select();
            field.ActivateInputField();
        }

        private void OpenInfoDialog(SubjectInfo s)
        {
            RectTransform card = BeginDialog($"{s.Id} の情報", 0.36f);
            (string id, string label, string value)[] rows =
            {
                ("info_name", "名前", s.Name), ("info_age", "年齢", s.Age), ("info_gender", "性別", s.Gender),
                ("info_eye", "利き目", s.DominantEye), ("info_vision", "視力矯正", s.VisionCorrection),
                ("info_xr", "XR の経験", s.XrExperience), ("info_note", "メモ", s.Note)
            };
            var fields = new Dictionary<string, InputField>();
            for (int i = 0; i < rows.Length; i++)
            {
                float top = 0.86f - i * 0.1f;
                Txt(card, "Label_" + rows[i].id, rows[i].label, 22, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.06f, top - 0.085f), new Vector2(0.3f, top), true);
                InputField f = CreateInputField(card, rows[i].id, "(任意)", new Vector2(0.3f, top - 0.085f), new Vector2(0.94f, top), false);
                f.text = rows[i].value ?? "";
                fields[rows[i].id] = f;
            }
            Txt(card, "Hint", "どれも任意です。subjects.csv に保存します。", 19, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.15f), new Vector2(0.94f, 0.18f));
            DialogButtons(card, "保存する", () =>
            {
                var info = s.Clone();
                info.Name = fields["info_name"].text;
                info.Age = fields["info_age"].text;
                info.Gender = fields["info_gender"].text;
                info.DominantEye = fields["info_eye"].text;
                info.VisionCorrection = fields["info_vision"].text;
                info.XrExperience = fields["info_xr"].text;
                info.Note = fields["info_note"].text;
                bool ok = _operator.UpdateSubject(info);
                _subjectsSignature = "";
                return ok;
            });
        }

        private void OpenHistoryDialog(SubjectInfo s)
        {
            RectTransform card = BeginDialog($"{s.DisplayName} ({s.Id}) の記録", 0.4f);
            List<TrialHistoryEntry> list = _operator.ListTrials(s.Id);
            var sb = new StringBuilder();
            if (list.Count == 0) sb.Append("<color=#8A95A8>まだ記録がありません。</color>");
            foreach (TrialHistoryEntry e in list)
            {
                string result = e.Result == "success" ? "<color=#2FBF71>成功</color>" : e.Result == "fail" ? "<color=#EF5A5A>失敗</color>" : "<color=#8A95A8>未評価</color>";
                string when = e.CompletedAt.Length >= 16 ? e.CompletedAt.Substring(5, 11).Replace('T', ' ') : e.CompletedAt;
                sb.Append($"<b>{e.TrialNo:00}</b>  {e.Condition}  {e.PatternId}  {e.TctSec} 秒  間違い {e.Errors}  {result}  <color=#8A95A8>{when}</color>\n");
            }
            RectTransform area = Img(card, "List", new Vector2(0.06f, 0.17f), new Vector2(0.94f, 0.86f), ColS2, 18f).rectTransform;
            area.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Rect(area, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);
            Text text = Txt(content, "Text", sb.ToString(), 22, ColText, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            text.lineSpacing = 1.3f;
            Pad(text.rectTransform, 18, 18);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(18, 18, 14, 14);
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            var scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = area;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            DialogButtons(card, null, null);
        }

        // ---------------- 評価・アンケート ----------------

        private void BuildEvaluationBody(RectTransform parent)
        {
            RectTransform b = Body(parent, OperatorStep.Evaluation);
            Image summary = Img(b, "Summary", new Vector2(0f, 0.89f), new Vector2(1f, 1f), ColS2, Pill);
            _evalSummary = Txt(summary.rectTransform, "Text", "", 21, ColText, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            Pad(_evalSummary.rectTransform, 24, 24);

            ResultTile(b, "result_success", "成功", UiSprites.Check, true, new Vector2(0f, 0.6f), new Vector2(0.49f, 0.85f));
            ResultTile(b, "result_fail", "失敗", UiSprites.Cross, false, new Vector2(0.51f, 0.6f), new Vector2(1f, 0.85f));

            RectTransform reasons = Rect(b, "FailReasons", new Vector2(0f, 0.445f), new Vector2(1f, 0.555f));
            _reasonGroup = reasons.gameObject.AddComponent<CanvasGroup>();
            Txt(reasons, "Label", "失敗の理由", 20, ColMuted, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(0.15f, 1f), true);
            int n = failReasons != null ? Mathf.Min(failReasons.Length, 5) : 0;
            for (int i = 0; i < n; i++)
            {
                int captured = i;
                float w = 0.85f / n;
                Button chip = Btn(reasons, "reason_" + i, failReasons[i], new Vector2(0.15f + i * w + 0.005f, 0.05f), new Vector2(0.15f + (i + 1) * w - 0.005f, 0.95f), Clear, ColText, 20, () => OnFailReason(captured), Pill);
                AddStroke((RectTransform)chip.transform, ColOff, Pill);
            }

            Txt(b, "MemoLabel", "メモ (任意)", 20, ColMuted, TextAnchor.LowerLeft, new Vector2(0f, 0.34f), new Vector2(1f, 0.41f), true);
            _memoField = CreateInputField(b, "memo", "タップして入力", new Vector2(0f, 0.02f), new Vector2(1f, 0.33f), true);
            _memoField.onValueChanged.AddListener(v => _operator.SetMemo(v));
        }

        private void ResultTile(RectTransform parent, string id, string label, Sprite icon, bool success, Vector2 min, Vector2 max)
        {
            Button tile = Btn(parent, id, label, min, max, ColS2, ColMuted, 34, () => OnResult(success), 24f);
            Image ring = AddStroke((RectTransform)tile.transform, ColLine, 24f);
            ring.gameObject.name = "Ring";
            Image circle = FixedImg((RectTransform)tile.transform, "Icon", new Vector2(0.36f, 0.5f), Vector2.zero, new Vector2(58f, 58f), ColS3, UiSprites.Circle);
            FixedImg(circle.rectTransform, "Mark", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 40f), Color.white, icon);
            Text t = _labels[id];
            Stretch(t.rectTransform, new Vector2(0.45f, 0f), new Vector2(0.95f, 1f));
            t.alignment = TextAnchor.MiddleLeft;
        }

        private void BuildSurveyBody(RectTransform parent)
        {
            RectTransform b = Body(parent, OperatorStep.Survey);
            Txt(b, "Hint", "被験者に Beam Pro を渡して答えてもらいます。試行の結果は出しません。", 20, ColMuted, TextAnchor.UpperLeft, new Vector2(0f, 0.9f), new Vector2(1f, 1f));
            Image card = Img(b, "Question", new Vector2(0f, 0.53f), new Vector2(1f, 0.885f), ColHero, 24f);
            AddStroke(card.rectTransform, ColLine, 24f);
            _qNum = Txt(card.rectTransform, "Number", "", 19, ColAccent2, TextAnchor.UpperLeft, new Vector2(0.04f, 0.74f), new Vector2(0.96f, 0.93f), true);
            _qText = Txt(card.rectTransform, "Text", "", 28, ColText, TextAnchor.UpperLeft, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.74f));
            _qText.lineSpacing = 1.25f;

            for (int i = 0; i < MaxScale; i++)
            {
                int index = i;
                Button btn = Btn(b, "q_" + i, (i + 1).ToString(), new Vector2(0f, 0.3f), new Vector2(0.1f, 0.47f), ColS2, ColText, 28, () =>
                {
                    QuestionnaireQuestion q = _operator.CurrentQuestion;
                    if (q != null) OnAnswer(q.min + index);
                });
                Image img = (Image)btn.targetGraphic;
                img.sprite = UiSprites.Circle;
                img.type = Image.Type.Simple;
                img.preserveAspect = true;
            }
            _scaleMin = Txt(b, "Min", "", 19, ColMuted, TextAnchor.UpperLeft, new Vector2(0f, 0.21f), new Vector2(0.33f, 0.28f));
            _scaleMid = Txt(b, "Mid", "", 19, ColMuted, TextAnchor.UpperCenter, new Vector2(0.33f, 0.21f), new Vector2(0.67f, 0.28f));
            _scaleMax = Txt(b, "Max", "", 19, ColMuted, TextAnchor.UpperRight, new Vector2(0.67f, 0.21f), new Vector2(1f, 0.28f));
            _scaleNowBg = Img(b, "Now", new Vector2(0.28f, 0.05f), new Vector2(0.72f, 0.17f), new Color(ColAccent.r, ColAccent.g, ColAccent.b, 0.16f), Pill);
            _scaleNow = Txt(_scaleNowBg.rectTransform, "Text", "", 30, ColAccent2, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
        }

        // ---------------- 設定シート ----------------

        private void BuildSettingsSheet()
        {
            _settingsSheet = Img(_root, "Settings", Vector2.zero, new Vector2(1f, 0.94f), ColBg).rectTransform;
            _settingsSheet.gameObject.SetActive(false); // 作り直したときも閉じた状態から (開くのは SetOpen)

            RectTransform viewport = Rect(_settingsSheet, "Viewport", Vector2.zero, Vector2.one);
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Rect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(36, 36, 26, 60);
            vlg.spacing = 12;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = _settingsSheet.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            RectTransform head = Row(content, 72, null);
            Text title = RowText(head, "設定", 200);
            title.fontSize = 40;
            title.fontStyle = FontStyle.Bold;
            Text back = RowText(head, "上の「閉じる」で戻ります", -1);
            back.alignment = TextAnchor.MiddleRight;
            back.color = ColMuted;
            back.fontSize = 21;

            // 実験の準備
            Section(content, "実験の準備");
            RectTransform prep = Group(content);
            RectTransform pattern = Row(prep, 80, prep);
            RowText(pattern, "パターン", 150);
            _patternText = RowText(pattern, "", -1);
            _patternText.alignment = TextAnchor.MiddleRight;
            _patternText.color = ColYellow;
            _patternText.fontStyle = FontStyle.Bold;
            RowButton(pattern, "pattern_prev", "‹", ColS3, ColText, OnPrevPattern, 64, Pill, 32);
            RowButton(pattern, "pattern_next", "›", ColS3, ColText, OnNextPattern, 64, Pill, 32);
            GroupHint(prep, "被験者・条件・試行番号は「実験ID」の手順で決めます。パターンは位置合わせ中・試行中は変えられません。");

            // 位置合わせ
            Section(content, "位置合わせ");
            if (_settings != null)
            {
                RectTransform basisGroup = Group(content);
                RectTransform basisRow = Row(basisGroup, 84, basisGroup);
                Image track = Img(basisRow, "Track", Vector2.zero, Vector2.one, ColS2, Pill);
                var trackLe = track.gameObject.AddComponent<LayoutElement>();
                trackLe.flexibleWidth = 1;
                for (int i = 0; i < BasisNames.Length; i++)
                {
                    int captured = i;
                    float w = 1f / BasisNames.Length;
                    Btn(track.rectTransform, "basis_" + i, BasisNames[i], new Vector2(i * w + 0.006f, 0.1f), new Vector2((i + 1) * w - 0.006f, 0.9f), Clear, ColMuted, 22, () => OnOffsetBasis(captured), Pill);
                }
                GroupHint(basisGroup, "矢印で動かす向きの基準。ふつうは「自分から見た向き」のまま。");
            }
            RectTransform sliders = Group(content);
            string[] axisNames = { "左右", "高さ", "奥・手前" };
            int[] order = { 0, 2, 1 };
            foreach (int axis in order)
            {
                RectTransform row = Row(sliders, 76, sliders);
                RowText(row, axisNames[axis], 140);
                _sliders[axis] = CreateSlider(row, axis);
                _sliderValues[axis] = RowPill(row, 140);
            }
            RectTransform actions = Group(content);
            if (_settings != null) ActionRow(actions, "typical_offset", "手前に 3cm ずらす (よくあるずれ)", ColAccent2, OnTypicalOffset);
            ActionRow(actions, "offset_reset_settings", "ずれを 0 に戻す", Hex("#FF9B9B"), OnResetOffset);

            if (_settings != null)
            {
                // グラスの動きの向き
                Section(content, "グラスの動きの向き");
                RectTransform axes = Group(content);
                foreach (var row in AxisRows)
                {
                    TrackingAxis axis = row.axis;
                    RectTransform r = Row(axes, 76, axes);
                    RowText(r, row.label, -1);
                    Text inv = RowText(r, "逆にする", 120);
                    inv.alignment = TextAnchor.MiddleRight;
                    inv.color = ColMuted;
                    inv.fontSize = 20;
                    SwitchButton(r, $"axis_{(int)axis}", () => OnToggleAxis(axis));
                }
                GroupHint(axes, "表示が逆向きに動くときだけ切り替えます。ふつうは変えません。");

                // 認識と記録
                Section(content, "認識と記録");
                RectTransform recal = Group(content);
                ActionRow(recal, "recalibrate", "石の白黒の見分けをやり直す", ColAccent2, OnRecalibrate);
                GroupHint(recal, "照明が変わったときや、石の色をまちがえて認識するときに。");
                RectTransform perf = Group(content);
                RectTransform perfRow = Row(perf, 76, perf);
                RowText(perfRow, "動作の記録 (フレームレートなど)", -1);
                SwitchButton(perfRow, "perf_log", OnTogglePerformanceLog);
                GroupHint(perf, "オフにしたときに CSV に保存します。実験ログとは別のファイルです。");
            }

            if (_operator.Recording != null) BuildRecordingSettings(content);

            Section(content, "記録の保存先");
            RectTransform store = Group(content);
            _storePathText = Txt(store, "Path", "", 19, ColMuted, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            var pathLe = _storePathText.gameObject.AddComponent<LayoutElement>();
            pathLe.minHeight = 60;
            GroupHint(store, "trial_summary.csv (試行) ・ evaluations.csv (評価) ・ questionnaire.csv (アンケート) ・ subjects.csv (被験者)");

            if (MockVisible)
            {
                Section(content, "テスト用の盤 (PC で確認するとき)");
                RectTransform mockGroup = Group(content);
                RectTransform mock = Row(mockGroup, 76, mockGroup);
                RowButton(mock, "mock_place", "正しく置く", ColS3, ColText, MockPlaceTarget, -1, Pill, 22);
                RowButton(mock, "mock_wrong", "違う色を置く", ColS3, ColText, MockPlaceWrongColor, -1, Pill, 22);
                RowButton(mock, "mock_clear", "盤を空にする", ColS3, ColText, MockClear, -1, Pill, 22);
                RowButton(mock, "mock_initial", "初期配置にする", ColS3, ColText, MockPlaceInitialStones, -1, Pill, 22);
            }

            // ログ
            Section(content, "ログと詳しい状態");
            RectTransform logGroup = Group(content);
            _detailText = Txt(logGroup, "Detail", "", 21, ColMuted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            _logSlot = Img(logGroup, "LogSlot", Vector2.zero, Vector2.one, Hex("#05070A")).rectTransform;
            var logLe = _logSlot.gameObject.AddComponent<LayoutElement>();
            logLe.preferredHeight = 360;
            logLe.minHeight = 360;
            _logSlot.gameObject.AddComponent<RectMask2D>();
        }

        private void BuildRecordingSettings(RectTransform content)
        {
            Section(content, "グラス画面の録画");
            RectTransform group = Group(content);
            RectTransform enabledRow = Row(group, 76, group);
            RowText(enabledRow, "試行中に録画する", -1);
            SwitchButton(enabledRow, "rec_enabled", OnToggleRecording);
            RectTransform sourceRow = Row(group, 84, group);
            RowText(sourceRow, "録るもの", 150);
            SegmentRow(sourceRow, "rec_source_", new[] { "見えている景色", "AR 表示だけ", "カメラ映像だけ" }, OnRecordingSource);
            _recHint = Txt(group, "Hint", "", 20, ColMuted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            _recHint.gameObject.AddComponent<LayoutElement>().minHeight = 40;

            RectTransform quality = Group(content);
            RectTransform qualityRow = Row(quality, 84, quality);
            RowText(qualityRow, "画質", 150);
            SegmentRow(qualityRow, "rec_quality_", new[] { "標準 720p", "高 1080p" }, OnRecordingQuality);
            GroupHint(quality, "高いほど Beam Pro の負荷が上がり、盤の認識のフレームレートが落ちることがあります。どちらも 30fps。");

            RectTransform store = Group(content);
            RectTransform pathRow = Row(store, 72, store);
            RowText(pathRow, "保存先", 150);
            _recStoreText = RowText(pathRow, "", -1);
            _recStoreText.alignment = TextAnchor.MiddleRight;
            _recStoreText.color = ColMuted;
            _recStoreText.fontSize = 20;
            RectTransform freeRow = Row(store, 72, store);
            RowText(freeRow, "本体の空き", 150);
            _recFreeText = RowText(freeRow, "", -1);
            _recFreeText.alignment = TextAnchor.MiddleRight;
            _recFreeText.color = ColMuted;
            _recFreeText.fontSize = 21;
            GroupHint(store, "やり直しで中断した試行も、途中までを「実験ID_reset.mp4」で残します。");
        }

        /// <summary>行の残りの幅いっぱいのセグメント切り替え。</summary>
        private void SegmentRow(RectTransform row, string idPrefix, string[] labels, Action<int> onSelect)
        {
            Image track = Img(row, "Track", Vector2.zero, Vector2.one, ColS2, Pill);
            track.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            for (int i = 0; i < labels.Length; i++)
            {
                int captured = i;
                float w = 1f / labels.Length;
                Btn(track.rectTransform, idPrefix + i, labels[i], new Vector2(i * w + 0.006f, 0.1f), new Vector2((i + 1) * w - 0.006f, 0.9f), Clear, ColMuted, 21, () => onSelect(captured), Pill);
            }
        }

        private void Section(RectTransform parent, string title)
        {
            Text t = Txt(parent, "Section", title, 21, ColMuted, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, true);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 58;
        }

        /// <summary>角丸のまとまり (中に行を縦に並べる)。</summary>
        private RectTransform Group(RectTransform parent)
        {
            Image g = Img(parent, "Group", Vector2.zero, Vector2.one, ColS1, 24f);
            AddStroke(g.rectTransform, ColLine, 24f).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var v = g.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(24, 24, 8, 12);
            v.spacing = 0;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            return g.rectTransform;
        }

        private void GroupHint(RectTransform group, string text)
        {
            Text t = Txt(group, "Hint", text, 20, ColMuted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            t.gameObject.AddComponent<LayoutElement>().minHeight = 40;
        }

        private RectTransform Row(RectTransform parent, float height, RectTransform group)
        {
            RectTransform rt = Rect(parent, "Row", Vector2.zero, Vector2.one);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12;
            h.padding = new RectOffset(0, 0, 6, 6);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            if (group != null && group.childCount > 2) // 0: 枠線, 1: この行
            {
                // 行の間に細い区切り線
                Img(rt, "Divider", new Vector2(0f, 1f), new Vector2(1f, 1f), ColLine).gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
                ((RectTransform)rt.Find("Divider")).offsetMin = new Vector2(0f, -1f);
            }
            return rt;
        }

        private Text RowText(RectTransform row, string text, float width)
        {
            Text t = Txt(row, "Label", text, 24, ColText, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            var le = t.gameObject.AddComponent<LayoutElement>();
            if (width > 0) { le.preferredWidth = width; le.flexibleWidth = 0; }
            else { le.preferredWidth = 100; le.flexibleWidth = 1; }
            return t;
        }

        private Text RowPill(RectTransform row, float width)
        {
            Image bg = Img(row, "Value", Vector2.zero, Vector2.one, ColS3, Pill);
            var le = bg.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.flexibleWidth = 0;
            return Txt(bg.rectTransform, "Text", "", 21, ColText, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        }

        private void RowButton(RectTransform row, string id, string label, Color color, Color fg, Action onClick, float width, float radius, int fontSize)
        {
            Button b = Btn(row, id, label, Vector2.zero, Vector2.one, color, fg, fontSize, onClick, radius);
            var le = b.GetComponent<LayoutElement>();
            if (width > 0) { le.preferredWidth = width; le.flexibleWidth = 0; }
            else { le.preferredWidth = 100; le.flexibleWidth = 1; }
        }

        /// <summary>文字だけの行ボタン (「手前に 3cm ずらす」など)。</summary>
        private void ActionRow(RectTransform group, string id, string label, Color fg, Action onClick)
        {
            RectTransform row = Row(group, 76, group);
            Button b = Btn(row, id, label, Vector2.zero, Vector2.one, Clear, fg, 24, onClick, 12f);
            var le = b.GetComponent<LayoutElement>();
            le.flexibleWidth = 1;
            _labels[id].alignment = TextAnchor.MiddleLeft;
            _labels[id].rectTransform.offsetMin = new Vector2(0f, 4f);
        }

        /// <summary>スイッチ (押すたびに入り切り)。</summary>
        private void SwitchButton(RectTransform row, string id, Action onClick)
        {
            RectTransform holder = Rect(row, "Switch_" + id, Vector2.zero, Vector2.one);
            var le = holder.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 96;
            le.flexibleWidth = 0;
            Button b = Btn(holder, id, "", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), ColOff, ColText, 10, onClick, Pill);
            ((RectTransform)b.transform).sizeDelta = new Vector2(0f, 52f);
            Image knob = FixedImg((RectTransform)b.transform, "Knob", new Vector2(0f, 0.5f), new Vector2(24f, 0f), new Vector2(42f, 42f), Color.white, UiSprites.Circle);
            _switchKnobs[id] = knob.rectTransform;
            Switches[id] = false;
        }

        private Slider CreateSlider(RectTransform row, int axis)
        {
            RectTransform rt = Rect(row, "Slider_" + axis, Vector2.zero, Vector2.one);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 200;
            le.flexibleWidth = 1;
            Img(rt, "Track", new Vector2(0f, 0.44f), new Vector2(1f, 0.56f), ColS3, Pill);
            Img(rt, "Center", new Vector2(0.4985f, 0.3f), new Vector2(0.5015f, 0.7f), Hex("#4A566C"));
            RectTransform area = Rect(rt, "Handle Slide Area", Vector2.zero, Vector2.one);
            area.offsetMin = new Vector2(20, 0);
            area.offsetMax = new Vector2(-20, 0);
            Image handleImage = Img(area, "Handle", Vector2.zero, Vector2.one, Color.white);
            RectTransform handle = handleImage.rectTransform;
            handle.sizeDelta = new Vector2(40, 0);
            handleImage.sprite = UiSprites.Circle;
            handleImage.preserveAspect = true;

            Slider s = rt.gameObject.AddComponent<Slider>();
            s.handleRect = handle;
            s.targetGraphic = handleImage;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = -6f;
            s.maxValue = 6f;
            s.SetValueWithoutNotify(0f);
            s.onValueChanged.AddListener(v => OnSlider(axis, v));
            return s;
        }

        private InputField CreateInputField(RectTransform parent, string id, string placeholder, Vector2 min, Vector2 max, bool multiline)
        {
            Image bg = Img(parent, "Input_" + id, min, max, ColS2, 18f);
            AddStroke(bg.rectTransform, ColLine, 18f);
            RectTransform rt = bg.rectTransform;

            Text MakeText(string name, Color color, FontStyle style)
            {
                Text t = Txt(rt, name, "", 26, color, multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
                t.rectTransform.offsetMin = new Vector2(20, 10);
                t.rectTransform.offsetMax = new Vector2(-20, -10);
                t.fontStyle = style;
                t.supportRichText = false;
                return t;
            }

            InputField f = rt.gameObject.AddComponent<InputField>();
            Text placeholderText = MakeText("Placeholder", new Color(1f, 1f, 1f, 0.3f), FontStyle.Normal);
            placeholderText.text = placeholder;
            f.placeholder = placeholderText;
            f.textComponent = MakeText("Text", ColText, FontStyle.Normal);
            f.targetGraphic = bg;
            f.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
            Inputs[id] = f;
            return f;
        }

        // ---------------- 既存の表示の取り込み ----------------

        private readonly List<(Transform t, Transform parent, int index, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)> _moved
            = new List<(Transform, Transform, int, Vector2, Vector2, Vector2, Vector2)>();
        private readonly List<GameObject> _hiddenLegacy = new List<GameObject>();

        private void AdoptLegacyViews()
        {
            Adopt(cameraView, _viewSlot, true);
            Adopt(overheadView, _viewSlot, true);
            Adopt(boardView, _boardSlot, true);
            Adopt(boardStatusText, _boardStatusSlot, false);
            Adopt(logText, _logSlot, false);
            if (boardStatusText != null) { boardStatusText.fontSize = 22; boardStatusText.alignment = TextAnchor.MiddleCenter; }
            if (logText != null) { logText.fontSize = 20; logText.alignment = TextAnchor.LowerLeft; }
            if (boardView != null)
            {
                AspectRatioFitter f = boardView.GetComponent<AspectRatioFitter>();
                if (f != null) f.aspectRatio = 1f;
            }

            if (legacyObjects != null)
            {
                foreach (GameObject go in legacyObjects)
                {
                    if (go == null || !go.activeSelf) continue;
                    go.SetActive(false);
                    _hiddenLegacy.Add(go);
                }
            }
            _legacyAdopted = true;
            if (_viewEmptyText != null) _viewEmptyText.transform.SetAsLastSibling();
            if (_viewTag != null) _viewTag.transform.parent.SetAsLastSibling();
        }

        private void Adopt(Graphic g, RectTransform slot, bool fitAspect)
        {
            if (g == null || slot == null) return;
            RectTransform rt = g.rectTransform;
            _moved.Add((rt, rt.parent, rt.GetSiblingIndex(), rt.anchorMin, rt.anchorMax, rt.offsetMin, rt.offsetMax));
            rt.SetParent(slot, false);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            Stretch(rt, Vector2.zero, Vector2.one);
            g.gameObject.SetActive(true);
            if (fitAspect)
            {
                AspectRatioFitter f = g.GetComponent<AspectRatioFitter>();
                if (f == null) f = g.gameObject.AddComponent<AspectRatioFitter>();
                f.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            }
        }

        /// <summary>取り込んだ既存の表示を元の場所へ戻し、隠した既存 UI を表示に戻す。</summary>
        private void ReleaseAdoptedViews()
        {
            for (int i = _moved.Count - 1; i >= 0; i--)
            {
                var m = _moved[i];
                if (m.t == null || m.parent == null) continue;
                var rt = (RectTransform)m.t;
                rt.SetParent(m.parent, false);
                rt.SetSiblingIndex(m.index);
                rt.anchorMin = m.min;
                rt.anchorMax = m.max;
                rt.offsetMin = m.offMin;
                rt.offsetMax = m.offMax;
                rt.gameObject.SetActive(true);
            }
            _moved.Clear();
            foreach (GameObject go in _hiddenLegacy) if (go != null) go.SetActive(true);
            _hiddenLegacy.Clear();
            _legacyAdopted = false;
        }

        private void OnDisable()
        {
            // コンポーネントを明示的に無効にしたときだけ元の画面に戻す (シーン終了・破棄時は触らない)
            if (_root == null || enabled || !gameObject.activeInHierarchy) return;
            ReleaseAdoptedViews();
            _root.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (_root == null || _operator == null) return;
            _root.gameObject.SetActive(true);
            AdoptLegacyViews();
            RefreshViews();
        }

        // ---------------- 部品づくり ----------------

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;

        private RectTransform Rect(RectTransform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Stretch(rt, min, max);
            return rt;
        }

        /// <summary>Image を作る。radius: 0 = 四角, 正 = 角丸 (半径), 負 = ピル型。</summary>
        private Image Img(RectTransform parent, string name, Vector2 min, Vector2 max, Color color, float radius = 0f)
        {
            RectTransform rt = Rect(parent, name, min, max);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (radius != 0f) MakeRound(img, radius, UiSprites.Rounded);
            return img;
        }

        private static void MakeRound(Image img, float radius, Sprite sprite)
        {
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.fillCenter = true;
            UiRound round = img.gameObject.AddComponent<UiRound>();
            round.Radius = radius > 0f ? radius : 0f;
            round.Pill = radius < 0f;
            round.Apply();
        }

        /// <summary>枠線 (角丸) を重ねる。</summary>
        private Image AddStroke(RectTransform target, Color color, float radius)
        {
            RectTransform rt = Rect(target, "Stroke", Vector2.zero, Vector2.one);
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            MakeRound(img, radius, UiSprites.Stroke);
            return img;
        }

        private Image FixedImg(RectTransform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color, Sprite sprite)
        {
            RectTransform rt = Rect(parent, name, anchor, anchor);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        private static void Pad(RectTransform rt, float left, float right)
        {
            rt.offsetMin = new Vector2(left, rt.offsetMin.y);
            rt.offsetMax = new Vector2(-right, rt.offsetMax.y);
        }

        private Text Txt(RectTransform parent, string name, string text, int size, Color color, TextAnchor align, Vector2 min, Vector2 max, bool bold = false)
        {
            RectTransform rt = Rect(parent, name, min, max);
            Text t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        private Button Btn(RectTransform parent, string id, string label, Vector2 min, Vector2 max, Color color, Color fg, int fontSize, Action onClick, float radius = 0f)
        {
            Image bg = Img(parent, "Btn_" + id, min, max, color, radius);
            RectTransform rt = bg.rectTransform;
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            b.colors = cb;
            b.onClick.AddListener(() => onClick());
            rt.gameObject.AddComponent<LayoutElement>();

            Text t = Txt(rt, "Text", label, fontSize, fg, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
            t.rectTransform.offsetMin = new Vector2(10, 4);
            t.rectTransform.offsetMax = new Vector2(-10, -4);
            Buttons[id] = b;
            _labels[id] = t;
            return b;
        }

        private static void Stretch(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void DestroyObj(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
