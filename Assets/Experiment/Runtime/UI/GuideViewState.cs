using System.Collections.Generic;

namespace AROthello.Experiment
{
    /// <summary>マス 1 つに出す目印の種類。</summary>
    public enum GuideMarkerKind
    {
        /// <summary>今置くべきマス (色付きのリング + 中央ドット、またはゴースト石)</summary>
        Target = 0,
        /// <summary>誤りのマス (赤の点滅)</summary>
        Error = 1,
        /// <summary>開始前チェックで、ここに石を置く必要がある (ゴースト石)</summary>
        SetupPlace = 2,
        /// <summary>開始前チェックで、ここの石を取り除く必要がある (赤)</summary>
        SetupRemove = 3
    }

    /// <summary>盤上の目印 1 つ分。</summary>
    public struct GuideMarker
    {
        public int Col;
        public int Row;
        public GuideMarkerKind Kind;
        /// <summary>Target / SetupPlace で置くべき色</summary>
        public StoneColor Color;

        public override string ToString() => $"{Kind} {BoardCoord.ToCoord(Col, Row)} {BoardCoord.ToColorCode(Color)}";
    }

    /// <summary>ガイド UI に表示する内容 (描画方法には依存しない)。</summary>
    public class GuideViewState
    {
        public ExperimentState State;
        /// <summary>盤上の目印。範囲外の座標は含めない。</summary>
        public readonly List<GuideMarker> Markers = new List<GuideMarker>();
        /// <summary>視界の端に出す短い文字列 (例: "3 / 10")。空なら非表示。</summary>
        public string ProgressText = "";
        /// <summary>現在の手順 (1 始まり)。Running 以外は 0。</summary>
        public int StepNumber;
        public int StepTotal;
        public bool IsErrorActive;
        public bool IsCompleted;

        public bool HasTarget
        {
            get
            {
                foreach (GuideMarker m in Markers) if (m.Kind == GuideMarkerKind.Target) return true;
                return false;
            }
        }
    }
}
