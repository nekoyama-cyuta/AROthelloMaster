using System;

namespace AROthello.Experiment
{
    /// <summary>石の色 (実験モジュール内の表現。既存の DiscColor とは独立)。</summary>
    public enum StoneColor
    {
        None = 0,
        Black = 1,
        White = 2
    }

    /// <summary>
    /// 盤面座標と色表記の変換。
    /// 座標表記は「列 = A..H (col 0..7)、行 = 1..8 (row 0..7)」。例: col 3, row 2 → "D3"。
    /// row 0 は盤の奥側 (カメラ画像の上側) で、既存の認識結果 DiscColor[row, col] と同じ向き。
    /// </summary>
    public static class BoardCoord
    {
        public const int Size = 8;

        public static bool IsInside(int col, int row)
        {
            return col >= 0 && col < Size && row >= 0 && row < Size;
        }

        public static string ToCoord(int col, int row)
        {
            if (!IsInside(col, row)) return "??";
            return $"{(char)('A' + col)}{row + 1}";
        }

        /// <summary>"D3" のような表記を (col, row) に変換する。大文字・小文字は区別しない。</summary>
        public static bool TryParseCoord(string coord, out int col, out int row)
        {
            col = -1;
            row = -1;
            if (string.IsNullOrWhiteSpace(coord)) return false;
            string s = coord.Trim().ToUpperInvariant();
            if (s.Length != 2) return false;
            int c = s[0] - 'A';
            int r = s[1] - '1';
            if (!IsInside(c, r)) return false;
            col = c;
            row = r;
            return true;
        }

        /// <summary>"B" → Black, "W" → White。前後の空白と大文字・小文字は無視する。</summary>
        public static bool TryParseColor(string color, out StoneColor result)
        {
            result = StoneColor.None;
            if (string.IsNullOrWhiteSpace(color)) return false;
            switch (color.Trim().ToUpperInvariant())
            {
                case "B":
                    result = StoneColor.Black;
                    return true;
                case "W":
                    result = StoneColor.White;
                    return true;
                default:
                    return false;
            }
        }

        public static string ToColorCode(StoneColor color)
        {
            switch (color)
            {
                case StoneColor.Black: return "B";
                case StoneColor.White: return "W";
                default: return "-";
            }
        }
    }
}
