using System;

namespace AROthello.Experiment
{
    /// <summary>
    /// 作業指示書ジェネレーターが出力する JSON (JsonUtility 互換) のデータモデル。
    /// フィールド名は JSON のキー名と一致させる必要があるため変更しないこと。
    /// </summary>
    [Serializable]
    public class InstructionStone
    {
        /// <summary>列 (0..7, 0 = A)</summary>
        public int col;
        /// <summary>行 (0..7, 0 = 1)</summary>
        public int row;
        /// <summary>表記用座標 (例: "D3")。空なら検証を省略する。</summary>
        public string coord;
        /// <summary>"B" (黒) または "W" (白)</summary>
        public string color;
    }

    /// <summary>手順 1 手分。</summary>
    [Serializable]
    public class InstructionStep
    {
        /// <summary>手順番号 (0 または 1 始まりの連番)</summary>
        public int step;
        public int col;
        public int row;
        public string coord;
        public string color;
    }

    /// <summary>単一パターン JSON。</summary>
    [Serializable]
    public class InstructionPatternData
    {
        public string patternId;
        /// <summary>難易度 (1..3)</summary>
        public int difficulty;
        /// <summary>手数 (steps の要素数と一致すること)</summary>
        public int stepCount;
        /// <summary>Lv.3 用のダミー石 (実験開始前に盤上にあるべき石)。無い場合は空配列。</summary>
        public InstructionStone[] initialStones = new InstructionStone[0];
        public InstructionStep[] steps = new InstructionStep[0];

        public bool HasInitialStones => initialStones != null && initialStones.Length > 0;

        public override string ToString()
        {
            return $"{patternId} (Lv.{difficulty}, {stepCount} steps)";
        }
    }

    /// <summary>一括パック JSON。</summary>
    [Serializable]
    public class InstructionPackData
    {
        public string packId;
        public InstructionPatternData[] patterns = new InstructionPatternData[0];
    }
}
