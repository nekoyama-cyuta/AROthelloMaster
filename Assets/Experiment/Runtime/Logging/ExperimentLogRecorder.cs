using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AROthello.Experiment
{
    /// <summary>試行 1 回分の書き出し内容。</summary>
    public class TrialLog
    {
        public TrialMetadata Metadata;
        public TrialResult Trial;
        /// <summary>手順ごとの平均 FPS (Trial.Steps と同じ順)</summary>
        public List<double> StepFps = new List<double>();
        /// <summary>試行全体の平均 FPS</summary>
        public double AverageFps;
        public DateTime StartedAt;
        public DateTime CompletedAt;
        /// <summary>拡張子なしのファイル名</summary>
        public string FileBaseName;
        /// <summary>ステップ CSV 全体 (ヘッダー込み)</summary>
        public string StepCsv;
        /// <summary>サマリー CSV の 1 行 (ヘッダーなし)</summary>
        public string SummaryRow;

        public string SummaryText =>
            $"[Experiment] {Trial?.Pattern?.patternId} subject={Metadata?.subjectId} condition={Metadata?.condition} order={Metadata?.order} " +
            $"TCT={ExperimentCsv.FormatNumber(Trial?.TotalTimeSec ?? 0)}s errors={Trial?.TotalErrorCount ?? 0} avgFPS={ExperimentCsv.FormatNumber(AverageFps)}";
    }

    /// <summary>TrialLog の書き出し先。</summary>
    public interface ITrialLogSink
    {
        /// <summary>書き出して、ステップ CSV のパス (またはそれに相当する識別子) を返す。失敗時は例外。</summary>
        string Write(TrialLog log);
    }

    /// <summary>
    /// ディレクトリへ書き出す。ステップ CSV は 1 試行 1 ファイル、サマリーは trial_summary.csv に 1 行ずつ追記する。
    /// Excel で文字化けしないよう UTF-8 (BOM 付き) で書く。
    /// </summary>
    public class FileTrialLogSink : ITrialLogSink
    {
        public const string SummaryFileName = "trial_summary.csv";
        private static readonly Encoding Utf8Bom = new UTF8Encoding(true);

        public string Directory { get; }

        public FileTrialLogSink(string directory)
        {
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        public string Write(TrialLog log)
        {
            System.IO.Directory.CreateDirectory(Directory);

            string path = Path.Combine(Directory, log.FileBaseName + ".csv");
            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(Directory, $"{log.FileBaseName}_{n}.csv");
            }
            File.WriteAllText(path, log.StepCsv, Utf8Bom);

            string summaryPath = Path.Combine(Directory, SummaryFileName);
            if (!File.Exists(summaryPath))
            {
                File.WriteAllText(summaryPath, ExperimentCsv.SummaryHeader + "\n", Utf8Bom);
            }
            File.AppendAllText(summaryPath, log.SummaryRow + "\n", Utf8Bom);
            return path;
        }
    }

    /// <summary>
    /// ExperimentStateMachine のイベントを購読し、試行完了時にログを書き出す (Unity に依存しない)。
    /// FPS は AddFrame でフレーム時間を受け取り、手順ごとと試行全体の平均を求める。
    /// </summary>
    public class ExperimentLogRecorder : IDisposable
    {
        private readonly ExperimentStateMachine _sm;
        private readonly ITrialLogSink _sink;
        private readonly Func<DateTime> _clock;
        private readonly FrameRateAccumulator _trialFps = new FrameRateAccumulator();
        private readonly FrameRateAccumulator _stepFps = new FrameRateAccumulator();
        private readonly List<double> _stepFpsList = new List<double>();

        private TrialMetadata _trialMetadata;
        private DateTime _startedAt;

        /// <summary>次に始まる試行に使う被験者情報 (試行開始時にコピーされる)。</summary>
        public TrialMetadata Metadata { get; set; } = new TrialMetadata();

        public string LastWrittenPath { get; private set; }
        public string LastError { get; private set; }
        public TrialLog LastLog { get; private set; }

        /// <summary>書き出しに成功した (ログ, パス)。</summary>
        public event Action<TrialLog, string> TrialLogged;
        /// <summary>書き出しに失敗した (ログ, エラーメッセージ)。</summary>
        public event Action<TrialLog, string> TrialLogFailed;

        public ExperimentLogRecorder(ExperimentStateMachine stateMachine, ITrialLogSink sink, Func<DateTime> clock = null)
        {
            _sm = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
            _sink = sink;
            _clock = clock ?? (() => DateTime.Now);

            _sm.TrialStarted += OnTrialStarted;
            _sm.StepStarted += OnStepStarted;
            _sm.StepCompleted += OnStepCompleted;
            _sm.TrialCompleted += OnTrialCompleted;
        }

        public void Dispose()
        {
            _sm.TrialStarted -= OnTrialStarted;
            _sm.StepStarted -= OnStepStarted;
            _sm.StepCompleted -= OnStepCompleted;
            _sm.TrialCompleted -= OnTrialCompleted;
        }

        /// <summary>1 フレーム分の時間 (秒) を加える。試行中のみ集計する。</summary>
        public void AddFrame(double deltaSeconds)
        {
            if (_sm.State != ExperimentState.Running) return;
            _trialFps.AddFrame(deltaSeconds);
            _stepFps.AddFrame(deltaSeconds);
        }

        private void OnTrialStarted(InstructionPatternData pattern, double time)
        {
            _trialMetadata = (Metadata ?? new TrialMetadata()).Clone();
            _startedAt = _clock();
            _trialFps.Reset();
            _stepFps.Reset();
            _stepFpsList.Clear();
        }

        private void OnStepStarted(int index, InstructionStep step)
        {
            _stepFps.Reset();
        }

        private void OnStepCompleted(StepResult result)
        {
            _stepFpsList.Add(_stepFps.AverageFps);
        }

        private void OnTrialCompleted(TrialResult trial)
        {
            TrialMetadata meta = _trialMetadata ?? (Metadata ?? new TrialMetadata()).Clone();
            DateTime completedAt = _clock();
            double avgFps = _trialFps.AverageFps;

            var log = new TrialLog
            {
                Metadata = meta,
                Trial = trial,
                StepFps = new List<double>(_stepFpsList),
                AverageFps = avgFps,
                StartedAt = _startedAt,
                CompletedAt = completedAt,
                FileBaseName = string.Join("_",
                    ExperimentCsv.SanitizeFileName(meta.subjectId, "subject"),
                    ExperimentCsv.SanitizeFileName(meta.condition, "cond"),
                    "o" + meta.order,
                    ExperimentCsv.SanitizeFileName(trial?.Pattern?.patternId, "pattern"),
                    completedAt.ToString("yyyyMMdd_HHmmss")),
                StepCsv = ExperimentCsv.BuildStepCsv(meta, trial, _stepFpsList),
                SummaryRow = ExperimentCsv.FormatSummaryRow(meta, trial, avgFps, _startedAt, completedAt)
            };
            LastLog = log;

            if (_sink == null) return;
            try
            {
                LastWrittenPath = _sink.Write(log);
                LastError = null;
                TrialLogged?.Invoke(log, LastWrittenPath);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                TrialLogFailed?.Invoke(log, ex.Message);
            }
        }
    }
}
