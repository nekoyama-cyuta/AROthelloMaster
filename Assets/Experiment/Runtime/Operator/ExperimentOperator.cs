using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AROthello.Experiment
{
    /// <summary>
    /// 盤の位置ずれを手で直すためのインターフェース。実機では既存の OthelloAutoTracker の
    /// 位置オフセット補正 (±6cm) に接続したアダプターが実装する。
    /// </summary>
    public interface IBoardOffsetAdjuster
    {
        /// <summary>現在のオフセット座標系の名前</summary>
        string ModeName { get; }
        /// <summary>現在のオフセット (cm)</summary>
        Vector3 CurrentOffsetCm { get; }
        /// <summary>axis (0 = X, 1 = Y, 2 = Z) を deltaCm だけずらす。</summary>
        void Nudge(int axis, float deltaCm);
        /// <summary>現在の座標系のオフセットを 0 に戻す。</summary>
        void ResetOffset();
    }

    /// <summary>実験者画面で見せる手順 (状態管理の状態 + 試行後の評価・アンケート)。</summary>
    public enum OperatorStep
    {
        Idle,
        Calibration,
        InitialBoardCheck,
        /// <summary>実験ID (被験者・試行番号・条件を決める)</summary>
        Setup,
        Running,
        Completed,
        /// <summary>成功 / 失敗とメモ</summary>
        Evaluation,
        /// <summary>直後アンケート</summary>
        Survey
    }

    /// <summary>
    /// 実験者の操作 (被験者の選択・試行番号・パターン選択・試行の進行・手動オーバーライド・評価・アンケート) をまとめたもの。
    /// UI (ボタン・キーボード) から呼ばれる。Unity の時刻には依存せず、clock で現在時刻を受け取る。
    ///
    /// 手順: 待機 → 位置合わせ → 初期配置 → 実験ID → 試行 → 完了 → 評価 → アンケート → (次の試行の) 初期配置 …
    /// </summary>
    public class ExperimentOperator : IDisposable
    {
        private enum PostPhase { None, Evaluation, Survey }

        private readonly ExperimentStateMachine _sm;
        private readonly InstructionLibrary _library;
        private readonly Func<double> _clock;
        private TrialResult _lastTrial;
        private TrialMetadata _lastTrialMetadata;

        private PostPhase _phase = PostPhase.None;
        private TrialEvaluation _evaluation = new TrialEvaluation();
        private int _surveyIndex;
        private int? _answer;
        private double _questionShownAt;

        /// <summary>被験者情報 (ExperimentLogger と同じインスタンスを渡すと、そのままログに使われる)。</summary>
        public TrialMetadata Metadata { get; }

        /// <summary>試行が完了するたびに order を 1 増やす (実験ID の手順を使うときは、そこで記録から数え直す)。</summary>
        public bool AutoIncrementOrder { get; set; }

        /// <summary>被験者一覧・試行回数・評価とアンケートの書き出し先 (任意。無ければ書き出さない)。</summary>
        public ExperimentRecordStore Store { get; set; }

        /// <summary>試行ログの書き出し (任意。評価に試行開始時の被験者情報と完了時刻を使う)。</summary>
        public ExperimentLogRecorder Recorder { get; set; }

        /// <summary>グラス画面の録画 (任意)。評価に録画のファイル名を書く。</summary>
        public ExperimentRecordingController Recording { get; set; }

        /// <summary>直後アンケートの定義。質問が無ければ評価のあと直ちに次の試行へ進む。</summary>
        public QuestionnaireDefinition Questionnaire { get; set; } = QuestionnaireDefinition.Default;

        /// <summary>壁時計 (CSV の日時用)。</summary>
        public Func<DateTime> WallClock { get; set; } = () => DateTime.Now;

        /// <summary>直近の書き出しエラー (無ければ null)。</summary>
        public string LastRecordError { get; private set; }

        /// <summary>実験者の操作を記録したいとき用 (画面ログなど)。</summary>
        public event Action<string> ActionLogged;

        public ExperimentStateMachine StateMachine => _sm;
        public InstructionLibrary Library => _library;
        public TrialResult LastTrial => _lastTrial;
        /// <summary>直前の試行を始めたときの被験者情報 (実験ID など)。</summary>
        public TrialMetadata LastTrialMetadata => _lastTrialMetadata ?? Metadata;

        public ExperimentOperator(ExperimentStateMachine stateMachine, InstructionLibrary library, TrialMetadata metadata, Func<double> clock)
        {
            _sm = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _library = library ?? new InstructionLibrary();
            Metadata = metadata ?? new TrialMetadata();
            _clock = clock ?? (() => 0);
            _sm.TrialCompleted += OnTrialCompleted;
            _sm.TrialStarted += OnTrialStarted;
            _sm.StateChanged += OnStateChanged;
        }

        public void Dispose()
        {
            _sm.TrialCompleted -= OnTrialCompleted;
            _sm.TrialStarted -= OnTrialStarted;
            _sm.StateChanged -= OnStateChanged;
        }

        /// <summary>試行中 (Calibration / InitialBoardCheck / Setup / Running) か。</summary>
        public bool IsTrialActive =>
            _sm.State == ExperimentState.Calibration ||
            _sm.State == ExperimentState.InitialBoardCheck ||
            _sm.State == ExperimentState.Setup ||
            _sm.State == ExperimentState.Running;

        /// <summary>パターンを変えられないとき (位置合わせ中・試行中)。初期配置と実験ID の間は変えると初期配置から確認し直す。</summary>
        public bool IsPatternLocked => _sm.State == ExperimentState.Calibration || _sm.State == ExperimentState.Running;

        /// <summary>いまの手順。</summary>
        public OperatorStep Step
        {
            get
            {
                switch (_sm.State)
                {
                    case ExperimentState.Calibration: return OperatorStep.Calibration;
                    case ExperimentState.InitialBoardCheck: return OperatorStep.InitialBoardCheck;
                    case ExperimentState.Setup: return OperatorStep.Setup;
                    case ExperimentState.Running: return OperatorStep.Running;
                    case ExperimentState.Completed:
                        return _phase == PostPhase.Evaluation ? OperatorStep.Evaluation
                            : _phase == PostPhase.Survey ? OperatorStep.Survey
                            : OperatorStep.Completed;
                    default: return OperatorStep.Idle;
                }
            }
        }

        // ---------------- 被験者情報 ----------------

        public void SetSubjectId(string id)
        {
            Metadata.subjectId = id?.Trim() ?? "";
            SubjectInfo s = Store?.Subjects.Find(Metadata.subjectId);
            Metadata.subjectName = s != null ? s.Name : "";
            UpdateExperimentId();
            Log($"subject = {Metadata.subjectId}");
        }

        public void SetCondition(string condition)
        {
            Metadata.condition = condition?.Trim() ?? "";
            UpdateExperimentId();
            Log($"condition = {Metadata.condition}");
        }

        public void SetNote(string note) { Metadata.note = note ?? ""; }

        public bool SetOrder(int order)
        {
            if (order < 1) return false;
            Metadata.order = order;
            UpdateExperimentId();
            Log($"trial no = {order}");
            return true;
        }

        public bool SetOrder(string text)
        {
            return int.TryParse(text?.Trim(), out int v) && SetOrder(v);
        }

        /// <summary>試行番号を手で直す (練習やミスのとき)。1 より小さくはしない。</summary>
        public void AdjustTrialNo(int delta) => SetOrder(Math.Max(1, Metadata.order + delta));

        /// <summary>被験者・条件・試行番号から作る ID (例: S01-AR-T03)。</summary>
        public static string BuildExperimentId(string subjectId, string condition, int trialNo)
        {
            if (string.IsNullOrWhiteSpace(subjectId)) return "";
            string Clean(string s) => (s ?? "").Trim().Replace(' ', '_').Replace(',', '_').Replace('-', '_');
            string cond = string.IsNullOrWhiteSpace(condition) ? "NA" : Clean(condition);
            return $"{Clean(subjectId)}-{cond}-T{trialNo:00}";
        }

        private void UpdateExperimentId()
        {
            Metadata.experimentId = BuildExperimentId(Metadata.subjectId, Metadata.condition, Metadata.order);
        }

        /// <summary>いま選んでいる被験者 (一覧に無い ID なら null)。</summary>
        public SubjectInfo SelectedSubject => string.IsNullOrEmpty(Metadata.subjectId) ? null : Store?.Subjects.Find(Metadata.subjectId);

        /// <summary>その被験者のこれまでの試行の数 (記録が無ければ 0)。</summary>
        public int CountTrials(string subjectId) => Store != null ? Store.CountTrials(subjectId) : 0;

        /// <summary>被験者を選び、試行番号を記録から数え直す (これまでの試行数 + 1)。</summary>
        public bool SelectSubject(string subjectId)
        {
            if (string.IsNullOrWhiteSpace(subjectId)) return false;
            SubjectInfo s = Store?.Subjects.Find(subjectId.Trim());
            if (Store != null && (s == null || s.Deleted)) return false;
            Metadata.subjectId = subjectId.Trim();
            Metadata.subjectName = s != null ? s.Name : "";
            Metadata.order = CountTrials(Metadata.subjectId) + 1;
            UpdateExperimentId();
            Log($"subject = {Metadata.subjectId} (trial {Metadata.order})");
            return true;
        }

        public SubjectInfo AddSubject(string name)
        {
            SubjectInfo s = Store?.Subjects.Add(name);
            if (s == null) return null;
            Log($"subject added: {s.Id}");
            SelectSubject(s.Id);
            return s;
        }

        public bool RenameSubject(string subjectId, string name)
        {
            if (Store == null || !Store.Subjects.Rename(subjectId, name)) return false;
            if (Metadata.subjectId == subjectId) Metadata.subjectName = Store.Subjects.Find(subjectId).Name;
            Log($"subject renamed: {subjectId}");
            return true;
        }

        public bool UpdateSubject(SubjectInfo info)
        {
            if (Store == null || !Store.Subjects.Update(info)) return false;
            if (Metadata.subjectId == info.Id) Metadata.subjectName = Store.Subjects.Find(info.Id).Name;
            return true;
        }

        /// <summary>被験者を一覧から隠す (CSV の記録は残す)。選んでいた人なら選択を外す。</summary>
        public bool DeleteSubject(string subjectId)
        {
            if (Store == null || !Store.Subjects.Delete(subjectId)) return false;
            if (Metadata.subjectId == subjectId)
            {
                Metadata.subjectId = "";
                Metadata.subjectName = "";
                UpdateExperimentId();
            }
            Log($"subject deleted (hidden): {subjectId}");
            return true;
        }

        public List<TrialHistoryEntry> ListTrials(string subjectId) => Store != null ? Store.ListTrials(subjectId) : new List<TrialHistoryEntry>();

        // ---------------- パターン選択 ----------------

        /// <summary>選択中のパターンを状態管理へ読み込む (試行中は不可)。</summary>
        public bool LoadSelectedPattern()
        {
            if (IsTrialActive || _library.Current == null) return false;
            if (_sm.Pattern == _library.Current && _sm.State == ExperimentState.Idle) return true;
            bool ok = _sm.LoadPattern(_library.Current);
            if (ok) Log($"pattern = {_library.Current}");
            return ok;
        }

        public bool SelectPattern(string patternId)
        {
            if (IsPatternLocked) return false;
            return _library.Select(patternId) && ApplySelectedPattern();
        }

        public bool SelectNextPattern()
        {
            if (IsPatternLocked || _library.Count == 0) return false;
            _library.SelectNext();
            return ApplySelectedPattern();
        }

        public bool SelectPreviousPattern()
        {
            if (IsPatternLocked || _library.Count == 0) return false;
            _library.SelectPrevious();
            return ApplySelectedPattern();
        }

        /// <summary>初期配置・実験ID の間にパターンを変えたら、位置合わせは済んだものとして新しいパターンの初期配置から確認し直す。</summary>
        private bool ApplySelectedPattern()
        {
            if (_sm.State != ExperimentState.InitialBoardCheck && _sm.State != ExperimentState.Setup) return LoadSelectedPattern();
            _sm.ResetTrial();
            if (!_sm.LoadPattern(_library.Current)) return false;
            _sm.BeginCalibration();
            _sm.CompleteCalibration(_clock());
            Log($"pattern = {_library.Current} (recheck initial board)");
            return true;
        }

        // ---------------- 試行の進行 ----------------

        /// <summary>位置合わせを始める (完了後なら同じパターンで次の試行を準備する)。</summary>
        public bool StartCalibration()
        {
            if (_sm.State == ExperimentState.Completed || _sm.Pattern == null)
            {
                if (_library.Current == null || !_sm.LoadPattern(_library.Current)) return false;
            }
            bool ok = _sm.BeginCalibration();
            if (ok) Log("calibration started");
            return ok;
        }

        /// <summary>位置合わせを終えて初期配置の確認へ。</summary>
        public bool ConfirmCalibration()
        {
            bool ok = _sm.CompleteCalibration(_clock());
            if (ok) Log("calibration confirmed");
            return ok;
        }

        /// <summary>実験ID の手順で、被験者を選んでいれば試行を始められる。</summary>
        public bool CanStartTrial => _sm.State == ExperimentState.Setup && !string.IsNullOrWhiteSpace(Metadata.subjectId);

        /// <summary>実験ID の手順から試行を始める。</summary>
        public bool StartTrial()
        {
            if (!CanStartTrial) return false;
            UpdateExperimentId();
            bool ok = _sm.StartTrial(_clock());
            if (ok) Log($"trial started: {Metadata.experimentId}");
            return ok;
        }

        /// <summary>
        /// 次の試行へ: 同じパターン (または選び直したパターン) を読み込み、位置合わせは済んだものとして初期配置の確認から始める。
        /// </summary>
        public bool StartNextTrial()
        {
            if (_sm.State != ExperimentState.Completed && _sm.State != ExperimentState.Idle) return false;
            _phase = PostPhase.None;
            if (_library.Current == null || !_sm.LoadPattern(_library.Current)) return false;
            _sm.BeginCalibration();
            bool ok = _sm.CompleteCalibration(_clock());
            if (ok) Log("next trial: initial board check");
            return ok;
        }

        /// <summary>状況に応じた「次へ」 (画面のメインボタン)。</summary>
        public bool PrimaryAction()
        {
            switch (Step)
            {
                case OperatorStep.Idle: return StartCalibration();
                case OperatorStep.Calibration: return ConfirmCalibration();
                case OperatorStep.Setup: return StartTrial();
                case OperatorStep.Completed: return BeginEvaluation();
                case OperatorStep.Evaluation: return RecordEvaluation();
                case OperatorStep.Survey: return RecordAnswer();
                default: return false;
            }
        }

        public string PrimaryActionLabel
        {
            get
            {
                switch (Step)
                {
                    case OperatorStep.Idle: return "位置合わせを始める";
                    case OperatorStep.Calibration: return "位置OK・初期配置へ";
                    case OperatorStep.Setup: return "試行を始める";
                    case OperatorStep.Completed: return "評価へ";
                    case OperatorStep.Evaluation: return "記録してアンケートへ";
                    case OperatorStep.Survey:
                        return _surveyIndex + 1 < Questionnaire.Count ? "回答を記録して次の質問へ" : "回答を記録して次の試行へ";
                    default: return "(試行中)";
                }
            }
        }

        /// <summary>メインボタンを押せるか (実験ID は被験者、評価は成否、アンケートは回答を選ぶまで押せない)。</summary>
        public bool CanPrimaryAction
        {
            get
            {
                switch (Step)
                {
                    case OperatorStep.Idle: return _library.Current != null || _sm.Pattern != null;
                    case OperatorStep.Calibration: return true;
                    case OperatorStep.Setup: return CanStartTrial;
                    case OperatorStep.Completed: return true;
                    case OperatorStep.Evaluation: return _evaluation.Success.HasValue;
                    case OperatorStep.Survey: return _answer.HasValue;
                    default: return false;
                }
            }
        }

        // ---------------- 評価 ----------------

        public TrialEvaluation Evaluation => _evaluation;

        /// <summary>完了から評価へ進む。</summary>
        public bool BeginEvaluation()
        {
            if (_sm.State != ExperimentState.Completed || _phase != PostPhase.None) return false;
            _phase = PostPhase.Evaluation;
            _evaluation = new TrialEvaluation();
            Log("evaluation");
            return true;
        }

        public void SetResult(bool success)
        {
            if (Step != OperatorStep.Evaluation) return;
            _evaluation.Success = success;
            if (success) _evaluation.FailReason = "";
        }

        /// <summary>失敗の理由を選ぶ。同じ理由をもう一度選ぶと外す。</summary>
        public void SetFailReason(string reason)
        {
            if (Step != OperatorStep.Evaluation) return;
            reason = reason ?? "";
            _evaluation.FailReason = _evaluation.FailReason == reason ? "" : reason;
            if (_evaluation.FailReason.Length > 0) _evaluation.Success = false;
        }

        public void SetMemo(string memo)
        {
            if (Step != OperatorStep.Evaluation) return;
            _evaluation.Memo = memo ?? "";
        }

        /// <summary>成否・理由・メモを evaluations.csv に記録して、アンケートへ (質問が無ければ次の試行へ)。</summary>
        public bool RecordEvaluation()
        {
            if (Step != OperatorStep.Evaluation || !_evaluation.Success.HasValue) return false;
            _evaluation.EvaluatedAt = WallClock();
            if (Store != null)
            {
                TrialLog log = Recorder?.LastLog;
                string video = Recording != null ? Recording.VideoFileFor(LastTrialMetadata.experimentId) : "";
                bool ok = Store.WriteEvaluation(LastTrialMetadata, _lastTrial, _evaluation, log != null ? log.CompletedAt : _evaluation.EvaluatedAt, video);
                LastRecordError = ok ? null : Store.LastError;
            }
            Log($"evaluation recorded: {_evaluation.ResultCode}");

            if (Questionnaire != null && Questionnaire.Count > 0)
            {
                _phase = PostPhase.Survey;
                _surveyIndex = 0;
                _answer = null;
                _questionShownAt = _clock();
            }
            else
            {
                StartNextTrial();
            }
            return true;
        }

        // ---------------- アンケート ----------------

        public int SurveyIndex => _surveyIndex;
        public QuestionnaireQuestion CurrentQuestion =>
            Step == OperatorStep.Survey && Questionnaire != null && _surveyIndex < Questionnaire.Count ? Questionnaire.questions[_surveyIndex] : null;
        /// <summary>選んだ回答 (まだ選んでいなければ null)。</summary>
        public int? CurrentAnswer => _answer;

        public void SetAnswer(int value)
        {
            QuestionnaireQuestion q = CurrentQuestion;
            if (q == null || value < q.min || value > q.Max) return;
            _answer = value;
        }

        /// <summary>回答を questionnaire.csv に記録して、次の質問へ (最後なら次の試行の初期配置へ)。</summary>
        public bool RecordAnswer()
        {
            QuestionnaireQuestion q = CurrentQuestion;
            if (q == null || !_answer.HasValue) return false;
            var answer = new QuestionnaireAnswer
            {
                QuestionId = q.id,
                Value = _answer.Value,
                Label = q.LabelOf(_answer.Value),
                AnswerTimeSec = Math.Max(0, _clock() - _questionShownAt),
                AnsweredAt = WallClock()
            };
            if (Store != null)
            {
                bool ok = Store.WriteAnswer(LastTrialMetadata, _lastTrial?.Pattern, answer);
                LastRecordError = ok ? null : Store.LastError;
            }
            Log($"answer {q.id} = {answer.Value}");

            _surveyIndex++;
            _answer = null;
            _questionShownAt = _clock();
            if (_surveyIndex >= Questionnaire.Count) StartNextTrial();
            return true;
        }

        // ---------------- 手動オーバーライド ----------------

        /// <summary>強制的に次へ進める (試行中の手順は is_override = 1 で記録される)。</summary>
        public bool ForceAdvance(string note = ExperimentStateMachine.DefaultOverrideNote)
        {
            ExperimentState before = _sm.State;
            if (before == ExperimentState.Setup) return StartTrial();
            int step = _sm.CurrentStepIndex + 1;
            bool ok = _sm.ForceAdvance(_clock(), note);
            if (ok) Log(before == ExperimentState.Running ? $"OVERRIDE: step {step} forced" : $"OVERRIDE: {before} skipped");
            return ok;
        }

        /// <summary>1 手戻る。</summary>
        public bool StepBack()
        {
            bool ok = _sm.StepBack(_clock());
            if (ok) Log($"step back -> step {_sm.CurrentStepIndex + 1}");
            return ok;
        }

        /// <summary>現在の試行を破棄して待機に戻る (評価・アンケートの途中なら、それも打ち切る)。</summary>
        public void ResetTrial()
        {
            bool active = IsTrialActive || _phase != PostPhase.None;
            _phase = PostPhase.None;
            _sm.ResetTrial();
            if (active) Log("trial reset");
        }

        // ---------------- 表示 ----------------

        /// <summary>実験者向けの状況表示 (複数行)。</summary>
        public string BuildStatusText()
        {
            var sb = new StringBuilder();
            sb.Append($"実験ID {Show(Metadata.experimentId)} / 被験者 {Show(Metadata.subjectId)} / 条件 {Show(Metadata.condition)} / 試行 {Metadata.order}\n");
            InstructionPatternData selected = _library.Current;
            sb.Append($"パターン {(selected != null ? selected.ToString() : "(なし)")}");
            if (_library.Count > 0) sb.Append($"  [{IndexOf(selected) + 1}/{_library.Count}]");
            sb.Append('\n');
            sb.Append($"状態 {Step}");

            switch (_sm.State)
            {
                case ExperimentState.InitialBoardCheck:
                    sb.Append($"  食い違い {_sm.LastInitialMismatches.Count} マス");
                    break;
                case ExperimentState.Running:
                    InstructionStep s = _sm.CurrentStep;
                    sb.Append($"  手順 {_sm.CurrentStepIndex + 1}/{_sm.StepTotal}");
                    if (s != null) sb.Append($" {BoardCoord.ToCoord(s.col, s.row)} {s.color}");
                    sb.Append($"  誤り {_sm.TotalErrorCount}");
                    if (_sm.IsErrorActive) sb.Append($" ({_sm.ActiveError.Type})");
                    break;
            }

            if (_lastTrial != null)
            {
                int overrides = 0;
                foreach (StepResult r in _lastTrial.Steps) if (r.IsOverride) overrides++;
                sb.Append($"\n前回 {LastTrialMetadata.experimentId} {_lastTrial.Pattern?.patternId}  TCT {ExperimentCsv.FormatNumber(_lastTrial.TotalTimeSec)}s  誤り {_lastTrial.TotalErrorCount}  強制 {overrides}");
            }
            if (!string.IsNullOrEmpty(LastRecordError)) sb.Append($"\n記録エラー: {LastRecordError}");
            return sb.ToString();
        }

        private int IndexOf(InstructionPatternData p)
        {
            for (int i = 0; i < _library.Patterns.Count; i++) if (_library.Patterns[i] == p) return i;
            return -1;
        }

        private static string Show(string s) => string.IsNullOrEmpty(s) ? "-" : s;

        private void OnTrialStarted(InstructionPatternData pattern, double time)
        {
            _lastTrialMetadata = Metadata.Clone();
        }

        private void OnStateChanged(ExperimentState prev, ExperimentState next)
        {
            if (next != ExperimentState.Completed) _phase = PostPhase.None;
            // 実験ID の手順に入るたびに、選んでいる被験者の試行番号を記録から数え直す
            if (next == ExperimentState.Setup && Store != null && !string.IsNullOrEmpty(Metadata.subjectId))
            {
                Metadata.order = CountTrials(Metadata.subjectId) + 1;
                UpdateExperimentId();
            }
        }

        private void OnTrialCompleted(TrialResult trial)
        {
            _lastTrial = trial;
            Log($"trial completed: TCT {ExperimentCsv.FormatNumber(trial.TotalTimeSec)}s, errors {trial.TotalErrorCount}");
            if (AutoIncrementOrder)
            {
                Metadata.order++;
                UpdateExperimentId();
            }
        }

        private void Log(string message)
        {
            ActionLogged?.Invoke(message);
        }
    }
}
