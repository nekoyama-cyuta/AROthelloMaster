using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AROthello
{
    /// <summary>
    /// 前回のバーチャル盤 (OthelloBoard_AR_Target, 22.8cm x 1.8cm x 22.8cm) の正規化座標系 [-0.5, +0.5] に完全準拠し、
    /// 8x8 天面グリッド線、白石・黒石 (シアン) の 3D ディスク、および白番合法手の黄色ハイライト (Cylinder)、
    /// ならびに Beam Pro 画面端の 2D ミニ盤面デバッグ UI を一括制御するビジュアライザー
    /// </summary>
    public class OthelloBoardVisualizer : MonoBehaviour
    {
        [Header("Target Team (固定: 白石 = 1)")]
        [Tooltip("合法手を計算・表示する対象チーム (1 = 白石, -1 = 黒石)")]
        [SerializeField] private int targetTeam = 1;

        [Header("Handheld Debug UI (Beam Pro 画面端用)")]
        [SerializeField] private RawImage miniBoardRawImage;
        [SerializeField] private Text miniBoardStatusText;

        [Header("Materials (Inspector アサイン または Assets/Materials/ から自動解決)")]
        [SerializeField] private Material customBoardMaterial;
        [SerializeField] private Material customWhiteDiscMaterial;
        [SerializeField] private Material customBlackDiscMaterial;
        [SerializeField] private Material customHighlightMaterial;

        public Material CustomBoardMaterial { get => customBoardMaterial; set => customBoardMaterial = value; }
        public Material CustomWhiteDiscMaterial { get => customWhiteDiscMaterial; set => customWhiteDiscMaterial = value; }
        public Material CustomBlackDiscMaterial { get => customBlackDiscMaterial; set => customBlackDiscMaterial = value; }
        public Material CustomHighlightMaterial { get => customHighlightMaterial; set => customHighlightMaterial = value; }

        // 生成された 64 マスの 3D オブジェクト (親 Cube の [-0.5, +0.5] 空間に配置)
        private readonly GameObject[,] _whiteDiscs = new GameObject[8, 8];
        private readonly GameObject[,] _blackDiscs = new GameObject[8, 8];
        private readonly GameObject[,] _highlights = new GameObject[8, 8];

        private Transform _gridContainer;
        private Transform _cellsContainer;
        private Transform _highlightsContainer;

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

        public string CalibrationStatusText { get; set; } = "";

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
            // 白番合法手の黄色ハイライトの呼吸アニメーション (各マスの中心で個別にスケール脈動)
            if (_highlightsContainer != null)
            {
                float pulse = 1.0f + 0.16f * Mathf.Sin(Time.time * 6.0f);
                Vector3 hScale = new Vector3(0.06f * pulse, 0.05f, 0.06f * pulse);

                for (int r = 0; r < 8; r++)
                {
                    for (int c = 0; c < 8; c++)
                    {
                        GameObject hGo = _highlights[r, c];
                        if (hGo != null && hGo.activeSelf)
                        {
                            hGo.transform.localScale = hScale;
                        }
                    }
                }
            }
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            // 親 Cube のソリッド MeshRenderer を無効化し、完全ワイヤーフレームにする
            MeshRenderer parentMr = GetComponent<MeshRenderer>();
            if (parentMr != null)
            {
                parentMr.enabled = false;
            }

            // 1. マテリアルの解決
            ResolveMaterials();

            // 既存の動的コンテナをクリーンアップ
            CleanupDynamicContainers();

            // 2. 直方体の完全ワイヤーフレーム (外枠12エッジ + 天面8x8グリッド線) の生成
            // 親 Cube (幅 0.228m, 厚さ 0.018m, 奥行き 0.228m) の正規化空間 [-0.5, +0.5]
            GameObject gridGo = new GameObject("GridLines_Container");
            gridGo.transform.SetParent(transform, false);
            gridGo.transform.localPosition = Vector3.zero;
            gridGo.transform.localRotation = Quaternion.identity;
            gridGo.transform.localScale = Vector3.one;
            _gridContainer = gridGo.transform;

            const float edgeThick = 0.012f; // 外枠エッジの太さ (約2.7mm)
            const float innerThick = 0.005f; // グリッド内線の太さ (約1.1mm)
            const float topY = 0.502f;
            const float bottomY = -0.502f;

            // --- 天面 8x8 グリッド線 (i = 0..8) ---
            for (int i = 0; i <= 8; i++)
            {
                float coord = -0.5f + i * 0.125f;
                bool isOuter = (i == 0 || i == 8);
                float thick = isOuter ? edgeThick : innerThick;

                // X方向グリッド線 (varying Z)
                GameObject hLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hLine.name = $"GridLine_Top_H_{i}";
                hLine.transform.SetParent(_gridContainer, false);
                hLine.transform.localPosition = new Vector3(0f, topY, coord);
                hLine.transform.localScale = new Vector3(1.0f, thick, thick);
                ApplyMaterialAndRemoveCollider(hLine, customBoardMaterial);

                // Z方向グリッド線 (varying X)
                GameObject vLine = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vLine.name = $"GridLine_Top_V_{i}";
                vLine.transform.SetParent(_gridContainer, false);
                vLine.transform.localPosition = new Vector3(coord, topY, 0f);
                vLine.transform.localScale = new Vector3(thick, thick, 1.0f);
                ApplyMaterialAndRemoveCollider(vLine, customBoardMaterial);
            }

            // --- 底面外枠 4エッジ (Y = -0.502f) ---
            float[] outerCoords = new float[] { -0.5f, 0.5f };
            foreach (float z in outerCoords)
            {
                GameObject hBot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hBot.name = $"Wireframe_Bot_H_{z:F1}";
                hBot.transform.SetParent(_gridContainer, false);
                hBot.transform.localPosition = new Vector3(0f, bottomY, z);
                hBot.transform.localScale = new Vector3(1.0f, edgeThick, edgeThick);
                ApplyMaterialAndRemoveCollider(hBot, customBoardMaterial);
            }
            foreach (float x in outerCoords)
            {
                GameObject vBot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vBot.name = $"Wireframe_Bot_V_{x:F1}";
                vBot.transform.SetParent(_gridContainer, false);
                vBot.transform.localPosition = new Vector3(x, bottomY, 0f);
                vBot.transform.localScale = new Vector3(edgeThick, edgeThick, 1.0f);
                ApplyMaterialAndRemoveCollider(vBot, customBoardMaterial);
            }

            // --- 4隅の垂直柱 (Vertical Posts: X=±0.5, Z=±0.5, Y: -0.5..+0.5) ---
            foreach (float x in outerCoords)
            {
                foreach (float z in outerCoords)
                {
                    GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    post.name = $"Wireframe_Post_{x:F1}_{z:F1}";
                    post.transform.SetParent(_gridContainer, false);
                    post.transform.localPosition = new Vector3(x, 0f, z);
                    post.transform.localScale = new Vector3(edgeThick, 1.0f, edgeThick);
                    ApplyMaterialAndRemoveCollider(post, customBoardMaterial);
                }
            }

            // 3. 64 マスの石・ハイライトコンテナの生成
            GameObject cellsGo = new GameObject("Cells_Container");
            cellsGo.transform.SetParent(transform, false);
            cellsGo.transform.localPosition = Vector3.zero;
            cellsGo.transform.localRotation = Quaternion.identity;
            cellsGo.transform.localScale = Vector3.one;
            _cellsContainer = cellsGo.transform;

            GameObject highlightsGo = new GameObject("Highlights_Container");
            highlightsGo.transform.SetParent(transform, false);
            highlightsGo.transform.localPosition = Vector3.zero;
            highlightsGo.transform.localRotation = Quaternion.identity;
            highlightsGo.transform.localScale = Vector3.one;
            _highlightsContainer = highlightsGo.transform;

            // 1mm ドットのスケール:
            // 直径: 0.007 * 0.228m = 1.6mm (実物の石を隠さず中心に乗る極小光点)
            // 高さ: 0.05 * 0.018m = 0.9mm
            Vector3 dotScale = new Vector3(0.007f, 0.05f, 0.007f);

            // 各マス (c = 列 0..7, r = 行 0..7)
            // 正規化空間: x = -0.5 + (c + 0.5) * 0.125, z = +0.5 - (r + 0.5) * 0.125
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    float normX = -0.5f + (c + 0.5f) * 0.125f;
                    float normZ = +0.5f - (r + 0.5f) * 0.125f;
                    Vector3 stonePos = new Vector3(normX, 0.53f, normZ);
                    Vector3 highlightPos = new Vector3(normX, 0.54f, normZ);

                    // 白石ドット (極小 Sphere / ドット, 約 1.6mm)
                    GameObject wGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    wGo.name = $"White_{r}_{c}";
                    wGo.transform.SetParent(_cellsContainer, false);
                    wGo.transform.localPosition = stonePos;
                    wGo.transform.localScale = dotScale;
                    ApplyMaterialAndRemoveCollider(wGo, customWhiteDiscMaterial);
                    wGo.SetActive(false);
                    _whiteDiscs[r, c] = wGo;

                    // 黒石ドット (極小 Sphere / ドット - シアン, 約 1.6mm)
                    GameObject bGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    bGo.name = $"Black_{r}_{c}";
                    bGo.transform.SetParent(_cellsContainer, false);
                    bGo.transform.localPosition = stonePos;
                    bGo.transform.localScale = dotScale;
                    ApplyMaterialAndRemoveCollider(bGo, customBlackDiscMaterial);
                    bGo.SetActive(false);
                    _blackDiscs[r, c] = bGo;

                    // 白番合法手ハイライト (Cylinder - 黄色, 直径 0.06 * 0.228m = 1.37cm)
                    GameObject hGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    hGo.name = $"Highlight_{r}_{c}";
                    hGo.transform.SetParent(_highlightsContainer, false);
                    hGo.transform.localPosition = highlightPos;
                    hGo.transform.localScale = new Vector3(0.06f, 0.04f, 0.06f);
                    ApplyMaterialAndRemoveCollider(hGo, customHighlightMaterial);
                    hGo.SetActive(false);
                    _highlights[r, c] = hGo;
                }
            }

            // 4. 手元 2D ミニ盤面テクスチャ (128x128) の初期化
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

        private void ResolveMaterials()
        {
            if (customBoardMaterial == null)
            {
                customBoardMaterial = CreateUnlitMaterial("Mat_Wireframe_Board", new Color(0.1f, 1.0f, 0.45f, 0.95f));
            }
            if (customWhiteDiscMaterial == null)
            {
                customWhiteDiscMaterial = CreateUnlitMaterial("Mat_WhiteDisc", new Color(1.0f, 1.0f, 1.0f, 0.98f));
            }
            if (customBlackDiscMaterial == null)
            {
                customBlackDiscMaterial = CreateUnlitMaterial("Mat_BlackDisc", new Color(0.0f, 0.85f, 1.0f, 0.98f)); // シアン/ブルー
            }
            if (customHighlightMaterial == null)
            {
                customHighlightMaterial = CreateUnlitMaterial("Mat_Highlight", new Color(1.0f, 0.88f, 0.0f, 0.98f)); // 黄色
            }
        }

        private void CleanupDynamicContainers()
        {
            string[] containerNames = new string[] { "GridLines_Container", "Cells_Container", "Highlights_Container", "Wireframe_Board_Mesh" };
            foreach (var cName in containerNames)
            {
                Transform child = transform.Find(cName);
                if (child != null)
                {
                    SafeDestroy(child.gameObject);
                }
            }
        }

        private static void ApplyMaterialAndRemoveCollider(GameObject go, Material mat)
        {
            if (go == null) return;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) SafeDestroy(col);
        }

        private static void SafeDestroy(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
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

            // 3. 3D 石・ハイライトの表示切り替え
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++)
                {
                    DiscColor dc = (detectedBoard != null) ? detectedBoard[r, c] : DiscColor.None;
                    bool isWhite = (dc == DiscColor.White);
                    bool isBlack = (dc == DiscColor.Black);
                    bool isLegal = (!isWhite && !isBlack && legalMoveSet.Contains(r * 8 + c));

                    if (_whiteDiscs[r, c] != null && _whiteDiscs[r, c].activeSelf != isWhite) _whiteDiscs[r, c].SetActive(isWhite);
                    if (_blackDiscs[r, c] != null && _blackDiscs[r, c].activeSelf != isBlack) _blackDiscs[r, c].SetActive(isBlack);
                    if (_highlights[r, c] != null && _highlights[r, c].activeSelf != isLegal) _highlights[r, c].SetActive(isLegal);
                }
            }

            // 4. 手元 2D ミニ盤面テクスチャの描画更新
            UpdateMiniBoardTexture(detectedBoard, legalMoveSet);

            // 5. 手元ステータステキスト更新
            if (miniBoardStatusText != null)
            {
                string teamName = (targetTeam == 1) ? "<color=#FFFFFF>WHITE</color>" : "<color=#00D4FF>BLACK</color>";
                int moveCount = (legalMoves != null) ? legalMoves.Count : 0;
                string calibPart = string.IsNullOrEmpty(CalibrationStatusText) ? "" : $"\n<size=12><color=#A0E0FF>{CalibrationStatusText}</color></size>";
                miniBoardStatusText.text = $"<color=#00D4FF>B:{blackCount}</color> <color=#FFFFFF>W:{whiteCount}</color> | Legal ({teamName}): <color=#FFE600>{moveCount}</color>{calibPart}";
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

            return mat;
        }
    }
}
