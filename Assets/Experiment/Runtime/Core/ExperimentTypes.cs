using System.Collections.Generic;

namespace AROthello.Experiment
{
    /// <summary>実験の状態。</summary>
    public enum ExperimentState
    {
        /// <summary>待機中 (パターン選択可能)</summary>
        Idle = 0,
        /// <summary>キャリブレーション中 (AR 位置合わせ・閾値調整の確認)</summary>
        Calibration = 1,
        /// <summary>開始前の盤面チェック中 (Lv.3 のダミー石、Lv.1/2 は空盤面)</summary>
        InitialBoardCheck = 2,
        /// <summary>試行中</summary>
        Running = 3,
        /// <summary>全手順が完了</summary>
        Completed = 4,
        /// <summary>開始前の盤面がそろい、実験者が被験者・試行番号を決めて開始するのを待っている (RequireSetupBeforeRunning のとき)</summary>
        Setup = 5
    }

    /// <summary>盤面の誤り。</summary>
    public enum PlacementErrorType
    {
        None = 0,
        /// <summary>指定マスに指定と逆の色が置かれた</summary>
        WrongColor = 1,
        /// <summary>指定外の空きマスに石が置かれた</summary>
        WrongPosition = 2,
        /// <summary>置き済み (または初期石) の石が無くなった</summary>
        MissingStone = 3,
        /// <summary>置き済み (または初期石) の石の色が変わった</summary>
        ChangedStone = 4
    }

    /// <summary>期待した盤面と異なるマス 1 つ分。</summary>
    public struct CellMismatch
    {
        public int Col;
        public int Row;
        public StoneColor Expected;
        public StoneColor Actual;

        public string Coord => BoardCoord.ToCoord(Col, Row);

        public override string ToString()
        {
            return $"{Coord}: expected {BoardCoord.ToColorCode(Expected)}, actual {BoardCoord.ToColorCode(Actual)}";
        }
    }

    /// <summary>確定した誤り (デバウンス後)。</summary>
    public class PlacementError
    {
        /// <summary>誤りが起きた手順 (0 始まりのインデックス)</summary>
        public int StepIndex;
        /// <summary>代表的な誤りの種類 (複数あるときは WrongColor > WrongPosition > MissingStone > ChangedStone の順)</summary>
        public PlacementErrorType Type;
        /// <summary>最初に誤りを検知した時刻</summary>
        public double FirstDetectTime;
        /// <summary>誤りとして確定した時刻</summary>
        public double ConfirmTime;
        public List<CellMismatch> Mismatches = new List<CellMismatch>();
    }

    /// <summary>手順 1 つ分の結果。</summary>
    public class StepResult
    {
        /// <summary>0 始まりのインデックス</summary>
        public int StepIndex;
        public InstructionStep Step;
        /// <summary>この手順のガイドを表示した時刻 (= 前の手順の確定時刻、または試行開始時刻)</summary>
        public double GuideShownTime;
        /// <summary>正解を最初に検知した時刻 (T_first_detect)</summary>
        public double FirstDetectTime;
        /// <summary>デバウンスを経て確定した時刻</summary>
        public double ConfirmTime;
        /// <summary>手順の所要時間 = 今回の T_first_detect − 前の手順の T_first_detect (1 手目は試行開始時刻から)</summary>
        public double StepTimeSec;
        /// <summary>この手順で確定した誤りの回数</summary>
        public int ErrorCount;
        /// <summary>この手順で確定した誤りの種類 (発生順)</summary>
        public List<PlacementErrorType> ErrorTypes = new List<PlacementErrorType>();
        /// <summary>実験者の手動操作で進めた場合 true (Phase 5)</summary>
        public bool IsOverride;
        /// <summary>この手順に付ける備考 (手動操作の理由など)。CSV の note 列に出る。</summary>
        public string Note = "";
    }

    /// <summary>試行 1 回分の結果。</summary>
    public class TrialResult
    {
        public InstructionPatternData Pattern;
        /// <summary>試行開始時刻 (開始前チェックの確定時刻)</summary>
        public double StartTime;
        /// <summary>最後の手順の T_first_detect</summary>
        public double EndTime;
        /// <summary>総作業時間 (TCT) = EndTime − StartTime</summary>
        public double TotalTimeSec;
        public int TotalErrorCount;
        /// <summary>開始前チェックを実験者が省略して開始した場合 true</summary>
        public bool InitialCheckOverridden;
        public List<StepResult> Steps = new List<StepResult>();
    }
}
