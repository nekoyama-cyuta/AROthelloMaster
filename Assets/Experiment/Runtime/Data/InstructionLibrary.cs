using System;
using System.Collections.Generic;

namespace AROthello.Experiment
{
    /// <summary>
    /// 読み込んだパターンの保持・検索・切り替え (Unity に依存しない純粋なロジック)。
    /// </summary>
    public class InstructionLibrary
    {
        private readonly List<InstructionPatternData> _patterns = new List<InstructionPatternData>();
        private readonly Dictionary<string, InstructionPatternData> _byId = new Dictionary<string, InstructionPatternData>();
        private readonly List<string> _errors = new List<string>();
        private int _currentIndex = -1;

        /// <summary>現在のパターンが変わったときに呼ばれる (null になる場合もある)。</summary>
        public event Action<InstructionPatternData> CurrentPatternChanged;

        public IReadOnlyList<InstructionPatternData> Patterns => _patterns;
        /// <summary>読み込み時のエラー (除外したパターンの理由など)</summary>
        public IReadOnlyList<string> Errors => _errors;
        public int Count => _patterns.Count;
        public InstructionPatternData Current => _currentIndex >= 0 && _currentIndex < _patterns.Count ? _patterns[_currentIndex] : null;

        public void Clear()
        {
            _patterns.Clear();
            _byId.Clear();
            _errors.Clear();
            SetCurrentIndex(-1);
        }

        /// <summary>解析結果を追加する。既に同じ patternId があれば後から来た方を無視する。</summary>
        public void Add(InstructionParseResult result)
        {
            if (result == null) return;
            _errors.AddRange(result.Errors);
            foreach (InstructionPatternData p in result.Patterns)
            {
                if (_byId.ContainsKey(p.patternId))
                {
                    _errors.Add($"{result.SourceName}: patternId \"{p.patternId}\" is already loaded (ignored)");
                    continue;
                }
                _byId[p.patternId] = p;
                _patterns.Add(p);
            }
            if (_currentIndex < 0 && _patterns.Count > 0)
            {
                SetCurrentIndex(0);
            }
        }

        /// <summary>JSON 文字列を解析して追加する。</summary>
        public InstructionParseResult AddJson(string json, string sourceName = "json")
        {
            InstructionParseResult result = InstructionJsonParser.Parse(json, sourceName);
            Add(result);
            return result;
        }

        public InstructionPatternData GetById(string patternId)
        {
            if (string.IsNullOrEmpty(patternId)) return null;
            _byId.TryGetValue(patternId, out InstructionPatternData p);
            return p;
        }

        /// <summary>条件に合うパターンを読み込み順で返す。引数が null の条件は無視する。</summary>
        public List<InstructionPatternData> Find(int? difficulty = null, int? stepCount = null)
        {
            var list = new List<InstructionPatternData>();
            foreach (InstructionPatternData p in _patterns)
            {
                if (difficulty.HasValue && p.difficulty != difficulty.Value) continue;
                if (stepCount.HasValue && p.stepCount != stepCount.Value) continue;
                list.Add(p);
            }
            return list;
        }

        public InstructionPatternData FindFirst(int difficulty, int stepCount)
        {
            List<InstructionPatternData> list = Find(difficulty, stepCount);
            return list.Count > 0 ? list[0] : null;
        }

        public bool Select(string patternId)
        {
            InstructionPatternData p = GetById(patternId);
            if (p == null) return false;
            SetCurrentIndex(_patterns.IndexOf(p));
            return true;
        }

        public bool Select(int difficulty, int stepCount)
        {
            InstructionPatternData p = FindFirst(difficulty, stepCount);
            if (p == null) return false;
            SetCurrentIndex(_patterns.IndexOf(p));
            return true;
        }

        /// <summary>次のパターンへ (末尾の次は先頭)。</summary>
        public InstructionPatternData SelectNext()
        {
            if (_patterns.Count == 0) return null;
            SetCurrentIndex((_currentIndex + 1) % _patterns.Count);
            return Current;
        }

        /// <summary>前のパターンへ (先頭の前は末尾)。</summary>
        public InstructionPatternData SelectPrevious()
        {
            if (_patterns.Count == 0) return null;
            SetCurrentIndex((_currentIndex - 1 + _patterns.Count) % _patterns.Count);
            return Current;
        }

        private void SetCurrentIndex(int index)
        {
            if (index == _currentIndex) return;
            _currentIndex = index;
            CurrentPatternChanged?.Invoke(Current);
        }
    }
}
