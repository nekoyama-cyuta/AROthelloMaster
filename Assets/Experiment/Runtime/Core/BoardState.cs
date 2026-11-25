using System;
using System.Text;

namespace AROthello.Experiment
{
    /// <summary>
    /// 8x8 盤面のスナップショット。添字は [row, col] (row 0 = 盤の奥側 / 座標 "x1", col 0 = "A")。
    /// </summary>
    public class BoardState
    {
        public const int Size = BoardCoord.Size;

        private readonly StoneColor[,] _cells = new StoneColor[Size, Size];

        public BoardState() { }

        /// <summary>[row, col] の配列からコピーして作る。サイズが 8x8 でなければ例外。</summary>
        public BoardState(StoneColor[,] cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (cells.GetLength(0) != Size || cells.GetLength(1) != Size)
                throw new ArgumentException("cells must be 8x8", nameof(cells));
            Array.Copy(cells, _cells, cells.Length);
        }

        /// <summary>範囲外は None を返す。</summary>
        public StoneColor Get(int col, int row)
        {
            return BoardCoord.IsInside(col, row) ? _cells[row, col] : StoneColor.None;
        }

        /// <summary>範囲外は無視する。</summary>
        public void Set(int col, int row, StoneColor color)
        {
            if (BoardCoord.IsInside(col, row)) _cells[row, col] = color;
        }

        public void Clear()
        {
            Array.Clear(_cells, 0, _cells.Length);
        }

        public void CopyFrom(BoardState other)
        {
            if (other == null) { Clear(); return; }
            Array.Copy(other._cells, _cells, _cells.Length);
        }

        public BoardState Clone()
        {
            var b = new BoardState();
            b.CopyFrom(this);
            return b;
        }

        public int CountStones()
        {
            int n = 0;
            foreach (StoneColor c in _cells) if (c != StoneColor.None) n++;
            return n;
        }

        /// <summary>
        /// テストやデバッグ用。8 行の文字列 (row 0 が先頭) から作る。
        /// 'B' = 黒, 'W' = 白, それ以外 ('.' など) = 空。空白は無視する。
        /// </summary>
        public static BoardState Parse(params string[] rows)
        {
            if (rows == null || rows.Length != Size) throw new ArgumentException("8 rows are required", nameof(rows));
            var b = new BoardState();
            for (int r = 0; r < Size; r++)
            {
                string line = (rows[r] ?? "").Replace(" ", "");
                if (line.Length != Size) throw new ArgumentException($"row {r} must have 8 cells: \"{rows[r]}\"", nameof(rows));
                for (int c = 0; c < Size; c++)
                {
                    char ch = char.ToUpperInvariant(line[c]);
                    b._cells[r, c] = ch == 'B' ? StoneColor.Black : ch == 'W' ? StoneColor.White : StoneColor.None;
                }
            }
            return b;
        }

        public override string ToString()
        {
            var sb = new StringBuilder(Size * (Size + 1));
            for (int r = 0; r < Size; r++)
            {
                for (int c = 0; c < Size; c++)
                {
                    StoneColor s = _cells[r, c];
                    sb.Append(s == StoneColor.Black ? 'B' : s == StoneColor.White ? 'W' : '.');
                }
                if (r < Size - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
