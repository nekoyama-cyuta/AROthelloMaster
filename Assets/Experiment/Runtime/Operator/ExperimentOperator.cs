using System;
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

    /// <summary>
    /// 実験者の操作 (被験者情報・パターン選択・試行の開始・手動オーバーライド) をまとめたもの。
    /// UI (ボタン・キーボード) から呼ばれる。Unity の時刻には依存せず、clock で現在時刻を受け取る。
    /// </summary>
    public class ExperimentOperator : IDisposable
    {
        private readonly ExperimentStateMachine _sm;
        private readonly InstructionLibrary _library;
        private readonly Func<double> _clock;
        private TrialResult _lastTrial;

        /// <summary>被験者情報 (ExperimentLogger と同じインスタンスを渡すと、そのままログに使われる)。</summary>
        public TrialMetadata Metadata { get; }

        /// <summary>試行が完了するたびに order を 1 増やす。</summary>
        public bool AutoIncrementOrder { get; set; }

        /// <summary>実験者の操作を記録したいとき用 (画面ログなど)。</summary>
        public event Action<string> ActionLogged;

        public ExperimentStateMachine StateMachine => _sm;
        public InstructionLibrary Library => _library;
        public TrialResult LastTrial => _lastTrial;

        public ExperimentOperator(ExperimentStateMachine stateMachine, InstructionLibrary library, TrialMetadata metadata, Func<double> clock)
        {
            _sm = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _library = library ?? new InstructionLibrary();
            Metadata = metadata ?? new TrialMetadata();
            _clock = clock ?? (() => 0);
            _sm.TrialCompleted += OnTrialCompleted;
        }

        public void Dispose()
        {
            _sm.TrialCompleted -= OnTrialCompleted;
        }

        /// <summary>試行中 (Calibration / InitialBoardCheck / Running) か。この間はパターンを変えられない。</summary>
        public bool IsTrialActive =>
            _sm.State == ExperimentState.Calibration ||
            _sm.State == ExperimentState.InitialBoardCheck ||
            _sm.State == ExperimentState.Running;

        // ---------------- 被験者情報 ----------------

        public void SetSubjectId(string id) { Metadata.subjectId = id?.Trim() ?? ""; Log($"subject = {Metadata.subjectId}"); }
        public void SetCondition(string condition) { Metadata.condition = condition?.Trim() ?? ""; Log($"condition = {Metadata.condition}"); }
        public void SetNote(string note) { Metadata.note = note ?? ""; }

        public bool SetOrder(int order)
        {
            if (order < 1) return false;
            Metadata.order = order;
            Log($"order = {order}");
            return true;
        }

        public bool SetOrder(string text)
        {
            return int.TryParse(text?.Trim(), out int v) && SetOrder(v);
        }

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
            if (IsTrialActive) return false;
            return _library.Select(patternId) && LoadSelectedPattern();
        }

        public bool SelectNextPattern()
        {
            if (IsTrialActive || _library.Count == 0) return false;
            _library.SelectNext();
            return LoadSelectedPattern();
        }

        public bool SelectPreviousPattern()
        {
            if (IsTrialActive || _library.Count == 0) return false;
            _library.SelectPrevious();
            return LoadSelectedPattern();
        }

        // ---------------- 試行の進行 ----------------

        /// <summary>キャリブレーション確認を始める (完了後なら同じパターンで次の試行を準備する)。</summary>
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

        /// <summary>キャリブレーションを終えて開始前チェックへ。</summary>
        public bool ConfirmCalibration()
        {
            bool ok = _sm.CompleteCalibration(_clock());
            if (ok) Log("calibration confirmed");
            return ok;
        }

        /// <summary>
        /// 状況に応じた「次へ」: Idle / Completed → キャリブレーション開始、Calibration → 確認。
        /// 開始前チェック・試行中は何もしない (強制的に進めるのは ForceAdvance)。
        /// </summary>
        public bool PrimaryAction()
        {
            switch (_sm.State)
            {
                case ExperimentState.Idle:
                case ExperimentState.Completed:
                    return StartCalibration();
                case ExperimentState.Calibration:
                    return ConfirmCalibration();
                default:
                    return false;
            }
        }

        public string PrimaryActionLabel
        {
            get
            {
                switch (_sm.State)
                {
                    case ExperimentState.Idle: return "キャリブ開始";
                    case ExperimentState.Calibration: return "位置OK→開始";
                    case ExperimentState.Completed: return "次の試行";
                    default: return "(試行中)";
                }
            }
        }

        // ---------------- 手動オーバーライド ----------------

        /// <summary>強制的に次へ進める (試行中の手順は is_override = 1 で記録される)。</summary>
        public bool ForceAdvance(string note = ExperimentStateMachine.DefaultOverrideNote)
        {
            ExperimentState before = _sm.State;
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

        /// <summary>現在の試行を破棄して Idle に戻る (ログは書き出さない)。</summary>
        public void ResetTrial()
        {
            bool active = IsTrialActive;
            _sm.ResetTrial();
            if (active) Log("trial reset");
        }

        // ---------------- 表示 ----------------

        /// <summary>実験者向けの状況表示 (複数行)。</summary>
        public string BuildStatusText()
        {
            var sb = new StringBuilder();
            sb.Append($"被験者 {Show(Metadata.subjectId)} / 条件 {Show(Metadata.condition)} / 順序 {Metadata.order}\n");
            InstructionPatternData selected = _library.Current;
            sb.Append($"パターン {(selected != null ? selected.ToString() : "(なし)")}");
            if (_library.Count > 0) sb.Append($"  [{IndexOf(selected) + 1}/{_library.Count}]");
            sb.Append('\n');
            sb.Append($"状態 {_sm.State}");

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
                sb.Append($"\n前回 {_lastTrial.Pattern?.patternId}  TCT {ExperimentCsv.FormatNumber(_lastTrial.TotalTimeSec)}s  誤り {_lastTrial.TotalErrorCount}  強制 {overrides}");
            }
            return sb.ToString();
        }

        private int IndexOf(InstructionPatternData p)
        {
            for (int i = 0; i < _library.Patterns.Count; i++) if (_library.Patterns[i] == p) return i;
            return -1;
        }

        private static string Show(string s) => string.IsNullOrEmpty(s) ? "-" : s;

        private void OnTrialCompleted(TrialResult trial)
        {
            _lastTrial = trial;
            Log($"trial completed: TCT {ExperimentCsv.FormatNumber(trial.TotalTimeSec)}s, errors {trial.TotalErrorCount}");
            if (AutoIncrementOrder) Metadata.order++;
        }

        private void Log(string message)
        {
            ActionLogged?.Invoke(message);
        }
    }
}
