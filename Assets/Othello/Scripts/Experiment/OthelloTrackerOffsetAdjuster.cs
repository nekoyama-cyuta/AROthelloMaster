using AROthello.Experiment;
using UnityEngine;

namespace AROthello
{
    /// <summary>
    /// 実験者パネルの位置微調整を、既存の OthelloAutoTracker の位置オフセット補正 (±6cm) へ渡すアダプター。
    /// 調整は現在のオフセット座標系 (水平視線 / 盤面 / ワールド) に対して行われ、既存 UI のスライダーと同じ値を共有する。
    /// </summary>
    public class OthelloTrackerOffsetAdjuster : MonoBehaviour, IBoardOffsetAdjuster
    {
        private const float MaxOffsetCm = 6f;

        [Tooltip("位置オフセットを持つトラッカー (未設定ならシーンから探す)")]
        [SerializeField] private OthelloAutoTracker tracker;

        private OthelloAutoTracker Tracker
        {
            get
            {
                if (tracker == null) tracker = FindFirstObjectByType<OthelloAutoTracker>();
                return tracker;
            }
        }

        public string ModeName
        {
            get
            {
                if (Tracker == null) return "(トラッカーなし)";
                switch (Tracker.CurrentOffsetMode)
                {
                    case OthelloAutoTracker.OffsetCoordinateMode.ViewHorizontal: return "水平視線系";
                    case OthelloAutoTracker.OffsetCoordinateMode.BoardLocal: return "盤面ローカル系";
                    default: return "ワールド系";
                }
            }
        }

        public Vector3 CurrentOffsetCm => GetOffsetMeters() * 100f;

        public void Nudge(int axis, float deltaCm)
        {
            if (Tracker == null || axis < 0 || axis > 2) return;
            Vector3 cm = CurrentOffsetCm;
            cm[axis] = Mathf.Clamp(cm[axis] + deltaCm, -MaxOffsetCm, MaxOffsetCm);
            SetOffsetMeters(cm * 0.01f);
        }

        public void ResetOffset()
        {
            if (Tracker != null) Tracker.ResetCurrentOffsets();
        }

        private Vector3 GetOffsetMeters()
        {
            if (Tracker == null) return Vector3.zero;
            switch (Tracker.CurrentOffsetMode)
            {
                case OthelloAutoTracker.OffsetCoordinateMode.ViewHorizontal: return Tracker.ViewHorizontalOffset;
                case OthelloAutoTracker.OffsetCoordinateMode.BoardLocal: return Tracker.BoardLocalOffset;
                default: return Tracker.WorldAbsoluteOffset;
            }
        }

        private void SetOffsetMeters(Vector3 value)
        {
            // 各プロパティの setter が PlayerPrefs への保存と既存 UI の更新を行う
            switch (Tracker.CurrentOffsetMode)
            {
                case OthelloAutoTracker.OffsetCoordinateMode.ViewHorizontal: Tracker.ViewHorizontalOffset = value; break;
                case OthelloAutoTracker.OffsetCoordinateMode.BoardLocal: Tracker.BoardLocalOffset = value; break;
                default: Tracker.WorldAbsoluteOffset = value; break;
            }
        }
    }
}
