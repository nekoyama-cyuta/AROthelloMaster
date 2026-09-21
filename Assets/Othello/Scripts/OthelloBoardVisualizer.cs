using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AROthello
{
    /// <summary>
    /// オセロ盤・石の 3D ワイヤーフレーム表示、OthelloLogic による白石合法手ハイライト、
    /// および Beam Pro 手元画面用 8x8 ミニ盤面デバッグ UI を一括管理するビジュアライザー
    /// </summary>
    public class OthelloBoardVisualizer : MonoBehaviour
    {
        [Header("Target Team (固定: 白石 = 1)")]
        [Tooltip("合法手を計算・表示する対象チーム (1 = 白石, -1 = 黒石)")]
        [SerializeField] private int targetTeam = 1;

        [Header("Board Dimensions (実寸 22.8cm x 22.8cm x 1.8cm)")]
        [SerializeField] private float boardWidth = 0.228f;
        [SerializeField] private float boardDepth = 0.228f;
        [SerializeField] private float boardThickness = 0.018f;

        [Header("Handheld Debug UI (Beam Pro 画面端用)")]
        [SerializeField] private RawImage miniBoardRawImage;
        [SerializeField] private Text miniBoardStatusText;

        [Header("Materials (未設定時は自動生成)")]
        [SerializeField] private Material customBoardMaterial;
        [SerializeField] private Material customWhiteDiscMaterial;
        [SerializeField] private Material customBlackDiscMaterial;
        [SerializeField] private Material customHighlightMaterial;

        public Material CustomBoardMaterial { get => customBoardMaterial; set => customBoardMaterial = value; }
        public Material CustomWhiteDiscMaterial { get => customWhiteDiscMaterial; set => customWhiteDiscMaterial = value; }
        public Material CustomBlackDiscMaterial { get => customBlackDiscMaterial; set => customBlackDiscMaterial = value; }
        public Material CustomHighlightMaterial { get => customHighlightMaterial; set => customHighlightMaterial = value; }

        // 生成された 64 マスの 3D ワイヤーフレームオブジェクト
        private readonly GameObject[,] _whiteDiscs = new GameObject[8, 8];
        private readonly GameObject[,] _blackDiscs = new GameObject[8, 8];
        private readonly GameObject[,] _highlights = new GameObject[8, 8];

        private GameObject _wireframeBoardGo;
        private Transform _highlightsContainer;

        // ワイヤーフレーム描画用共有マテリアル
        private Material _matBoard;
        private Material _matWhiteDisc;
        private Material _matBlackDisc;
        private Material _matHighlight;

        // 共有メッシュ
        private Mesh _boardMesh;
        private Mesh _discMesh;
        private Mesh _highlightMesh;

        // 手元画面用 128x128 ミニ盤面テクスチャバッファ
        private const int TEX_SIZE = 128;
        private const int CELL_PX = 16; // 128 / 8 = 16px
        private Texture2D _miniBoardTex;
        private Color32[] _miniBoardPixels;

        private bool _isInitialized = false;

        public RawImage MiniBoardRawImage
        {
            get => miniBoardRawImage;
            set
            {
                miniBoardRawImage = value;
                if (miniBoardRawImage != null && _miniBoardTex != null)
                {
                    miniBoardRawImage.texture = _miniBoardTex;
                }
            }
        }

        public Text MiniBoardStatusText
        {
            get => miniBoardStatusText;
            set => miniBoardStatusText = value;
        }

        public int TargetTeam
        {
            get => targetTeam;
            set => targetTeam = value;
        }

        private void Awake()
        {
            Initialize();
        }

        private void Start()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
        }

        private void Update()
        {
            // 合法手ハイライトのやわらかな明滅 (Breathing Pulse) アニメーション
            // ※コンテナのスケールを変えると各マスの位置がズレるため、マテリアルのアルファ・輝度を明滅させる
            if (_matHighlight != null)
            {
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * 5.0f);
                Color c = new Color(1.0f, 0.92f, 0.0f, pulse);
                _matHighlight.color = c;
                if (_matHighlight.HasProperty("_BaseColor"))
                {
                    _matHighlight.SetColor("_BaseColor", c);
                }
            }
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            // 1. マテリアル初期化 (カスタムアサインがあればそれをインスタンス化して使用、なければ自動生成)
            _matBoard = customBoardMaterial != null ? new Material(customBoardMaterial) : CreateUnlitMaterial("Mat_Wireframe_Board", new Color(0.1f, 1.0f, 0.45f, 0.95f)); // ネオングリーン
            _matWhiteDisc = customWhiteDiscMaterial != null ? new Material(customWhiteDiscMaterial) : CreateUnlitMaterial("Mat_Wireframe_White", new Color(0.98f, 0.98f, 1.0f, 0.98f)); // 純白
            _matBlackDisc = customBlackDiscMaterial != null ? new Material(customBlackDiscMaterial) : CreateUnlitMaterial("Mat_Wireframe_Black", new Color(0.05f, 0.75f, 1.0f, 0.98f)); // シアン/ブルー (シースルーARで高視認性)
            _matHighlight = customHighlightMaterial != null ? new Material(customHighlightMaterial) : CreateUnlitMaterial("Mat_Wireframe_Highlight", new Color(1.0f, 0.92f, 0.0f, 1.0f)); // 鮮やかな黄色

            // 2. 共有メッシュ生成
            _boardMesh = BuildBoardWireframeMesh();
            _discMesh = BuildDiscWireframeMesh();
            _highlightMesh = BuildHighlightWireframeMesh();

            // 3. 3D 盤面ワイヤーフレームオブジェクトの生成
            _wireframeBoardGo = new GameObject("Wireframe_Board_Mesh");
            _wireframeBoardGo.transform.SetParent(transform, false);
            _wireframeBoardGo.transform.localPosition = Vector3.zero;
            _wireframeBoardGo.transform.localRotation = Quaternion.identity;
            _wireframeBoardGo.transform.localScale = Vector3.one;

            var mf = _wireframeBoardGo.AddComponent<MeshFilter>();
            mf.sharedMesh = _boardMesh;
            var mr = _wireframeBoardGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _matBoard;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 4. 64 マスの石・ハイライトスロットの生成
            GameObject cellsContainer = new GameObject("Cells_Container");
            cellsContainer.transform.SetParent(transform, false);
            cellsContainer.transform.localPosition = Vector3.zero;
            cellsContainer.transform.localRotation = Quaternion.identity;

            GameObject highlightsParent = new GameObject("Highlights_Container");
            highlightsParent.transform.SetParent(transform, false);
            highlightsParent.transform.localPosition = Vector3.zero;
            highlightsParent.transform.localRotation = Quaternion.identity;
            _highlightsContainer = highlightsParent.transform;

            float cellW = boardWidth / 8.0f;   // 0.0285m
            float cellD = boardDepth / 8.0f;   // 0.0285m
            float halfW = boardWidth * 0.5f;   // 0.114m
            float halfD = boardDepth * 0.5f;   // 0.114m
            float topY = boardThickness * 0.5f; // +0.009m

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    float localX = -halfW + (c + 0.5f) * cellW;
                    float localZ = halfD - (r + 0.5f) * cellD;
                    Vector3 cellPos = new Vector3(localX, topY, localZ);

                    // 白石
                    GameObject wGo = new GameObject($"White_{r}_{c}");
                    wGo.transform.SetParent(cellsContainer.transform, false);
                    wGo.transform.localPosition = cellPos;
                    var wMf = wGo.AddComponent<MeshFilter>();
                    wMf.sharedMesh = _discMesh;
                    var wMr = wGo.AddComponent<MeshRenderer>();
                    wMr.sharedMaterial = _matWhiteDisc;
                    wMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    wGo.SetActive(false);
                    _whiteDiscs[r, c] = wGo;

                    // 黒石
                    GameObject bGo = new GameObject($"Black_{r}_{c}");
                    bGo.transform.SetParent(cellsContainer.transform, false);
                    bGo.transform.localPosition = cellPos;
                    var bMf = bGo.AddComponent<MeshFilter>();
                    bMf.sharedMesh = _discMesh;
                    var bMr = bGo.AddComponent<MeshRenderer>();
                    bMr.sharedMaterial = _matBlackDisc;
                    bMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    bGo.SetActive(false);
                    _blackDiscs[r, c] = bGo;

                    // 合法手ハイライト (黄色枠)
                    GameObject hGo = new GameObject($"Highlight_{r}_{c}");
                    hGo.transform.SetParent(_highlightsContainer, false);
                    hGo.transform.localPosition = cellPos;
                    var hMf = hGo.AddComponent<MeshFilter>();
                    hMf.sharedMesh = _highlightMesh;
                    var hMr = hGo.AddComponent<MeshRenderer>();
                    hMr.sharedMaterial = _matHighlight;
                    hMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    hGo.SetActive(false);
                    _highlights[r, c] = hGo;
                }
            }

            // 5. 手元 2D ミニ盤面テクスチャ (128x128) の初期化
            _miniBoardTex = new Texture2D(TEX_SIZE, TEX_SIZE, TextureFormat.RGBA32, false);
            _miniBoardTex.filterMode = FilterMode.Point;
            _miniBoardPixels = new Color32[TEX_SIZE * TEX_SIZE];
            DrawInitialMiniBoard();

            if (miniBoardRawImage != null)
            {
                miniBoardRawImage.texture = _miniBoardTex;
            }

            _isInitialized = true;
        }

        /// <summary>
        /// OpenCV 認識結果の 8x8 二次元配列を受け取り、3D 盤面および手元デバッグ UI を更新
        /// </summary>
        public void UpdateBoard(DiscColor[,] detectedBoard)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            // 1. DataLayer.StoneData の同期
            if (DataLayer.StoneData == null || DataLayer.StoneData.Length != 8)
            {
                DataLayer.StoneData = new int[8][];
                for (int i = 0; i < 8; i++)
                {
                    DataLayer.StoneData[i] = new int[8];
                }
            }

            int blackCount = 0;
            int whiteCount = 0;

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    DiscColor dc = (detectedBoard != null) ? detectedBoard[r, c] : DiscColor.None;
                    int stoneVal = 0;
                    if (dc == DiscColor.Black)
                    {
                        stoneVal = -1;
                        blackCount++;
                    }
                    else if (dc == DiscColor.White)
                    {
                        stoneVal = 1;
                        whiteCount++;
                    }

                    // c = X (横列 0..7), r = Z (縦行 0..7)
                    DataLayer.StoneData[c][r] = stoneVal;
                }
            }

            // 2. OthelloLogic による対象チーム (白石 = 1) の合法手算出
            List<Vector2Int> legalMoves = null;
            var logic = OthelloLogic.Instance != null ? OthelloLogic.Instance : FindFirstObjectByType<OthelloLogic>();
            if (logic != null)
            {
                try
                {
                    legalMoves = logic.playableStonePositions(targetTeam);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OthelloBoardVisualizer] playableStonePositions exception: {ex.Message}");
                }
            }

            // 合法手の高速ルックアップ用 HashSet
            HashSet<int> legalMoveSet = new HashSet<int>();
            if (legalMoves != null)
            {
                for (int i = 0; i < legalMoves.Count; i++)
                {
                    // key = r * 8 + c (x=c, z=r)
                    legalMoveSet.Add(legalMoves[i].y * 8 + legalMoves[i].x);
                }
            }

            // 3. 3D ワイヤーフレーム石・ハイライトの表示切り替え
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    DiscColor dc = (detectedBoard != null) ? detectedBoard[r, c] : DiscColor.None;
                    bool isWhite = (dc == DiscColor.White);
                    bool isBlack = (dc == DiscColor.Black);
                    bool isLegal = (!isWhite && !isBlack && legalMoveSet.Contains(r * 8 + c));

                    if (_whiteDiscs[r, c].activeSelf != isWhite) _whiteDiscs[r, c].SetActive(isWhite);
                    if (_blackDiscs[r, c].activeSelf != isBlack) _blackDiscs[r, c].SetActive(isBlack);
                    if (_highlights[r, c].activeSelf != isLegal) _highlights[r, c].SetActive(isLegal);
                }
            }

            // 4. 手元 2D ミニ盤面テクスチャの描画更新
            UpdateMiniBoardTexture(detectedBoard, legalMoveSet);

            // 5. 手元ステータステキスト更新
            if (miniBoardStatusText != null)
            {
                string teamName = (targetTeam == 1) ? "<color=#FFFFFF>WHITE</color>" : "<color=#00D4FF>BLACK</color>";
                int moveCount = (legalMoves != null) ? legalMoves.Count : 0;
                miniBoardStatusText.text = $"<color=#00D4FF>B:{blackCount}</color> <color=#FFFFFF>W:{whiteCount}</color> | Legal ({teamName}): <color=#FFE600>{moveCount}</color>";
            }
        }

        #region Mini Board 2D Texture Renderer

        private void DrawInitialMiniBoard()
        {
            Color32 bgCol = new Color32(16, 48, 28, 255);
            Color32 gridCol = new Color32(10, 32, 18, 255);

            for (int y = 0; y < TEX_SIZE; y++)
            {
                for (int x = 0; x < TEX_SIZE; x++)
                {
                    bool isGrid = (x % CELL_PX == 0) || (x == TEX_SIZE - 1) ||
                                  (y % CELL_PX == 0) || (y == TEX_SIZE - 1);
                    _miniBoardPixels[y * TEX_SIZE + x] = isGrid ? gridCol : bgCol;
                }
            }

            _miniBoardTex.SetPixels32(_miniBoardPixels);
            _miniBoardTex.Apply(false);
        }

        private void UpdateMiniBoardTexture(DiscColor[,] detectedBoard, HashSet<int> legalMoves)
        {
            Color32 bgCol = new Color32(18, 56, 32, 255);
            Color32 gridCol = new Color32(10, 32, 18, 255);
            Color32 whiteDiscCol = new Color32(245, 245, 250, 255);
            Color32 whiteBorderCol = new Color32(180, 180, 190, 255);
            Color32 blackDiscCol = new Color32(15, 15, 20, 255);
            Color32 blackBorderCol = new Color32(0, 210, 255, 255); // 鮮やかなシアン枠
            Color32 yellowLegalCol = new Color32(255, 230, 0, 255); // 黄色ハイライト

            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    DiscColor dc = (detectedBoard != null) ? detectedBoard[r, c] : DiscColor.None;
                    bool isLegal = legalMoves.Contains(r * 8 + c);

                    // テクスチャ座標系: y=0 が最下段、r=0 は上段なので反転
                    int cellBottom = (7 - r) * CELL_PX;
                    int cellLeft = c * CELL_PX;

                    int centerPxX = cellLeft + CELL_PX / 2;
                    int centerPxY = cellBottom + CELL_PX / 2;
                    int discRadiusSq = 5 * 5;
                    int discBorderRadiusSq = 6 * 6;
                    int legalRadiusSq = 3 * 3;

                    for (int dy = 0; dy < CELL_PX; dy++)
                    {
                        int py = cellBottom + dy;
                        for (int dx = 0; dx < CELL_PX; dx++)
                        {
                            int px = cellLeft + dx;
                            bool isBorder = (dx == 0 || dy == 0 || dx == CELL_PX - 1 || dy == CELL_PX - 1);
                            Color32 col = isBorder ? gridCol : bgCol;

                            int distSq = (px - centerPxX) * (px - centerPxX) + (py - centerPxY) * (py - centerPxY);

                            if (dc == DiscColor.White)
                            {
                                if (distSq <= discRadiusSq) col = whiteDiscCol;
                                else if (distSq <= discBorderRadiusSq) col = whiteBorderCol;
                            }
                            else if (dc == DiscColor.Black)
                            {
                                if (distSq <= discRadiusSq) col = blackDiscCol;
                                else if (distSq <= discBorderRadiusSq) col = blackBorderCol;
                            }
                            else if (isLegal)
                            {
                                if (distSq <= legalRadiusSq)
                                {
                                    col = yellowLegalCol;
                                }
                                else if (distSq <= (5 * 5) && distSq >= (4 * 4))
                                {
                                    col = yellowLegalCol;
                                }
                            }

                            _miniBoardPixels[py * TEX_SIZE + px] = col;
                        }
                    }
                }
            }

            _miniBoardTex.SetPixels32(_miniBoardPixels);
            _miniBoardTex.Apply(false);
        }

        #endregion

        #region Procedural Wireframe Mesh Builders

        /// <summary>
        /// 実機オセロ盤の外枠 12 エッジ + 天面 8x8 グリッド線を太さ 1.6mm のリボンクアッドで構築
        /// </summary>
        private Mesh BuildBoardWireframeMesh()
        {
            Mesh mesh = new Mesh { name = "BoardWireframeRibbons" };
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            float hw = boardWidth * 0.5f;   // 0.114m
            float hd = boardDepth * 0.5f;   // 0.114m
            float ht = boardThickness * 0.5f; // 0.009m
            float lineW = 0.0016f;          // 1.6mm 幅のリボン

            // 1. 天面の 8x8 グリッド線 (9本横 + 9本縦)
            for (int i = 0; i <= 8; i++)
            {
                float z = -hd + i * (boardDepth / 8.0f);
                AddRibbonSegment(verts, tris, new Vector3(-hw, ht + 0.0002f, z), new Vector3(hw, ht + 0.0002f, z), Vector3.up, lineW);

                float x = -hw + i * (boardWidth / 8.0f);
                AddRibbonSegment(verts, tris, new Vector3(x, ht + 0.0002f, -hd), new Vector3(x, ht + 0.0002f, hd), Vector3.up, lineW);
            }

            // 2. 底面の 4 エッジ
            AddRibbonSegment(verts, tris, new Vector3(-hw, -ht, -hd), new Vector3(hw, -ht, -hd), Vector3.down, lineW);
            AddRibbonSegment(verts, tris, new Vector3(hw, -ht, -hd), new Vector3(hw, -ht, hd), Vector3.down, lineW);
            AddRibbonSegment(verts, tris, new Vector3(hw, -ht, hd), new Vector3(-hw, -ht, hd), Vector3.down, lineW);
            AddRibbonSegment(verts, tris, new Vector3(-hw, -ht, hd), new Vector3(-hw, -ht, -hd), Vector3.down, lineW);

            // 3. 垂直 4 コーナーエッジ
            AddRibbonSegment(verts, tris, new Vector3(-hw, -ht, -hd), new Vector3(-hw, ht, -hd), new Vector3(-1, 0, -1).normalized, lineW);
            AddRibbonSegment(verts, tris, new Vector3(hw, -ht, -hd), new Vector3(hw, ht, -hd), new Vector3(1, 0, -1).normalized, lineW);
            AddRibbonSegment(verts, tris, new Vector3(hw, -ht, hd), new Vector3(hw, ht, hd), new Vector3(1, 0, 1).normalized, lineW);
            AddRibbonSegment(verts, tris, new Vector3(-hw, -ht, hd), new Vector3(-hw, ht, hd), new Vector3(-1, 0, 1).normalized, lineW);

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// オセロ石用 3D ワイヤーフレーム (天面リング + 底面リング + 4本垂直ピラー)
        /// 直径 2.3cm, 厚み 3.5mm
        /// </summary>
        private Mesh BuildDiscWireframeMesh()
        {
            Mesh mesh = new Mesh { name = "DiscWireframeRibbons" };
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            float radius = 0.0115f; // 半径 1.15cm
            float height = 0.0035f; // 厚み 3.5mm
            float ringW = 0.0014f;  // 線幅 1.4mm
            const int segs = 16;

            // 天面リング (Y = height)
            AddRibbonRing(verts, tris, radius, height, Vector3.up, ringW, segs);
            // 底面リング (Y = 0.0003m)
            AddRibbonRing(verts, tris, radius, 0.0003f, Vector3.up, ringW, segs);

            // 4本の垂直ピラー
            for (int i = 0; i < 4; i++)
            {
                float ang = i * Mathf.PI * 0.5f;
                float px = Mathf.Cos(ang) * radius;
                float pz = Mathf.Sin(ang) * radius;
                Vector3 pNormal = new Vector3(px, 0f, pz).normalized;
                AddRibbonSegment(verts, tris, new Vector3(px, 0.0003f, pz), new Vector3(px, height, pz), pNormal, ringW);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 合法手用黄色ハイライト枠 (マス目内寸 2.4cm x 2.4cm)
        /// </summary>
        private Mesh BuildHighlightWireframeMesh()
        {
            Mesh mesh = new Mesh { name = "HighlightWireframeRibbons" };
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();

            float halfSize = 0.012f; // 1.2cm (一辺 2.4cm)
            float lineW = 0.0018f;   // 1.8mm 幅でクッキリ発光
            float y = 0.0006f;       // 盤面直上

            Vector3 p0 = new Vector3(-halfSize, y, -halfSize);
            Vector3 p1 = new Vector3(halfSize, y, -halfSize);
            Vector3 p2 = new Vector3(halfSize, y, halfSize);
            Vector3 p3 = new Vector3(-halfSize, y, halfSize);

            AddRibbonSegment(verts, tris, p0, p1, Vector3.up, lineW);
            AddRibbonSegment(verts, tris, p1, p2, Vector3.up, lineW);
            AddRibbonSegment(verts, tris, p2, p3, Vector3.up, lineW);
            AddRibbonSegment(verts, tris, p3, p0, Vector3.up, lineW);

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddRibbonSegment(
            List<Vector3> verts,
            List<int> tris,
            Vector3 pA,
            Vector3 pB,
            Vector3 surfaceNormal,
            float width
        ) {
            Vector3 dir = (pB - pA).normalized;
            Vector3 side = Vector3.Cross(dir, surfaceNormal).normalized * (width * 0.5f);

            int bIdx = verts.Count;
            verts.Add(pA - side);
            verts.Add(pA + side);
            verts.Add(pB + side);
            verts.Add(pB - side);

            tris.Add(bIdx + 0);
            tris.Add(bIdx + 1);
            tris.Add(bIdx + 2);

            tris.Add(bIdx + 0);
            tris.Add(bIdx + 2);
            tris.Add(bIdx + 3);
        }

        private static void AddRibbonRing(
            List<Vector3> verts,
            List<int> tris,
            float radius,
            float y,
            Vector3 normal,
            float width,
            int segments
        ) {
            float rIn = radius - width * 0.5f;
            float rOut = radius + width * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2.0f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2.0f;

                Vector3 v0 = new Vector3(Mathf.Cos(a0) * rIn, y, Mathf.Sin(a0) * rIn);
                Vector3 v1 = new Vector3(Mathf.Cos(a0) * rOut, y, Mathf.Sin(a0) * rOut);
                Vector3 v2 = new Vector3(Mathf.Cos(a1) * rOut, y, Mathf.Sin(a1) * rOut);
                Vector3 v3 = new Vector3(Mathf.Cos(a1) * rIn, y, Mathf.Sin(a1) * rIn);

                int bIdx = verts.Count;
                verts.Add(v0);
                verts.Add(v1);
                verts.Add(v2);
                verts.Add(v3);

                tris.Add(bIdx + 0);
                tris.Add(bIdx + 1);
                tris.Add(bIdx + 2);

                tris.Add(bIdx + 0);
                tris.Add(bIdx + 2);
                tris.Add(bIdx + 3);
            }
        }

        private static Material CreateUnlitMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Sprites/Default");

            Material mat = new Material(shader)
            {
                name = name,
                color = color
            };

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", color);
            }

            mat.SetFloat("_Surface", 1.0f); // Transparent
            mat.SetFloat("_Blend", 0.0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 100;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            return mat;
        }

        #endregion
    }
}
