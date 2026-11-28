using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AROthello.Experiment.Tests
{
    /// <summary>実験ID (被験者・試行番号)・評価・直後アンケートの流れと、その記録。</summary>
    public class ExperimentSessionTests
    {
        private const double Dt = 0.1;

        private ExperimentStateMachine _sm;
        private BoardState _board;
        private string _dir;
        private readonly List<Object> _created = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _sm = new ExperimentStateMachine { RequireSetupBeforeRunning = true };
            _board = new BoardState();
            _dir = Path.Combine(Path.GetTempPath(), "aro_session_" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private T Track<T>(T obj) where T : Object { _created.Add(obj); return obj; }

        private static InstructionPatternData Pattern(string id = "SES")
        {
            return new InstructionPatternData
            {
                patternId = id,
                difficulty = 1,
                stepCount = 3,
                initialStones = new InstructionStone[0],
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

        private string ReadFile(string name) => File.ReadAllText(Path.Combine(_dir, name), Encoding.UTF8).TrimStart('﻿');

        // ---------------- 状態管理: 実験ID (Setup) ----------------

        [Test]
        public void Setup_IsInsertedBetweenInitialCheckAndRunning()
        {
            Assert.IsTrue(_sm.LoadPattern(Pattern()));
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 0.5);
            Assert.AreEqual(ExperimentState.Setup, _sm.State, "board ready -> setup, not running");
            Assert.IsTrue(_sm.ForceAdvance(0.6));
            Assert.AreEqual(ExperimentState.Running, _sm.State, "force advance in setup starts the trial");
        }

        [Test]
        public void Setup_StartTrial_AndBoardDisturbanceReturnsToCheck()
        {
            _sm.LoadPattern(Pattern());
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 0.5);
            Assert.AreEqual(ExperimentState.Setup, _sm.State);

            _board.Set(0, 0, StoneColor.Black);
            Feed(0.6, 0.8);
            Assert.AreEqual(ExperimentState.Setup, _sm.State, "short glitches are ignored");
            Feed(0.9, 1.4);
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State, "board disturbed -> check again");
            Assert.AreEqual(1, _sm.LastInitialMismatches.Count);

            _board.Clear();
            Feed(1.5, 2.0);
            Assert.AreEqual(ExperimentState.Setup, _sm.State);
            Assert.IsTrue(_sm.StartTrial(2.1));
            Assert.AreEqual(ExperimentState.Running, _sm.State);
            Assert.AreEqual(2.1, _sm.TrialStartTime, 1e-9);
            Assert.IsFalse(_sm.StartTrial(2.2), "only from setup");
        }

        [Test]
        public void Setup_AfterSkippedCheck_DoesNotWatchTheBoard()
        {
            _sm.LoadPattern(Pattern());
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            _board.Set(0, 0, StoneColor.White);
            Assert.IsTrue(_sm.ForceAdvance(0.1));
            Assert.AreEqual(ExperimentState.Setup, _sm.State);
            Feed(0.2, 1.0);
            Assert.AreEqual(ExperimentState.Setup, _sm.State, "the experimenter skipped the check on purpose");
        }

        [Test]
        public void WithoutSetup_BehavesAsBefore()
        {
            _sm.RequireSetupBeforeRunning = false;
            _sm.LoadPattern(Pattern());
            _sm.BeginCalibration();
            _sm.CompleteCalibration(0);
            Feed(0.0, 0.5);
            Assert.AreEqual(ExperimentState.Running, _sm.State);
        }

        // ---------------- CSV の読み取り ----------------

        [Test]
        public void Csv_Parse_HandlesQuotesNewlinesAndBom()
        {
            string text = "﻿a,b,c\n1,\"x,y\",\"he said \"\"hi\"\"\"\n2,\"line1\nline2\",\n\n";
            List<string[]> rows = ExperimentCsv.Parse(text);
            Assert.AreEqual(3, rows.Count);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "1", "x,y", "he said \"hi\"" }, rows[1]);
            CollectionAssert.AreEqual(new[] { "2", "line1\nline2", "" }, rows[2]);

            string escaped = string.Join(",", ExperimentCsv.Escape("S,1"), ExperimentCsv.Escape("say \"x\""));
            CollectionAssert.AreEqual(new[] { "S,1", "say \"x\"" }, ExperimentCsv.Parse(escaped)[0], "round trip");
        }

        // ---------------- 被験者の一覧 ----------------

        [Test]
        public void Subjects_AddRenameEditDelete_ArePersisted()
        {
            var reg = new SubjectRegistry(Path.Combine(_dir, SubjectRegistry.FileName), () => new DateTime(2026, 11, 28, 10, 0, 0));
            SubjectInfo a = reg.Add("山田");
            SubjectInfo b = reg.Add("佐藤, 花子");
            Assert.IsNull(reg.Add("  "), "empty name is rejected");
            Assert.AreEqual("S01", a.Id);
            Assert.AreEqual("S02", b.Id);

            Assert.IsTrue(reg.Rename("S01", "山田 太郎"));
            var info = reg.Find("S02").Clone();
            info.Age = "23";
            info.DominantEye = "右";
            Assert.IsTrue(reg.Update(info));
            Assert.IsTrue(reg.Delete("S01"));
            Assert.IsFalse(reg.Delete("S01"), "already deleted");

            var reloaded = new SubjectRegistry(reg.Path);
            Assert.AreEqual(2, reloaded.All.Count, "deleted subjects stay in the file");
            Assert.AreEqual(1, reloaded.Active.Count);
            Assert.AreEqual("佐藤, 花子", reloaded.Active[0].Name, "commas survive the round trip");
            Assert.AreEqual("23", reloaded.Active[0].Age);
            Assert.AreEqual("右", reloaded.Active[0].DominantEye);
            Assert.IsTrue(reloaded.Find("S01").Deleted);
            Assert.AreEqual("山田 太郎", reloaded.Find("S01").Name);
            Assert.AreEqual("S03", reloaded.NextId(), "numbers are not reused after deletion");
            StringAssert.StartsWith(SubjectRegistry.Header, ReadFile(SubjectRegistry.FileName));
        }

        // ---------------- 記録 (試行回数・評価・アンケート) ----------------

        private static TrialResult Trial(InstructionPatternData p, double tct, int errors)
        {
            return new TrialResult
            {
                Pattern = p,
                TotalTimeSec = tct,
                TotalErrorCount = errors,
                Steps = p.steps.Select((s, i) => new StepResult { StepIndex = i, Step = s, IsOverride = i == 0 }).ToList()
            };
        }

        private void WriteSummary(TrialMetadata meta, TrialResult trial, DateTime completed)
        {
            var sink = new FileTrialLogSink(_dir);
            sink.Write(new TrialLog
            {
                Metadata = meta,
                Trial = trial,
                FileBaseName = "t_" + Guid.NewGuid().ToString("N"),
                StepCsv = ExperimentCsv.BuildStepCsv(meta, trial, null),
                SummaryRow = ExperimentCsv.FormatSummaryRow(meta, trial, 60, completed, completed)
            });
        }

        [Test]
        public void Store_CountsTrialsFromSummary_IncludingOldFormatFiles()
        {
            Directory.CreateDirectory(_dir);
            // 列を足す前の古い書式のファイル (experiment_id 列が無い)
            File.WriteAllText(Path.Combine(_dir, FileTrialLogSink.SummaryFileName),
                "subject_id,condition,order,pattern_id,difficulty,step_count,tct_sec,total_errors,override_count,avg_fps,started_at,completed_at,note\n" +
                "S01,AR,1,OLD,1,3,10.00,0,0,60.00,2026-11-01T10:00:00,2026-11-01T10:00:10,\n", Encoding.UTF8);

            var store = new ExperimentRecordStore(_dir);
            Assert.AreEqual(1, store.CountTrials("S01"));

            var meta = new TrialMetadata { subjectId = "S01", condition = "AR", order = 2, experimentId = "S01-AR-T02" };
            WriteSummary(meta, Trial(Pattern("NEW"), 20.6, 1), new DateTime(2026, 11, 28, 10, 0, 0));
            Assert.IsTrue(Directory.GetFiles(_dir, "trial_summary_before_*.csv").Length == 1, "the old file is kept under a new name");
            StringAssert.StartsWith(ExperimentCsv.SummaryHeader, ReadFile(FileTrialLogSink.SummaryFileName));

            Assert.AreEqual(2, store.CountTrials("S01"));
            Assert.AreEqual(0, store.CountTrials("S02"));

            Assert.IsTrue(store.WriteEvaluation(meta, Trial(Pattern("NEW"), 20.6, 1),
                new TrialEvaluation { Success = false, FailReason = "置き間違い", Memo = "照明, 暗め", EvaluatedAt = new DateTime(2026, 11, 28, 10, 1, 0) },
                new DateTime(2026, 11, 28, 10, 0, 0)));
            List<TrialHistoryEntry> history = store.ListTrials("S01");
            Assert.AreEqual(2, history.Count);
            Assert.AreEqual("OLD", history[0].PatternId);
            Assert.AreEqual("", history[0].Result, "old trials are unrated");
            Assert.AreEqual("NEW", history[1].PatternId);
            Assert.AreEqual(2, history[1].TrialNo);
            Assert.AreEqual("fail", history[1].Result);

            List<string[]> eval = ExperimentCsv.Parse(ReadFile(ExperimentRecordStore.EvaluationFileName));
            Dictionary<string, int> col = ExperimentCsv.IndexHeader(eval[0]);
            Assert.AreEqual(ExperimentRecordStore.EvaluationHeader, string.Join(",", eval[0]));
            Assert.AreEqual("S01-AR-T02", ExperimentCsv.Field(eval[1], col, "experiment_id"));
            Assert.AreEqual("fail", ExperimentCsv.Field(eval[1], col, "result"));
            Assert.AreEqual("置き間違い", ExperimentCsv.Field(eval[1], col, "fail_reason"));
            Assert.AreEqual("照明, 暗め", ExperimentCsv.Field(eval[1], col, "memo"));
            Assert.AreEqual("1", ExperimentCsv.Field(eval[1], col, "override_count"));
            Assert.AreEqual("20.60", ExperimentCsv.Field(eval[1], col, "tct_sec"));
        }

        [Test]
        public void Questionnaire_DefaultAndJsonDefinition()
        {
            QuestionnaireDefinition d = QuestionnaireDefinition.Default;
            Assert.AreEqual(1, d.Count);
            Assert.AreEqual(9, d.questions[0].Max);
            Assert.AreEqual("どちらでもない（普通）", d.questions[0].LabelOf(5));
            Assert.AreEqual("", d.questions[0].LabelOf(10));

            TextAsset asset = Resources.Load<TextAsset>("ExperimentQuestionnaire");
            Assert.IsNotNull(asset, "the questionnaire JSON ships in Resources");
            QuestionnaireDefinition fromJson = QuestionnaireDefinition.FromJson(asset.text, JsonUtility.FromJson<QuestionnaireDefinition>);
            Assert.IsNotNull(fromJson);
            Assert.AreEqual("Q1", fromJson.questions[0].id);
            CollectionAssert.AreEqual(d.questions[0].labels, fromJson.questions[0].labels);
            StringAssert.Contains("**次に石を置く場所を探して配置するために費やした精神的な労力**", fromJson.questions[0].text);

            Assert.IsNull(QuestionnaireDefinition.FromJson("{\"questions\":[]}", JsonUtility.FromJson<QuestionnaireDefinition>));
            Assert.IsNull(QuestionnaireDefinition.FromJson("not json", JsonUtility.FromJson<QuestionnaireDefinition>));
        }

        // ---------------- 実験者操作: 1 試行の流れ ----------------

        private (ExperimentOperator op, ExperimentLogRecorder rec, double[] now) CreateSession()
        {
            var lib = new InstructionLibrary();
            lib.Add(new InstructionParseResult { SourceName = "test", Patterns = { Pattern("P1"), Pattern("P2") } });
            double[] now = { 0 };
            var meta = new TrialMetadata { condition = "AR" };
            var op = new ExperimentOperator(_sm, lib, meta, () => now[0])
            {
                Store = new ExperimentRecordStore(_dir),
                WallClock = () => new DateTime(2026, 11, 28, 10, 0, 0)
            };
            var rec = new ExperimentLogRecorder(_sm, new FileTrialLogSink(_dir), () => new DateTime(2026, 11, 28, 10, 0, 0)) { Metadata = meta };
            op.Recorder = rec;
            return (op, rec, now);
        }

        private void RunToSetup(ExperimentOperator op, double[] now)
        {
            if (_sm.State == ExperimentState.Idle)
            {
                Assert.IsTrue(op.PrimaryAction());   // 位置合わせを始める
                Assert.IsTrue(op.PrimaryAction());   // 位置OK・初期配置へ
            }
            Assert.AreEqual(ExperimentState.InitialBoardCheck, _sm.State);
            Feed(now[0], now[0] + 0.5);
            now[0] += 0.6;
            Assert.AreEqual(OperatorStep.Setup, op.Step);
        }

        private void FinishTrial(ExperimentOperator op, double[] now)
        {
            for (int i = 0; i < 3; i++) { now[0] += 1.0; Assert.IsTrue(op.ForceAdvance()); }
            Assert.AreEqual(OperatorStep.Completed, op.Step);
        }

        [Test]
        public void Operator_FullTrial_SetupEvaluationSurvey_AndNextTrialNumber()
        {
            var (op, rec, now) = CreateSession();
            op.LoadSelectedPattern();
            RunToSetup(op, now);

            Assert.IsFalse(op.CanPrimaryAction, "needs a subject first");
            Assert.IsFalse(op.StartTrial());
            SubjectInfo s = op.AddSubject("山田");
            Assert.AreEqual("S01", s.Id);
            Assert.AreEqual("S01", op.Metadata.subjectId);
            Assert.AreEqual("山田", op.Metadata.subjectName);
            Assert.AreEqual(1, op.Metadata.order);
            Assert.AreEqual("S01-AR-T01", op.Metadata.experimentId);
            Assert.AreEqual("試行を始める", op.PrimaryActionLabel);

            op.AdjustTrialNo(+1);
            Assert.AreEqual("S01-AR-T02", op.Metadata.experimentId);
            op.AdjustTrialNo(-5);
            Assert.AreEqual(1, op.Metadata.order, "never below 1");

            Assert.IsTrue(op.PrimaryAction());
            Assert.AreEqual(OperatorStep.Running, op.Step);
            FinishTrial(op, now);
            Assert.AreEqual("S01-AR-T01", rec.LastLog.Metadata.experimentId, "the trial log carries the experiment ID");
            StringAssert.Contains("S01-AR-T01", rec.LastLog.SummaryRow);

            // 完了 → 評価
            Assert.AreEqual("評価へ", op.PrimaryActionLabel);
            Assert.IsTrue(op.PrimaryAction());
            Assert.AreEqual(OperatorStep.Evaluation, op.Step);
            Assert.IsFalse(op.CanPrimaryAction, "success / fail must be chosen");
            Assert.IsFalse(op.RecordEvaluation());
            op.SetFailReason("認識の不具合");
            Assert.AreEqual(false, op.Evaluation.Success, "choosing a reason means failure");
            op.SetMemo("途中で照明が変わった");
            Assert.IsTrue(op.CanPrimaryAction);
            Assert.AreEqual("記録してアンケートへ", op.PrimaryActionLabel);
            Assert.IsTrue(op.PrimaryAction());

            // アンケート
            Assert.AreEqual(OperatorStep.Survey, op.Step);
            Assert.AreEqual("Q1", op.CurrentQuestion.id);
            Assert.IsFalse(op.CanPrimaryAction, "no default answer");
            op.SetAnswer(0);
            Assert.IsNull(op.CurrentAnswer, "out of range");
            now[0] += 4.25;
            op.SetAnswer(6);
            Assert.AreEqual("回答を記録して次の試行へ", op.PrimaryActionLabel);
            Assert.IsTrue(op.PrimaryAction());

            // 次の試行: 位置合わせを飛ばして初期配置から、試行番号は記録から 2
            Assert.AreEqual(OperatorStep.InitialBoardCheck, op.Step);
            RunToSetup(op, now);
            Assert.AreEqual(2, op.Metadata.order, "counted from trial_summary.csv");
            Assert.AreEqual("S01-AR-T02", op.Metadata.experimentId);

            List<string[]> eval = ExperimentCsv.Parse(ReadFile(ExperimentRecordStore.EvaluationFileName));
            Dictionary<string, int> ec = ExperimentCsv.IndexHeader(eval[0]);
            Assert.AreEqual(2, eval.Count);
            Assert.AreEqual("S01-AR-T01", ExperimentCsv.Field(eval[1], ec, "experiment_id"));
            Assert.AreEqual("山田", ExperimentCsv.Field(eval[1], ec, "subject_name"));
            Assert.AreEqual("fail", ExperimentCsv.Field(eval[1], ec, "result"));
            Assert.AreEqual("認識の不具合", ExperimentCsv.Field(eval[1], ec, "fail_reason"));
            Assert.AreEqual("途中で照明が変わった", ExperimentCsv.Field(eval[1], ec, "memo"));

            List<string[]> q = ExperimentCsv.Parse(ReadFile(ExperimentRecordStore.QuestionnaireFileName));
            Dictionary<string, int> qc = ExperimentCsv.IndexHeader(q[0]);
            Assert.AreEqual(ExperimentRecordStore.QuestionnaireHeader, string.Join(",", q[0]));
            Assert.AreEqual("S01-AR-T01", ExperimentCsv.Field(q[1], qc, "experiment_id"));
            Assert.AreEqual("Q1", ExperimentCsv.Field(q[1], qc, "question_id"));
            Assert.AreEqual("6", ExperimentCsv.Field(q[1], qc, "answer"));
            Assert.AreEqual("やや高い", ExperimentCsv.Field(q[1], qc, "answer_label"));
            Assert.AreEqual("4.25", ExperimentCsv.Field(q[1], qc, "answer_time_sec"));
            rec.Dispose();
            op.Dispose();
        }

        [Test]
        public void Operator_SuccessWithoutQuestions_GoesStraightToNextTrial()
        {
            var (op, rec, now) = CreateSession();
            op.Questionnaire = new QuestionnaireDefinition();
            op.LoadSelectedPattern();
            RunToSetup(op, now);
            op.AddSubject("佐藤");
            op.StartTrial();
            FinishTrial(op, now);
            op.BeginEvaluation();
            op.SetFailReason("その他");
            op.SetResult(true);
            Assert.AreEqual("", op.Evaluation.FailReason, "success clears the failure reason");
            Assert.IsTrue(op.RecordEvaluation());
            Assert.AreEqual(OperatorStep.InitialBoardCheck, op.Step);
            Assert.IsFalse(File.Exists(Path.Combine(_dir, ExperimentRecordStore.QuestionnaireFileName)));
            rec.Dispose();
            op.Dispose();
        }

        [Test]
        public void Operator_PatternChangeDuringSetup_RechecksInitialBoard_AndResetAbortsEvaluation()
        {
            var (op, rec, now) = CreateSession();
            op.LoadSelectedPattern();
            RunToSetup(op, now);
            Assert.IsTrue(op.SelectNextPattern(), "pattern can still change before the trial");
            Assert.AreEqual("P2", _sm.Pattern.patternId);
            Assert.AreEqual(OperatorStep.InitialBoardCheck, op.Step, "new pattern -> check its initial board again");

            RunToSetup(op, now);
            op.AddSubject("山田");
            op.StartTrial();
            Assert.IsFalse(op.SelectNextPattern(), "locked while running");
            FinishTrial(op, now);
            op.BeginEvaluation();
            op.ResetTrial();
            Assert.AreEqual(OperatorStep.Idle, op.Step);
            Assert.IsFalse(File.Exists(Path.Combine(_dir, ExperimentRecordStore.EvaluationFileName)));
            rec.Dispose();
            op.Dispose();
        }

        [Test]
        public void Operator_DeleteSelectedSubject_ClearsSelection()
        {
            var (op, rec, now) = CreateSession();
            op.LoadSelectedPattern();
            RunToSetup(op, now);
            op.AddSubject("山田");
            Assert.IsTrue(op.RenameSubject("S01", "山田 太郎"));
            Assert.AreEqual("山田 太郎", op.Metadata.subjectName);
            Assert.IsTrue(op.DeleteSubject("S01"));
            Assert.AreEqual("", op.Metadata.subjectId);
            Assert.AreEqual("", op.Metadata.experimentId);
            Assert.IsFalse(op.SelectSubject("S01"), "deleted subjects cannot be selected");
            Assert.IsFalse(op.CanStartTrial);
            rec.Dispose();
            op.Dispose();
        }

        [Test]
        public void ExperimentId_IsBuiltFromSubjectConditionAndTrial()
        {
            Assert.AreEqual("S01-AR-T03", ExperimentOperator.BuildExperimentId("S01", "AR", 3));
            Assert.AreEqual("S01-比較条件-T12", ExperimentOperator.BuildExperimentId("S01", "比較条件", 12));
            Assert.AreEqual("S01-NA-T01", ExperimentOperator.BuildExperimentId("S01", "", 1));
            Assert.AreEqual("S_1-A_B-T01", ExperimentOperator.BuildExperimentId("S-1", "A,B", 1), "no separators inside parts");
            Assert.AreEqual("", ExperimentOperator.BuildExperimentId("", "AR", 1));
        }

        // ---------------- 画面 ----------------

        [Test]
        public void Panel_SetupEvaluationSurvey_DrivenByButtons()
        {
            var (op, rec, now) = CreateSession();
            op.LoadSelectedPattern();
            var canvasGo = Track(new GameObject("Canvas", typeof(Canvas)));
            ExperimentOperatorPanel panel = Track(new GameObject("Panel")).AddComponent<ExperimentOperatorPanel>();
            panel.Initialize(op, canvasGo.GetComponent<Canvas>());
            panel.SetConditions("AR", "比較条件");

            panel.Buttons["primary"].onClick.Invoke();
            panel.Buttons["primary"].onClick.Invoke();
            Feed(0.0, 0.5);
            now[0] = 0.6;
            panel.RefreshAll();
            Assert.AreEqual(OperatorStep.Setup, panel.ShownStep);
            Assert.IsFalse(panel.Buttons["primary"].interactable, "no subject yet");

            // 被験者を追加 (ダイアログ)
            panel.Buttons["subject_add"].onClick.Invoke();
            Assert.IsTrue(panel.IsDialogOpen);
            panel.Inputs["dialog_name"].text = "山田";
            panel.Buttons["dialog_ok"].onClick.Invoke();
            Assert.IsFalse(panel.IsDialogOpen);
            Assert.AreEqual("S01", op.Metadata.subjectId);
            Assert.IsTrue(panel.Buttons.ContainsKey("subject_S01"), "the new subject is listed");
            Assert.IsTrue(panel.Buttons["primary"].interactable);

            panel.Buttons["cond_1"].onClick.Invoke();
            Assert.AreEqual("比較条件", op.Metadata.condition);
            Assert.AreEqual("S01-比較条件-T01", op.Metadata.experimentId);
            panel.Buttons["trial_plus"].onClick.Invoke();
            Assert.AreEqual(2, op.Metadata.order);
            panel.Buttons["trial_minus"].onClick.Invoke();
            Assert.AreEqual(1, op.Metadata.order);

            // ⋯ メニュー: 名前の変更
            panel.Buttons["subject_more_S01"].onClick.Invoke();
            Assert.IsTrue(panel.IsMenuOpen);
            panel.Buttons["menu_rename"].onClick.Invoke();
            Assert.IsFalse(panel.IsMenuOpen);
            Assert.IsTrue(panel.IsDialogOpen);
            Assert.AreEqual("山田", panel.Inputs["dialog_name"].text);
            panel.Inputs["dialog_name"].text = "山田 太郎";
            panel.Buttons["dialog_ok"].onClick.Invoke();
            Assert.AreEqual("山田 太郎", op.Store.Subjects.Find("S01").Name);

            // 試行
            Assert.AreEqual("試行を始める", panel.Labels["primary"].text);
            panel.Buttons["primary"].onClick.Invoke();
            Assert.AreEqual(OperatorStep.Running, panel.ShownStep);
            for (int i = 0; i < 3; i++) { now[0] += 1; panel.Buttons["force"].onClick.Invoke(); }
            Assert.AreEqual(OperatorStep.Completed, panel.ShownStep);

            // 評価
            panel.Buttons["primary"].onClick.Invoke();
            Assert.AreEqual(OperatorStep.Evaluation, panel.ShownStep);
            Assert.IsFalse(panel.Buttons["primary"].interactable);
            panel.Buttons["result_fail"].onClick.Invoke();
            panel.Buttons["reason_1"].onClick.Invoke();
            panel.Inputs["memo"].text = "メモ";
            Assert.AreEqual("メモ", op.Evaluation.Memo);
            Assert.AreEqual("認識の不具合", op.Evaluation.FailReason);
            Assert.IsTrue(panel.Buttons["primary"].interactable);
            panel.Buttons["primary"].onClick.Invoke();

            // アンケート
            Assert.AreEqual(OperatorStep.Survey, panel.ShownStep);
            Assert.IsTrue(panel.Buttons["q_8"].gameObject.activeSelf, "9 points");
            Assert.IsFalse(panel.Buttons["q_9"].gameObject.activeSelf);
            Assert.IsFalse(panel.Buttons["primary"].interactable);
            panel.Buttons["q_2"].onClick.Invoke();
            Assert.AreEqual(3, op.CurrentAnswer);
            panel.Buttons["primary"].onClick.Invoke();
            Assert.AreEqual(OperatorStep.InitialBoardCheck, panel.ShownStep, "next trial starts at the initial board");

            StringAssert.Contains("S01-比較条件-T01", ReadFile(ExperimentRecordStore.EvaluationFileName));
            StringAssert.Contains(",3,低い,", ReadFile(ExperimentRecordStore.QuestionnaireFileName));

            // 次の試行の実験ID と、⋯ メニューからの削除 (2 回押し)
            Feed(now[0], now[0] + 0.5);
            panel.RefreshAll();
            Assert.AreEqual(OperatorStep.Setup, panel.ShownStep);
            Assert.AreEqual("S01-比較条件-T02", op.Metadata.experimentId);
            panel.Buttons["subject_more_S01"].onClick.Invoke();
            panel.Buttons["menu_delete"].onClick.Invoke();
            Assert.IsNotNull(op.SelectedSubject, "first press only arms the delete");
            StringAssert.StartsWith("もう一度押すと削除", panel.Labels["menu_delete"].text);
            panel.Buttons["menu_delete"].onClick.Invoke();
            Assert.IsTrue(op.Store.Subjects.Find("S01").Deleted);
            panel.RefreshAll();
            Assert.IsFalse(panel.Buttons.ContainsKey("subject_S01"));
            rec.Dispose();
            op.Dispose();
        }

        [Test]
        public void Panel_EditInfoAndHistoryDialogs()
        {
            var (op, rec, now) = CreateSession();
            op.LoadSelectedPattern();
            var canvasGo = Track(new GameObject("Canvas", typeof(Canvas)));
            ExperimentOperatorPanel panel = Track(new GameObject("Panel")).AddComponent<ExperimentOperatorPanel>();
            panel.Initialize(op, canvasGo.GetComponent<Canvas>());
            RunToSetup(op, now);
            op.AddSubject("山田");
            panel.RefreshAll();

            panel.Buttons["subject_more_S01"].onClick.Invoke();
            panel.Buttons["menu_edit"].onClick.Invoke();
            Assert.IsTrue(panel.IsDialogOpen);
            panel.Inputs["info_age"].text = "24";
            panel.Inputs["info_xr"].text = "あり";
            panel.Buttons["dialog_ok"].onClick.Invoke();
            Assert.AreEqual("24", op.Store.Subjects.Find("S01").Age);
            Assert.AreEqual("あり", op.Store.Subjects.Find("S01").XrExperience);

            panel.Buttons["subject_more_S01"].onClick.Invoke();
            panel.Buttons["menu_history"].onClick.Invoke();
            Assert.IsTrue(panel.IsDialogOpen);
            Assert.AreEqual("閉じる", panel.Labels["dialog_cancel"].text);
            Assert.IsTrue(!panel.Buttons.TryGetValue("dialog_ok", out Button ok) || ok == null, "the history has only a close button");
            panel.Buttons["dialog_cancel"].onClick.Invoke();
            Assert.IsFalse(panel.IsDialogOpen);
            rec.Dispose();
            op.Dispose();
        }
    }
}
