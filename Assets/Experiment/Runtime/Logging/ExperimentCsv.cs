using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AROthello.Experiment
{
    /// <summary>被験者・条件など、試行ごとに実験者が設定する情報。</summary>
    [Serializable]
    public class TrialMetadata
    {
        public string subjectId = "";
        /// <summary>被験者の名前 (評価 CSV に出す)</summary>
        public string subjectName = "";
        public string condition = "";
        /// <summary>試行番号 = その被験者の何回目の試行か (1 始まり)。CSV の order 列に出る。</summary>
        public int order = 1;
        /// <summary>被験者・条件・試行番号から作る ID (例: S01-AR-T03)。全ログに出る。</summary>
        public string experimentId = "";
        public string note = "";

        public TrialMetadata Clone()
        {
            return new TrialMetadata
            {
                subjectId = subjectId, subjectName = subjectName, condition = condition, order = order,
                experimentId = experimentId, note = note
            };
        }
    }

    /// <summary>
    /// 実験ログ CSV の書式。1 行 = 1 手順。数値は InvariantCulture で小数第 2 位まで。
    /// </summary>
    public static class ExperimentCsv
    {
        public const string StepHeader =
            "subject_id,condition,order,pattern_id,difficulty,step_count,step,target_coord,target_color," +
            "step_time_sec,error_type,is_override,avg_fps,note,experiment_id";

        public const string SummaryHeader =
            "subject_id,condition,order,pattern_id,difficulty,step_count,tct_sec,total_errors,override_count," +
            "avg_fps,started_at,completed_at,note,experiment_id";

        /// <summary>誤りなしのときの error_type</summary>
        public const string NoError = "none";
        /// <summary>1 手順で複数の誤りがあったときの区切り</summary>
        public const string ErrorSeparator = "|";
        /// <summary>開始前チェックを省略した試行のサマリー note</summary>
        public const string InitialCheckOverriddenNote = "initial check overridden";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0;
            return value.ToString("F2", Inv);
        }

        public static string ErrorTypeCode(PlacementErrorType type)
        {
            switch (type)
            {
                case PlacementErrorType.WrongColor: return "wrong_color";
                case PlacementErrorType.WrongPosition: return "wrong_position";
                case PlacementErrorType.MissingStone: return "missing_stone";
                case PlacementErrorType.ChangedStone: return "changed_stone";
                default: return NoError;
            }
        }

        public static string FormatErrorTypes(IList<PlacementErrorType> types)
        {
            if (types == null || types.Count == 0) return NoError;
            var parts = new List<string>(types.Count);
            foreach (PlacementErrorType t in types) parts.Add(ErrorTypeCode(t));
            return string.Join(ErrorSeparator, parts);
        }

        /// <summary>CSV のフィールドとして安全な文字列にする (カンマ・引用符・改行を含む場合は引用符で囲む)。</summary>
        public static string Escape(string field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            bool needsQuote = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || field.Trim() != field;
            return needsQuote ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
        }

        public static string JoinNotes(string a, string b)
        {
            bool hasA = !string.IsNullOrWhiteSpace(a), hasB = !string.IsNullOrWhiteSpace(b);
            if (hasA && hasB) return a.Trim() + " / " + b.Trim();
            return hasA ? a.Trim() : hasB ? b.Trim() : "";
        }

        public static string FormatStepRow(TrialMetadata meta, InstructionPatternData pattern, StepResult step, double avgFps)
        {
            meta = meta ?? new TrialMetadata();
            InstructionStep s = step?.Step;
            string coord = s != null ? BoardCoord.ToCoord(s.col, s.row) : "";
            string color = "";
            if (s != null && BoardCoord.TryParseColor(s.color, out StoneColor c)) color = BoardCoord.ToColorCode(c);

            var fields = new[]
            {
                Escape(meta.subjectId),
                Escape(meta.condition),
                meta.order.ToString(Inv),
                Escape(pattern?.patternId),
                (pattern?.difficulty ?? 0).ToString(Inv),
                (pattern?.stepCount ?? 0).ToString(Inv),
                (s?.step ?? 0).ToString(Inv),
                coord,
                color,
                FormatNumber(step?.StepTimeSec ?? 0),
                FormatErrorTypes(step?.ErrorTypes),
                step != null && step.IsOverride ? "1" : "0",
                FormatNumber(avgFps),
                Escape(JoinNotes(meta.note, step?.Note)),
                Escape(meta.experimentId)
            };
            return string.Join(",", fields);
        }

        public static string FormatSummaryRow(TrialMetadata meta, TrialResult trial, double avgFps, DateTime startedAt, DateTime completedAt)
        {
            meta = meta ?? new TrialMetadata();
            int overrides = 0;
            if (trial?.Steps != null) foreach (StepResult s in trial.Steps) if (s.IsOverride) overrides++;
            InstructionPatternData p = trial?.Pattern;

            var fields = new[]
            {
                Escape(meta.subjectId),
                Escape(meta.condition),
                meta.order.ToString(Inv),
                Escape(p?.patternId),
                (p?.difficulty ?? 0).ToString(Inv),
                (p?.stepCount ?? 0).ToString(Inv),
                FormatNumber(trial?.TotalTimeSec ?? 0),
                (trial?.TotalErrorCount ?? 0).ToString(Inv),
                overrides.ToString(Inv),
                FormatNumber(avgFps),
                startedAt.ToString("yyyy-MM-ddTHH:mm:ss", Inv),
                completedAt.ToString("yyyy-MM-ddTHH:mm:ss", Inv),
                Escape(JoinNotes(meta.note, trial != null && trial.InitialCheckOverridden ? InitialCheckOverriddenNote : null)),
                Escape(meta.experimentId)
            };
            return string.Join(",", fields);
        }

        /// <summary>ステップ CSV 全体 (ヘッダー + 手順数ぶんの行)。stepFps は手順ごとの平均 FPS (足りなければ 0)。</summary>
        public static string BuildStepCsv(TrialMetadata meta, TrialResult trial, IList<double> stepFps)
        {
            var sb = new StringBuilder();
            sb.Append(StepHeader).Append('\n');
            if (trial?.Steps != null)
            {
                for (int i = 0; i < trial.Steps.Count; i++)
                {
                    double fps = stepFps != null && i < stepFps.Count ? stepFps[i] : 0;
                    sb.Append(FormatStepRow(meta, trial.Pattern, trial.Steps[i], fps)).Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// CSV テキスト全体を行ごとのフィールドに分ける (引用符・引用符内のカンマや改行に対応)。
        /// 先頭の BOM は取り除く。空行は飛ばす。
        /// </summary>
        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(text)) return rows;
            if (text[0] == '\uFEFF') text = text.Substring(1);

            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false, any = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(ch);
                    continue;
                }
                switch (ch)
                {
                    case '"': quoted = true; any = true; break;
                    case ',': fields.Add(sb.ToString()); sb.Clear(); any = true; break;
                    case '\r': break;
                    case '\n':
                        if (any || sb.Length > 0) { fields.Add(sb.ToString()); rows.Add(fields.ToArray()); }
                        fields.Clear(); sb.Clear(); any = false;
                        break;
                    default: sb.Append(ch); any = true; break;
                }
            }
            if (any || sb.Length > 0) { fields.Add(sb.ToString()); rows.Add(fields.ToArray()); }
            return rows;
        }

        /// <summary>見出し行から 列名 → 位置 の表を作る。</summary>
        public static Dictionary<string, int> IndexHeader(string[] header)
        {
            var map = new Dictionary<string, int>();
            if (header == null) return map;
            for (int i = 0; i < header.Length; i++)
            {
                string key = header[i].Trim();
                if (!map.ContainsKey(key)) map[key] = i;
            }
            return map;
        }

        /// <summary>列名で値を取る (列が無い・行が短いときは空)。</summary>
        public static string Field(string[] row, Dictionary<string, int> col, string name)
        {
            return row != null && col != null && col.TryGetValue(name, out int i) && i < row.Length ? row[i] : "";
        }

        /// <summary>日時の書式 (ISO 8601、秒まで)。</summary>
        public static string FormatTime(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ss", Inv);

        /// <summary>ファイル名に使えない文字を '_' に置き換える。空なら fallback。</summary>
        public static string SanitizeFileName(string name, string fallback = "na")
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            var sb = new StringBuilder(name.Length);
            foreach (char ch in name.Trim())
            {
                bool ok = char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.';
                sb.Append(ok ? ch : '_');
            }
            return sb.ToString();
        }
    }
}
