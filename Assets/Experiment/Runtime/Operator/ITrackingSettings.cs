namespace AROthello.Experiment
{
    /// <summary>グラスの動きの向きを補正する軸 (表示が逆に動くときに反転する)。</summary>
    public enum TrackingAxis
    {
        /// <summary>上下に動かしたとき (位置 Y)</summary>
        Height = 0,
        /// <summary>上下を向いたとき (回転 X)</summary>
        Pitch = 1,
        /// <summary>左右を向いたとき (回転 Y)</summary>
        Yaw = 2,
        /// <summary>首をかしげたとき (回転 Z)</summary>
        Roll = 3
    }

    /// <summary>
    /// 実験者画面 (Beam Pro) の「設定」から触る、トラッキング側の機能。
    /// 実験システムはトラッカーの実装を知らずに済むよう、このインターフェース越しに呼ぶ。
    /// </summary>
    public interface ITrackingSettings
    {
        /// <summary>いま盤を認識できているか</summary>
        bool IsTracking { get; }

        /// <summary>位置合わせで動かす向きの基準 (0 = 自分から見た向き, 1 = 盤の向き, 2 = 部屋の向き)</summary>
        int OffsetBasis { get; }
        void SetOffsetBasis(int basis);

        /// <summary>よくあるずれ (表示が奥に 3cm ずれる) を直す。</summary>
        void ApplyTypicalOffset();

        bool IsAxisInverted(TrackingAxis axis);
        void SetAxisInverted(TrackingAxis axis, bool inverted);

        /// <summary>石の白黒を見分けるしきい値を、いまの映像から取り直す。</summary>
        void RecalibrateStoneDetection();

        /// <summary>動作の記録 (フレームレートなど) の最中か</summary>
        bool IsPerformanceLogging { get; }
        void TogglePerformanceLogging();
    }
}
