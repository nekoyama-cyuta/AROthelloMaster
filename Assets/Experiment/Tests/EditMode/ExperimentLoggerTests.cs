using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NUnit.Framework;

namespace AROthello.Experiment.Tests
{
    /// <summary>Phase 4: 実験ログ (CSV) の記録と書き出し。</summary>
    public class ExperimentLoggerTests
    {
        private const double Dt = 0.1;          // 認識フレーム間隔
        private const double FrameDt = 1.0 / 60; // 描画フレーム間隔 (60 fps)

        private class MemorySink : ITrialLogSink
        {
            public readonly List<TrialLog> Logs = new List<TrialLog>();
            public string Write(TrialLog log) { Logs.Add(log); return "memory://" + log.FileBaseName; }
        }

        private class FailingSink : ITrialLogSink
        {
            public string Write(TrialLog log) => throw new IOException("disk full");
        }

        private ExperimentStateMachine _sm;
        private BoardState _board;
        private MemorySink _sink;
        private ExperimentLogRecorder _recorder;
        private DateTime _now;
        private string _tempDir;

        [SetUp]
        public void SetUp()
        {
            _sm = new ExperimentStateMachine();
            _board = new BoardState();
            _sink = new MemorySink();
            _now = new DateTime(2026, 11, 26, 10, 0, 0);
            _recorder = new ExperimentLogRecorder(_sm, _sink, () => _now)
            {
                Metadata = new TrialMetadata { subjectId = "S01", condition = "AR", order = 2, note = "" }
            };
        }

        [TearDown]
        public void TearDown()
        {
            _recorder.Dispose();
            if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }

        // ---------------- ヘルパー ----------------

        private static InstructionPatternData Pattern()
        {
            return new InstructionPatternData
            {
                patternId = "L1-03-LOG",
                difficulty = 1,
                stepCount = 3,
                steps = new[]
                {
                    new InstructionStep { step = 1, col = 3, row = 2, coord = "D3", color = "B" },
                    new InstructionStep { step = 2, col = 2, row = 3, coord = "C4", color = "W" },
                    new InstructionStep { step = 3, col = 5, row = 4, coord = "F5", color = "B" }
                }
            };
        }

        /// <summary>認識フレームの合間に 60 fps の描画フレームを流す。</summary>
        private void Feed(double from, double to)
        {
            int n = (int)Math.Round((to - from) / Dt);
            for (int i = 0; i <= n; i++)
            {
                _sm.ProcessBoard(_board, from + i * Dt);
                for (int f = 0; f < 6; f++) _recorder.AddFrame(FrameDt);
            }
        }

        /// <summary>
        /// 開始 0.4 → D3 (1.0 初検知, 途中で指定外の誤り 1 回) → C4 (2.0) → F5 (3.5) で完走。
        /// 期待される step_time: 0.60, 1.00, 1.50 / TCT 3.10
        /// </summary>
        private void RunDummyTrial()
        {
            Assert.IsTrue(_sm.LoadPattern(Pattern()));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 0.4);
            Assert.AreEqual(ExperimentState.Running, _sm.State);

            _board.Set(0, 7, StoneColor.White);  // 指定外 (A8) → 誤り
            Feed(0.5, 0.9);
            _board.Set(0, 7, StoneColor.None);
            _board.Set(3, 2, StoneColor.Black);
            Feed(1.0, 1.4);
            _board.Set(2, 3, StoneColor.White);
            Feed(2.0, 2.4);
            _board.Set(5, 4, StoneColor.Black);
            _now = _now.AddSeconds(4);
            Feed(3.5, 3.9);
            Assert.AreEqual(ExperimentState.Completed, _sm.State);
        }

        /// <summary>引用符を考慮して CSV の 1 行を分割する。</summary>
        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else if (c == '"') quoted = false;
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }

        private static string[] Lines(string csv) => csv.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        private static Dictionary<string, string> Row(string header, string line)
        {
            List<string> h = SplitCsv(header), v = SplitCsv(line);
            Assert.AreEqual(h.Count, v.Count, $"column count mismatch: {line}");
            return h.Zip(v, (k, x) => new KeyValuePair<string, string>(k, x)).ToDictionary(p => p.Key, p => p.Value);
        }

        // ---------------- ダミー試行の完走 ----------------

        [Test]
        public void DummyTrial_ProducesCsvWithSpecHeaderAndOneRowPerStep()
        {
            RunDummyTrial();

            TrialLog log = _sink.Logs.Single();
            string[] lines = Lines(log.StepCsv);

            Assert.AreEqual(
                "subject_id,condition,order,pattern_id,difficulty,step_count,step,target_coord,target_color,step_time_sec,error_type,is_override,avg_fps,note,experiment_id",
                lines[0]);
            Assert.AreEqual(1 + 3, lines.Length, "header + 3 steps");

            var rows = lines.Skip(1).Select(l => Row(lines[0], l)).ToList();
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, rows.Select(r => r["step"]));
            CollectionAssert.AreEqual(new[] { "D3", "C4", "F5" }, rows.Select(r => r["target_coord"]));
            CollectionAssert.AreEqual(new[] { "B", "W", "B" }, rows.Select(r => r["target_color"]));
            CollectionAssert.AreEqual(new[] { "0.60", "1.00", "1.50" }, rows.Select(r => r["step_time_sec"]), "T_first_detect based");
            CollectionAssert.AreEqual(new[] { "wrong_position", "none", "none" }, rows.Select(r => r["error_type"]));
            CollectionAssert.AreEqual(new[] { "0", "0", "0" }, rows.Select(r => r["is_override"]));

            foreach (var r in rows)
            {
                Assert.AreEqual("S01", r["subject_id"]);
                Assert.AreEqual("AR", r["condition"]);
                Assert.AreEqual("2", r["order"]);
                Assert.AreEqual("L1-03-LOG", r["pattern_id"]);
                Assert.AreEqual("1", r["difficulty"]);
                Assert.AreEqual("3", r["step_count"]);
                Assert.AreEqual("60.00", r["avg_fps"]);
                Assert.AreEqual("", r["note"]);
            }
        }

        [Test]
        public void DummyTrial_NumbersHaveExactlyTwoDecimals()
        {
            RunDummyTrial();
            string[] lines = Lines(_sink.Logs.Single().StepCsv);
            var twoDecimals = new Regex(@"^-?\d+\.\d{2}$");
            foreach (string line in lines.Skip(1))
            {
                var r = Row(lines[0], line);
                StringAssert.IsMatch(twoDecimals.ToString(), r["step_time_sec"]);
                StringAssert.IsMatch(twoDecimals.ToString(), r["avg_fps"]);
            }
        }

        [Test]
        public void DummyTrial_SummaryContainsTctErrorsAndAverageFps()
        {
            RunDummyTrial();
            TrialLog log = _sink.Logs.Single();

            var r = Row(ExperimentCsv.SummaryHeader, log.SummaryRow);
            Assert.AreEqual("3.10", r["tct_sec"]);
            Assert.AreEqual("1", r["total_errors"]);
            Assert.AreEqual("0", r["override_count"]);
            Assert.AreEqual("60.00", r["avg_fps"]);
            Assert.AreEqual("2026-11-26T10:00:00", r["started_at"]);
            Assert.AreEqual("2026-11-26T10:00:04", r["completed_at"]);
            StringAssert.Contains("TCT=3.10s", log.SummaryText);
            StringAssert.Contains("avgFPS=60.00", log.SummaryText);
            Assert.AreEqual("S01_AR_o2_L1-03-LOG_20261126_100004", log.FileBaseName);
            Assert.AreEqual("memory://S01_AR_o2_L1-03-LOG_20261126_100004", _recorder.LastWrittenPath);
        }

        [Test]
        public void Fps_IsMeasuredPerStep()
        {
            Assert.IsTrue(_sm.LoadPattern(Pattern()));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 0.4);

            _board.Set(3, 2, StoneColor.Black);
            for (int i = 0; i < 30; i++) _recorder.AddFrame(1.0 / 30);   // 1 手目は 30 fps
            Feed(1.0, 1.4);                                              // (Feed 自体も 60 fps を加える)
            _board.Set(2, 3, StoneColor.White);
            Feed(2.0, 2.4);
            _board.Set(5, 4, StoneColor.Black);
            Feed(3.0, 3.4);

            List<double> fps = _sink.Logs.Single().StepFps;
            Assert.AreEqual(3, fps.Count);
            Assert.Less(fps[0], 59.0, "step 1 includes the 30 fps frames");
            Assert.AreEqual(60.0, fps[1], 1e-6);
            Assert.AreEqual(60.0, fps[2], 1e-6);
        }

        [Test]
        public void MetadataIsSnapshottedAtTrialStart()
        {
            Assert.IsTrue(_sm.LoadPattern(Pattern()));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 0.4);

            _recorder.Metadata.subjectId = "CHANGED"; // 試行中に書き換えても
            _recorder.Metadata = new TrialMetadata { subjectId = "S99" };

            _board.Set(3, 2, StoneColor.Black); Feed(1.0, 1.4);
            _board.Set(2, 3, StoneColor.White); Feed(2.0, 2.4);
            _board.Set(5, 4, StoneColor.Black); Feed(3.0, 3.4);

            Assert.AreEqual("S01", _sink.Logs.Single().Metadata.subjectId, "metadata is cloned at trial start; later edits are ignored");
            StringAssert.StartsWith("S01_", _sink.Logs.Single().FileBaseName);
        }

        [Test]
        public void ResetMidTrial_WritesNothing_AndNextTrialIsClean()
        {
            Assert.IsTrue(_sm.LoadPattern(Pattern()));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 0.4);
            _board.Set(3, 2, StoneColor.Black);
            Feed(1.0, 1.4);
            _sm.ResetTrial();
            Assert.AreEqual(0, _sink.Logs.Count);

            _board.Clear();
            RunDummyTrial();
            Assert.AreEqual(1, _sink.Logs.Count);
            Assert.AreEqual(3, _sink.Logs[0].StepFps.Count);
        }

        [Test]
        public void NoFrames_AverageFpsIsZero()
        {
            var recorder = new ExperimentLogRecorder(_sm, _sink, () => _now);
            _recorder.Dispose();
            try
            {
                Assert.IsTrue(_sm.LoadPattern(Pattern()));
                _sm.BeginCalibration();
                _sm.CompleteCalibration(0);
                for (double t = 0; t <= 0.41; t += Dt) _sm.ProcessBoard(_board, t);
                _board.Set(3, 2, StoneColor.Black); for (double t = 1.0; t <= 1.41; t += Dt) _sm.ProcessBoard(_board, t);
                _board.Set(2, 3, StoneColor.White); for (double t = 2.0; t <= 2.41; t += Dt) _sm.ProcessBoard(_board, t);
                _board.Set(5, 4, StoneColor.Black); for (double t = 3.0; t <= 3.41; t += Dt) _sm.ProcessBoard(_board, t);

                TrialLog log = _sink.Logs.Single();
                Assert.AreEqual(0.0, log.AverageFps);
                StringAssert.Contains(",0.00,", Lines(log.StepCsv)[1]);
            }
            finally
            {
                recorder.Dispose();
            }
        }

        [Test]
        public void SinkFailure_IsReportedWithoutThrowing()
        {
            _recorder.Dispose();
            var recorder = new ExperimentLogRecorder(_sm, new FailingSink(), () => _now);
            string error = null;
            recorder.TrialLogFailed += (log, e) => error = e;
            try
            {
                Assert.DoesNotThrow(RunDummyTrialWithoutFrames);
                Assert.AreEqual("disk full", error);
                Assert.AreEqual("disk full", recorder.LastError);
                Assert.IsNotNull(recorder.LastLog, "the log content is still available");
            }
            finally
            {
                recorder.Dispose();
                _recorder = new ExperimentLogRecorder(_sm, _sink);
            }
        }

        private void RunDummyTrialWithoutFrames()
        {
            Assert.IsTrue(_sm.LoadPattern(Pattern()));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            foreach (double t in new[] { 0.0, 0.4 }) _sm.ProcessBoard(_board, t);
            _board.Set(3, 2, StoneColor.Black); foreach (double t in new[] { 1.0, 1.4 }) _sm.ProcessBoard(_board, t);
            _board.Set(2, 3, StoneColor.White); foreach (double t in new[] { 2.0, 2.4 }) _sm.ProcessBoard(_board, t);
            _board.Set(5, 4, StoneColor.Black); foreach (double t in new[] { 3.0, 3.4 }) _sm.ProcessBoard(_board, t);
        }

        // ---------------- 異常値フラグ・書式 ----------------

        [Test]
        public void OverrideFlagAndNote_AreWrittenToRowAndSummary()
        {
            var pattern = Pattern();
            var trial = new TrialResult
            {
                Pattern = pattern,
                TotalTimeSec = 12.346,
                TotalErrorCount = 2,
                Steps = new List<StepResult>
                {
                    new StepResult { Step = pattern.steps[0], StepTimeSec = 2.004, ErrorTypes = { PlacementErrorType.WrongColor, PlacementErrorType.WrongPosition } },
                    new StepResult { Step = pattern.steps[1], StepTimeSec = 3.0, IsOverride = true, Note = "manual advance" },
                    new StepResult { Step = pattern.steps[2], StepTimeSec = 0.996 }
                }
            };
            var meta = new TrialMetadata { subjectId = "S02", condition = "Paper", order = 1, note = "lighting changed" };

            string[] lines = Lines(ExperimentCsv.BuildStepCsv(meta, trial, new List<double> { 59.876, 60 }));
            Assert.AreEqual(4, lines.Length);

            var r1 = Row(lines[0], lines[1]);
            var r2 = Row(lines[0], lines[2]);
            var r3 = Row(lines[0], lines[3]);
            Assert.AreEqual("wrong_color|wrong_position", r1["error_type"]);
            Assert.AreEqual("2.00", r1["step_time_sec"]);
            Assert.AreEqual("59.88", r1["avg_fps"]);
            Assert.AreEqual("0", r1["is_override"]);
            Assert.AreEqual("1", r2["is_override"]);
            Assert.AreEqual("lighting changed / manual advance", r2["note"]);
            Assert.AreEqual("1.00", r3["step_time_sec"], "rounded to 2 decimals");
            Assert.AreEqual("0.00", r3["avg_fps"], "missing fps value -> 0");

            var s = Row(ExperimentCsv.SummaryHeader, ExperimentCsv.FormatSummaryRow(meta, trial, 59.9, DateTime.MinValue, DateTime.MinValue));
            Assert.AreEqual("1", s["override_count"]);
            Assert.AreEqual("12.35", s["tct_sec"]);
            Assert.AreEqual("2", s["total_errors"]);
        }

        [Test]
        public void Fields_WithCommasQuotesOrNewlines_AreEscaped()
        {
            var meta = new TrialMetadata { subjectId = "S,03", condition = "say \"hi\"", order = 3, note = "line1\nline2" };
            var pattern = Pattern();
            string row = ExperimentCsv.FormatStepRow(meta, pattern, new StepResult { Step = pattern.steps[0] }, 60);

            var r = Row(ExperimentCsv.StepHeader, row);
            Assert.AreEqual("S,03", r["subject_id"]);
            Assert.AreEqual("say \"hi\"", r["condition"]);
            Assert.AreEqual("line1\nline2", r["note"]);
            Assert.AreEqual("", ExperimentCsv.Escape(null));
            Assert.AreEqual("plain", ExperimentCsv.Escape("plain"));
        }

        [Test]
        public void Numbers_UseInvariantCulture()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE"); // 小数点がカンマの文化圏
                Assert.AreEqual("1.23", ExperimentCsv.FormatNumber(1.234));
                Assert.AreEqual("2.00", ExperimentCsv.FormatNumber(2));
                Assert.AreEqual("0.00", ExperimentCsv.FormatNumber(double.NaN));
                Assert.AreEqual("0.00", ExperimentCsv.FormatNumber(double.PositiveInfinity));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void FileNames_AreSanitized()
        {
            Assert.AreEqual("S_01_", ExperimentCsv.SanitizeFileName("S/01:"));
            Assert.AreEqual("na", ExperimentCsv.SanitizeFileName("  "));
            Assert.AreEqual("被験者1", ExperimentCsv.SanitizeFileName("被験者1"), "letters in any language are kept");
        }

        [Test]
        public void FrameRateAccumulator_IgnoresInvalidFrames()
        {
            var acc = new FrameRateAccumulator();
            Assert.AreEqual(0, acc.AverageFps);
            acc.AddFrame(0);
            acc.AddFrame(-1);
            acc.AddFrame(double.NaN);
            Assert.AreEqual(0, acc.FrameCount);
            for (int i = 0; i < 10; i++) acc.AddFrame(0.02);
            Assert.AreEqual(50.0, acc.AverageFps, 1e-9);
            acc.Reset();
            Assert.AreEqual(0, acc.AverageFps);
        }

        // ---------------- ファイル出力 ----------------

        [Test]
        public void FileSink_WritesStepCsvAndAppendsSummary()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "AROthelloLoggerTest_" + Guid.NewGuid().ToString("N"));
            var sink = new FileTrialLogSink(Path.Combine(_tempDir, "ExperimentLogs"));
            _recorder.Dispose();
            _recorder = new ExperimentLogRecorder(_sm, sink, () => _now)
            {
                Metadata = new TrialMetadata { subjectId = "S01", condition = "AR", order = 1 }
            };

            RunDummyTrial();
            string first = _recorder.LastWrittenPath;
            Assert.IsTrue(File.Exists(first));
            Assert.AreEqual("S01_AR_o1_L1-03-LOG_20261126_100004.csv", Path.GetFileName(first));

            byte[] bytes = File.ReadAllBytes(first);
            Assert.IsTrue(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "UTF-8 BOM for Excel");
            string[] lines = File.ReadAllLines(first, Encoding.UTF8);
            Assert.AreEqual(ExperimentCsv.StepHeader, lines[0]);
            Assert.AreEqual(4, lines.Length);

            // 同じ秒にもう 1 試行 → ファイル名が衝突しないこと、サマリーに追記されること
            _board.Clear();
            _now = new DateTime(2026, 11, 26, 10, 0, 0);
            RunDummyTrial();
            string second = _recorder.LastWrittenPath;
            Assert.AreNotEqual(first, second);
            StringAssert.EndsWith("_2.csv", second);

            string[] summary = File.ReadAllLines(Path.Combine(sink.Directory, FileTrialLogSink.SummaryFileName), Encoding.UTF8);
            Assert.AreEqual(3, summary.Length, "header + 2 trials");
            Assert.AreEqual(ExperimentCsv.SummaryHeader, summary[0]);
            Assert.AreEqual("3.10", Row(summary[0], summary[1])["tct_sec"]);
        }
    }
}
