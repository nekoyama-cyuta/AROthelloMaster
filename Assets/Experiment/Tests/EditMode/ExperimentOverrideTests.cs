using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AROthello.Experiment.Tests
{
    /// <summary>Phase 5: 手動オーバーライド・実験者操作・キャリブレーション確認。</summary>
    public class ExperimentOverrideTests
    {
        private const double Dt = 0.1;

        private ExperimentStateMachine _sm;
        private BoardState _board;
        private List<StepResult> _steps;
        private TrialResult _trial;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _sm = new ExperimentStateMachine();
            _board = new BoardState();
            _steps = new List<StepResult>();
            _trial = null;
            _sm.StepCompleted += r => _steps.Add(r);
            _sm.TrialCompleted += t => _trial = t;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static InstructionPatternData Pattern(string id = "OVR", params InstructionStone[] initialStones)
        {
            return new InstructionPatternData
            {
                patternId = id,
                difficulty = initialStones.Length > 0 ? 3 : 1,
                stepCount = 3,
                initialStones = initialStones,
                steps = new[]
                {
                    new InstructionStep { step = 1, col = 3, row = 2, coord = "D3", color = "B" },
                    new InstructionStep { step = 2, col = 2, row = 3, coord = "C4", color = "W" },
                    new InstructionStep { step = 3, col = 5, row = 4, coord = "F5", color = "B" }
                }
            };
        }

        private void Feed(double from, double to)
        {
            int n = (int)Math.Round((to - from) / Dt);
            for (int i = 0; i <= n; i++) _sm.ProcessBoard(_board, from + i * Dt);
        }

        private void StartRunning(InstructionPatternData p)
        {
            Assert.IsTrue(_sm.LoadPattern(p));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 0.4);
            Assert.AreEqual(ExperimentState.Running, _sm.State);
        }

        private T Track<T>(T obj) where T : Object { _created.Add(obj); return obj; }

        // ---------------- 強制で次へ ----------------

        [Test]
        public void ForceAdvance_ConfirmsCurrentStepAsOverride()
        {
            StartRunning(Pattern());

            Assert.IsTrue(_sm.ForceAdvance(2.0)); // 石は認識されていない
            Assert.AreEqual(1, _sm.CurrentStepIndex);
            StepResult r = _steps.Single();
            Assert.IsTrue(r.IsOverride);
            Assert.AreEqual(ExperimentStateMachine.DefaultOverrideNote, r.Note);
            Assert.AreEqual(2.0, r.FirstDetectTime, 1e-9, "no detection -> override time");
            Assert.AreEqual(1.6, r.StepTimeSec, 1e-9);

            // 強制確定した石は期待盤面に入る (後から実物を置けば一致する)
            Assert.AreEqual(StoneColor.Black, _sm.GetExpectedBoard().Get(3, 2));
        }

        [Test]
        public void ForceAdvance_UsesPendingFirstDetectTime()
        {
            StartRunning(Pattern());
            _board.Set(3, 2, StoneColor.Black);
            Feed(1.0, 1.2);  // 確定前 (認識が途切れがち等)
            Assert.IsTrue(_sm.ForceAdvance(1.25, "lighting"));

            StepResult r = _steps.Single();
            Assert.AreEqual(1.0, r.FirstDetectTime, 1e-9, "T_first_detect is kept for an override");
            Assert.AreEqual("lighting", r.Note);
        }

        [Test]
        public void ForceAdvance_ClearsActiveError()
        {
            StartRunning(Pattern());
            var cleared = new List<int>();
            _sm.ErrorCleared += i => cleared.Add(i);
            _board.Set(0, 0, StoneColor.White);
            Feed(1.0, 1.5);
            Assert.IsTrue(_sm.IsErrorActive);

            Assert.IsTrue(_sm.ForceAdvance(2.0));
            Assert.IsFalse(_sm.IsErrorActive);
            CollectionAssert.AreEqual(new[] { 0 }, cleared);
            Assert.AreEqual(1, _steps[0].ErrorCount, "the error before the override is still recorded");
        }

        [Test]
        public void ForceAdvance_OnLastStep_CompletesTrial()
        {
            StartRunning(Pattern());
            _sm.ForceAdvance(1.0);
            _sm.ForceAdvance(2.0);
            _sm.ForceAdvance(3.0);

            Assert.AreEqual(ExperimentState.Completed, _sm.State);
            Assert.IsNotNull(_trial);
            Assert.AreEqual(3, _trial.Steps.Count(s => s.IsOverride));
            Assert.AreEqual(2.6, _trial.TotalTimeSec, 1e-9);
        }

        [Test]
        public void ForceAdvance_InOtherStates()
        {
            Assert.IsFalse(_sm.ForceAdvance(0), "Idle");

            Assert.IsTrue(_sm.LoadPattern(Pattern("INIT", new InstructionStone { col = 0, row = 0, coord = "A1", color = "W" })));
            Assert.IsTrue(_sm.BeginCalibration());
            Assert.IsTrue(_sm.ForceAdvance(0.5), "Calibration -> InitialBoardCheck");
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);

            Feed(0.6, 1.5); // 初期石が認識されない
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
            Assert.IsTrue(_sm.ForceAdvance(2.0), "skip the initial check");
            Assert.AreEqual(ExperimentState.Running, _sm.State);
            Assert.AreEqual(2.0, _sm.TrialStartTime, 1e-9);

            _sm.ForceAdvance(3.0); _sm.ForceAdvance(4.0); _sm.ForceAdvance(5.0);
            Assert.IsTrue(_trial.InitialCheckOverridden);
            Assert.IsFalse(_sm.ForceAdvance(6.0), "Completed");
        }

        [Test]
        public void InitialCheckOverriddenFlag_IsClearedForNextTrial()
        {
            Assert.IsTrue(_sm.LoadPattern(Pattern()));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            _sm.ForceAdvance(0.1);
            _sm.ResetTrial();

            StartRunning(Pattern());
            _sm.ForceAdvance(1); _sm.ForceAdvance(2); _sm.ForceAdvance(3);
            Assert.IsFalse(_trial.InitialCheckOverridden);
        }

        // ---------------- 1 手戻る ----------------

        [Test]
        public void StepBack_RevertsLastStep_AndMeasuresAgainFromPreviousDetection()
        {
            StartRunning(Pattern());
            var reverted = new List<StepResult>();
            _sm.StepReverted += r => reverted.Add(r);

            _board.Set(3, 2, StoneColor.Black); Feed(1.0, 1.4);  // 1 手目
            _board.Set(2, 3, StoneColor.White); Feed(2.0, 2.4);  // 2 手目 (誤認識で確定したとする)
            Assert.AreEqual(2, _sm.CurrentStepIndex);

            Assert.IsTrue(_sm.StepBack(3.0));
            Assert.AreEqual(1, _sm.CurrentStepIndex);
            Assert.AreEqual(1, _sm.CompletedSteps.Count);
            Assert.AreEqual(1, reverted.Single().StepIndex);
            Assert.AreEqual(StoneColor.None, _sm.GetExpectedBoard().Get(2, 3), "cell is open again");
            Assert.AreEqual(2, _sm.CurrentStep.step);

            // 石を一度取って置き直す → 前の手順 (1.0) から測り直す
            _board.Set(2, 3, StoneColor.None); Feed(3.1, 3.2);
            _board.Set(2, 3, StoneColor.White); Feed(4.0, 4.4);
            StepResult redo = _sm.CompletedSteps.Last();
            Assert.AreEqual(1, redo.StepIndex);
            Assert.AreEqual(3.0, redo.StepTimeSec, 1e-9, "4.0 - 1.0");
            Assert.AreEqual(ExperimentStateMachine.StepBackNote, redo.Note);
            Assert.IsFalse(redo.IsOverride);
        }

        [Test]
        public void StepBack_StoneStillOnBoard_IsReconfirmedAfterDebounce()
        {
            StartRunning(Pattern());
            _board.Set(3, 2, StoneColor.Black); Feed(1.0, 1.4);
            Assert.IsTrue(_sm.StepBack(2.0));
            Assert.AreEqual(0, _sm.CurrentStepIndex);

            Feed(2.1, 2.5); // 石は置かれたまま → 再び正解として確定
            Assert.AreEqual(1, _sm.CurrentStepIndex);
            Assert.AreEqual(2.1, _sm.CompletedSteps.Last().FirstDetectTime, 1e-9);
        }

        [Test]
        public void StepBack_CarriesErrorsAndOverride_IntoRedo()
        {
            StartRunning(Pattern());
            _board.Set(0, 0, StoneColor.White); Feed(1.0, 1.4);  // 誤り 1 回
            _board.Set(0, 0, StoneColor.None);
            _sm.ForceAdvance(2.0, "stuck");
            Assert.IsTrue(_sm.StepBack(2.5));

            Assert.AreEqual(1, _sm.CurrentStepErrorCount, "errors of the undone step are kept");
            Assert.AreEqual(1, _sm.TotalErrorCount);
            _board.Set(3, 2, StoneColor.Black); Feed(3.0, 3.4);
            StepResult redo = _sm.CompletedSteps.Single();
            Assert.AreEqual(1, redo.ErrorCount);
            Assert.AreEqual("stuck / " + ExperimentStateMachine.StepBackNote, redo.Note);
        }

        [Test]
        public void StepBack_IsRejectedWhenNothingToUndo()
        {
            Assert.IsFalse(_sm.StepBack(0), "Idle");
            StartRunning(Pattern());
            Assert.IsFalse(_sm.StepBack(1.0), "first step");
            _sm.ForceAdvance(1); _sm.ForceAdvance(2); _sm.ForceAdvance(3);
            Assert.IsFalse(_sm.StepBack(4.0), "completed trial is final");
        }

        [Test]
        public void ResetTrial_RaisesTrialResetOnlyWhenActive()
        {
            int resets = 0;
            _sm.TrialReset += () => resets++;
            Assert.IsTrue(_sm.LoadPattern(Pattern()));
            _sm.ResetTrial();
            Assert.AreEqual(0, resets, "Idle -> Idle");
            StartRunning(Pattern());
            _sm.ResetTrial();
            Assert.AreEqual(1, resets);
        }

        // ---------------- ロガーへの記録 ----------------

        private class MemorySink : ITrialLogSink
        {
            public readonly List<TrialLog> Logs = new List<TrialLog>();
            public string Write(TrialLog log) { Logs.Add(log); return log.FileBaseName; }
        }

        [Test]
        public void Override_IsRecordedAsIsOverride1InCsv()
        {
            var sink = new MemorySink();
            using (var recorder = new ExperimentLogRecorder(_sm, sink) { Metadata = new TrialMetadata { subjectId = "S01", condition = "AR", order = 1 } })
            {
                StartRunning(Pattern());
                _board.Set(3, 2, StoneColor.Black); Feed(1.0, 1.4);
                _sm.ForceAdvance(2.0);                               // 2 手目を強制
                _board.Set(2, 3, StoneColor.White);
                _board.Set(5, 4, StoneColor.Black); Feed(3.0, 3.4);
            }

            TrialLog log = sink.Logs.Single();
            string[] lines = log.StepCsv.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string[] header = lines[0].Split(',');
            int iOverride = Array.IndexOf(header, "is_override");
            int iNote = Array.IndexOf(header, "note");
            CollectionAssert.AreEqual(new[] { "0", "1", "0" }, lines.Skip(1).Select(l => l.Split(',')[iOverride]));
            Assert.AreEqual(ExperimentStateMachine.DefaultOverrideNote, lines[2].Split(',')[iNote]);

            string[] summaryHeader = ExperimentCsv.SummaryHeader.Split(',');
            Assert.AreEqual("1", log.SummaryRow.Split(',')[Array.IndexOf(summaryHeader, "override_count")]);
        }

        [Test]
        public void StepBack_RemovesRevertedStepFromFpsList()
        {
            var sink = new MemorySink();
            using (var recorder = new ExperimentLogRecorder(_sm, sink))
            {
                StartRunning(Pattern());
                _board.Set(3, 2, StoneColor.Black); Feed(1.0, 1.4);
                _board.Set(2, 3, StoneColor.White); Feed(2.0, 2.4);
                _sm.StepBack(2.5);
                Feed(2.6, 3.0);                                     // 2 手目を再確定
                _board.Set(5, 4, StoneColor.Black); Feed(3.5, 3.9);
            }
            TrialLog log = sink.Logs.Single();
            Assert.AreEqual(3, log.Trial.Steps.Count);
            Assert.AreEqual(3, log.StepFps.Count, "one fps value per final step row");
        }

        [Test]
        public void InitialCheckOverride_IsNotedInSummary()
        {
            var sink = new MemorySink();
            using (new ExperimentLogRecorder(_sm, sink))
            {
                Assert.IsTrue(_sm.LoadPattern(Pattern()));
                _sm.BeginCalibration();
                _sm.CompleteCalibration(0);
                _sm.ForceAdvance(0.1);
                _sm.ForceAdvance(1); _sm.ForceAdvance(2); _sm.ForceAdvance(3);
            }
            StringAssert.EndsWith(ExperimentCsv.InitialCheckOverriddenNote, sink.Logs.Single().SummaryRow);
        }

        // ---------------- 実験者操作 ----------------

        private (ExperimentOperator op, InstructionLibrary lib, Func<double> setTime) CreateOperator(out double[] now)
        {
            var lib = new InstructionLibrary();
            lib.Add(new InstructionParseResult { SourceName = "test", Patterns = { Pattern("P1"), Pattern("P2") } });
            double[] t = { 0 };
            now = t;
            var op = new ExperimentOperator(_sm, lib, new TrialMetadata(), () => t[0]);
            return (op, lib, () => t[0]);
        }

        [Test]
        public void Operator_SetsMetadataAndSelectsPatterns()
        {
            var (op, lib, _) = CreateOperator(out _);
            op.SetSubjectId(" S07 ");
            op.SetCondition("AR");
            Assert.IsTrue(op.SetOrder("3"));
            Assert.IsFalse(op.SetOrder("abc"));
            Assert.IsFalse(op.SetOrder(0));
            Assert.AreEqual("S07", op.Metadata.subjectId);
            Assert.AreEqual(3, op.Metadata.order);

            Assert.IsTrue(op.LoadSelectedPattern());
            Assert.AreEqual("P1", _sm.Pattern.patternId);
            Assert.IsTrue(op.SelectNextPattern());
            Assert.AreEqual("P2", _sm.Pattern.patternId);
            Assert.IsTrue(op.SelectPattern("P1"));
            Assert.AreEqual("P1", _sm.Pattern.patternId);
            Assert.IsFalse(op.SelectPattern("NOPE"));
            op.Dispose();
        }

        [Test]
        public void Operator_PrimaryAction_DrivesTrialFlow_AndLocksPatternDuringTrial()
        {
            var (op, lib, _) = CreateOperator(out double[] now);
            op.LoadSelectedPattern();
            Assert.AreEqual("キャリブ開始", op.PrimaryActionLabel);

            Assert.IsTrue(op.PrimaryAction());
            Assert.AreEqual(ExperimentState.Calibration, _sm.State);
            Assert.IsFalse(op.SelectNextPattern(), "pattern is locked during a trial");
            Assert.AreEqual("P1", lib.Current.patternId);

            now[0] = 1.0;
            Assert.IsTrue(op.PrimaryAction());
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
            Assert.IsFalse(op.PrimaryAction(), "no-op during the check");

            Assert.IsTrue(op.ForceAdvance());           // 開始前チェックを省略
            now[0] = 2.0; Assert.IsTrue(op.ForceAdvance());
            now[0] = 3.0; Assert.IsTrue(op.StepBack());
            now[0] = 4.0; Assert.IsTrue(op.ForceAdvance());
            now[0] = 5.0; Assert.IsTrue(op.ForceAdvance());
            now[0] = 6.0; Assert.IsTrue(op.ForceAdvance());
            Assert.AreEqual(ExperimentState.Completed, _sm.State);
            Assert.IsNotNull(op.LastTrial);
            StringAssert.Contains("TCT 5.00s", op.BuildStatusText());

            Assert.AreEqual("次の試行", op.PrimaryActionLabel);
            Assert.IsTrue(op.SelectNextPattern(), "unlocked after completion");
            Assert.IsTrue(op.PrimaryAction(), "next trial starts with the newly selected pattern");
            Assert.AreEqual("P2", _sm.Pattern.patternId);
            Assert.AreEqual(ExperimentState.Calibration, _sm.State);
            op.Dispose();
        }

        [Test]
        public void Operator_AutoIncrementOrder_AndStatusText()
        {
            var (op, _, _) = CreateOperator(out double[] now);
            op.AutoIncrementOrder = true;
            op.SetSubjectId("S01");
            op.LoadSelectedPattern();
            op.PrimaryAction();
            op.PrimaryAction();
            op.ForceAdvance();
            StringAssert.Contains("手順 1/3 D3 B", op.BuildStatusText());
            op.ForceAdvance(); op.ForceAdvance(); op.ForceAdvance();
            Assert.AreEqual(2, op.Metadata.order);
            StringAssert.Contains("被験者 S01", op.BuildStatusText());
            op.Dispose();
        }

        [Test]
        public void Operator_ResetTrial_ReturnsToIdle()
        {
            var (op, _, _) = CreateOperator(out _);
            var logged = new List<string>();
            op.ActionLogged += logged.Add;
            op.LoadSelectedPattern();
            op.PrimaryAction();
            op.ResetTrial();
            Assert.AreEqual(ExperimentState.Idle, _sm.State);
            Assert.Contains("trial reset", logged);
            op.Dispose();
        }

        // ---------------- パネル UI ----------------

        [Test]
        public void Panel_BuildsUi_AndButtonsDriveOperator()
        {
            var (op, _, _) = CreateOperator(out _);
            op.LoadSelectedPattern();

            var canvasGo = Track(new GameObject("Canvas", typeof(Canvas)));
            ExperimentOperatorPanel panel = Track(new GameObject("Panel")).AddComponent<ExperimentOperatorPanel>();
            Assert.DoesNotThrow(() => panel.Initialize(op, canvasGo.GetComponent<Canvas>()));

            Assert.IsFalse(panel.IsOpen, "settings closed by default");
            panel.Buttons["settings"].onClick.Invoke();
            Assert.IsTrue(panel.IsOpen);
            panel.Buttons["settings_close"].onClick.Invoke();
            Assert.IsFalse(panel.IsOpen);

            Assert.AreEqual("位置合わせを始める", panel.Labels["primary"].text);
            panel.Buttons["primary"].onClick.Invoke();
            Assert.AreEqual(ExperimentState.Calibration, _sm.State);
            Assert.AreEqual("位置OK・試行を始める", panel.Labels["primary"].text, "plain label follows the state");
            panel.Buttons["primary"].onClick.Invoke();
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
            Assert.AreEqual("確認をとばして始める", panel.Labels["force"].text);

            panel.Buttons["force"].onClick.Invoke();
            Assert.AreEqual(ExperimentState.Running, _sm.State);
            panel.Buttons["force"].onClick.Invoke();
            Assert.AreEqual(1, _sm.CurrentStepIndex);
            Assert.IsTrue(_sm.CompletedSteps[0].IsOverride);
            panel.Buttons["back"].onClick.Invoke();
            Assert.AreEqual(0, _sm.CurrentStepIndex);

            // やり直しは 2 回押しで確定
            panel.Buttons["reset"].onClick.Invoke();
            Assert.AreEqual(ExperimentState.Running, _sm.State, "first press only arms the reset");
            Assert.AreEqual("もう一度押すとやり直し", panel.Labels["reset"].text);
            panel.Buttons["reset"].onClick.Invoke();
            Assert.AreEqual(ExperimentState.Idle, _sm.State);

            Assert.DoesNotThrow(() => panel.Buttons["nudge_x+"].onClick.Invoke(), "no offset adjuster connected");
            Assert.DoesNotThrow(() => panel.Buttons["dots"].onClick.Invoke(), "no dots view connected");
            Assert.DoesNotThrow(() => panel.Buttons["view_overhead"].onClick.Invoke(), "no views connected");
            Assert.IsFalse(panel.Buttons.ContainsKey("recalibrate"), "tracking settings are hidden without a tracker");
            Assert.IsNotNull(canvasGo.transform.Find(ExperimentOperatorPanel.RootName + "/Settings"));
            op.Dispose();
        }

        private class FakeTrackingSettings : ITrackingSettings
        {
            public bool IsTracking { get; set; }
            public int OffsetBasis { get; private set; }
            public int TypicalOffsetCount, RecalibrateCount;
            public readonly bool[] Inverted = new bool[4];
            public bool IsPerformanceLogging { get; private set; }
            public void SetOffsetBasis(int basis) => OffsetBasis = basis;
            public void ApplyTypicalOffset() => TypicalOffsetCount++;
            public bool IsAxisInverted(TrackingAxis axis) => Inverted[(int)axis];
            public void SetAxisInverted(TrackingAxis axis, bool inverted) => Inverted[(int)axis] = inverted;
            public void RecalibrateStoneDetection() => RecalibrateCount++;
            public void TogglePerformanceLogging() => IsPerformanceLogging = !IsPerformanceLogging;
        }

        [Test]
        public void Panel_SettingsButtonsDriveTrackingSettings()
        {
            var (op, _, _) = CreateOperator(out _);
            op.LoadSelectedPattern();
            var canvasGo = Track(new GameObject("Canvas", typeof(Canvas)));
            ExperimentOperatorPanel panel = Track(new GameObject("Panel")).AddComponent<ExperimentOperatorPanel>();
            panel.Initialize(op, canvasGo.GetComponent<Canvas>());
            var fake = new FakeTrackingSettings();
            panel.SetTrackingSettings(fake);

            panel.Buttons["basis_1"].onClick.Invoke();
            Assert.AreEqual(1, fake.OffsetBasis);
            panel.Buttons["typical_offset"].onClick.Invoke();
            Assert.AreEqual(1, fake.TypicalOffsetCount);
            panel.Buttons["axis_1_invert"].onClick.Invoke();
            Assert.IsTrue(fake.Inverted[(int)TrackingAxis.Pitch]);
            panel.Buttons["axis_1_normal"].onClick.Invoke();
            Assert.IsFalse(fake.Inverted[(int)TrackingAxis.Pitch]);
            panel.Buttons["recalibrate"].onClick.Invoke();
            Assert.AreEqual(1, fake.RecalibrateCount);

            Assert.AreEqual("動作の記録を始める", panel.Labels["perf_log"].text);
            panel.Buttons["perf_log"].onClick.Invoke();
            Assert.IsTrue(fake.IsPerformanceLogging);
            Assert.AreEqual("記録を止めて保存する", panel.Labels["perf_log"].text);
            op.Dispose();
        }

        // ---------------- キャリブレーション確認ドット ----------------

        [Test]
        public void CalibrationDots_ShowAtFourCornersAndCenter()
        {
            Transform board = Track(new GameObject("Board")).transform;
            CalibrationDotsView dots = Track(new GameObject("Dots")).AddComponent<CalibrationDotsView>();
            dots.BoardRoot = board;

            dots.SetVisible(true);
            Transform container = board.Find("CalibrationDots_Container");
            Assert.IsNotNull(container);
            Assert.IsTrue(container.gameObject.activeSelf);
            Assert.AreEqual(5, container.childCount);

            var positions = container.Cast<Transform>().Select(t => new Vector2(t.localPosition.x, t.localPosition.z)).ToList();
            foreach (Vector2 expected in CalibrationDotsView.DotPositions)
            {
                Assert.IsTrue(positions.Any(p => Vector2.Distance(p, expected) < 1e-5f), $"dot at {expected}");
            }
            Assert.IsTrue(container.Cast<Transform>().All(t => t.localPosition.y > BoardGeometry.TopSurfaceY));

            dots.Toggle();
            Assert.IsFalse(dots.Visible);
            Assert.IsFalse(container.gameObject.activeSelf);
        }

        [Test]
        public void CalibrationDots_WithoutBoard_AreSafe()
        {
            CalibrationDotsView dots = Track(new GameObject("Dots")).AddComponent<CalibrationDotsView>();
            Assert.DoesNotThrow(() => dots.SetVisible(true));
            Assert.DoesNotThrow(() => dots.Toggle());
        }

        // ---------------- モック操作 ----------------

        [Test]
        public void Mock_PlaceTargetAndInitialStones_DriveTrial()
        {
            var mock = Track(new GameObject("Mock")).AddComponent<MockBoardStateProvider>();
            mock.BoardUpdated += _sm.ProcessBoard;
            Assert.IsTrue(_sm.LoadPattern(Pattern("MOCK", new InstructionStone { col = 7, row = 7, coord = "H8", color = "B" })));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);

            mock.PlaceInitialStones(_sm.Pattern);
            for (int i = 0; i <= 4; i++) mock.Publish(i * Dt);
            Assert.AreEqual(ExperimentState.Running, _sm.State);

            for (int s = 0; s < 3; s++)
            {
                mock.Place(_sm.CurrentStep);
                for (int i = 0; i <= 4; i++) mock.Publish(1 + s + i * Dt);
            }
            Assert.AreEqual(ExperimentState.Completed, _sm.State);
            Assert.AreEqual(4, mock.Board.CountStones());
        }
    }
}
