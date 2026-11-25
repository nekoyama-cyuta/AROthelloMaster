using System;
using AROthello.Experiment;
using UnityEngine;

namespace AROthello
{
    /// <summary>
    /// 既存の盤面認識 (OthelloAutoTracker.BoardRecognized) を実験システムの IBoardStateProvider へ変換するアダプター。
    /// 既存コードへの変更はイベント 1 つの追加のみで、認識処理そのものには手を入れない。
    /// </summary>
    public class OthelloTrackerBoardStateProvider : MonoBehaviour, IBoardStateProvider
    {
        [Tooltip("盤面認識の元 (未設定ならシーンから探す)")]
        [SerializeField] private OthelloAutoTracker tracker;

        private readonly BoardState _board = new BoardState();

        public event Action<BoardState, double> BoardUpdated;

        private void OnEnable()
        {
            if (tracker == null) tracker = FindFirstObjectByType<OthelloAutoTracker>();
            if (tracker != null) tracker.BoardRecognized += OnBoardRecognized;
            else Debug.LogWarning("[OthelloTrackerBoardStateProvider] OthelloAutoTracker was not found.");
        }

        private void OnDisable()
        {
            if (tracker != null) tracker.BoardRecognized -= OnBoardRecognized;
        }

        private void OnBoardRecognized(DiscColor[,] recognized)
        {
            if (recognized == null) return;
            for (int r = 0; r < BoardState.Size; r++)
            {
                for (int c = 0; c < BoardState.Size; c++)
                {
                    _board.Set(c, r, Convert(recognized[r, c]));
                }
            }
            BoardUpdated?.Invoke(_board, Time.realtimeSinceStartupAsDouble);
        }

        private static StoneColor Convert(DiscColor d)
        {
            switch (d)
            {
                case DiscColor.Black: return StoneColor.Black;
                case DiscColor.White: return StoneColor.White;
                default: return StoneColor.None;
            }
        }
    }
}
