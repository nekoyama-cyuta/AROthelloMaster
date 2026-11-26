using System.Collections.Generic;
using UnityEngine;

namespace AROthello
{
    /// <summary>
    /// 被験者実験用の表示プロファイル。既存の AR デバッグ表示のうち、実験に不要で視覚的クラッターになるものを隠す。
    /// 既存コードは変更せず、Start (全 Awake 後 = 盤面ビジュアライザーの動的オブジェクト生成後) に適用する。
    /// 無効化 (OnDisable) すると元の表示に戻るので、デバッグ時はこのコンポーネントを切ればよい。
    /// </summary>
    public class ExperimentDisplayProfile : MonoBehaviour
    {
        [Tooltip("AR 盤面ビジュアライザー (未設定ならシーンから探す)")]
        [SerializeField] private OthelloBoardVisualizer boardVisualizer;

        [Header("AR 盤面 (グラス)")]
        [Tooltip("合法手 (黄色ハイライト・ミニ盤面の黄色・Legal 表記) を出さない。合法手の計算自体も止める")]
        [SerializeField] private bool hideLegalMoves = true;
        [Tooltip("認識した石の白/シアンのドットを出さない (認識結果は Beam Pro のミニ盤面で確認できる)")]
        [SerializeField] private bool hideDetectedStoneDots = true;
        [Tooltip("緑のワイヤーフレーム盤 (グリッド線) を出さない。位置合わせは四隅ドットで確認する")]
        [SerializeField] private bool hideBoardGrid = true;
        [Tooltip("盤の前方を示す黄色の棒を出さない")]
        [SerializeField] private bool hideForwardIndicator = true;

        [Header("その他に隠すオブジェクト")]
        [Tooltip("グラス側のカメラプレビュー・ログ表示など、被験者の視界に入る不要な UI")]
        [SerializeField] private GameObject[] hiddenObjects = new GameObject[0];

        public static readonly string[] DetectedStoneContainers = { "Cells_Container" };
        public static readonly string[] BoardGridContainers = { "GridLines_Container" };
        public const string ForwardIndicatorName = "ForwardIndicator";

        private readonly List<GameObject> _hidden = new List<GameObject>();
        private bool _legalMovesWereShown;
        private bool _applied;
        private bool _wasStarted;

        public bool IsApplied => _applied;

        private OthelloBoardVisualizer Visualizer
        {
            get
            {
                if (boardVisualizer == null) boardVisualizer = FindFirstObjectByType<OthelloBoardVisualizer>();
                return boardVisualizer;
            }
        }

        private void Start()
        {
            Apply();
        }

        private void OnEnable()
        {
            // 再有効化時のみ (初回は Start で、ビジュアライザーの初期化後に適用する)
            if (_wasStarted) Apply();
        }

        private void OnDisable()
        {
            Restore();
        }

        /// <summary>プロファイルを適用して不要な表示を隠す。</summary>
        [ContextMenu("Apply")]
        public void Apply()
        {
            _wasStarted = true;
            if (_applied) return;

            OthelloBoardVisualizer vis = Visualizer;
            if (vis != null)
            {
                vis.Initialize(); // 動的コンテナを確実に生成してから隠す (初期化済みなら何もしない)
                _legalMovesWereShown = vis.ShowLegalMoves;
                if (hideLegalMoves) vis.ShowLegalMoves = false;

                Transform board = vis.transform;
                if (hideDetectedStoneDots) HideChildren(board, DetectedStoneContainers);
                if (hideBoardGrid) HideChildren(board, BoardGridContainers);
                if (hideForwardIndicator) HideChildren(board, new[] { ForwardIndicatorName });
            }

            if (hiddenObjects != null)
            {
                foreach (GameObject go in hiddenObjects) Hide(go);
            }
            _applied = true;
        }

        /// <summary>隠した表示を元に戻す (デバッグ用)。</summary>
        [ContextMenu("Restore")]
        public void Restore()
        {
            if (!_applied) return;
            foreach (GameObject go in _hidden)
            {
                if (go != null) go.SetActive(true);
            }
            _hidden.Clear();
            if (boardVisualizer != null && hideLegalMoves) boardVisualizer.ShowLegalMoves = _legalMovesWereShown;
            _applied = false;
        }

        private void HideChildren(Transform parent, IEnumerable<string> names)
        {
            foreach (string n in names)
            {
                Transform child = parent.Find(n);
                if (child != null) Hide(child.gameObject);
            }
        }

        private void Hide(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            _hidden.Add(go);
        }
    }
}
