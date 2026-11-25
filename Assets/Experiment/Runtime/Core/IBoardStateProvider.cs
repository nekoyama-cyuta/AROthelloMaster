using System;

namespace AROthello.Experiment
{
    /// <summary>
    /// 盤面認識結果の供給元。実機では既存の OthelloAutoTracker に接続したアダプター、
    /// PC ではモック (MockBoardStateProvider) が実装する。
    /// </summary>
    public interface IBoardStateProvider
    {
        /// <summary>
        /// 盤面を認識するたびに呼ばれる。
        /// 引数: 盤面 (呼び出し後に内容が書き換わる可能性があるため、保持する側でコピーすること),
        ///       認識した時刻 (秒、単調増加。Time.realtimeSinceStartupAsDouble など)。
        /// </summary>
        event Action<BoardState, double> BoardUpdated;
    }
}
