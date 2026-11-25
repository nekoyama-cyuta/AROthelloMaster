using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AROthello.Experiment
{
    /// <summary>
    /// 低クラッターの次工程ガイドを AR 盤上に描く。
    /// - 今置くべきマス: マス中央の控えめなリング + 中央ドット (または半透明ゴースト石)。色で置くべき石の色を示す。
    /// - 誤り: 該当マスに赤いリングを点滅表示。
    /// - 開始前チェック: 置くべき初期石をゴースト石で、取り除くべき石を赤いリングで示す。
    /// - 進捗 ("3 / 10") と完了表示は視界の端の Text に出す。
    ///
    /// 光学透過型ディスプレイ (XREAL) では黒が透明になって見えないため、黒石は既存の表示と同じくシアンで示す。
    /// マーカーは盤の Transform (boardRoot) の子として置くので、AR 盤の追従にそのまま連動する。
    /// </summary>
    public class ExperimentGuideView : MonoBehaviour
    {
        public enum TargetStyle
        {
            /// <summary>細いリング + 中央ドット (既定)</summary>
            RingAndDot = 0,
            /// <summary>半透明のゴースト石</summary>
            GhostStone = 1
        }

        private const string ContainerName = "ExperimentGuide_Container";
        /// <summary>markerMaterial 未設定時に Resources から読むマテリアル (ビルドに必ず含まれる)</summary>
        public const string DefaultMaterialResource = "ExperimentGuideMaterial";

        [Header("References")]
        [Tooltip("状態管理 (未設定でも Render を直接呼べば表示できる)")]
        [SerializeField] private ExperimentController controller;
        [Tooltip("AR 盤の Transform (ローカル空間 [-0.5, +0.5]、天面 y = +0.5)")]
        [SerializeField] private Transform boardRoot;
        [Tooltip("進捗・完了表示 (視界の端に置いた小さな Text)")]
        [SerializeField] private Text progressText;
        [Tooltip("半透明 Unlit マテリアル (未設定なら URP/Unlit から作る)")]
        [SerializeField] private Material markerMaterial;

        [Header("Appearance")]
        [SerializeField] private TargetStyle targetStyle = TargetStyle.RingAndDot;
        [Tooltip("黒石の表示色 (光学透過ディスプレイでは黒が見えないためシアン)")]
        [SerializeField] private Color blackStoneColor = new Color(0f, 0.85f, 1f, 0.6f);
        [SerializeField] private Color whiteStoneColor = new Color(1f, 1f, 1f, 0.6f);
        [SerializeField] private Color errorColor = new Color(1f, 0.15f, 0.15f, 0.85f);
        [Tooltip("リングの外半径 (マス 1 辺に対する比)")]
        [SerializeField, Range(0.1f, 0.5f)] private float ringOuterRatio = 0.36f;
        [Tooltip("リングの太さ (マス 1 辺に対する比)")]
        [SerializeField, Range(0.01f, 0.2f)] private float ringWidthRatio = 0.04f;
        [Tooltip("中央ドットの半径 (マス 1 辺に対する比)")]
        [SerializeField, Range(0.01f, 0.2f)] private float dotRadiusRatio = 0.06f;
        [Tooltip("ゴースト石の半径 (マス 1 辺に対する比)")]
        [SerializeField, Range(0.1f, 0.5f)] private float ghostRadiusRatio = 0.34f;
        [Tooltip("誤りリングの太さ (マス 1 辺に対する比)")]
        [SerializeField, Range(0.01f, 0.2f)] private float errorRingWidthRatio = 0.07f;
        [Tooltip("天面からの高さ (盤のローカル単位)")]
        [SerializeField] private float heightAboveTop = 0.05f;
        [Tooltip("誤り表示の点滅周波数 (Hz)。0 なら点滅しない")]
        [SerializeField, Min(0f)] private float errorBlinkHz = 3f;

        [Header("Existing Board Visuals")]
        [Tooltip("表示中に非表示にする AR 盤の子 (既存の合法手ハイライトなど、実験の邪魔になるもの)")]
        [SerializeField] private string[] boardChildrenToHide = { "Highlights_Container" };

        private class MarkerObject
        {
            public GameObject Root;
            public MeshFilter RingFilter;
            public MeshRenderer RingRenderer;
            public MeshFilter CenterFilter;
            public MeshRenderer CenterRenderer;
            public bool Blinks;
        }

        private readonly List<MarkerObject> _pool = new List<MarkerObject>();
        private readonly List<GameObject> _hiddenChildren = new List<GameObject>();
        private Transform _container;
        private Mesh _ringMesh, _dotMesh, _ghostMesh, _errorRingMesh;
        private Material _runtimeMaterial;
        private MaterialPropertyBlock _mpb;
        private ExperimentGuidePresenter _presenter;
        private GuideViewState _lastView;
        private bool _blinkOn = true;

        public Transform BoardRoot
        {
            get => boardRoot;
            set
            {
                if (boardRoot == value) return;
                DestroyContainer();
                boardRoot = value;
                if (_lastView != null) Render(_lastView);
            }
        }

        public Text ProgressText { get => progressText; set => progressText = value; }
        public TargetStyle Style { get => targetStyle; set => targetStyle = value; }
        public Color BlackStoneColor => blackStoneColor;
        public Color WhiteStoneColor => whiteStoneColor;
        public Color ErrorColor => errorColor;

        /// <summary>現在表示中のマーカー数 (テスト・デバッグ用)</summary>
        public int ActiveMarkerCount
        {
            get
            {
                int n = 0;
                foreach (MarkerObject m in _pool) if (m.Root != null && m.Root.activeSelf) n++;
                return n;
            }
        }

        private void OnEnable()
        {
            HideBoardChildren();
            if (controller != null) Bind(controller.StateMachine);
        }

        private void OnDisable()
        {
            Unbind();
            RestoreBoardChildren();
        }

        private void OnDestroy()
        {
            DestroyContainer();
            DestroyAsset(_ringMesh);
            DestroyAsset(_dotMesh);
            DestroyAsset(_ghostMesh);
            DestroyAsset(_errorRingMesh);
            DestroyAsset(_runtimeMaterial);
        }

        private void Update()
        {
            bool on = errorBlinkHz <= 0f || Mathf.Repeat(Time.time * errorBlinkHz, 1f) < 0.5f;
            SetBlinkPhase(on);
        }

        /// <summary>状態管理に接続する (既存の接続は解除)。</summary>
        public void Bind(ExperimentStateMachine stateMachine)
        {
            Unbind();
            if (stateMachine == null) return;
            _presenter = new ExperimentGuidePresenter(stateMachine);
            _presenter.Changed += Render;
            Render(_presenter.View);
        }

        public void Unbind()
        {
            if (_presenter == null) return;
            _presenter.Changed -= Render;
            _presenter.Dispose();
            _presenter = null;
        }

        /// <summary>点滅の明滅状態を設定する (Update から呼ばれる。テスト用に公開)。</summary>
        public void SetBlinkPhase(bool on)
        {
            if (_blinkOn == on) return;
            _blinkOn = on;
            foreach (MarkerObject m in _pool)
            {
                if (m.Root == null || !m.Root.activeSelf || !m.Blinks) continue;
                m.RingRenderer.enabled = on;
            }
        }

        /// <summary>表示内容を反映する。null や範囲外の座標は安全に無視する。</summary>
        public void Render(GuideViewState view)
        {
            _lastView = view;

            if (progressText != null)
            {
                string text = view?.ProgressText ?? "";
                progressText.text = text;
                progressText.enabled = !string.IsNullOrEmpty(text);
            }

            int used = 0;
            if (view != null && boardRoot != null)
            {
                EnsureResources();
                foreach (GuideMarker marker in view.Markers)
                {
                    if (!BoardCoord.IsInside(marker.Col, marker.Row)) continue;
                    MarkerObject mo = GetMarker(used++);
                    ApplyMarker(mo, marker);
                }
            }

            for (int i = used; i < _pool.Count; i++)
            {
                if (_pool[i].Root != null) _pool[i].Root.SetActive(false);
            }
        }

        // ---------------- 内部処理 ----------------

        private void ApplyMarker(MarkerObject mo, GuideMarker marker)
        {
            mo.Root.SetActive(true);
            mo.Root.name = $"Guide_{marker.Kind}_{BoardCoord.ToCoord(marker.Col, marker.Row)}";
            mo.Root.transform.localPosition = BoardGeometry.CellCenterLocal(marker.Col, marker.Row, heightAboveTop);
            mo.Root.transform.localRotation = Quaternion.identity;
            mo.Root.transform.localScale = Vector3.one;

            Color stone = marker.Color == StoneColor.Black ? blackStoneColor : whiteStoneColor;
            mo.Blinks = false;

            switch (marker.Kind)
            {
                case GuideMarkerKind.Target:
                    if (targetStyle == TargetStyle.GhostStone)
                    {
                        SetPart(mo.RingFilter, mo.RingRenderer, null, stone);
                        SetPart(mo.CenterFilter, mo.CenterRenderer, _ghostMesh, stone);
                    }
                    else
                    {
                        SetPart(mo.RingFilter, mo.RingRenderer, _ringMesh, stone);
                        SetPart(mo.CenterFilter, mo.CenterRenderer, _dotMesh, stone);
                    }
                    break;

                case GuideMarkerKind.Error:
                    SetPart(mo.RingFilter, mo.RingRenderer, _errorRingMesh, errorColor);
                    SetPart(mo.CenterFilter, mo.CenterRenderer, null, errorColor);
                    mo.Blinks = true;
                    mo.RingRenderer.enabled = _blinkOn;
                    break;

                case GuideMarkerKind.SetupPlace:
                    SetPart(mo.RingFilter, mo.RingRenderer, _ringMesh, stone);
                    SetPart(mo.CenterFilter, mo.CenterRenderer, _ghostMesh, stone);
                    break;

                case GuideMarkerKind.SetupRemove:
                    SetPart(mo.RingFilter, mo.RingRenderer, _errorRingMesh, errorColor);
                    SetPart(mo.CenterFilter, mo.CenterRenderer, null, errorColor);
                    break;
            }
        }

        private void SetPart(MeshFilter filter, MeshRenderer renderer, Mesh mesh, Color color)
        {
            filter.sharedMesh = mesh;
            renderer.enabled = mesh != null;
            if (mesh == null) return;
            _mpb.Clear();
            _mpb.SetColor("_BaseColor", color);
            _mpb.SetColor("_Color", color);
            renderer.SetPropertyBlock(_mpb);
        }

        private MarkerObject GetMarker(int index)
        {
            while (_pool.Count <= index) _pool.Add(new MarkerObject());
            MarkerObject mo = _pool[index];
            if (mo.Root == null)
            {
                mo.Root = new GameObject("Guide_Marker");
                mo.Root.transform.SetParent(_container, false);
                CreatePart(mo.Root.transform, "Ring", out mo.RingFilter, out mo.RingRenderer, 0f);
                // 中央ドット / ゴースト石はリングよりわずかに上に置き、Z ファイティングを避ける
                CreatePart(mo.Root.transform, "Center", out mo.CenterFilter, out mo.CenterRenderer, 0.01f);
            }
            return mo;
        }

        private void CreatePart(Transform parent, string name, out MeshFilter filter, out MeshRenderer renderer, float yOffset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, yOffset, 0f);
            filter = go.AddComponent<MeshFilter>();
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = markerMaterial != null ? markerMaterial : _runtimeMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void EnsureResources()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            float cell = BoardGeometry.CellSize;
            if (_ringMesh == null) _ringMesh = BoardGeometry.BuildRingMesh((ringOuterRatio - ringWidthRatio) * cell, ringOuterRatio * cell);
            if (_dotMesh == null) _dotMesh = BoardGeometry.BuildRingMesh(0f, dotRadiusRatio * cell, 24);
            if (_ghostMesh == null) _ghostMesh = BoardGeometry.BuildRingMesh(0f, ghostRadiusRatio * cell);
            if (_errorRingMesh == null) _errorRingMesh = BoardGeometry.BuildRingMesh((ringOuterRatio + 0.06f - errorRingWidthRatio) * cell, (ringOuterRatio + 0.06f) * cell);

            if (markerMaterial == null) markerMaterial = Resources.Load<Material>(DefaultMaterialResource);
            if (markerMaterial == null && _runtimeMaterial == null) _runtimeMaterial = CreateTransparentMaterial();

            if (_container == null)
            {
                Transform existing = boardRoot.Find(ContainerName);
                if (existing != null) _container = existing;
                else
                {
                    var go = new GameObject(ContainerName);
                    go.transform.SetParent(boardRoot, false);
                    _container = go.transform;
                }
            }
        }

        private static Material CreateTransparentMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "ExperimentGuide_Runtime" };
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 0f);   // Alpha
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.SetOverrideTag("RenderType", "Transparent");
            }
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return mat;
        }

        /// <summary>boardChildrenToHide に挙げた既存の表示を隠す (OnEnable で自動実行)。</summary>
        public void HideBoardChildren()
        {
            _hiddenChildren.Clear();
            if (boardRoot == null || boardChildrenToHide == null) return;
            foreach (string childName in boardChildrenToHide)
            {
                if (string.IsNullOrEmpty(childName)) continue;
                Transform child = boardRoot.Find(childName);
                if (child != null && child.gameObject.activeSelf)
                {
                    child.gameObject.SetActive(false);
                    _hiddenChildren.Add(child.gameObject);
                }
            }
        }

        /// <summary>隠した既存の表示を元に戻す (OnDisable で自動実行)。</summary>
        public void RestoreBoardChildren()
        {
            foreach (GameObject go in _hiddenChildren)
            {
                if (go != null) go.SetActive(true);
            }
            _hiddenChildren.Clear();
        }

        private void DestroyContainer()
        {
            if (_container != null) DestroyAsset(_container.gameObject);
            _container = null;
            _pool.Clear();
        }

        private static void DestroyAsset(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
