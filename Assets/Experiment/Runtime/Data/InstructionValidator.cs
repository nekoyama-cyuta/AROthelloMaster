using System.Collections.Generic;

namespace AROthello.Experiment
{
    /// <summary>
    /// 単一パターンの整合性チェック。エラーが 1 件でもあればそのパターンは使用しない。
    /// </summary>
    public static class InstructionValidator
    {
        public const int MinDifficulty = 1;
        public const int MaxDifficulty = 3;

        /// <summary>
        /// パターンを検証し、エラーメッセージの一覧を返す (空なら有効)。
        /// あわせて null 配列を空配列に置き換える。
        /// </summary>
        public static List<string> Validate(InstructionPatternData pattern)
        {
            var errors = new List<string>();
            if (pattern == null)
            {
                errors.Add("pattern is null");
                return errors;
            }

            if (pattern.initialStones == null) pattern.initialStones = new InstructionStone[0];
            if (pattern.steps == null) pattern.steps = new InstructionStep[0];

            string id = string.IsNullOrWhiteSpace(pattern.patternId) ? "(no id)" : pattern.patternId;

            if (string.IsNullOrWhiteSpace(pattern.patternId))
            {
                errors.Add("patternId is empty");
            }

            if (pattern.difficulty < MinDifficulty || pattern.difficulty > MaxDifficulty)
            {
                errors.Add($"[{id}] difficulty {pattern.difficulty} is out of range {MinDifficulty}..{MaxDifficulty}");
            }

            if (pattern.steps.Length == 0)
            {
                errors.Add($"[{id}] steps is empty");
            }

            if (pattern.stepCount != pattern.steps.Length)
            {
                errors.Add($"[{id}] stepCount {pattern.stepCount} does not match steps.Length {pattern.steps.Length}");
            }

            // 盤上で使用済みのマス (初期石 + 手順) の重複検出用
            var occupied = new Dictionary<int, string>();

            for (int i = 0; i < pattern.initialStones.Length; i++)
            {
                InstructionStone s = pattern.initialStones[i];
                string label = $"[{id}] initialStones[{i}]";
                if (s == null)
                {
                    errors.Add($"{label} is null");
                    continue;
                }
                ValidateCell(label, s.col, s.row, s.coord, s.color, errors);
                RegisterCell(label, s.col, s.row, occupied, errors);
            }

            int firstStepNumber = pattern.steps.Length > 0 && pattern.steps[0] != null ? pattern.steps[0].step : 1;
            if (firstStepNumber != 0 && firstStepNumber != 1)
            {
                errors.Add($"[{id}] steps must start at step 0 or 1 (found {firstStepNumber})");
            }

            for (int i = 0; i < pattern.steps.Length; i++)
            {
                InstructionStep s = pattern.steps[i];
                string label = $"[{id}] steps[{i}]";
                if (s == null)
                {
                    errors.Add($"{label} is null");
                    continue;
                }
                if (s.step != firstStepNumber + i)
                {
                    errors.Add($"{label} step number {s.step} is not sequential (expected {firstStepNumber + i})");
                }
                ValidateCell(label, s.col, s.row, s.coord, s.color, errors);
                RegisterCell(label, s.col, s.row, occupied, errors);
            }

            return errors;
        }

        private static void ValidateCell(string label, int col, int row, string coord, string color, List<string> errors)
        {
            if (!BoardCoord.IsInside(col, row))
            {
                errors.Add($"{label} col/row ({col},{row}) is out of range 0..7");
            }
            else if (!string.IsNullOrWhiteSpace(coord))
            {
                string expected = BoardCoord.ToCoord(col, row);
                if (!string.Equals(coord.Trim(), expected, System.StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{label} coord \"{coord}\" does not match col/row ({col},{row}) = \"{expected}\"");
                }
            }

            if (!BoardCoord.TryParseColor(color, out _))
            {
                errors.Add($"{label} color \"{color}\" must be \"B\" or \"W\"");
            }
        }

        private static void RegisterCell(string label, int col, int row, Dictionary<int, string> occupied, List<string> errors)
        {
            if (!BoardCoord.IsInside(col, row)) return;
            int key = row * BoardCoord.Size + col;
            if (occupied.TryGetValue(key, out string owner))
            {
                errors.Add($"{label} cell {BoardCoord.ToCoord(col, row)} is already used by {owner}");
            }
            else
            {
                occupied[key] = label;
            }
        }
    }
}
