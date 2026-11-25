using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AROthello.Experiment.Tests
{
    /// <summary>Phase 2: 状態管理・正誤判定・デバウンス。</summary>
    public class ExperimentStateMachineTests
    {
        private const double Dt = 0.1; // 認識フレーム間隔 (10 fps 相当)

        private ExperimentStateMachine _sm;
        private BoardState _board;
        private List<string> _events;
        private List<StepResult> _stepResults;
        private List<PlacementError> _errors;
        private TrialResult _trial;

        [SetUp]
        public void SetUp()
        {
            _sm = new ExperimentStateMachine();
            _board = new BoardState();
            _events = new List<string>();
            _stepResults = new List<StepResult>();
            _errors = new List<PlacementError>();
            _trial = null;

            _sm.StateChanged += (a, b) => _events.Add($"State:{b}");
            _sm.TrialStarted += (p, t) => _events.Add("TrialStarted");
            _sm.StepStarted += (i, s) => _events.Add($"StepStarted:{i}");
            _sm.StepCompleted += r => { _events.Add($"StepCompleted:{r.StepIndex}"); _stepResults.Add(r); };
            _sm.ErrorDetected += e => { _events.Add($"Error:{e.Type}"); _errors.Add(e); };
            _sm.ErrorCleared += i => _events.Add($"ErrorCleared:{i}");
            _sm.TrialCompleted += t => { _events.Add("TrialCompleted"); _trial = t; };
        }

        // ---------------- ヘルパー ----------------

        /// <summary>3 手のパターン: D3 黒 → C4 白 → F5 黒</summary>
        private static InstructionPatternData ThreeSteps(params InstructionStone[] initialStones)
        {
            return new InstructionPatternData
            {
                patternId = "TEST-3",
                difficulty = initialStones.Length > 0 ? 3 : 1,
                stepCount = 3,
                initialStones = initialStones,
                steps = new[]
                {
                    Step(1, 3, 2, "B"),
                    Step(2, 2, 3, "W"),
                    Step(3, 5, 4, "B")
                }
            };
        }

        private static InstructionStep Step(int n, int col, int row, string color)
        {
            return new InstructionStep { step = n, col = col, row = row, coord = BoardCoord.ToCoord(col, row), color = color };
        }

        private static InstructionStone Stone(int col, int row, string color)
        {
            return new InstructionStone { col = col, row = row, coord = BoardCoord.ToCoord(col, row), color = color };
        }

        private void Put(int col, int row, StoneColor c) => _board.Set(col, row, c);

        private void Put(InstructionStep s)
        {
            BoardCoord.TryParseColor(s.color, out StoneColor c);
            _board.Set(s.col, s.row, c);
        }

        /// <summary>from から to まで Dt 間隔で同じ盤面を流す (両端を含む)。</summary>
        private void Feed(double from, double to)
        {
            int n = (int)System.Math.Round((to - from) / Dt);
            for (int i = 0; i <= n; i++) _sm.ProcessBoard(_board, from + i * Dt);
        }

        /// <summary>パターンを読み込み、空盤面チェックを 0.0〜0.4 秒で通して試行開始 (開始時刻 0.4)。</summary>
        private void StartTrial(InstructionPatternData pattern)
        {
            Assert.IsTrue(_sm.LoadPattern(pattern));
            Assert.IsTrue(_sm.BeginCalibration());
            Assert.IsTrue(_sm.CompleteCalibration(0.0));
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
            if (pattern.HasInitialStones)
            {
                foreach (InstructionStone s in pattern.initialStones)
                {
                    BoardCoord.TryParseColor(s.color, out StoneColor c);
                    _board.Set(s.col, s.row, c);
                }
            }
            Feed(0.0, 0.4);
            Assert.AreEqual(ExperimentState.Running, _sm.State);
            Assert.AreEqual(0.4, _sm.TrialStartTime, 1e-9);
        }

        // ---------------- 状態遷移 ----------------

        [Test]
        public void StateTransitions_FollowIdleCalibrationCheckRunningCompleted()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);
            foreach (InstructionStep s in p.steps)
            {
                double t = 1.0 + s.step;
                Put(s);
                Feed(t, t + 0.4);
            }

            Assert.AreEqual(ExperimentState.Completed, _sm.State);
            CollectionAssert.AreEqual(new[]
            {
                "State:Calibration", "State:InitialBoardCheck", "State:Running", "TrialStarted", "StepStarted:0",
                "StepCompleted:0", "StepStarted:1",
                "StepCompleted:1", "StepStarted:2",
                "StepCompleted:2", "State:Completed", "TrialCompleted"
            }, _events);
        }

        [Test]
        public void Operations_AreRejectedInWrongStates()
        {
            Assert.IsFalse(_sm.BeginCalibration(), "no pattern");
            Assert.IsFalse(_sm.CompleteCalibration(0), "not calibrating");
            Assert.IsFalse(_sm.LoadPattern(null));

            var invalid = ThreeSteps();
            invalid.stepCount = 99;
            Assert.IsFalse(_sm.LoadPattern(invalid), "invalid pattern is rejected");

            Assert.IsTrue(_sm.LoadPattern(ThreeSteps()));
            _sm.ProcessBoard(_board, 0); // Idle では無視される
            Assert.AreEqual(ExperimentState.Idle, _sm.State);

            Assert.IsTrue(_sm.BeginCalibration());
            Assert.IsFalse(_sm.LoadPattern(ThreeSteps()), "cannot change pattern during a trial");
            Assert.IsFalse(_sm.BeginCalibration());
            Assert.DoesNotThrow(() => _sm.ProcessBoard(null, 0));
        }

        [Test]
        public void ResetTrial_ReturnsToIdleAndClearsProgress()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);
            Put(p.steps[0]);
            Feed(1.0, 1.4);
            Assert.AreEqual(1, _sm.CurrentStepIndex);

            _sm.ResetTrial();

            Assert.AreEqual(ExperimentState.Idle, _sm.State);
            Assert.AreEqual(0, _sm.CurrentStepIndex);
            Assert.AreEqual(0, _sm.CompletedSteps.Count);
            Assert.AreSame(p, _sm.Pattern, "pattern is kept");
            Assert.IsNull(_sm.CurrentStep);
        }

        [Test]
        public void CompleteCalibration_SkipsCheck_WhenDisabledAndNoInitialStones()
        {
            _sm.CheckEmptyBoardWhenNoInitialStones = false;
            Assert.IsTrue(_sm.LoadPattern(ThreeSteps()));
            Assert.IsTrue(_sm.BeginCalibration());
            Assert.IsTrue(_sm.CompleteCalibration(5.0));

            Assert.AreEqual(ExperimentState.Running, _sm.State);
            Assert.AreEqual(5.0, _sm.TrialStartTime, 1e-9);
        }

        // ---------------- 正常進行と時間記録 ----------------

        [Test]
        public void NormalProgress_RecordsFirstDetectTimeNotConfirmTime()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p); // 開始 0.4

            Put(p.steps[0]);
            Feed(1.0, 1.4);  // 1.0 で初検知, 1.4 で確定
            Put(p.steps[1]);
            Feed(2.0, 2.4);
            Put(p.steps[2]);
            Feed(3.5, 3.9);

            Assert.AreEqual(ExperimentState.Completed, _sm.State);
            Assert.AreEqual(3, _stepResults.Count);

            Assert.AreEqual(1.0, _stepResults[0].FirstDetectTime, 1e-9);
            Assert.AreEqual(1.4, _stepResults[0].ConfirmTime, 1e-9);
            Assert.AreEqual(0.6, _stepResults[0].StepTimeSec, 1e-9, "1.0 - start 0.4");
            Assert.AreEqual(0.4, _stepResults[0].GuideShownTime, 1e-9);

            Assert.AreEqual(1.0, _stepResults[1].StepTimeSec, 1e-9, "2.0 - 1.0");
            Assert.AreEqual(1.4, _stepResults[1].GuideShownTime, 1e-9);
            Assert.AreEqual(1.5, _stepResults[2].StepTimeSec, 1e-9, "3.5 - 2.0");

            Assert.IsNotNull(_trial);
            Assert.AreEqual(0.4, _trial.StartTime, 1e-9);
            Assert.AreEqual(3.5, _trial.EndTime, 1e-9);
            Assert.AreEqual(3.1, _trial.TotalTimeSec, 1e-9);
            Assert.AreEqual(0, _trial.TotalErrorCount);
            Assert.IsTrue(_stepResults.All(r => !r.IsOverride && r.ErrorCount == 0));
        }

        [Test]
        public void StepIsNotConfirmed_BeforeDebounceElapsed()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);
            Put(p.steps[0]);
            Feed(1.0, 1.3); // 0.3 秒だけ

            Assert.AreEqual(0, _sm.CurrentStepIndex);
            Assert.AreEqual(1.0, _sm.PendingCorrectSince.Value, 1e-9);

            _sm.ProcessBoard(_board, 1.4);
            Assert.AreEqual(1, _sm.CurrentStepIndex);
        }

        [Test]
        public void DebounceTime_IsConfigurable()
        {
            _sm.CorrectDebounceSec = 1.0;
            InstructionPatternData p = ThreeSteps();
            Assert.IsTrue(_sm.LoadPattern(p));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 1.0);
            Assert.AreEqual(ExperimentState.Running, _sm.State);

            Put(p.steps[0]);
            Feed(2.0, 2.9);
            Assert.AreEqual(0, _sm.CurrentStepIndex, "0.9 s < 1.0 s");
            _sm.ProcessBoard(_board, 3.0);
            Assert.AreEqual(1, _sm.CurrentStepIndex);
            Assert.AreEqual(2.0, _stepResults[0].FirstDetectTime, 1e-9);
        }

        // ---------------- チャタリング ----------------

        [Test]
        public void Chattering_ShortCorrectBlip_IsIgnored()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);

            Put(p.steps[0]);
            Feed(1.0, 1.2);                       // 0.2 秒だけ見えて
            _board.Clear();
            Feed(1.3, 2.0);                       // 消える (手の遮蔽・誤検知)
            Assert.AreEqual(0, _sm.CurrentStepIndex, "0.2 s blip must not confirm");
            Assert.IsNull(_sm.PendingCorrectSince);

            Put(p.steps[0]);
            Feed(2.5, 2.9);
            Assert.AreEqual(1, _sm.CurrentStepIndex);
            Assert.AreEqual(2.5, _stepResults[0].FirstDetectTime, 1e-9, "first detect restarts after the blip");
        }

        [Test]
        public void Chattering_FlickeringDetection_NeverConfirms()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);
            for (int i = 0; i < 20; i++)
            {
                double t = 1.0 + i * Dt;
                if (i % 3 == 2) _board.Clear(); else Put(p.steps[0]); // 0.2 秒見えて 0.1 秒消える
                _sm.ProcessBoard(_board, t);
            }
            Assert.AreEqual(0, _sm.CurrentStepIndex);
        }

        [Test]
        public void Chattering_ShortErrorBlip_IsNotCountedAsError()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);

            Put(0, 0, StoneColor.Black);   // 指定外のマス
            Feed(1.0, 1.2);
            _board.Clear();
            Feed(1.3, 1.6);

            Assert.AreEqual(0, _errors.Count);
            Assert.AreEqual(0, _sm.TotalErrorCount);
            Assert.IsFalse(_sm.IsErrorActive);
        }

        // ---------------- 誤配置 ----------------

        [Test]
        public void WrongPosition_BlocksProgress_AndIsCountedOnce()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);

            Put(0, 0, StoneColor.Black);
            Feed(1.0, 2.0);
            Assert.AreEqual(1, _errors.Count, "one error per episode");
            Assert.AreEqual(PlacementErrorType.WrongPosition, _errors[0].Type);
            Assert.AreEqual(1.0, _errors[0].FirstDetectTime, 1e-9);
            Assert.AreEqual(1.4, _errors[0].ConfirmTime, 1e-9);
            Assert.AreEqual("A1", _errors[0].Mismatches.Single().Coord);
            Assert.IsTrue(_sm.IsErrorActive);

            // 正しい石も置いたが、誤った石が残っているので進まない
            Put(p.steps[0]);
            Feed(2.1, 3.0);
            Assert.AreEqual(0, _sm.CurrentStepIndex, "locked while the wrong stone remains");
            Assert.AreEqual(1, _sm.TotalErrorCount);

            // 誤った石を取り除くと解除され、そこから正解の検知が始まる
            Put(0, 0, StoneColor.None);
            Feed(3.5, 3.9);
            Assert.AreEqual(1, _sm.CurrentStepIndex);
            Assert.Contains("ErrorCleared:0", _events);
            Assert.AreEqual(3.5, _stepResults[0].FirstDetectTime, 1e-9);
            Assert.AreEqual(1, _stepResults[0].ErrorCount);
            CollectionAssert.AreEqual(new[] { PlacementErrorType.WrongPosition }, _stepResults[0].ErrorTypes);
        }

        [Test]
        public void WrongColor_AtTargetCell_IsDetected()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);

            Put(3, 2, StoneColor.White); // D3 は黒の指定
            Feed(1.0, 1.5);
            Assert.AreEqual(PlacementErrorType.WrongColor, _errors.Single().Type);
            Assert.AreEqual(0, _sm.CurrentStepIndex);

            Put(3, 2, StoneColor.Black); // 石を裏返して修正
            Feed(2.0, 2.4);
            Assert.AreEqual(1, _sm.CurrentStepIndex);
            Assert.AreEqual(2.0, _stepResults[0].FirstDetectTime, 1e-9);
        }

        [Test]
        public void MissingAndChangedStones_AfterConfirmation_AreDetected()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);
            Put(p.steps[0]);
            Feed(1.0, 1.4);
            Assert.AreEqual(1, _sm.CurrentStepIndex);

            Put(3, 2, StoneColor.None);  // 置き済みの D3 が消えた
            Feed(2.0, 2.5);
            Assert.AreEqual(PlacementErrorType.MissingStone, _errors.Last().Type);

            Put(3, 2, StoneColor.Black); // 戻す → 解除
            Feed(2.6, 2.7);
            Assert.IsFalse(_sm.IsErrorActive);

            Put(3, 2, StoneColor.White); // 置き済みの石の色が変わった
            Feed(3.0, 3.5);
            Assert.AreEqual(PlacementErrorType.ChangedStone, _errors.Last().Type);
            Assert.AreEqual(2, _sm.CurrentStepErrorCount);
            Assert.AreEqual(2, _sm.TotalErrorCount);
        }

        [Test]
        public void RepeatedErrorEpisodes_AreCountedSeparately_AndSummedInTrial()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);

            Put(7, 7, StoneColor.White);
            Feed(1.0, 1.4);
            Put(7, 7, StoneColor.None);
            Feed(1.5, 1.6);
            Put(3, 2, StoneColor.White);
            Feed(2.0, 2.4);
            Put(3, 2, StoneColor.Black);
            Feed(3.0, 3.4);

            Assert.AreEqual(1, _sm.CurrentStepIndex);
            Assert.AreEqual(2, _stepResults[0].ErrorCount);
            CollectionAssert.AreEqual(new[] { PlacementErrorType.WrongPosition, PlacementErrorType.WrongColor }, _stepResults[0].ErrorTypes);

            Put(p.steps[1]);
            Feed(4.0, 4.4);
            Put(p.steps[2]);
            Feed(5.0, 5.4);
            Assert.AreEqual(0, _stepResults[1].ErrorCount, "error count is per step");
            Assert.AreEqual(2, _trial.TotalErrorCount);
        }

        [Test]
        public void PlacingNextStepEarly_IsAnErrorUntilCurrentStepIsDone()
        {
            InstructionPatternData p = ThreeSteps();
            StartTrial(p);

            Put(p.steps[1]); // 2 手目を先に置いた
            Feed(1.0, 1.5);
            Assert.AreEqual(PlacementErrorType.WrongPosition, _errors.Single().Type);
            Assert.AreEqual(0, _sm.CurrentStepIndex);
        }

        // ---------------- Lv.3 初期石チェック ----------------

        [Test]
        public void InitialStoneCheck_WaitsUntilAllDummyStonesAreCorrect()
        {
            InstructionPatternData p = ThreeSteps(Stone(0, 0, "B"), Stone(7, 0, "W"), Stone(0, 7, "W"));
            var statuses = new List<(bool ok, int count)>();
            _sm.InitialBoardStatusChanged += (ok, list) => statuses.Add((ok, list.Count));

            Assert.IsTrue(_sm.LoadPattern(p));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);

            Feed(0.0, 1.0); // 空盤面のまま
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
            Assert.AreEqual(3, _sm.LastInitialMismatches.Count);

            Put(0, 0, StoneColor.Black);
            Put(7, 0, StoneColor.White);
            Put(0, 7, StoneColor.Black); // 色違い
            Feed(1.1, 2.0);
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
            CellMismatch m = _sm.LastInitialMismatches.Single();
            Assert.AreEqual("A8", m.Coord);
            Assert.AreEqual(StoneColor.White, m.Expected);
            Assert.AreEqual(StoneColor.Black, m.Actual);

            Put(0, 7, StoneColor.White);
            Feed(2.1, 2.4);
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State, "0.3 s is shorter than the debounce time");
            _sm.ProcessBoard(_board, 2.5);
            Assert.AreEqual(ExperimentState.Running, _sm.State);
            Assert.AreEqual(2.5, _sm.TrialStartTime, 1e-9, "starts when the correct board has lasted the debounce time");

            CollectionAssert.AreEqual(new[] { (false, 3), (false, 1), (true, 0) }, statuses);
        }

        [Test]
        public void InitialStoneCheck_ExtraStone_BlocksStart()
        {
            InstructionPatternData p = ThreeSteps(Stone(0, 0, "B"));
            Assert.IsTrue(_sm.LoadPattern(p));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);

            Put(0, 0, StoneColor.Black);
            Put(4, 4, StoneColor.White); // 余計な石
            Feed(0.0, 2.0);
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
            Assert.AreEqual("E5", _sm.LastInitialMismatches.Single().Coord);
        }

        [Test]
        public void InitialStoneCheck_ShortCorrectBlip_DoesNotStart()
        {
            InstructionPatternData p = ThreeSteps(Stone(0, 0, "B"));
            Assert.IsTrue(_sm.LoadPattern(p));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);

            Put(0, 0, StoneColor.Black);
            Feed(0.0, 0.2);
            Put(0, 0, StoneColor.None);
            Feed(0.3, 1.0);
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
        }

        [Test]
        public void Lv1_EmptyBoardCheck_BlocksStartWhileStonesRemain()
        {
            InstructionPatternData p = ThreeSteps();
            Assert.IsTrue(_sm.LoadPattern(p));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);

            Put(3, 3, StoneColor.White); // 前の試行の石が残っている
            Feed(0.0, 1.0);
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);

            _board.Clear();
            Feed(1.1, 1.5);
            Assert.AreEqual(ExperimentState.Running, _sm.State);
        }

        [Test]
        public void InitialStones_ArePartOfExpectedBoardDuringTrial()
        {
            InstructionPatternData p = ThreeSteps(Stone(0, 0, "B"), Stone(7, 7, "W"));
            StartTrial(p);

            // 初期石を残したまま 1 手目 → 正解
            Put(p.steps[0]);
            Feed(1.0, 1.4);
            Assert.AreEqual(1, _sm.CurrentStepIndex);

            // 初期石を取ると誤り
            Put(7, 7, StoneColor.None);
            Feed(2.0, 2.4);
            Assert.AreEqual(PlacementErrorType.MissingStone, _errors.Single().Type);

            BoardState expected = _sm.GetExpectedBoard();
            Assert.AreEqual(StoneColor.Black, expected.Get(0, 0));
            Assert.AreEqual(StoneColor.White, expected.Get(7, 7));
            Assert.AreEqual(StoneColor.Black, expected.Get(3, 2));
            Assert.AreEqual(3, expected.CountStones());
        }

        // ---------------- BoardState / モック ----------------

        [Test]
        public void BoardState_ParseAndAccessors()
        {
            BoardState b = BoardState.Parse(
                "B.......",
                "........",
                "...W....",
                "........",
                "........",
                "........",
                "........",
                ".......W");
            Assert.AreEqual(StoneColor.Black, b.Get(0, 0));
            Assert.AreEqual(StoneColor.White, b.Get(3, 2));
            Assert.AreEqual(StoneColor.White, b.Get(7, 7));
            Assert.AreEqual(StoneColor.None, b.Get(8, 0), "out of range is None");
            Assert.DoesNotThrow(() => b.Set(-1, 9, StoneColor.Black));
            Assert.AreEqual(3, b.CountStones());
            Assert.AreEqual("B.......", b.ToString().Split('\n')[0]);
            Assert.Throws<System.ArgumentException>(() => BoardState.Parse("B"));

            BoardState c = b.Clone();
            c.Set(0, 0, StoneColor.None);
            Assert.AreEqual(StoneColor.Black, b.Get(0, 0), "clone is independent");
        }

        [Test]
        public void MockProvider_PublishesBoard_AndDrivesStateMachine()
        {
            var go = new GameObject("MockBoard");
            try
            {
                MockBoardStateProvider mock = go.AddComponent<MockBoardStateProvider>();
                mock.BoardUpdated += _sm.ProcessBoard;

                InstructionPatternData p = ThreeSteps(Stone(0, 0, "W"));
                Assert.IsTrue(_sm.LoadPattern(p));
                _sm.BeginCalibration();
                _sm.CompleteCalibration(0);

                mock.PlaceInitialStones(p);
                for (int i = 0; i <= 4; i++) mock.Publish(i * Dt);
                Assert.AreEqual(ExperimentState.Running, _sm.State);

                foreach (InstructionStep s in p.steps)
                {
                    mock.Place(s);
                    for (int i = 0; i <= 4; i++) mock.Publish(s.step + i * Dt);
                }
                Assert.AreEqual(ExperimentState.Completed, _sm.State);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
