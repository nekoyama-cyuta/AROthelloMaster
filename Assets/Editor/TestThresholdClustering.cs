using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AROthelloEditor
{
    public static class TestThresholdClustering
    {
        private class TestCase
        {
            public string Name;
            public float FeltMean;
            public float FeltStd;
            public float BlackMean;
            public float BlackStd;
            public float WhiteMean;
            public float WhiteStd;
            public float FeltSat;
            public float BlackSat;
            public float WhiteSat;
            public int NumBlack;
            public int NumWhite;
        }

        [MenuItem("Tools/Run Multi-Otsu Simulation Experiments")]
        public static void RunExperiments()
        {
            var testCases = new TestCase[]
            {
                new TestCase
                {
                    Name = "1. 通常環境 (Standard Office Lighting)",
                    FeltMean = 110f, FeltStd = 7f,
                    BlackMean = 25f, BlackStd = 5f,
                    WhiteMean = 225f, WhiteStd = 7f,
                    FeltSat = 130f, BlackSat = 20f, WhiteSat = 15f,
                    NumBlack = 2, NumWhite = 2
                },
                new TestCase
                {
                    Name = "2. 薄暗い環境 (Dim / Low-Light Environment - 45% Brightness)",
                    FeltMean = 52f, FeltStd = 5f,
                    BlackMean = 12f, BlackStd = 3f,
                    WhiteMean = 118f, WhiteStd = 7f,
                    FeltSat = 110f, BlackSat = 18f, WhiteSat = 15f,
                    NumBlack = 8, NumWhite = 6
                },
                new TestCase
                {
                    Name = "3. 明るすぎる環境 (Overly Bright / Harsh Light - Overexposure Risk)",
                    FeltMean = 170f, FeltStd = 8f,
                    BlackMean = 68f, BlackStd = 6f,
                    WhiteMean = 248f, WhiteStd = 5f,
                    FeltSat = 100f, BlackSat = 22f, WhiteSat = 12f,
                    NumBlack = 10, NumWhite = 14
                },
                new TestCase
                {
                    Name = "4. 電球色・暖色系照明 (Warm Tungsten Tint - Yellow/Red Bias)",
                    FeltMean = 132f, FeltStd = 8f,
                    BlackMean = 38f, BlackStd = 5f,
                    WhiteMean = 236f, WhiteStd = 6f,
                    FeltSat = 150f, BlackSat = 28f, WhiteSat = 25f,
                    NumBlack = 5, NumWhite = 5
                },
                new TestCase
                {
                    Name = "5. 寒色・青色系照明 (Cool Fluorescent / Blue Bias)",
                    FeltMean = 88f, FeltStd = 6f,
                    BlackMean = 18f, BlackStd = 4f,
                    WhiteMean = 208f, WhiteStd = 7f,
                    FeltSat = 125f, BlackSat = 22f, WhiteSat = 20f,
                    NumBlack = 7, NumWhite = 9
                },
                new TestCase
                {
                    Name = "6. 空盤面・石なし (Degenerate Case: Empty Board - 0 Stones)",
                    FeltMean = 112f, FeltStd = 6.5f,
                    BlackMean = 25f, BlackStd = 5f,
                    WhiteMean = 225f, WhiteStd = 7f,
                    FeltSat = 130f, BlackSat = 20f, WhiteSat = 15f,
                    NumBlack = 0, NumWhite = 0
                }
            };

            var sb = new StringBuilder();
            sb.AppendLine("==========================================================================================");
            sb.AppendLine("  OTHELLO AR: 統計学的動的閾値決定アルゴリズム (Multi-Otsu) バーチャル環境総合検証実験");
            sb.AppendLine("==========================================================================================");

            int totalTests = testCases.Length;
            int passedTests = 0;

            foreach (var tc in testCases)
            {
                sb.AppendLine("\n### 【実験ケース】: " + tc.Name);

                float[] values = new float[64];
                float[] saturations = new float[64];
                DiscColor[] groundTruth = new DiscColor[64];

                var rand = new System.Random(42);
                float NextGaussian(float mean, float std)
                {
                    double u1 = 1.0 - rand.NextDouble();
                    double u2 = 1.0 - rand.NextDouble();
                    double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
                    return Mathf.Clamp((float)(mean + std * randStdNormal), 0f, 255f);
                }

                for (int i = 0; i < 64; i++)
                {
                    values[i] = NextGaussian(tc.FeltMean, tc.FeltStd);
                    saturations[i] = NextGaussian(tc.FeltSat, 10f);
                    groundTruth[i] = DiscColor.None;
                }

                int placedBlack = 0;
                while (placedBlack < tc.NumBlack)
                {
                    int idx = rand.Next(0, 64);
                    if (groundTruth[idx] == DiscColor.None)
                    {
                        groundTruth[idx] = DiscColor.Black;
                        values[idx] = NextGaussian(tc.BlackMean, tc.BlackStd);
                        saturations[idx] = NextGaussian(tc.BlackSat, 5f);
                        placedBlack++;
                    }
                }

                int placedWhite = 0;
                while (placedWhite < tc.NumWhite)
                {
                    int idx = rand.Next(0, 64);
                    if (groundTruth[idx] == DiscColor.None)
                    {
                        groundTruth[idx] = DiscColor.White;
                        values[idx] = NextGaussian(tc.WhiteMean, tc.WhiteStd);
                        saturations[idx] = NextGaussian(tc.WhiteSat, 5f);
                        placedWhite++;
                    }
                }

                // 1. 固定閾値方式
                int fixedCorrect = 0;
                int fixedBlackMis = 0;
                int fixedWhiteMis = 0;
                for (int i = 0; i < 64; i++)
                {
                    DiscColor pred = DiscColor.None;
                    if (values[i] < 65) pred = DiscColor.Black;
                    else if (values[i] > 165) pred = DiscColor.White;

                    if (pred == groundTruth[i]) fixedCorrect++;
                    else
                    {
                        if (groundTruth[i] == DiscColor.None && pred == DiscColor.Black) fixedBlackMis++;
                        if (groundTruth[i] == DiscColor.None && pred == DiscColor.White) fixedWhiteMis++;
                    }
                }
                float fixedAccuracy = (float)fixedCorrect / 64f * 100f;

                // 2. 多クラス大津法 (Multi-Otsu)
                var result = NativeOthelloRecognizer.CalibrateMultiOtsu(values, saturations);

                int otsuCorrect = 0;
                int otsuBlackMis = 0;
                int otsuWhiteMis = 0;
                for (int i = 0; i < 64; i++)
                {
                    DiscColor pred = DiscColor.None;
                    if (values[i] < result.BlackThreshold) pred = DiscColor.Black;
                    else if (values[i] > result.WhiteThreshold) pred = DiscColor.White;

                    if (pred == groundTruth[i]) otsuCorrect++;
                    else
                    {
                        if (groundTruth[i] == DiscColor.None && pred == DiscColor.Black) otsuBlackMis++;
                        if (groundTruth[i] == DiscColor.None && pred == DiscColor.White) otsuWhiteMis++;
                    }
                }
                float otsuAccuracy = (float)otsuCorrect / 64f * 100f;

                sb.AppendLine("  - 条件パラメータ: フェルト輝度=" + tc.FeltMean.ToString("F0") + "+-" + tc.FeltStd.ToString("F0") + ", 黒石=" + tc.BlackMean.ToString("F0") + ", 白石=" + tc.WhiteMean.ToString("F0") + " (黒石:" + tc.NumBlack + "個, 白石:" + tc.NumWhite + "個)");
                sb.AppendLine("  - 【固定閾値方式 (B<65, W>165)】:");
                sb.AppendLine("      正解率: " + fixedAccuracy.ToString("F1") + "% (正解 " + fixedCorrect + "/64, フェルト誤認: 黒=" + fixedBlackMis + "マス, 白=" + fixedWhiteMis + "マス)");
                sb.AppendLine("  - 【多クラス大津法 (Multi-Otsu 動的フィードバック)】:");
                sb.AppendLine("      決定境界: 黒境界 B < " + result.BlackThreshold + ", 白境界 W > " + result.WhiteThreshold);
                sb.AppendLine("      分離度指標 eta: " + result.Separability.ToString("F3") + " | フェルト推定: " + result.FeltMean.ToString("F1") + "+-" + result.FeltStdDev.ToString("F1"));
                sb.AppendLine("      状態判定: " + (result.IsDegenerate ? "退化保護 (3.5sigma Safety Rule 適用)" : "正常3峰クラスタ (Multi-Otsu Optimal)"));
                sb.AppendLine("      正解率: " + otsuAccuracy.ToString("F1") + "% (正解 " + otsuCorrect + "/64, フェルト誤認: 黒=" + otsuBlackMis + "マス, 白=" + otsuWhiteMis + "マス)");

                if (otsuAccuracy >= 98.0f)
                {
                    sb.AppendLine("      判定結果: [PASS] 完璧な認識精度 (正解率 " + otsuAccuracy.ToString("F1") + "%)");
                    passedTests++;
                }
                else
                {
                    sb.AppendLine("      判定結果: [FAIL] 正解率 " + otsuAccuracy.ToString("F1") + "%");
                }
            }

            sb.AppendLine("\n==========================================================================================");
            sb.AppendLine("  総合評価: " + passedTests + "/" + totalTests + " ケース PASS (成功率 " + ((float)passedTests / totalTests * 100f).ToString("F1") + "%)");
            sb.AppendLine("==========================================================================================");

            string finalReport = sb.ToString();
            Debug.Log(finalReport);

            string reportPath = "Assets/Editor/ThresholdClusteringExperimentReport.txt";
            System.IO.File.WriteAllText(reportPath, finalReport, Encoding.UTF8);
            Debug.Log("Experiment report written to: " + reportPath);
        }
    }
}