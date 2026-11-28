using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AROthello.Experiment
{
    /// <summary>
    /// 実験者用の手元画面 (Beam Pro)。uGUI を実行時に組み立てるので、シーンには本コンポーネントを置くだけでよい。
    ///
    /// 画面はいまの手順 (位置合わせ → 初期配置 → 試行 → 完了) を中心に置き、
    /// 被験者情報・パターン・位置合わせの基準・グラスの動きの向き・記録などの設定は右上の「設定」の中にまとめる。
    /// 既存の手元 UI (カメラ映像・俯瞰ビュー・認識した盤・ログ) は参照があれば新しい画面の中へ移して使い、
    /// 残りの既存パネルは実行時だけ非表示にする (シーンは変更しない。本コンポーネントを無効にすると元の画面に戻る)。
    ///
    /// キーボード (PC での確認用。入力欄の編集中は無効):
    ///   F1 設定 / Enter 次へ進む / PageUp・PageDown パターン切替 / D 四隅の印 / 矢印 位置合わせ
    ///   N 認識を待たず次へ / B 1 手戻る / Shift+R やり直す
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
        [Tooltip("試行完了ごとに順序 (order) を 1 増やす")]
        [SerializeField] private bool autoIncrementOrder = false;
        [Tooltip("やり直しは 2 回押しで実行 (誤操作防止)。この秒数以内の 2 回目で実行")]
        [SerializeField, Min(0.5f)] private float resetConfirmSeconds = 2.5f;
        [Tooltip("モック盤面の操作ボタンを実機でも出す (既定はエディタだけ)")]
        [SerializeField] private bool showMockOnDevice = false;

        // ---------------- 配色 ----------------
        private static readonly Color ColBg = new Color(0.055f, 0.07f, 0.095f, 1f);
        private static readonly Color ColSurface = new Color(0.09f, 0.115f, 0.15f, 1f);
        private static readonly Color ColSurface2 = new Color(0.15f, 0.18f, 0.23f, 1f);
        private static readonly Color ColText = new Color(0.92f, 0.95f, 0.98f, 1f);
        private static readonly Color ColMuted = new Color(0.58f, 0.64f, 0.72f, 1f);
        private static readonly Color ColGreen = new Color(0.12f, 0.6f, 0.34f, 1f);
        private static readonly Color ColBlue = new Color(0.2f, 0.42f, 0.78f, 1f);
        private static readonly Color ColCyan = new Color(0f, 0.8f, 0.95f, 1f);
        private static readonly Color ColAmber = new Color(0.9f, 0.6f, 0.15f, 1f);
        private static readonly Color ColRed = new Color(0.78f, 0.2f, 0.23f, 1f);
        private static readonly Color ColPurple = new Color(0.55f, 0.2f, 0.65f, 1f);

        private static readonly string[] BasisNames = { "自分から見た向き", "盤の向き", "部屋の向き" };
        private static readonly string[] StageNames = { "位置合わせ", "初期配置", "試行", "完了" };
        private static readonly (TrackingAxis axis, string label)[] AxisRows =
        {
            (TrackingAxis.Height, "上下に動かしたとき"),
            (TrackingAxis.Pitch, "上下を向いたとき"),
            (TrackingAxis.Yaw, "左右を向いたとき"),
            (TrackingAxis.Roll, "首をかしげたとき"),
        };

        private ExperimentOperator _operator;
        private IBoardOffsetAdjuster _adjuster;
        private ITrackingSettings _settings;
        private Font _font;
        private float _resetArmedUntil = -1f;
        private float _statusTimer;
        private bool _refreshing;
        private int _viewIndex;

        // 画面の部品
        private RectTransform _root, _settingsSheet, _viewSlot, _boardSlot, _boardStatusSlot, _logSlot;
        private Image _stateDot, _trackDot, _nextStone;
        private Text _stateText, _trackText, _infoText, _headline, _viewEmptyText;
        private Text _idleText, _calibReadout, _checkCount, _checkList, _runProgress, _runNext, _runError, _runStats, _doneText;
        private Text _patternText, _detailText;
        private GameObject _errorBanner, _primaryHolder, _overrideHolder;
        private readonly Dictionary<ExperimentState, GameObject> _bodies = new Dictionary<ExperimentState, GameObject>();
        private readonly List<(Image circle, Text number, Text label)> _stages = new List<(Image, Text, Text)>();
        private readonly Slider[] _sliders = new Slider[3];
        private readonly Text[] _sliderValues = new Text[3];
        private InputField _subjectField, _conditionField, _orderField;
        private readonly Dictionary<string, Text> _labels = new Dictionary<string, Text>();
        private bool _legacyAdopted;

        public ExperimentOperator Operator => _operator;
        /// <summary>設定シートが開いているか</summary>
        public bool IsOpen => _settingsSheet != null && _settingsSheet.gameObject.activeSelf;
        /// <summary>組み立てたボタン (ID → Button)。テスト・自動操作用。</summary>
        public Dictionary<string, Button> Buttons { get; } = new Dictionary<string, Button>();
        /// <summary>ボタンのラベル (ID → Text)。テスト用。</summary>
        public IReadOnlyDictionary<string, Text> Labels => _labels;

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
            Initialize(new ExperimentOperator(
                controller.StateMachine,
                loader != null ? loader.Library : null,
                logger != null ? logger.Metadata : new TrialMetadata(),
                () => Time.realtimeSinceStartupAsDouble));

            if (loader != null)
            {
                if (loader.IsLoaded) _operator.LoadSelectedPattern();
                loader.Loaded += lib => { _operator.LoadSelectedPattern(); RefreshAll(); };
            }
        }

        private void OnDestroy()
        {
            _operator?.Dispose();
        }

        /// <summary>操作ロジックを受け取り画面を組み立てる (Start から呼ばれる。テスト用に公開)。</summary>
        public void Initialize(ExperimentOperator op, Canvas canvas = null)
        {
            _operator = op ?? throw new ArgumentNullException(nameof(op));
            _operator.AutoIncrementOrder = autoIncrementOrder;
            _operator.ActionLogged += msg => Debug.Log($"[Experiment] {msg}");
            _adjuster = offsetAdjuster as IBoardOffsetAdjuster;
            _settings = trackingSettings as ITrackingSettings;
            if (canvas != null) targetCanvas = canvas;
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

        private void Update()
        {
            if (_operator == null) return;
            if (enableKeyboard) HandleKeyboard();

            if (_resetArmedUntil > 0 && Time.unscaledTime > _resetArmedUntil)
            {
                _resetArmedUntil = -1f;
                RefreshAll();
            }

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
        public void OnRecalibrate() { _settings?.RecalibrateStoneDetection(); RefreshAll(); }
        public void OnTogglePerformanceLog() { _settings?.TogglePerformanceLogging(); RefreshAll(); }

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
                ExperimentStateMachine sm = _operator.StateMachine;
                ExperimentState state = sm.State;
                RefreshTopBar(state);
                RefreshInfo();
                RefreshStages(state);
                RefreshStepCard(sm, state);
                RefreshSettings();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private void RefreshTopBar(ExperimentState state)
        {
            (string text, Color color) chip;
            switch (state)
            {
                case ExperimentState.Calibration: chip = ("位置合わせ中", ColCyan); break;
                case ExperimentState.InitialBoardCheck: chip = ("初期配置の確認中", ColAmber); break;
                case ExperimentState.Running: chip = ("試行中", ColGreen); break;
                case ExperimentState.Completed: chip = ("完了", ColBlue); break;
                default: chip = ("待機中", ColMuted); break;
            }
            _stateText.text = chip.text;
            _stateDot.color = chip.color;

            bool hasTracking = _settings != null;
            _trackDot.gameObject.SetActive(hasTracking);
            _trackText.gameObject.SetActive(hasTracking);
            if (hasTracking)
            {
                bool ok = _settings.IsTracking;
                _trackDot.color = ok ? ColGreen : ColRed;
                _trackText.text = ok ? "盤を認識中" : "盤が見えていません";
            }
            SetButtonColor("settings", IsOpen ? ColBlue : ColSurface2);
            SetLabel("settings", IsOpen ? "閉じる" : "設定");
        }

        private void RefreshInfo()
        {
            TrialMetadata m = _operator.Metadata;
            InstructionPatternData p = _operator.Library.Current;
            string pattern = p != null ? p.ToString() : "(パターンなし)";
            _infoText.text = $"被験者  <b>{Show(m.subjectId)}</b>\n条件  <b>{Show(m.condition)}</b>　順序  <b>{m.order}</b>\n{pattern}";
            if (_patternText != null) _patternText.text = pattern;
        }

        private void RefreshStages(ExperimentState state)
        {
            int current;
            switch (state)
            {
                case ExperimentState.Calibration: current = 0; break;
                case ExperimentState.InitialBoardCheck: current = 1; break;
                case ExperimentState.Running: current = 2; break;
                case ExperimentState.Completed: current = 3; break;
                default: current = -1; break;
            }
            for (int i = 0; i < _stages.Count; i++)
            {
                bool done = current > i || state == ExperimentState.Completed;
                bool now = current == i && state != ExperimentState.Completed;
                _stages[i].circle.color = now ? ColCyan : done ? ColGreen : ColSurface2;
                _stages[i].number.color = now || done ? ColBg : ColMuted;
                _stages[i].label.color = now ? ColText : ColMuted;
                _stages[i].label.fontStyle = now ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        private void RefreshStepCard(ExperimentStateMachine sm, ExperimentState state)
        {
            foreach (var kv in _bodies) kv.Value.SetActive(kv.Key == state);

            bool trialActive = state == ExperimentState.InitialBoardCheck || state == ExperimentState.Running;
            _primaryHolder.SetActive(!trialActive);
            _overrideHolder.SetActive(trialActive);

            bool armed = _resetArmedUntil > 0 && Time.unscaledTime <= _resetArmedUntil;
            SetLabel("reset", armed ? "もう一度押すとやり直し" : "最初からやり直す");
            SetButtonColor("reset", armed ? ColRed : new Color(0.45f, 0.16f, 0.18f, 1f));
            SetLabel("force", state == ExperimentState.InitialBoardCheck ? "確認をとばして始める" : "認識を待たず次へ");
            if (Buttons.TryGetValue("back", out Button back))
            {
                back.gameObject.SetActive(state == ExperimentState.Running);
                back.interactable = sm.CurrentStepIndex > 0;
            }
            if (Buttons.TryGetValue("force", out Button force))
            {
                // 初期配置の確認中は「1手戻る」が無いので左へ広げる
                ((RectTransform)force.transform).anchorMin = new Vector2(state == ExperimentState.Running ? 0.26f : 0f, 0f);
            }

            InstructionPatternData pattern = _operator.Library.Current;
            switch (state)
            {
                case ExperimentState.Idle:
                    _headline.text = "準備ができたら位置合わせを始めます";
                    _idleText.text = pattern != null
                        ? $"使うパターン\n<size=40><color=#FFE680><b>{pattern.patternId}</b></color></size>\n{pattern.stepCount} 手・難易度 {pattern.difficulty}\n\n<color=#8FA0B5>被験者・条件・パターンは「設定」で変えられます。</color>"
                        : "<color=#FF9A7A>パターンが読み込まれていません。</color>\n「設定」でパターンを選んでください。";
                    SetLabel("primary", "位置合わせを始める");
                    SetInteractable("primary", pattern != null);
                    break;

                case ExperimentState.Calibration:
                    _headline.text = "グラスの表示を実物の盤に合わせます";
                    _calibReadout.text = BuildOffsetReadout();
                    bool dots = calibrationDots != null && calibrationDots.Visible;
                    SetLabel("dots", dots ? "四隅の印を消す" : "四隅の印を出す");
                    SetButtonColor("dots", dots ? ColPurple : ColSurface2);
                    SetLabel("primary", "位置OK・試行を始める");
                    SetInteractable("primary", true);
                    break;

                case ExperimentState.InitialBoardCheck:
                    IReadOnlyList<CellMismatch> mm = sm.LastInitialMismatches;
                    bool empty = pattern == null || pattern.initialStones == null || pattern.initialStones.Length == 0;
                    _headline.text = empty ? "盤を空にしてください" : "盤を初期配置にしてください";
                    _checkCount.text = mm.Count == 0 ? "そろいました" : $"あと <b>{mm.Count}</b> マス";
                    _checkCount.color = mm.Count == 0 ? ColGreen : ColAmber;
                    _checkList.text = DescribeMismatches(mm);
                    break;

                case ExperimentState.Running:
                    _headline.text = "被験者が石を置いています";
                    _runProgress.text = $"<b>{sm.CurrentStepIndex + 1}</b> / {sm.StepTotal} 手目";
                    InstructionStep step = sm.CurrentStep;
                    if (step != null)
                    {
                        BoardCoord.TryParseColor(step.color, out StoneColor c);
                        _nextStone.color = c == StoneColor.White ? new Color(0.95f, 0.95f, 0.95f, 1f) : new Color(0.05f, 0.05f, 0.05f, 1f);
                        _runNext.text = $"{step.coord} に{ColorName(c)}";
                    }
                    _errorBanner.SetActive(sm.IsErrorActive);
                    if (sm.IsErrorActive) _runError.text = DescribeError(sm.ActiveError, step);
                    _runStats.text = $"間違い　この手 {sm.CurrentStepErrorCount} 回　合計 {sm.TotalErrorCount} 回";
                    break;

                case ExperimentState.Completed:
                    _headline.text = "試行が終わりました";
                    TrialResult r = _operator.LastTrial;
                    if (r != null)
                    {
                        int forced = 0;
                        foreach (StepResult s in r.Steps) if (s.IsOverride) forced++;
                        _doneText.text = $"かかった時間　<b>{r.TotalTimeSec:0.0} 秒</b>\n間違い　<b>{r.TotalErrorCount} 回</b>\n手動で進めた手　<b>{forced} 手</b>\n\n<color=#8FA0B5>「次の試行へ」で同じパターンをもう一度始めます。被験者・条件・パターンは「設定」で変えられます。</color>";
                    }
                    else _doneText.text = "";
                    SetLabel("primary", "次の試行へ");
                    SetInteractable("primary", true);
                    break;
            }
        }

        private void RefreshSettings()
        {
            if (_detailText != null)
            {
                string dots = calibrationDots != null && calibrationDots.Visible ? "  [四隅の印を表示中]" : "";
                _detailText.text = _operator.BuildStatusText() + dots;
            }
            if (_subjectField != null && !_subjectField.isFocused) _subjectField.text = _operator.Metadata.subjectId;
            if (_conditionField != null && !_conditionField.isFocused) _conditionField.text = _operator.Metadata.condition;
            if (_orderField != null && !_orderField.isFocused) _orderField.text = _operator.Metadata.order.ToString();

            Vector3 cm = _adjuster != null ? _adjuster.CurrentOffsetCm : Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                if (_sliders[i] != null) _sliders[i].SetValueWithoutNotify(cm[i]);
                if (_sliderValues[i] != null) _sliderValues[i].text = $"{cm[i]:+0.0;-0.0;0.0} cm";
            }

            if (_settings == null) return;
            for (int b = 0; b < BasisNames.Length; b++) SetButtonColor("basis_" + b, _settings.OffsetBasis == b ? ColBlue : ColSurface2);
            foreach (var row in AxisRows)
            {
                bool inv = _settings.IsAxisInverted(row.axis);
                SetButtonColor($"axis_{(int)row.axis}_normal", inv ? ColSurface2 : ColBlue);
                SetButtonColor($"axis_{(int)row.axis}_invert", inv ? ColBlue : ColSurface2);
            }
            bool logging = _settings.IsPerformanceLogging;
            SetLabel("perf_log", logging ? "記録を止めて保存する" : "動作の記録を始める");
            SetButtonColor("perf_log", logging ? ColRed : ColSurface2);
        }

        private string BuildOffsetReadout()
        {
            if (_adjuster == null) return "<color=#8FA0B5>(位置合わせは未接続)</color>";
            Vector3 cm = _adjuster.CurrentOffsetCm;
            string basis = _settings != null ? BasisNames[Mathf.Clamp(_settings.OffsetBasis, 0, 2)] : _adjuster.ModeName;
            return $"いまのずれ補正　{Dir(cm.x, "右", "左", "左右")}・{Dir(cm.z, "奥", "手前", "奥行")}・{Dir(cm.y, "上", "下", "高さ")}\n<color=#8FA0B5>基準: {basis}</color>";
        }

        private static string Dir(float v, string plus, string minus, string zero)
        {
            if (Mathf.Abs(v) < 0.05f) return $"{zero} 0";
            return $"{(v > 0 ? plus : minus)} {Mathf.Abs(v):0.0}cm";
        }

        private static string ColorName(StoneColor c) => c == StoneColor.White ? "白" : c == StoneColor.Black ? "黒" : "なし";

        private static string DescribeMismatch(CellMismatch m)
        {
            if (m.Expected == StoneColor.None) return $"{m.Coord} の石をとる";
            if (m.Actual == StoneColor.None) return $"{m.Coord} に{ColorName(m.Expected)}を置く";
            return $"{m.Coord} を{ColorName(m.Expected)}にする";
        }

        private static string DescribeMismatches(IReadOnlyList<CellMismatch> list)
        {
            if (list.Count == 0) return "<color=#8FA0B5>そろうと自動で試行が始まります。</color>";
            var sb = new StringBuilder();
            int shown = Mathf.Min(list.Count, 6);
            for (int i = 0; i < shown; i++) sb.Append("・").Append(DescribeMismatch(list[i])).Append('\n');
            if (list.Count > shown) sb.Append($"ほか {list.Count - shown} マス");
            return sb.ToString().TrimEnd('\n');
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
                        ? $"置き間違い: {m.Coord} に{ColorName(m.Actual)}が置かれています (正しくは{ColorName(want)})"
                        : $"置き間違い: {m.Coord} の色が違います";
                case PlacementErrorType.WrongPosition:
                    return step != null
                        ? $"置き間違い: {m.Coord} は指示と違うマスです (正しくは {step.coord})"
                        : $"置き間違い: {m.Coord} は指示と違うマスです";
                case PlacementErrorType.MissingStone: return $"{m.Coord} の石がなくなっています";
                default: return $"{m.Coord} の石の色が変わっています";
            }
        }

        private static string Show(string s) => string.IsNullOrEmpty(s) ? "(未入力)" : s;

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
            SetButtonColor("view_camera", _viewIndex == 0 ? ColBlue : ColSurface2);
            SetButtonColor("view_overhead", _viewIndex == 1 ? ColBlue : ColSurface2);
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
            _labels.Clear();
            _bodies.Clear();
            _stages.Clear();

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

            _root = Rect((RectTransform)canvasRoot, RootName, Vector2.zero, Vector2.one, ColBg);
            _root.SetAsLastSibling();

            BuildTopBar();
            BuildViewArea();
            BuildBoardCard();
            BuildInfoCard();
            BuildStepCard();
            BuildSettingsSheet();
            AdoptLegacyViews();
            RefreshViews();
        }

        private void BuildTopBar()
        {
            RectTransform bar = Rect(_root, "TopBar", new Vector2(0f, 0.945f), Vector2.one, ColSurface);
            _stateDot = Fixed(bar, "StateDot", new Vector2(0.045f, 0.5f), new Vector2(28, 28), ColMuted, Circle);
            _stateText = Txt(bar, "StateText", "", 34, ColText, TextAnchor.MiddleLeft, new Vector2(0.075f, 0f), new Vector2(0.5f, 1f), true);
            _trackDot = Fixed(bar, "TrackDot", new Vector2(0.52f, 0.5f), new Vector2(22, 22), ColMuted, Circle);
            _trackText = Txt(bar, "TrackText", "", 25, ColText, TextAnchor.MiddleLeft, new Vector2(0.545f, 0f), new Vector2(0.81f, 1f));
            Btn(bar, "settings", "設定", ColSurface2, TogglePanel, new Vector2(0.82f, 0.16f), new Vector2(0.975f, 0.84f), 28);
        }

        private void BuildViewArea()
        {
            Btn(_root, "view_camera", "カメラの映像", ColBlue, () => ShowView(0), new Vector2(0.03f, 0.897f), new Vector2(0.497f, 0.936f), 25);
            Btn(_root, "view_overhead", "上から見た配置", ColSurface2, () => ShowView(1), new Vector2(0.503f, 0.897f), new Vector2(0.97f, 0.936f), 25);
            RectTransform frame = Rect(_root, "View", new Vector2(0.03f, 0.565f), new Vector2(0.97f, 0.89f), new Color(0.02f, 0.025f, 0.035f, 1f));
            _viewSlot = Rect(frame, "Slot", Vector2.zero, Vector2.one);
            _viewEmptyText = Txt(frame, "Empty", "映像がありません", 26, ColMuted, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        }

        private void BuildBoardCard()
        {
            RectTransform card = Rect(_root, "BoardCard", new Vector2(0.03f, 0.25f), new Vector2(0.405f, 0.55f), ColSurface);
            Txt(card, "Title", "カメラが認識した盤", 24, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.88f), new Vector2(0.96f, 0.98f), true);
            _boardSlot = Rect(card, "BoardSlot", new Vector2(0.06f, 0.15f), new Vector2(0.94f, 0.87f));
            _boardStatusSlot = Rect(card, "StatusSlot", new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.14f));
        }

        private void BuildInfoCard()
        {
            RectTransform card = Rect(_root, "InfoCard", new Vector2(0.03f, 0.02f), new Vector2(0.405f, 0.235f), ColSurface);
            Txt(card, "Title", "この試行", 24, ColMuted, TextAnchor.MiddleLeft, new Vector2(0.06f, 0.80f), new Vector2(0.96f, 0.96f), true);
            _infoText = Txt(card, "Info", "", 25, ColText, TextAnchor.UpperLeft, new Vector2(0.06f, 0.30f), new Vector2(0.96f, 0.80f));
            _infoText.lineSpacing = 1.15f;
            Btn(card, "edit_info", "変更する", ColSurface2, () => SetOpen(true), new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.27f), 24);
        }

        private void BuildStepCard()
        {
            RectTransform card = Rect(_root, "StepCard", new Vector2(0.425f, 0.02f), new Vector2(0.97f, 0.55f), ColSurface);

            // 手順の進み具合 (位置合わせ → 初期配置 → 試行 → 完了)
            RectTransform stepper = Rect(card, "Stages", new Vector2(0.04f, 0.895f), new Vector2(0.96f, 0.985f));
            Rect(stepper, "Line", new Vector2(0.125f, 0.66f), new Vector2(0.875f, 0.69f), ColSurface2);
            for (int i = 0; i < StageNames.Length; i++)
            {
                RectTransform node = Rect(stepper, "Stage" + i, new Vector2(i / 4f, 0f), new Vector2((i + 1) / 4f, 1f));
                Image circle = Fixed(node, "Circle", new Vector2(0.5f, 0.675f), new Vector2(42, 42), ColSurface2, Circle);
                Text number = Txt((RectTransform)circle.transform, "N", (i + 1).ToString(), 22, ColMuted, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
                Text label = Txt(node, "Label", StageNames[i], 20, ColMuted, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 0.36f));
                _stages.Add((circle, number, label));
            }

            _headline = Txt(card, "Headline", "", 34, ColText, TextAnchor.MiddleLeft, new Vector2(0.05f, 0.77f), new Vector2(0.95f, 0.885f), true);

            RectTransform body = Rect(card, "Body", new Vector2(0.05f, 0.19f), new Vector2(0.95f, 0.76f));
            BuildIdleBody(body);
            BuildCalibrationBody(body);
            BuildCheckBody(body);
            BuildRunningBody(body);
            BuildCompletedBody(body);

            RectTransform bottom = Rect(card, "Bottom", new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.17f));
            _primaryHolder = Rect(bottom, "Primary", Vector2.zero, Vector2.one).gameObject;
            Btn((RectTransform)_primaryHolder.transform, "primary", "位置合わせを始める", ColGreen, OnPrimary, Vector2.zero, Vector2.one, 34);

            RectTransform overrides = Rect(bottom, "Overrides", Vector2.zero, Vector2.one);
            _overrideHolder = overrides.gameObject;
            Btn(overrides, "back", "1手戻る", ColSurface2, OnStepBack, new Vector2(0f, 0f), new Vector2(0.24f, 1f), 24);
            Btn(overrides, "force", "認識を待たず次へ", new Color(0.62f, 0.38f, 0.08f, 1f), OnForceAdvance, new Vector2(0.26f, 0f), new Vector2(0.63f, 1f), 24);
            Btn(overrides, "reset", "最初からやり直す", new Color(0.45f, 0.16f, 0.18f, 1f), OnReset, new Vector2(0.65f, 0f), new Vector2(1f, 1f), 24);
        }

        private RectTransform Body(RectTransform parent, ExperimentState state)
        {
            RectTransform b = Rect(parent, "Body_" + state, Vector2.zero, Vector2.one);
            _bodies[state] = b.gameObject;
            return b;
        }

        private void BuildIdleBody(RectTransform parent)
        {
            RectTransform b = Body(parent, ExperimentState.Idle);
            _idleText = Txt(b, "Text", "", 28, ColText, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
            _idleText.lineSpacing = 1.2f;
        }

        private void BuildCalibrationBody(RectTransform parent)
        {
            RectTransform b = Body(parent, ExperimentState.Calibration);
            Txt(b, "Hint", "被験者にグラスをかけてもらい、四隅の印が盤の角に重なるまで矢印で動かします。",
                22, ColMuted, TextAnchor.UpperLeft, new Vector2(0f, 0.86f), new Vector2(1f, 1f));

            // 平面の向き (左右・奥/手前) を十字に配置し、真ん中に盤を描く
            RectTransform pad = Rect(b, "Pad", new Vector2(0f, 0.25f), new Vector2(0.7f, 0.84f));
            ArrowButton(pad, "nudge_z+", "奥へ", 0f, () => OnNudge(2, +1), new Vector2(1 / 3f, 2 / 3f), new Vector2(2 / 3f, 1f));
            ArrowButton(pad, "nudge_x-", "左へ", 90f, () => OnNudge(0, -1), new Vector2(0f, 1 / 3f), new Vector2(1 / 3f, 2 / 3f));
            ArrowButton(pad, "nudge_x+", "右へ", -90f, () => OnNudge(0, +1), new Vector2(2 / 3f, 1 / 3f), new Vector2(1f, 2 / 3f));
            ArrowButton(pad, "nudge_z-", "手前へ", 180f, () => OnNudge(2, -1), new Vector2(1 / 3f, 0f), new Vector2(2 / 3f, 1 / 3f));
            RectTransform board = Rect(pad, "BoardIcon", new Vector2(1 / 3f + 0.02f, 1 / 3f + 0.03f), new Vector2(2 / 3f - 0.02f, 2 / 3f - 0.03f), new Color(0.12f, 0.36f, 0.23f, 1f));
            for (int i = 1; i < 4; i++)
            {
                Rect(board, "V" + i, new Vector2(i / 4f - 0.006f, 0f), new Vector2(i / 4f + 0.006f, 1f), new Color(0.05f, 0.18f, 0.1f, 1f));
                Rect(board, "H" + i, new Vector2(0f, i / 4f - 0.008f), new Vector2(1f, i / 4f + 0.008f), new Color(0.05f, 0.18f, 0.1f, 1f));
            }
            Txt(board, "Label", "盤", 22, new Color(0.85f, 1f, 0.9f, 0.9f), TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);

            // 高さ (上下)
            RectTransform height = Rect(b, "Height", new Vector2(0.75f, 0.25f), new Vector2(1f, 0.84f));
            ArrowButton(height, "nudge_y+", "上へ", 0f, () => OnNudge(1, +1), new Vector2(0f, 0.6f), new Vector2(1f, 1f));
            Txt(height, "Label", "高さ", 22, ColMuted, TextAnchor.MiddleCenter, new Vector2(0f, 0.4f), new Vector2(1f, 0.6f), true);
            ArrowButton(height, "nudge_y-", "下へ", 180f, () => OnNudge(1, -1), new Vector2(0f, 0f), new Vector2(1f, 0.4f));

            _calibReadout = Txt(b, "Readout", "", 22, ColCyan, TextAnchor.MiddleLeft, new Vector2(0f, 0.12f), new Vector2(1f, 0.24f));
            Btn(b, "dots", "四隅の印を出す", ColSurface2, OnToggleDots, new Vector2(0f, 0f), new Vector2(0.49f, 0.11f), 23);
            Btn(b, "offset_reset", "ずれを 0 に戻す", ColSurface2, OnResetOffset, new Vector2(0.51f, 0f), new Vector2(1f, 0.11f), 23);
        }

        private void ArrowButton(RectTransform parent, string id, string label, float angle, Action onClick, Vector2 min, Vector2 max)
        {
            Button b = Btn(parent, id, label, ColBlue, onClick, min, max, 22);
            RectTransform rt = (RectTransform)b.transform;
            rt.offsetMin = new Vector2(4, 4);
            rt.offsetMax = new Vector2(-4, -4);
            Text t = _labels[id];
            Stretch(t.rectTransform, new Vector2(0f, 0.02f), new Vector2(1f, 0.42f));
            Image arrow = Fixed(rt, "Arrow", new Vector2(0.5f, 0.68f), new Vector2(46, 46), ColText, Triangle);
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void BuildCheckBody(RectTransform parent)
        {
            RectTransform b = Body(parent, ExperimentState.InitialBoardCheck);
            _checkCount = Txt(b, "Count", "", 52, ColAmber, TextAnchor.MiddleLeft, new Vector2(0f, 0.72f), new Vector2(1f, 1f));
            _checkList = Txt(b, "List", "", 28, ColText, TextAnchor.UpperLeft, new Vector2(0f, 0f), new Vector2(1f, 0.7f));
            _checkList.lineSpacing = 1.2f;
        }

        private void BuildRunningBody(RectTransform parent)
        {
            RectTransform b = Body(parent, ExperimentState.Running);
            _runProgress = Txt(b, "Progress", "", 46, ColText, TextAnchor.MiddleLeft, new Vector2(0f, 0.82f), new Vector2(1f, 1f));
            Txt(b, "NextLabel", "次に置く場所", 22, ColMuted, TextAnchor.MiddleLeft, new Vector2(0f, 0.70f), new Vector2(1f, 0.8f));
            RectTransform next = Rect(b, "Next", new Vector2(0f, 0.48f), new Vector2(1f, 0.70f), ColSurface2);
            _nextStone = Fixed(next, "Stone", new Vector2(0.14f, 0.5f), new Vector2(72, 72), Color.black, Circle);
            Fixed(next, "StoneRim", new Vector2(0.14f, 0.5f), new Vector2(80, 80), new Color(0.6f, 0.65f, 0.7f, 1f), Circle).transform.SetAsFirstSibling();
            _runNext = Txt(next, "Text", "", 44, ColText, TextAnchor.MiddleLeft, new Vector2(0.28f, 0f), new Vector2(1f, 1f), true);

            RectTransform banner = Rect(b, "Error", new Vector2(0f, 0.2f), new Vector2(1f, 0.42f), ColRed);
            _errorBanner = banner.gameObject;
            _runError = Txt(banner, "Text", "", 26, Color.white, TextAnchor.MiddleLeft, new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), true);
            _runStats = Txt(b, "Stats", "", 23, ColMuted, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(1f, 0.16f));
        }

        private void BuildCompletedBody(RectTransform parent)
        {
            RectTransform b = Body(parent, ExperimentState.Completed);
            _doneText = Txt(b, "Text", "", 30, ColText, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            _doneText.lineSpacing = 1.25f;
        }

        // ---------------- 設定シート ----------------

        private void BuildSettingsSheet()
        {
            _settingsSheet = Rect(_root, "Settings", Vector2.zero, new Vector2(1f, 0.945f), ColBg);

            RectTransform viewport = Rect(_settingsSheet, "Viewport", Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.001f));
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = Rect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(36, 36, 28, 48);
            vlg.spacing = 14;
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

            // 実験の準備
            Section(content, "実験の準備");
            RectTransform meta = Row(content, 76);
            RowText(meta, "被験者", 110);
            _subjectField = CreateInputField(meta, "S01", v => { _operator.SetSubjectId(v); RefreshAll(); });
            RowText(meta, "条件", 80);
            _conditionField = CreateInputField(meta, "AR", v => { _operator.SetCondition(v); RefreshAll(); });
            RowText(meta, "順序", 80);
            _orderField = CreateInputField(meta, "1", v => { _operator.SetOrder(v); RefreshAll(); });
            _orderField.contentType = InputField.ContentType.IntegerNumber;

            RectTransform pattern = Row(content, 76);
            RowButton(pattern, "pattern_prev", "◀ 前", ColSurface2, OnPrevPattern, 140);
            _patternText = RowText(pattern, "", -1);
            _patternText.alignment = TextAnchor.MiddleCenter;
            _patternText.color = new Color(1f, 0.9f, 0.5f, 1f);
            _patternText.fontStyle = FontStyle.Bold;
            RowButton(pattern, "pattern_next", "次 ▶", ColSurface2, OnNextPattern, 140);
            Hint(content, "パターンは試行していないときに変えられます。");

            // 位置合わせ
            Section(content, "位置合わせ");
            if (_settings != null)
            {
                Hint(content, "矢印で動かす向きの基準です。ふつうは「自分から見た向き」のままにします。");
                RectTransform basis = Row(content, 72);
                for (int i = 0; i < BasisNames.Length; i++)
                {
                    int captured = i;
                    RowButton(basis, "basis_" + i, BasisNames[i], ColSurface2, () => OnOffsetBasis(captured), -1);
                }
            }
            string[] axisNames = { "左右", "高さ", "奥・手前" };
            int[] order = { 0, 2, 1 };
            foreach (int axis in order)
            {
                RectTransform row = Row(content, 64);
                RowText(row, axisNames[axis], 140);
                _sliders[axis] = CreateSlider(row, axis);
                _sliderValues[axis] = RowText(row, "", 150);
                _sliderValues[axis].alignment = TextAnchor.MiddleRight;
            }
            RectTransform offsetRow = Row(content, 72);
            if (_settings != null) RowButton(offsetRow, "typical_offset", "手前に 3cm ずらす (よくあるずれ)", ColSurface2, OnTypicalOffset, -1);
            RowButton(offsetRow, "offset_reset_settings", "ずれを 0 に戻す", ColSurface2, OnResetOffset, -1);

            if (_settings != null)
            {
                // グラスの動きの向き
                Section(content, "グラスの動きの向き");
                Hint(content, "グラスを動かしたときに表示が逆向きに動くときだけ切り替えます。ふつうは変えません。");
                foreach (var row in AxisRows)
                {
                    TrackingAxis axis = row.axis;
                    RectTransform r = Row(content, 66);
                    RowText(r, row.label, -1);
                    RowButton(r, $"axis_{(int)axis}_normal", "そのまま", ColSurface2, () => OnAxis(axis, false), 190);
                    RowButton(r, $"axis_{(int)axis}_invert", "逆にする", ColSurface2, () => OnAxis(axis, true), 190);
                }

                // 認識と記録
                Section(content, "認識と記録");
                RectTransform rec = Row(content, 76);
                RowButton(rec, "recalibrate", "石の白黒の見分けをやり直す", ColSurface2, OnRecalibrate, -1);
                Hint(content, "照明が変わったときや、石の色をまちがえて認識するときに押します。");
                RectTransform perf = Row(content, 76);
                RowButton(perf, "perf_log", "動作の記録を始める", ColSurface2, OnTogglePerformanceLog, -1);
                Hint(content, "フレームレートや処理時間を CSV に残します。実験ログとは別のファイルです。");
            }

            if (MockVisible)
            {
                Section(content, "テスト用の盤 (PC で確認するとき)");
                RectTransform mock = Row(content, 72);
                RowButton(mock, "mock_place", "正しく置く", ColSurface2, MockPlaceTarget, -1);
                RowButton(mock, "mock_wrong", "違う色を置く", ColSurface2, MockPlaceWrongColor, -1);
                RowButton(mock, "mock_clear", "盤を空にする", ColSurface2, MockClear, -1);
                RowButton(mock, "mock_initial", "初期配置にする", ColSurface2, MockPlaceInitialStones, -1);
            }

            // ログ
            Section(content, "ログと詳しい状態");
            _detailText = Txt(content, "Detail", "", 22, ColMuted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
            _logSlot = Rect(content, "LogSlot", Vector2.zero, Vector2.one, new Color(0.02f, 0.025f, 0.035f, 1f));
            var logLe = _logSlot.gameObject.AddComponent<LayoutElement>();
            logLe.preferredHeight = 360;
            logLe.minHeight = 360;
            _logSlot.gameObject.AddComponent<RectMask2D>();

            RectTransform close = Row(content, 84);
            RowButton(close, "settings_close", "閉じる", ColBlue, () => SetOpen(false), -1);
        }

        private void Section(RectTransform parent, string title)
        {
            Text t = Txt(parent, "Section", title, 30, ColCyan, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, true);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 64;
        }

        private void Hint(RectTransform parent, string text)
        {
            Txt(parent, "Hint", text, 22, ColMuted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one);
        }

        private RectTransform Row(RectTransform parent, float height)
        {
            RectTransform rt = Rect(parent, "Row", Vector2.zero, Vector2.one);
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;
            return rt;
        }

        private Text RowText(RectTransform row, string text, float width)
        {
            Text t = Txt(row, "Label", text, 25, ColText, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
            var le = t.gameObject.AddComponent<LayoutElement>();
            if (width > 0) { le.preferredWidth = width; le.flexibleWidth = 0; }
            else { le.preferredWidth = 100; le.flexibleWidth = 1; }
            return t;
        }

        private void RowButton(RectTransform row, string id, string label, Color color, Action onClick, float width)
        {
            Button b = Btn(row, id, label, color, onClick, Vector2.zero, Vector2.one, 24);
            var le = b.GetComponent<LayoutElement>();
            if (width > 0) { le.preferredWidth = width; le.flexibleWidth = 0; }
            else { le.preferredWidth = 100; le.flexibleWidth = 1; }
        }

        private Slider CreateSlider(RectTransform row, int axis)
        {
            RectTransform rt = Rect(row, "Slider_" + axis, Vector2.zero, Vector2.one);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 200;
            le.flexibleWidth = 1;
            Rect(rt, "Track", new Vector2(0f, 0.42f), new Vector2(1f, 0.58f), ColSurface2);
            Rect(rt, "Center", new Vector2(0.497f, 0.25f), new Vector2(0.503f, 0.75f), ColMuted);
            RectTransform area = Rect(rt, "Handle Slide Area", Vector2.zero, Vector2.one);
            area.offsetMin = new Vector2(20, 0);
            area.offsetMax = new Vector2(-20, 0);
            RectTransform handle = Rect(area, "Handle", Vector2.zero, Vector2.one, ColCyan);
            handle.sizeDelta = new Vector2(40, 0);
            Image handleImage = handle.GetComponent<Image>();
            handleImage.sprite = Circle;
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

        private InputField CreateInputField(RectTransform parent, string placeholder, Action<string> onEndEdit)
        {
            RectTransform rt = Rect(parent, "Input_" + placeholder, Vector2.zero, Vector2.one, ColSurface2);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 100;
            le.flexibleWidth = 1;

            Text MakeText(string name, Color color, FontStyle style)
            {
                Text t = Txt(rt, name, "", 26, color, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one);
                t.rectTransform.offsetMin = new Vector2(14, 4);
                t.rectTransform.offsetMax = new Vector2(-14, -4);
                t.fontStyle = style;
                t.supportRichText = false;
                return t;
            }

            InputField f = rt.gameObject.AddComponent<InputField>();
            Text placeholderText = MakeText("Placeholder", new Color(1f, 1f, 1f, 0.35f), FontStyle.Italic);
            placeholderText.text = placeholder;
            f.placeholder = placeholderText;
            f.textComponent = MakeText("Text", ColText, FontStyle.Normal);
            f.targetGraphic = rt.GetComponent<Image>();
            f.onEndEdit.AddListener(v => onEndEdit(v));
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
            if (boardStatusText != null) { boardStatusText.fontSize = 24; boardStatusText.alignment = TextAnchor.MiddleCenter; }
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

        private RectTransform Rect(RectTransform parent, string name, Vector2 min, Vector2 max, Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Stretch(rt, min, max);
            if (color.HasValue)
            {
                Image img = go.AddComponent<Image>();
                img.color = color.Value;
            }
            return rt;
        }

        private Image Fixed(RectTransform parent, string name, Vector2 anchor, Vector2 size, Color color, Sprite sprite)
        {
            RectTransform rt = Rect(parent, name, anchor, anchor, color);
            rt.sizeDelta = size;
            Image img = rt.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
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

        private Button Btn(RectTransform parent, string id, string label, Color color, Action onClick, Vector2 min, Vector2 max, int fontSize)
        {
            RectTransform rt = Rect(parent, "Btn_" + id, min, max, color);
            Button b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = rt.GetComponent<Image>();
            ColorBlock cb = b.colors;
            cb.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            cb.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);
            b.colors = cb;
            b.onClick.AddListener(() => onClick());
            rt.gameObject.AddComponent<LayoutElement>();

            Text t = Txt(rt, "Text", label, fontSize, ColText, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, true);
            t.rectTransform.offsetMin = new Vector2(10, 4);
            t.rectTransform.offsetMax = new Vector2(-10, -4);
            Buttons[id] = b;
            _labels[id] = t;
            return b;
        }

        private static Sprite _circle, _triangle;

        private static Sprite Circle => _circle != null ? _circle : (_circle = MakeSprite(64, (x, y) =>
        {
            float dx = x - 31.5f, dy = y - 31.5f;
            return Mathf.Clamp01(31.5f - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
        }));

        /// <summary>上向きの三角形 (矢印)。回転して向きを変える。</summary>
        private static Sprite Triangle => _triangle != null ? _triangle : (_triangle = MakeSprite(64, (x, y) =>
        {
            if (y < 6 || y > 58) return 0f;
            float half = (58f - y) / 52f * 28f;
            return Mathf.Clamp01(half - Mathf.Abs(x - 31.5f) + 0.5f);
        }));

        private static Sprite MakeSprite(int n, Func<int, int, float> alpha)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(alpha(x, y) * 255f));
            tex.SetPixels32(px);
            tex.Apply();
            Sprite s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
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
