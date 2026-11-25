using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AROthello.Experiment.Tests
{
    /// <summary>Phase 1: 作業指示書 JSON の読み込み・検証・切り替え。</summary>
    public class InstructionLoaderTests
    {
        // ---- テスト用 JSON ----

        private const string SingleLv1 = @"{
            ""patternId"": ""T-L1"",
            ""difficulty"": 1,
            ""stepCount"": 3,
            ""initialStones"": [],
            ""steps"": [
                { ""step"": 1, ""col"": 3, ""row"": 2, ""coord"": ""D3"", ""color"": ""B"" },
                { ""step"": 2, ""col"": 2, ""row"": 3, ""coord"": ""C4"", ""color"": ""W"" },
                { ""step"": 3, ""col"": 7, ""row"": 7, ""coord"": ""H8"", ""color"": ""B"" }
            ]
        }";

        private const string Pack = @"{
            ""packId"": ""T-PACK"",
            ""patterns"": [
                {
                    ""patternId"": ""P-L1-05"", ""difficulty"": 1, ""stepCount"": 5, ""initialStones"": [],
                    ""steps"": [
                        { ""step"": 1, ""col"": 0, ""row"": 0, ""coord"": ""A1"", ""color"": ""B"" },
                        { ""step"": 2, ""col"": 1, ""row"": 0, ""coord"": ""B1"", ""color"": ""W"" },
                        { ""step"": 3, ""col"": 2, ""row"": 0, ""coord"": ""C1"", ""color"": ""B"" },
                        { ""step"": 4, ""col"": 3, ""row"": 0, ""coord"": ""D1"", ""color"": ""W"" },
                        { ""step"": 5, ""col"": 4, ""row"": 0, ""coord"": ""E1"", ""color"": ""B"" }
                    ]
                },
                {
                    ""patternId"": ""P-L2-05"", ""difficulty"": 2, ""stepCount"": 5, ""initialStones"": [],
                    ""steps"": [
                        { ""step"": 1, ""col"": 0, ""row"": 1, ""coord"": ""A2"", ""color"": ""W"" },
                        { ""step"": 2, ""col"": 1, ""row"": 1, ""coord"": ""B2"", ""color"": ""W"" },
                        { ""step"": 3, ""col"": 2, ""row"": 1, ""coord"": ""C2"", ""color"": ""B"" },
                        { ""step"": 4, ""col"": 3, ""row"": 1, ""coord"": ""D2"", ""color"": ""B"" },
                        { ""step"": 5, ""col"": 4, ""row"": 1, ""coord"": ""E2"", ""color"": ""W"" }
                    ]
                },
                {
                    ""patternId"": ""P-L3-05"", ""difficulty"": 3, ""stepCount"": 5,
                    ""initialStones"": [
                        { ""col"": 7, ""row"": 0, ""coord"": ""H1"", ""color"": ""W"" },
                        { ""col"": 0, ""row"": 7, ""coord"": ""A8"", ""color"": ""B"" }
                    ],
                    ""steps"": [
                        { ""step"": 1, ""col"": 3, ""row"": 3, ""coord"": ""D4"", ""color"": ""W"" },
                        { ""step"": 2, ""col"": 4, ""row"": 4, ""coord"": ""E5"", ""color"": ""W"" },
                        { ""step"": 3, ""col"": 3, ""row"": 4, ""coord"": ""D5"", ""color"": ""B"" },
                        { ""step"": 4, ""col"": 4, ""row"": 3, ""coord"": ""E4"", ""color"": ""B"" },
                        { ""step"": 5, ""col"": 5, ""row"": 5, ""coord"": ""F6"", ""color"": ""B"" }
                    ]
                },
                {
                    ""patternId"": ""P-L1-03"", ""difficulty"": 1, ""stepCount"": 3, ""initialStones"": [],
                    ""steps"": [
                        { ""step"": 1, ""col"": 0, ""row"": 2, ""coord"": ""A3"", ""color"": ""B"" },
                        { ""step"": 2, ""col"": 1, ""row"": 2, ""coord"": ""B3"", ""color"": ""B"" },
                        { ""step"": 3, ""col"": 2, ""row"": 2, ""coord"": ""C3"", ""color"": ""W"" }
                    ]
                }
            ]
        }";

        private static string SingleWithStep(string stepJson, int stepCount = 1, int difficulty = 1, string initialStones = "[]")
        {
            return "{ \"patternId\": \"BAD\", \"difficulty\": " + difficulty + ", \"stepCount\": " + stepCount +
                   ", \"initialStones\": " + initialStones + ", \"steps\": [" + stepJson + "] }";
        }

        private static void AssertRejected(string json, string expectedErrorFragment)
        {
            InstructionParseResult r = InstructionJsonParser.Parse(json, "test");
            Assert.AreEqual(0, r.Patterns.Count, "invalid pattern must not be loaded");
            Assert.IsTrue(r.Errors.Any(e => e.Contains(expectedErrorFragment)),
                $"expected an error containing \"{expectedErrorFragment}\" but got:\n{string.Join("\n", r.Errors)}");
        }

        // ---- 正常系 ----

        [Test]
        public void Parse_SinglePattern_ReadsAllFields()
        {
            InstructionParseResult r = InstructionJsonParser.Parse(SingleLv1, "single");

            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Errors));
            Assert.IsNull(r.PackId);
            Assert.AreEqual(1, r.Patterns.Count);

            InstructionPatternData p = r.Patterns[0];
            Assert.AreEqual("T-L1", p.patternId);
            Assert.AreEqual(1, p.difficulty);
            Assert.AreEqual(3, p.stepCount);
            Assert.IsFalse(p.HasInitialStones);
            Assert.AreEqual(3, p.steps.Length);

            Assert.AreEqual(1, p.steps[0].step);
            Assert.AreEqual(3, p.steps[0].col);
            Assert.AreEqual(2, p.steps[0].row);
            Assert.AreEqual("D3", p.steps[0].coord);
            Assert.AreEqual("B", p.steps[0].color);

            Assert.AreEqual(7, p.steps[2].col);
            Assert.AreEqual(7, p.steps[2].row);
            Assert.AreEqual("H8", p.steps[2].coord);
        }

        [Test]
        public void Parse_Pack_ReadsAllPatternsIncludingLv3InitialStones()
        {
            InstructionParseResult r = InstructionJsonParser.Parse(Pack, "pack");

            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Errors));
            Assert.AreEqual("T-PACK", r.PackId);
            CollectionAssert.AreEqual(new[] { "P-L1-05", "P-L2-05", "P-L3-05", "P-L1-03" }, r.Patterns.Select(p => p.patternId).ToArray());

            InstructionPatternData lv3 = r.Patterns[2];
            Assert.AreEqual(3, lv3.difficulty);
            Assert.IsTrue(lv3.HasInitialStones);
            Assert.AreEqual(2, lv3.initialStones.Length);
            Assert.AreEqual(7, lv3.initialStones[0].col);
            Assert.AreEqual(0, lv3.initialStones[0].row);
            Assert.AreEqual("H1", lv3.initialStones[0].coord);
            Assert.AreEqual("W", lv3.initialStones[0].color);
            Assert.AreEqual("A8", lv3.initialStones[1].coord);
            Assert.AreEqual("B", lv3.initialStones[1].color);
        }

        [Test]
        public void Parse_MissingInitialStonesKey_IsTreatedAsEmpty()
        {
            string json = "{ \"patternId\": \"NO-INIT\", \"difficulty\": 2, \"stepCount\": 1, " +
                          "\"steps\": [ { \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"W\" } ] }";
            InstructionParseResult r = InstructionJsonParser.Parse(json, "no-init");

            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Errors));
            Assert.IsNotNull(r.Patterns[0].initialStones);
            Assert.AreEqual(0, r.Patterns[0].initialStones.Length);
        }

        [Test]
        public void Parse_StepsStartingAtZero_AndEmptyCoord_AreAccepted()
        {
            string json = SingleWithStep(
                "{ \"step\": 0, \"col\": 0, \"row\": 0, \"coord\": \"\", \"color\": \"b\" }," +
                "{ \"step\": 1, \"col\": 1, \"row\": 0, \"coord\": \"b1\", \"color\": \" W \" }", stepCount: 2);
            InstructionParseResult r = InstructionJsonParser.Parse(json, "zero-based");

            Assert.IsFalse(r.HasErrors, string.Join("\n", r.Errors));
            Assert.AreEqual(1, r.Patterns.Count);
        }

        [Test]
        public void BoardCoord_ConvertsBothWays()
        {
            Assert.AreEqual("A1", BoardCoord.ToCoord(0, 0));
            Assert.AreEqual("H1", BoardCoord.ToCoord(7, 0));
            Assert.AreEqual("A8", BoardCoord.ToCoord(0, 7));
            Assert.AreEqual("D3", BoardCoord.ToCoord(3, 2));
            Assert.AreEqual("??", BoardCoord.ToCoord(8, 0));

            Assert.IsTrue(BoardCoord.TryParseCoord("d3", out int c, out int r));
            Assert.AreEqual(3, c);
            Assert.AreEqual(2, r);
            Assert.IsFalse(BoardCoord.TryParseCoord("I1", out _, out _));
            Assert.IsFalse(BoardCoord.TryParseCoord("A9", out _, out _));
            Assert.IsFalse(BoardCoord.TryParseCoord("", out _, out _));

            Assert.IsTrue(BoardCoord.TryParseColor("B", out StoneColor b));
            Assert.AreEqual(StoneColor.Black, b);
            Assert.IsTrue(BoardCoord.TryParseColor("w", out StoneColor w));
            Assert.AreEqual(StoneColor.White, w);
            Assert.IsFalse(BoardCoord.TryParseColor("X", out _));
            Assert.IsFalse(BoardCoord.TryParseColor(null, out _));
        }

        // ---- 異常系 ----

        [Test]
        public void Parse_EmptyOrBrokenJson_ReturnsErrorWithoutThrowing()
        {
            Assert.DoesNotThrow(() =>
            {
                Assert.IsTrue(InstructionJsonParser.Parse("", "empty").HasErrors);
                Assert.IsTrue(InstructionJsonParser.Parse(null, "null").HasErrors);
                Assert.IsTrue(InstructionJsonParser.Parse("{ this is not json", "broken").HasErrors);
                Assert.IsTrue(InstructionJsonParser.Parse("{}", "empty-object").HasErrors);
            });
        }

        [Test]
        public void Parse_PackWithoutPatterns_IsError()
        {
            AssertRejected("{ \"packId\": \"EMPTY\", \"patterns\": [] }", "has no patterns");
        }

        [Test]
        public void Validate_OutOfRangeCell_IsRejected()
        {
            AssertRejected(SingleWithStep("{ \"step\": 1, \"col\": 8, \"row\": 0, \"coord\": \"\", \"color\": \"B\" }"), "out of range 0..7");
            AssertRejected(SingleWithStep("{ \"step\": 1, \"col\": 0, \"row\": -1, \"coord\": \"\", \"color\": \"B\" }"), "out of range 0..7");
        }

        [Test]
        public void Validate_InvalidColor_IsRejected()
        {
            AssertRejected(SingleWithStep("{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"G\" }"), "must be \"B\" or \"W\"");
            AssertRejected(SingleWithStep("{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\" }"), "must be \"B\" or \"W\"");
        }

        [Test]
        public void Validate_CoordMismatch_IsRejected()
        {
            AssertRejected(SingleWithStep("{ \"step\": 1, \"col\": 3, \"row\": 2, \"coord\": \"C4\", \"color\": \"B\" }"), "does not match col/row");
        }

        [Test]
        public void Validate_StepCountMismatch_IsRejected()
        {
            AssertRejected(SingleWithStep("{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"B\" }", stepCount: 5), "does not match steps.Length");
        }

        [Test]
        public void Validate_NonSequentialSteps_AreRejected()
        {
            AssertRejected(SingleWithStep(
                "{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"B\" }," +
                "{ \"step\": 3, \"col\": 1, \"row\": 0, \"coord\": \"B1\", \"color\": \"W\" }", stepCount: 2), "not sequential");
            AssertRejected(SingleWithStep("{ \"step\": 2, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"B\" }"), "must start at step 0 or 1");
        }

        [Test]
        public void Validate_DifficultyOutOfRange_IsRejected()
        {
            AssertRejected(SingleWithStep("{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"B\" }", difficulty: 4), "difficulty 4 is out of range");
            AssertRejected(SingleWithStep("{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"B\" }", difficulty: 0), "difficulty 0 is out of range");
        }

        [Test]
        public void Validate_DuplicateCell_InStepsOrAgainstInitialStones_IsRejected()
        {
            AssertRejected(SingleWithStep(
                "{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"B\" }," +
                "{ \"step\": 2, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"W\" }", stepCount: 2), "already used");

            AssertRejected(SingleWithStep(
                "{ \"step\": 1, \"col\": 7, \"row\": 0, \"coord\": \"H1\", \"color\": \"B\" }", difficulty: 3,
                initialStones: "[ { \"col\": 7, \"row\": 0, \"coord\": \"H1\", \"color\": \"W\" } ]"), "already used");
        }

        [Test]
        public void Validate_InvalidInitialStone_IsRejected()
        {
            AssertRejected(SingleWithStep(
                "{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"B\" }", difficulty: 3,
                initialStones: "[ { \"col\": 9, \"row\": 0, \"coord\": \"\", \"color\": \"W\" } ]"), "initialStones[0] col/row (9,0) is out of range");
        }

        [Test]
        public void Parse_Pack_SkipsOnlyInvalidPatterns()
        {
            string json = "{ \"packId\": \"MIX\", \"patterns\": [ " +
                          SingleLv1 + ", " +
                          SingleWithStep("{ \"step\": 1, \"col\": 0, \"row\": 0, \"coord\": \"A1\", \"color\": \"X\" }") +
                          " ] }";
            InstructionParseResult r = InstructionJsonParser.Parse(json, "mixed");

            Assert.AreEqual(1, r.Patterns.Count);
            Assert.AreEqual("T-L1", r.Patterns[0].patternId);
            Assert.IsTrue(r.HasErrors);
        }

        [Test]
        public void Parse_Pack_DuplicatePatternId_KeepsFirst()
        {
            string json = "{ \"packId\": \"DUP\", \"patterns\": [ " + SingleLv1 + ", " + SingleLv1 + " ] }";
            InstructionParseResult r = InstructionJsonParser.Parse(json, "dup");

            Assert.AreEqual(1, r.Patterns.Count);
            Assert.IsTrue(r.Errors.Any(e => e.Contains("duplicate patternId")));
        }

        // ---- InstructionLibrary (検索・切り替え) ----

        [Test]
        public void Library_FindsByIdDifficultyAndStepCount()
        {
            var lib = new InstructionLibrary();
            lib.AddJson(Pack, "pack");

            Assert.AreEqual(4, lib.Count);
            Assert.AreEqual("P-L3-05", lib.GetById("P-L3-05").patternId);
            Assert.IsNull(lib.GetById("NOPE"));
            Assert.IsNull(lib.GetById(null));

            CollectionAssert.AreEqual(new[] { "P-L1-05", "P-L1-03" }, lib.Find(difficulty: 1).Select(p => p.patternId).ToArray());
            CollectionAssert.AreEqual(new[] { "P-L1-05", "P-L2-05", "P-L3-05" }, lib.Find(stepCount: 5).Select(p => p.patternId).ToArray());
            Assert.AreEqual("P-L2-05", lib.FindFirst(2, 5).patternId);
            Assert.IsNull(lib.FindFirst(3, 15));
        }

        [Test]
        public void Library_SelectsAndSwitchesPatterns_WithEvents()
        {
            var lib = new InstructionLibrary();
            int changeCount = 0;
            InstructionPatternData last = null;
            lib.CurrentPatternChanged += p => { changeCount++; last = p; };

            Assert.IsNull(lib.Current);
            lib.AddJson(Pack, "pack");
            Assert.AreEqual("P-L1-05", lib.Current.patternId, "first pattern is selected automatically");
            Assert.AreEqual(1, changeCount);

            Assert.IsTrue(lib.Select("P-L3-05"));
            Assert.AreEqual("P-L3-05", lib.Current.patternId);
            Assert.AreSame(lib.Current, last);

            Assert.IsFalse(lib.Select("NOPE"));
            Assert.AreEqual("P-L3-05", lib.Current.patternId, "failed selection keeps current");

            Assert.IsTrue(lib.Select(1, 3));
            Assert.AreEqual("P-L1-03", lib.Current.patternId);

            Assert.AreEqual("P-L1-05", lib.SelectNext().patternId, "wraps to first");
            Assert.AreEqual("P-L1-03", lib.SelectPrevious().patternId, "wraps to last");
        }

        [Test]
        public void Library_IgnoresPatternIdAlreadyLoadedFromAnotherFile()
        {
            var lib = new InstructionLibrary();
            lib.AddJson(SingleLv1, "a.json");
            lib.AddJson(SingleLv1, "b.json");

            Assert.AreEqual(1, lib.Count);
            Assert.IsTrue(lib.Errors.Any(e => e.StartsWith("b.json") && e.Contains("already loaded")));
        }

        [Test]
        public void Library_EmptyLibrary_IsSafe()
        {
            var lib = new InstructionLibrary();
            Assert.IsNull(lib.Current);
            Assert.IsNull(lib.SelectNext());
            Assert.IsNull(lib.SelectPrevious());
            Assert.IsFalse(lib.Select("X"));
            Assert.IsFalse(lib.Select(1, 5));
            Assert.AreEqual(0, lib.Find().Count);
        }

        // ---- StreamingAssets 同梱サンプル ----

        [Test]
        public void StreamingAssetsSamples_AreValid()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Instructions");

            var lib = new InstructionLibrary();
            lib.AddJson(File.ReadAllText(Path.Combine(dir, "sample_pack.json")), "sample_pack.json");
            lib.AddJson(File.ReadAllText(Path.Combine(dir, "sample_single_lv1.json")), "sample_single_lv1.json");

            Assert.AreEqual(0, lib.Errors.Count, string.Join("\n", lib.Errors));
            Assert.AreEqual(4, lib.Count);
            Assert.AreEqual(5, lib.FindFirst(1, 5).steps.Length);
            Assert.AreEqual(10, lib.FindFirst(2, 10).steps.Length);

            InstructionPatternData lv3 = lib.FindFirst(3, 15);
            Assert.IsNotNull(lv3);
            Assert.IsTrue(lv3.HasInitialStones);
        }
    }
}
