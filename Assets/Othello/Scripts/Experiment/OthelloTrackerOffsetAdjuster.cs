using AROthello.Experiment;
using UnityEngine;

namespace AROthello
{
    /// <summary>
    /// 実験者パネルの位置微調整を、既存の OthelloAutoTracker の位置オフセット補正 (±6cm) へ渡すアダプター。
    /// 調整は現在のオフセット座標系 (水平視線 / 盤面 / ワールド) に対して行われ、既存 UI のスライダーと同じ値を共有する。
    /// あわせて、実験者画面の「設定」(グラスの動きの向き・石の見分け直し・動作の記録) もトラッカーへ橋渡しする。
    /// </summary>
    public class OthelloTrackerOffsetAdjuster : MonoBehaviour, IBoardOffsetAdjuster, ITrackingSettings
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

        // ---------------- ITrackingSettings ----------------

        public bool IsTracking => Tracker != null && Tracker.IsTracking;

        public int OffsetBasis => Tracker != null ? (int)Tracker.CurrentOffsetMode : 0;

        public void SetOffsetBasis(int basis)
        {
            if (Tracker == null || basis < 0 || basis > 2) return;
            Tracker.SetOffsetMode((OthelloAutoTracker.OffsetCoordinateMode)basis);
        }

        public void ApplyTypicalOffset()
        {
            if (Tracker != null) Tracker.ApplyQuick3cmOffset();
        }

        public bool IsAxisInverted(TrackingAxis axis)
        {
            if (Tracker == null) return false;
            switch (axis)
            {
                case TrackingAxis.Height: return Tracker.InvertLocalY;
                case TrackingAxis.Pitch: return Tracker.InvertLocalRotX;
                case TrackingAxis.Yaw: return Tracker.InvertLocalRotY;
                default: return Tracker.InvertLocalRotZ;
            }
        }

        public void SetAxisInverted(TrackingAxis axis, bool inverted)
        {
            if (Tracker == null) return;
            switch (axis)
            {
                case TrackingAxis.Height: Tracker.InvertLocalY = inverted; break;
                case TrackingAxis.Pitch: Tracker.InvertLocalRotX = inverted; break;
                case TrackingAxis.Yaw: Tracker.InvertLocalRotY = inverted; break;
                default: Tracker.InvertLocalRotZ = inverted; break;
            }
        }

        public void RecalibrateStoneDetection()
        {
            if (Tracker != null) Tracker.RequestRecalibration();
        }

        public bool IsPerformanceLogging => PerformanceLogger.Instance != null && PerformanceLogger.Instance.IsLogging;

        public void TogglePerformanceLogging()
        {
            if (PerformanceLogger.Instance != null) PerformanceLogger.Instance.ToggleLogging();
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
