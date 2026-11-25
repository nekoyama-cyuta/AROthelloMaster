using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AROthello.Experiment
{
    /// <summary>
    /// 実験者用の軽量コントロールパネル (手元の Beam Pro 画面用)。uGUI を実行時に組み立てるので、
    /// シーンには本コンポーネントを置くだけでよい。右上の「実験」ボタンで開閉する。
    ///
    /// キーボード (PC での確認用。入力欄の編集中は無効):
    ///   F1 パネル開閉 / Enter キャリブ開始・確認 / PageUp・PageDown パターン切替 / D 四隅ドット
    ///   N 強制で次へ / B 1 手戻る / Shift+R リセット
    ///   モック盤面使用時: P 正しく置く / W 色違いで置く / E 盤を空に / I 初期石を置く
    /// </summary>
    public class ExperimentOperatorPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ExperimentController controller;
        [SerializeField] private ExperimentLogger logger;
        [SerializeField] private CalibrationDotsView calibrationDots;
        [Tooltip("IBoardOffsetAdjuster を実装したコンポーネント (任意)")]
        [SerializeField] private MonoBehaviour offsetAdjuster;
        [Tooltip("PC 確認用のモック盤面 (任意。設定するとモック操作ボタンが出る)")]
        [SerializeField] private MockBoardStateProvider mockProvider;
        [Tooltip("パネルを置く Canvas (未設定なら専用の Screen Space - Overlay Canvas を作る)")]
        [SerializeField] private Canvas targetCanvas;

        [Header("Behaviour")]
        [SerializeField] private bool startOpen = false;
        [SerializeField] private bool enableKeyboard = true;
        [Tooltip("位置オフセット 1 回分の移動量 (cm)")]
        [SerializeField, Min(0.1f)] private float nudgeStepCm = 0.5f;
        [Tooltip("試行完了ごとに順序 (order) を 1 増やす")]
        [SerializeField] private bool autoIncrementOrder = false;
        [Tooltip("リセットは 2 回押しで実行 (誤操作防止)。この秒数以内の 2 回目で実行")]
        [SerializeField, Min(0.5f)] private float resetConfirmSeconds = 2.5f;

        private ExperimentOperator _operator;
        private IBoardOffsetAdjuster _adjuster;
        private Font _font;
        private GameObject _panel;
        private Button _toggleButton;
        private Text _statusText, _patternText, _primaryLabel, _offsetText, _resetLabel;
        private InputField _subjectField, _conditionField, _orderField;
        private float _resetArmedUntil = -1f;
        private float _statusTimer;

        public ExperimentOperator Operator => _operator;
        public bool IsOpen => _panel != null && _panel.activeSelf;
        /// <summary>組み立てたボタン (ラベル → Button)。テスト・自動操作用。</summary>
        public Dictionary<string, Button> Buttons { get; } = new Dictionary<string, Button>();

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

        /// <summary>操作ロジックを受け取り UI を組み立てる (Start から呼ばれる。テスト用に公開)。</summary>
        public void Initialize(ExperimentOperator op, Canvas canvas = null)
        {
            _operator = op ?? throw new ArgumentNullException(nameof(op));
            _operator.AutoIncrementOrder = autoIncrementOrder;
            _operator.ActionLogged += msg => Debug.Log($"[Experiment] {msg}");
            _adjuster = offsetAdjuster as IBoardOffsetAdjuster;
            if (canvas != null) targetCanvas = canvas;
            BuildUi();
            SetOpen(startOpen);
            RefreshAll();
        }

        private void Update()
        {
            if (_operator == null) return;
            if (enableKeyboard) HandleKeyboard();

            if (_resetArmedUntil > 0 && Time.unscaledTime > _resetArmedUntil)
            {
                _resetArmedUntil = -1f;
                if (_resetLabel != null) _resetLabel.text = "リセット";
            }

            _statusTimer -= Time.unscaledDeltaTime;
            if (_statusTimer <= 0f)
            {
                _statusTimer = 0.2f;
                RefreshStatus();
            }
        }

        // ---------------- 操作 ----------------

        public void SetOpen(bool open)
        {
            if (_panel != null) _panel.SetActive(open);
        }

        public void TogglePanel() => SetOpen(!IsOpen);

        public void OnPrimary() { _operator.PrimaryAction(); RefreshAll(); }
        public void OnForceAdvance() { _operator.ForceAdvance(); RefreshAll(); }
        public void OnStepBack() { _operator.StepBack(); RefreshAll(); }
        public void OnPrevPattern() { _operator.SelectPreviousPattern(); RefreshAll(); }
        public void OnNextPattern() { _operator.SelectNextPattern(); RefreshAll(); }
        public void OnToggleDots() { if (calibrationDots != null) calibrationDots.Toggle(); RefreshAll(); }

        /// <summary>1 回目で確認待ち、resetConfirmSeconds 以内の 2 回目でリセット。</summary>
        public void OnReset()
        {
            if (_resetArmedUntil > 0 && Time.unscaledTime <= _resetArmedUntil)
            {
                _resetArmedUntil = -1f;
                _operator.ResetTrial();
                if (_resetLabel != null) _resetLabel.text = "リセット";
            }
            else
            {
                _resetArmedUntil = Time.unscaledTime + resetConfirmSeconds;
                if (_resetLabel != null) _resetLabel.text = "もう一度で確定";
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
            if (Input.GetKeyDown(KeyCode.R) && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                _operator.ResetTrial();
                RefreshAll();
            }

            if (mockProvider != null)
            {
                if (Input.GetKeyDown(KeyCode.P)) MockPlaceTarget();
                if (Input.GetKeyDown(KeyCode.W)) MockPlaceWrongColor();
                if (Input.GetKeyDown(KeyCode.E)) MockClear();
                if (Input.GetKeyDown(KeyCode.I)) MockPlaceInitialStones();
            }
        }

        // ---------------- 表示更新 ----------------

        public void RefreshAll()
        {
            if (_operator == null) return;
            RefreshStatus();
            InstructionPatternData p = _operator.Library.Current;
            if (_patternText != null) _patternText.text = p != null ? p.ToString() : "(パターンなし)";
            if (_primaryLabel != null) _primaryLabel.text = _operator.PrimaryActionLabel;
            if (_offsetText != null)
            {
                _offsetText.text = _adjuster != null
                    ? $"{_adjuster.ModeName}  X {_adjuster.CurrentOffsetCm.x:+0.0;-0.0;0.0}  Y {_adjuster.CurrentOffsetCm.y:+0.0;-0.0;0.0}  Z {_adjuster.CurrentOffsetCm.z:+0.0;-0.0;0.0} cm"
                    : "(位置オフセット未接続)";
            }
            if (_subjectField != null && !_subjectField.isFocused) _subjectField.text = _operator.Metadata.subjectId;
            if (_conditionField != null && !_conditionField.isFocused) _conditionField.text = _operator.Metadata.condition;
            if (_orderField != null && !_orderField.isFocused) _orderField.text = _operator.Metadata.order.ToString();
        }

        private void RefreshStatus()
        {
            if (_statusText == null || _operator == null) return;
            string dots = calibrationDots != null && calibrationDots.Visible ? "  [四隅ドット表示中]" : "";
            _statusText.text = _operator.BuildStatusText() + dots;
            if (_primaryLabel != null) _primaryLabel.text = _operator.PrimaryActionLabel;
        }

        // ---------------- UI 組み立て ----------------

        private void BuildUi()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Buttons.Clear();

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

            Transform root = targetCanvas.transform;
            Transform old = root.Find("Experiment_Operator");
            if (old != null) DestroyObj(old.gameObject);
            var holder = new GameObject("Experiment_Operator", typeof(RectTransform));
            holder.transform.SetParent(root, false);
            Stretch(holder.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);

            // 開閉ボタン (右上)
            _toggleButton = CreateButton(holder.transform, "実験", TogglePanel, new Color(0.55f, 0.15f, 0.65f, 0.95f), out _);
            Stretch(_toggleButton.GetComponent<RectTransform>(), new Vector2(0.86f, 0.885f), new Vector2(0.995f, 0.935f));
            _toggleButton.GetComponent<LayoutElement>().ignoreLayout = true;

            // パネル本体
            _panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            _panel.transform.SetParent(holder.transform, false);
            _panel.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.08f, 0.96f);
            Stretch(_panel.GetComponent<RectTransform>(), new Vector2(0.03f, 0.12f), new Vector2(0.97f, 0.875f));
            var vlg = _panel.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(16, 16, 16, 16);
            vlg.spacing = 10;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            Transform p = _panel.transform;

            CreateLabel(p, "実験コントロール", 26, FontStyle.Bold, new Color(0.85f, 0.6f, 1f), 40);

            Transform rowMeta = CreateRow(p, 64);
            CreateLabel(rowMeta, "被験者", 20, FontStyle.Normal, Color.white, 0, 90);
            _subjectField = CreateInputField(rowMeta, "S01", v => _operator.SetSubjectId(v));
            CreateLabel(rowMeta, "条件", 20, FontStyle.Normal, Color.white, 0, 70);
            _conditionField = CreateInputField(rowMeta, "AR", v => _operator.SetCondition(v));
            CreateLabel(rowMeta, "順序", 20, FontStyle.Normal, Color.white, 0, 70);
            _orderField = CreateInputField(rowMeta, "1", v => { if (!_operator.SetOrder(v)) RefreshAll(); });
            _orderField.contentType = InputField.ContentType.IntegerNumber;

            Transform rowPattern = CreateRow(p, 64);
            CreateButton(rowPattern, "<", OnPrevPattern, new Color(0.25f, 0.27f, 0.32f, 0.95f), out _, 90);
            _patternText = CreateLabel(rowPattern, "", 22, FontStyle.Bold, new Color(1f, 0.92f, 0.5f), 0);
            _patternText.alignment = TextAnchor.MiddleCenter;
            CreateButton(rowPattern, ">", OnNextPattern, new Color(0.25f, 0.27f, 0.32f, 0.95f), out _, 90);

            Transform rowFlow = CreateRow(p, 80);
            CreateButton(rowFlow, "キャリブ開始", OnPrimary, new Color(0.1f, 0.55f, 0.3f, 0.95f), out _primaryLabel);
            CreateButton(rowFlow, "四隅ドット", OnToggleDots, new Color(0.55f, 0.15f, 0.65f, 0.95f), out _);

            Transform rowOffset = CreateRow(p, 64);
            CreateButton(rowOffset, "X-", () => OnNudge(0, -1), new Color(0.2f, 0.3f, 0.45f, 0.95f), out _);
            CreateButton(rowOffset, "X+", () => OnNudge(0, +1), new Color(0.2f, 0.3f, 0.45f, 0.95f), out _);
            CreateButton(rowOffset, "Y-", () => OnNudge(1, -1), new Color(0.2f, 0.3f, 0.45f, 0.95f), out _);
            CreateButton(rowOffset, "Y+", () => OnNudge(1, +1), new Color(0.2f, 0.3f, 0.45f, 0.95f), out _);
            CreateButton(rowOffset, "Z-", () => OnNudge(2, -1), new Color(0.2f, 0.3f, 0.45f, 0.95f), out _);
            CreateButton(rowOffset, "Z+", () => OnNudge(2, +1), new Color(0.2f, 0.3f, 0.45f, 0.95f), out _);
            CreateButton(rowOffset, "0cm", OnResetOffset, new Color(0.45f, 0.15f, 0.15f, 0.95f), out _);
            _offsetText = CreateLabel(p, "", 18, FontStyle.Normal, new Color(0.7f, 0.85f, 1f), 30);

            CreateLabel(p, "手動オーバーライド", 20, FontStyle.Bold, new Color(1f, 0.55f, 0.4f), 32);
            Transform rowOverride = CreateRow(p, 90);
            CreateButton(rowOverride, "強制で次へ", OnForceAdvance, new Color(0.85f, 0.45f, 0.05f, 0.95f), out _);
            CreateButton(rowOverride, "1手戻る", OnStepBack, new Color(0.3f, 0.35f, 0.6f, 0.95f), out _);
            CreateButton(rowOverride, "リセット", OnReset, new Color(0.7f, 0.12f, 0.15f, 0.95f), out _resetLabel);

            if (mockProvider != null)
            {
                CreateLabel(p, "モック盤面 (PC確認用)", 18, FontStyle.Bold, new Color(0.6f, 0.9f, 0.6f), 28);
                Transform rowMock = CreateRow(p, 64);
                CreateButton(rowMock, "正しく置く", MockPlaceTarget, new Color(0.15f, 0.4f, 0.2f, 0.95f), out _);
                CreateButton(rowMock, "色違い", MockPlaceWrongColor, new Color(0.45f, 0.2f, 0.15f, 0.95f), out _);
                CreateButton(rowMock, "盤を空に", MockClear, new Color(0.25f, 0.27f, 0.32f, 0.95f), out _);
                CreateButton(rowMock, "初期石", MockPlaceInitialStones, new Color(0.25f, 0.27f, 0.32f, 0.95f), out _);
            }

            _statusText = CreateLabel(p, "", 20, FontStyle.Normal, new Color(0.9f, 0.95f, 1f), 0);
            _statusText.alignment = TextAnchor.UpperLeft;
            _statusText.GetComponent<LayoutElement>().flexibleHeight = 1;
        }

        private Transform CreateRow(Transform parent, float height)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            var h = go.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 8;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            go.GetComponent<LayoutElement>().preferredHeight = height;
            return go.transform;
        }

        private Text CreateLabel(Transform parent, string text, int size, FontStyle style, Color color, float height, float width = -1)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            Text t = go.GetComponent<Text>();
            t.font = _font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var le = go.GetComponent<LayoutElement>();
            if (height > 0) le.preferredHeight = height;
            if (width > 0) { le.preferredWidth = width; le.flexibleWidth = 0; }
            else le.flexibleWidth = 1;
            return t;
        }

        private Button CreateButton(Transform parent, string label, Action onClick, Color color, out Text labelText, float width = -1)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            Button b = go.GetComponent<Button>();
            b.onClick.AddListener(() => onClick());
            var le = go.GetComponent<LayoutElement>();
            if (width > 0) { le.preferredWidth = width; le.flexibleWidth = 0; }
            else le.flexibleWidth = 1;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            Stretch(textGo.GetComponent<RectTransform>(), Vector2.zero, Vector2.one);
            labelText = textGo.GetComponent<Text>();
            labelText.font = _font;
            labelText.text = label;
            labelText.fontSize = 22;
            labelText.fontStyle = FontStyle.Bold;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = Color.white;

            Buttons[label] = b;
            return b;
        }

        private InputField CreateInputField(Transform parent, string placeholder, Action<string> onEndEdit)
        {
            var go = new GameObject("Input_" + placeholder, typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.15f, 0.17f, 0.22f, 1f);
            go.GetComponent<LayoutElement>().flexibleWidth = 1;

            Text MakeText(string name, Color color, FontStyle style)
            {
                var tgo = new GameObject(name, typeof(RectTransform), typeof(Text));
                tgo.transform.SetParent(go.transform, false);
                RectTransform rt = tgo.GetComponent<RectTransform>();
                Stretch(rt, Vector2.zero, Vector2.one);
                rt.offsetMin = new Vector2(10, 4);
                rt.offsetMax = new Vector2(-10, -4);
                Text t = tgo.GetComponent<Text>();
                t.font = _font;
                t.fontSize = 22;
                t.color = color;
                t.fontStyle = style;
                t.alignment = TextAnchor.MiddleLeft;
                t.supportRichText = false;
                return t;
            }

            InputField f = go.GetComponent<InputField>();
            Text placeholderText = MakeText("Placeholder", new Color(1f, 1f, 1f, 0.35f), FontStyle.Italic);
            placeholderText.text = placeholder;
            f.placeholder = placeholderText;
            f.textComponent = MakeText("Text", Color.white, FontStyle.Normal);
            f.onEndEdit.AddListener(v => onEndEdit(v));
            return f;
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
