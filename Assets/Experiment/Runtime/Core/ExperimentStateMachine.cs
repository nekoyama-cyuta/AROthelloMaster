using System;
using System.Collections.Generic;

namespace AROthello.Experiment
{
    /// <summary>
    /// 実験の状態管理・正誤判定・デバウンス (Unity に依存しない純粋なロジック)。
    ///
    /// 状態遷移: Idle → (BeginCalibration) → Calibration → (CompleteCalibration) → InitialBoardCheck
    ///           → (開始前の盤面が正しい状態でデバウンス時間続く) → Running → (全手順確定) → Completed
    ///
    /// 判定の考え方:
    /// - 「期待盤面」= 初期石 + 確定済みの手順の石。現在の手順で置くべきマスだけが空。
    /// - 認識した盤面と期待盤面の差分が「指定マスに指定色」の 1 マスだけなら正解候補。
    /// - 差分が無ければ待機中。それ以外は誤り (誤った色・指定外のマス・石の消失・石の色の変化)。
    /// - 正解候補がデバウンス時間 (既定 0.4 秒) 途切れずに続いたら手順を確定する。
    ///   所要時間には確定時刻ではなく、最初に正解候補を検知した時刻 (T_first_detect) を使う。
    /// - 誤りもデバウンス時間続いたら確定し、ErrorDetected を通知して誤り回数に数える。
    ///   誤りが残っている間は正解候補にならないので、次の手順へは進まない。
    /// </summary>
    public class ExperimentStateMachine
    {
        public const double DefaultDebounceSec = 0.4;
        private const double TimeEpsilon = 1e-6;

        private InstructionPatternData _pattern;
        private ExperimentState _state = ExperimentState.Idle;

        private readonly BoardState _initialBoard = new BoardState();
        private readonly BoardState _expectedBoard = new BoardState();
        private readonly List<StepResult> _results = new List<StepResult>();
        private readonly List<PlacementErrorType> _currentStepErrorTypes = new List<PlacementErrorType>();
        private readonly List<CellMismatch> _mismatchBuffer = new List<CellMismatch>();

        private int _stepIndex;
        private int _currentStepErrorCount;
        private int _totalErrorCount;
        private double _trialStartTime;
        private double _lastFirstDetectTime;
        private double _guideShownTime;

        private double? _pendingCorrectSince;
        private double? _pendingErrorSince;
        private PlacementError _activeError;

        private bool _initialCheckOverridden;
        private string _currentStepNote = "";

        private bool? _lastInitialOk;
        private int _lastInitialMismatchCount = -1;
        private List<CellMismatch> _lastInitialMismatches = new List<CellMismatch>();

        // ---------------- 設定 ----------------

        /// <summary>正解状態をこの秒数続けて検知したら手順を確定する。</summary>
        public double CorrectDebounceSec { get; set; } = DefaultDebounceSec;

        /// <summary>誤り状態をこの秒数続けて検知したら誤りとして確定する。</summary>
        public double ErrorDebounceSec { get; set; } = DefaultDebounceSec;

        /// <summary>
        /// 初期石が無いパターン (Lv.1/2) でも、開始前に盤面が空であることを確認するか。
        /// false の場合、初期石が無ければ CompleteCalibration で直ちに Running になる。
        /// </summary>
        public bool CheckEmptyBoardWhenNoInitialStones { get; set; } = true;

        // ---------------- イベント ----------------

        public event Action<ExperimentState, ExperimentState> StateChanged;
        public event Action<InstructionPatternData> PatternLoaded;
        /// <summary>開始前チェックの結果が変わったとき (正否、または食い違うマスの数)。</summary>
        public event Action<bool, IReadOnlyList<CellMismatch>> InitialBoardStatusChanged;
        /// <summary>試行開始 (パターン, 開始時刻)。</summary>
        public event Action<InstructionPatternData, double> TrialStarted;
        /// <summary>手順のガイド表示開始 (手順インデックス, 手順)。</summary>
        public event Action<int, InstructionStep> StepStarted;
        /// <summary>手順確定。FirstDetectTime (T_first_detect) と所要時間を含む。</summary>
        public event Action<StepResult> StepCompleted;
        public event Action<PlacementError> ErrorDetected;
        /// <summary>確定していた誤りが解消された (手順インデックス)。</summary>
        public event Action<int> ErrorCleared;
        public event Action<TrialResult> TrialCompleted;
        /// <summary>StepBack で取り消された手順の結果。</summary>
        public event Action<StepResult> StepReverted;
        /// <summary>試行中 (Calibration 以降) に ResetTrial された。</summary>
        public event Action TrialReset;

        // ---------------- 状態 ----------------

        public ExperimentState State => _state;
        public InstructionPatternData Pattern => _pattern;
        /// <summary>現在の手順インデックス (0 始まり)。Completed では手順数と同じ。</summary>
        public int CurrentStepIndex => _stepIndex;
        /// <summary>Running 中の現在の手順。それ以外は null。</summary>
        public InstructionStep CurrentStep =>
            _state == ExperimentState.Running && _pattern != null && _stepIndex < _pattern.steps.Length ? _pattern.steps[_stepIndex] : null;
        public int StepTotal => _pattern != null ? _pattern.steps.Length : 0;
        public IReadOnlyList<StepResult> CompletedSteps => _results;
        public int TotalErrorCount => _totalErrorCount;
        public int CurrentStepErrorCount => _currentStepErrorCount;
        public bool IsErrorActive => _activeError != null;
        public PlacementError ActiveError => _activeError;
        public double TrialStartTime => _trialStartTime;
        /// <summary>正解候補を検知中ならその最初の時刻。</summary>
        public double? PendingCorrectSince => _pendingCorrectSince;
        /// <summary>直近の開始前チェックで食い違ったマス。</summary>
        public IReadOnlyList<CellMismatch> LastInitialMismatches => _lastInitialMismatches;

        /// <summary>現在の期待盤面 (初期石 + 確定済みの手順) のコピー。</summary>
        public BoardState GetExpectedBoard()
        {
            return _state == ExperimentState.Running || _state == ExperimentState.Completed ? _expectedBoard.Clone() : _initialBoard.Clone();
        }

        // ---------------- 操作 ----------------

        /// <summary>パターンを設定する。Idle / Completed のときのみ可能。不正なパターンは拒否する。</summary>
        public bool LoadPattern(InstructionPatternData pattern)
        {
            if (_state != ExperimentState.Idle && _state != ExperimentState.Completed) return false;
            if (pattern == null || InstructionValidator.Validate(pattern).Count > 0) return false;

            _pattern = pattern;
            _initialBoard.Clear();
            foreach (InstructionStone s in pattern.initialStones)
            {
                BoardCoord.TryParseColor(s.color, out StoneColor c);
                _initialBoard.Set(s.col, s.row, c);
            }
            ClearProgress();
            SetState(ExperimentState.Idle);
            PatternLoaded?.Invoke(pattern);
            return true;
        }

        public bool BeginCalibration()
        {
            if (_state != ExperimentState.Idle || _pattern == null) return false;
            SetState(ExperimentState.Calibration);
            return true;
        }

        /// <summary>キャリブレーションを終えて開始前チェックへ (初期石が無く空盤面チェックも不要なら直ちに試行開始)。</summary>
        public bool CompleteCalibration(double time)
        {
            if (_state != ExperimentState.Calibration) return false;
            if (!_pattern.HasInitialStones && !CheckEmptyBoardWhenNoInitialStones)
            {
                StartRunning(time);
            }
            else
            {
                ResetInitialCheckStatus();
                SetState(ExperimentState.InitialBoardCheck);
            }
            return true;
        }

        /// <summary>試行を破棄して Idle に戻る (パターンは保持)。</summary>
        public void ResetTrial()
        {
            bool wasActive = _state != ExperimentState.Idle;
            ClearProgress();
            SetState(ExperimentState.Idle);
            if (wasActive) TrialReset?.Invoke();
        }

        // ---------------- 手動オーバーライド (実験者用の非常口) ----------------

        /// <summary>
        /// 認識が詰まったときなどに、実験者が強制的に先へ進める。
        /// - Running: 現在の手順を「置けた」ものとして確定する (IsOverride = true)。
        ///   正解候補を検知中ならその時刻を、そうでなければ time を T_first_detect とする。
        /// - InitialBoardCheck: 開始前チェックを省略して試行を始める (TrialResult.InitialCheckOverridden = true)。
        /// - Calibration: キャリブレーションを終える (CompleteCalibration と同じ)。
        /// </summary>
        public bool ForceAdvance(double time, string note = DefaultOverrideNote)
        {
            switch (_state)
            {
                case ExperimentState.Calibration:
                    return CompleteCalibration(time);

                case ExperimentState.InitialBoardCheck:
                    _initialCheckOverridden = true;
                    StartRunning(time);
                    return true;

                case ExperimentState.Running:
                    double firstDetect = _pendingCorrectSince ?? time;
                    if (_activeError != null)
                    {
                        _activeError = null;
                        ErrorCleared?.Invoke(_stepIndex);
                    }
                    ConfirmStep(firstDetect, time, true, note);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 直前に確定した手順を取り消し、その手順からやり直す (Running 中、2 手目以降のみ)。
        /// 取り消した手順の誤りは、やり直す手順に引き継ぐ。所要時間はさらに 1 つ前の手順の T_first_detect から測り直す。
        /// </summary>
        public bool StepBack(double time)
        {
            if (_state != ExperimentState.Running || _results.Count == 0) return false;

            StepResult undone = _results[_results.Count - 1];
            _results.RemoveAt(_results.Count - 1);
            _stepIndex = undone.StepIndex;

            InstructionStep step = _pattern.steps[_stepIndex];
            _expectedBoard.Set(step.col, step.row, _initialBoard.Get(step.col, step.row));
            _lastFirstDetectTime = _results.Count > 0 ? _results[_results.Count - 1].FirstDetectTime : _trialStartTime;
            _guideShownTime = time;

            ResetStepTracking();
            _currentStepErrorCount = undone.ErrorCount;
            _currentStepErrorTypes.AddRange(undone.ErrorTypes);
            _currentStepNote = ExperimentCsv.JoinNotes(undone.Note, StepBackNote);

            StepReverted?.Invoke(undone);
            StepStarted?.Invoke(_stepIndex, step);
            return true;
        }

        public const string DefaultOverrideNote = "manual override";
        public const string StepBackNote = "redo after step back";

        /// <summary>認識した盤面を 1 フレーム分処理する。time は秒 (単調増加)。</summary>
        public void ProcessBoard(BoardState board, double time)
        {
            if (board == null || _pattern == null) return;
            switch (_state)
            {
                case ExperimentState.InitialBoardCheck:
                    ProcessInitialCheck(board, time);
                    break;
                case ExperimentState.Running:
                    ProcessRunning(board, time);
                    break;
            }
        }

        // ---------------- 内部処理 ----------------

        private void ProcessInitialCheck(BoardState board, double time)
        {
            CollectMismatches(_initialBoard, board, _mismatchBuffer);
            bool ok = _mismatchBuffer.Count == 0;

            if (_lastInitialOk != ok || _lastInitialMismatchCount != _mismatchBuffer.Count)
            {
                _lastInitialOk = ok;
                _lastInitialMismatchCount = _mismatchBuffer.Count;
                _lastInitialMismatches = new List<CellMismatch>(_mismatchBuffer);
                InitialBoardStatusChanged?.Invoke(ok, _lastInitialMismatches);
            }

            if (!ok)
            {
                _pendingCorrectSince = null;
                return;
            }

            if (!_pendingCorrectSince.HasValue) _pendingCorrectSince = time;
            if (time - _pendingCorrectSince.Value >= CorrectDebounceSec - TimeEpsilon)
            {
                StartRunning(time);
            }
        }

        private void StartRunning(double time)
        {
            _expectedBoard.CopyFrom(_initialBoard);
            _results.Clear();
            _stepIndex = 0;
            _totalErrorCount = 0;
            _trialStartTime = time;
            _lastFirstDetectTime = time;
            _guideShownTime = time;
            ResetStepTracking();

            SetState(ExperimentState.Running);
            TrialStarted?.Invoke(_pattern, time);
            StepStarted?.Invoke(_stepIndex, _pattern.steps[_stepIndex]);
        }

        private void ProcessRunning(BoardState board, double time)
        {
            InstructionStep target = _pattern.steps[_stepIndex];
            BoardCoord.TryParseColor(target.color, out StoneColor targetColor);

            CollectMismatches(_expectedBoard, board, _mismatchBuffer);

            bool isWaiting = _mismatchBuffer.Count == 0;
            bool isCorrect = _mismatchBuffer.Count == 1 &&
                             _mismatchBuffer[0].Col == target.col &&
                             _mismatchBuffer[0].Row == target.row &&
                             _mismatchBuffer[0].Actual == targetColor;

            if (isCorrect)
            {
                _pendingErrorSince = null;
                ClearActiveError();
                if (!_pendingCorrectSince.HasValue) _pendingCorrectSince = time;
                if (time - _pendingCorrectSince.Value >= CorrectDebounceSec - TimeEpsilon)
                {
                    ConfirmStep(_pendingCorrectSince.Value, time, false, null);
                }
                return;
            }

            _pendingCorrectSince = null;

            if (isWaiting)
            {
                _pendingErrorSince = null;
                ClearActiveError();
                return;
            }

            // 誤り (確定済みの誤りが続いている間は数え直さない)
            if (_activeError != null) return;
            if (!_pendingErrorSince.HasValue) _pendingErrorSince = time;
            if (time - _pendingErrorSince.Value >= ErrorDebounceSec - TimeEpsilon)
            {
                PlacementErrorType type = Classify(_mismatchBuffer, target.col, target.row, targetColor);
                _activeError = new PlacementError
                {
                    StepIndex = _stepIndex,
                    Type = type,
                    FirstDetectTime = _pendingErrorSince.Value,
                    ConfirmTime = time,
                    Mismatches = new List<CellMismatch>(_mismatchBuffer)
                };
                _pendingErrorSince = null;
                _currentStepErrorCount++;
                _totalErrorCount++;
                _currentStepErrorTypes.Add(type);
                ErrorDetected?.Invoke(_activeError);
            }
        }

        /// <summary>現在の手順を確定して次へ進む。</summary>
        private void ConfirmStep(double firstDetectTime, double confirmTime, bool isOverride, string overrideNote)
        {
            InstructionStep step = _pattern.steps[_stepIndex];
            BoardCoord.TryParseColor(step.color, out StoneColor color);

            var result = new StepResult
            {
                StepIndex = _stepIndex,
                Step = step,
                GuideShownTime = _guideShownTime,
                FirstDetectTime = firstDetectTime,
                ConfirmTime = confirmTime,
                StepTimeSec = firstDetectTime - _lastFirstDetectTime,
                ErrorCount = _currentStepErrorCount,
                ErrorTypes = new List<PlacementErrorType>(_currentStepErrorTypes),
                IsOverride = isOverride,
                Note = ExperimentCsv.JoinNotes(_currentStepNote, isOverride ? overrideNote : null)
            };

            _results.Add(result);
            _expectedBoard.Set(step.col, step.row, color);
            _lastFirstDetectTime = firstDetectTime;
            _guideShownTime = confirmTime;
            _stepIndex++;
            ResetStepTracking();

            StepCompleted?.Invoke(result);

            if (_stepIndex >= _pattern.steps.Length)
            {
                var trial = new TrialResult
                {
                    Pattern = _pattern,
                    StartTime = _trialStartTime,
                    EndTime = firstDetectTime,
                    TotalTimeSec = firstDetectTime - _trialStartTime,
                    TotalErrorCount = _totalErrorCount,
                    InitialCheckOverridden = _initialCheckOverridden,
                    Steps = new List<StepResult>(_results)
                };
                SetState(ExperimentState.Completed);
                TrialCompleted?.Invoke(trial);
            }
            else
            {
                StepStarted?.Invoke(_stepIndex, _pattern.steps[_stepIndex]);
            }
        }

        /// <summary>
        /// 差分から代表的な誤りの種類を決める。優先順: WrongColor > WrongPosition > MissingStone > ChangedStone。
        /// 目標マスに正しい色が置かれていても、他のマスに差分があればそちらで分類する。
        /// </summary>
        private static PlacementErrorType Classify(List<CellMismatch> mismatches, int targetCol, int targetRow, StoneColor targetColor)
        {
            bool wrongColor = false, wrongPosition = false, missing = false, changed = false;
            foreach (CellMismatch m in mismatches)
            {
                if (m.Col == targetCol && m.Row == targetRow)
                {
                    if (m.Actual != StoneColor.None && m.Actual != targetColor) wrongColor = true;
                    continue;
                }
                if (m.Expected == StoneColor.None) wrongPosition = true;
                else if (m.Actual == StoneColor.None) missing = true;
                else changed = true;
            }

            if (wrongColor) return PlacementErrorType.WrongColor;
            if (wrongPosition) return PlacementErrorType.WrongPosition;
            if (missing) return PlacementErrorType.MissingStone;
            if (changed) return PlacementErrorType.ChangedStone;
            return PlacementErrorType.None;
        }

        private static void CollectMismatches(BoardState expected, BoardState actual, List<CellMismatch> output)
        {
            output.Clear();
            for (int r = 0; r < BoardState.Size; r++)
            {
                for (int c = 0; c < BoardState.Size; c++)
                {
                    StoneColor e = expected.Get(c, r);
                    StoneColor a = actual.Get(c, r);
                    if (e != a) output.Add(new CellMismatch { Col = c, Row = r, Expected = e, Actual = a });
                }
            }
        }

        private void ClearActiveError()
        {
            if (_activeError == null) return;
            int idx = _activeError.StepIndex;
            _activeError = null;
            ErrorCleared?.Invoke(idx);
        }

        private void ResetStepTracking()
        {
            _pendingCorrectSince = null;
            _pendingErrorSince = null;
            _activeError = null;
            _currentStepErrorCount = 0;
            _currentStepErrorTypes.Clear();
            _currentStepNote = "";
        }

        private void ResetInitialCheckStatus()
        {
            _pendingCorrectSince = null;
            _lastInitialOk = null;
            _lastInitialMismatchCount = -1;
            _lastInitialMismatches = new List<CellMismatch>();
        }

        private void ClearProgress()
        {
            _expectedBoard.Clear();
            _results.Clear();
            _stepIndex = 0;
            _totalErrorCount = 0;
            _trialStartTime = 0;
            _lastFirstDetectTime = 0;
            _guideShownTime = 0;
            _initialCheckOverridden = false;
            ResetStepTracking();
            ResetInitialCheckStatus();
        }

        private void SetState(ExperimentState next)
        {
            if (_state == next) return;
            ExperimentState prev = _state;
            _state = next;
            StateChanged?.Invoke(prev, next);
        }
    }
}
