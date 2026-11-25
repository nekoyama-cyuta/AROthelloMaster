using UnityEngine;

namespace AROthello.Experiment
{
    /// <summary>
    /// シーン上で ExperimentStateMachine を動かすコンポーネント。
    /// 盤面の供給元 (IBoardStateProvider) から届いた盤面を状態管理へ流し、
    /// 指示書 (InstructionLoader) の現在のパターンを試行に使う。
    /// UI・ログ・実験者操作は StateMachine のイベントを購読して接続する。
    /// </summary>
    public class ExperimentController : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("指示書の読み込み元")]
        [SerializeField] private InstructionLoader instructionLoader;

        [Tooltip("IBoardStateProvider を実装したコンポーネント (未設定ならシーンから探す)")]
        [SerializeField] private MonoBehaviour boardProvider;

        [Tooltip("Unity エディタ上で実行したときに代わりに使う供給元 (モック盤面など)。未設定なら boardProvider を使う")]
        [SerializeField] private MonoBehaviour editorBoardProvider;

        [Header("Judgement")]
        [Tooltip("正解状態をこの秒数続けて検知したら手順を確定する")]
        [SerializeField, Min(0f)] private float correctDebounceSec = 0.4f;

        [Tooltip("誤り状態をこの秒数続けて検知したら誤りとして確定する")]
        [SerializeField, Min(0f)] private float errorDebounceSec = 0.4f;

        [Tooltip("初期石の無いパターンでも、開始前に盤面が空であることを確認する")]
        [SerializeField] private bool checkEmptyBoardWhenNoInitialStones = true;

        private readonly ExperimentStateMachine _stateMachine = new ExperimentStateMachine();
        private IBoardStateProvider _provider;

        public ExperimentStateMachine StateMachine => _stateMachine;
        public InstructionLoader InstructionLoader => instructionLoader;

        private void Awake()
        {
            ApplySettings();
        }

        private void OnValidate()
        {
            ApplySettings();
        }

        private void OnEnable()
        {
            _provider = Application.isEditor && editorBoardProvider is IBoardStateProvider editorProvider
                ? editorProvider
                : boardProvider as IBoardStateProvider;
            if (_provider == null)
            {
                foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (mb is IBoardStateProvider p)
                    {
                        _provider = p;
                        boardProvider = mb;
                        break;
                    }
                }
            }

            if (_provider != null) _provider.BoardUpdated += OnBoardUpdated;
            else Debug.LogWarning("[ExperimentController] No IBoardStateProvider found in the scene.");
        }

        private void OnDisable()
        {
            if (_provider != null) _provider.BoardUpdated -= OnBoardUpdated;
        }

        private void ApplySettings()
        {
            _stateMachine.CorrectDebounceSec = correctDebounceSec;
            _stateMachine.ErrorDebounceSec = errorDebounceSec;
            _stateMachine.CheckEmptyBoardWhenNoInitialStones = checkEmptyBoardWhenNoInitialStones;
        }

        private void OnBoardUpdated(BoardState board, double time)
        {
            _stateMachine.ProcessBoard(board, time);
        }

        /// <summary>InstructionLoader で選択中のパターンを試行に設定する。</summary>
        public bool LoadCurrentPattern()
        {
            InstructionPatternData p = instructionLoader != null ? instructionLoader.Library.Current : null;
            return _stateMachine.LoadPattern(p);
        }

        public bool BeginCalibration() => _stateMachine.BeginCalibration();

        public bool CompleteCalibration() => _stateMachine.CompleteCalibration(Time.realtimeSinceStartupAsDouble);

        public void ResetTrial() => _stateMachine.ResetTrial();
    }
}
