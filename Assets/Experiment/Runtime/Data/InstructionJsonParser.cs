using System;
using System.Collections.Generic;
using UnityEngine;

namespace AROthello.Experiment
{
    /// <summary>JSON 1 ファイル分の解析結果。</summary>
    public class InstructionParseResult
    {
        /// <summary>解析元の名前 (ファイル名など、ログ用)</summary>
        public string SourceName;
        /// <summary>パック JSON なら packId、単一パターンなら null</summary>
        public string PackId;
        /// <summary>検証を通過したパターン</summary>
        public readonly List<InstructionPatternData> Patterns = new List<InstructionPatternData>();
        /// <summary>JSON の構文エラーや、検証で除外したパターンの理由</summary>
        public readonly List<string> Errors = new List<string>();

        public bool HasErrors => Errors.Count > 0;
    }

    /// <summary>
    /// 単一パターン JSON と一括パック JSON (InstructionPackData) を自動判別して読み込む。
    /// 判別方法: トップレベルに "patterns" 配列があればパック、なければ単一パターン。
    /// </summary>
    public static class InstructionJsonParser
    {
        public static InstructionParseResult Parse(string json, string sourceName = "json")
        {
            var result = new InstructionParseResult { SourceName = sourceName };

            if (string.IsNullOrWhiteSpace(json))
            {
                result.Errors.Add($"{sourceName}: JSON text is empty");
                return result;
            }

            InstructionPackData pack;
            try
            {
                pack = JsonUtility.FromJson<InstructionPackData>(json);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"{sourceName}: invalid JSON ({ex.Message})");
                return result;
            }

            if (pack == null)
            {
                result.Errors.Add($"{sourceName}: JSON could not be parsed");
                return result;
            }

            List<InstructionPatternData> candidates;
            if (pack.patterns != null && pack.patterns.Length > 0)
            {
                result.PackId = pack.packId;
                candidates = new List<InstructionPatternData>(pack.patterns);
            }
            else if (!string.IsNullOrWhiteSpace(pack.packId))
            {
                result.PackId = pack.packId;
                result.Errors.Add($"{sourceName}: pack \"{pack.packId}\" has no patterns");
                return result;
            }
            else
            {
                InstructionPatternData single;
                try
                {
                    single = JsonUtility.FromJson<InstructionPatternData>(json);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{sourceName}: invalid JSON ({ex.Message})");
                    return result;
                }

                if (single == null || (string.IsNullOrWhiteSpace(single.patternId) && (single.steps == null || single.steps.Length == 0)))
                {
                    result.Errors.Add($"{sourceName}: neither a pattern (patternId/steps) nor a pack (patterns) was found");
                    return result;
                }
                candidates = new List<InstructionPatternData> { single };
            }

            var seenIds = new HashSet<string>();
            for (int i = 0; i < candidates.Count; i++)
            {
                InstructionPatternData p = candidates[i];
                List<string> errors = InstructionValidator.Validate(p);
                if (errors.Count > 0)
                {
                    foreach (string e in errors)
                    {
                        result.Errors.Add($"{sourceName}: {e}");
                    }
                    continue;
                }
                if (!seenIds.Add(p.patternId))
                {
                    result.Errors.Add($"{sourceName}: duplicate patternId \"{p.patternId}\" (later one ignored)");
                    continue;
                }
                result.Patterns.Add(p);
            }

            return result;
        }
    }
}
