using System;
using UnityEngine;

namespace AROthello.Experiment
{
    /// <summary>
    /// 実機なしで動作確認するためのダミー盤面入力。
    /// Board を書き換えて Publish すると、カメラで認識したのと同じように BoardUpdated が通知される。
    /// publishEveryFrame が true なら、認識が毎フレーム続いている状況を再現する。
    /// </summary>
    public class MockBoardStateProvider : MonoBehaviour, IBoardStateProvider
    {
        [Tooltip("Update ごとに現在の盤面を通知する (カメラ認識の連続入力を再現)")]
        [SerializeField] private bool publishEveryFrame = true;

        private readonly BoardState _board = new BoardState();

        public event Action<BoardState, double> BoardUpdated;

        /// <summary>ダミーの盤面。直接書き換えてよい。</summary>
        public BoardState Board => _board;

        public bool PublishEveryFrame
        {
            get => publishEveryFrame;
            set => publishEveryFrame = value;
        }

        private void Update()
        {
            if (publishEveryFrame) Publish(Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>現在の盤面を指定時刻で通知する。</summary>
        public void Publish(double time)
        {
            BoardUpdated?.Invoke(_board, time);
        }

        public void SetCell(int col, int row, StoneColor color)
        {
            _board.Set(col, row, color);
        }

        public void SetBoard(BoardState board)
        {
            _board.CopyFrom(board);
        }

        public void ClearBoard()
        {
            _board.Clear();
        }

        /// <summary>手順どおりに石を置く。</summary>
        public void Place(InstructionStep step)
        {
            if (step == null) return;
            BoardCoord.TryParseColor(step.color, out StoneColor c);
            _board.Set(step.col, step.row, c);
        }

        /// <summary>初期石をすべて置く。</summary>
        public void PlaceInitialStones(InstructionPatternData pattern)
        {
            if (pattern?.initialStones == null) return;
            foreach (InstructionStone s in pattern.initialStones)
            {
                BoardCoord.TryParseColor(s.color, out StoneColor c);
                _board.Set(s.col, s.row, c);
            }
        }
    }
}
