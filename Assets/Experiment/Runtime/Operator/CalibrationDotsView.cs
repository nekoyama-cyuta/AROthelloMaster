using UnityEngine;

namespace AROthello.Experiment
{
    /// <summary>
    /// 簡易キャリブレーション確認用に、AR 盤の四隅と中央にテストドットを表示する。
    /// 実物の盤の四隅・中央と重なっているかを目視で確かめ、ずれていれば位置オフセットで直す。
    /// </summary>
    public class CalibrationDotsView : MonoBehaviour
    {
        [Tooltip("AR 盤の Transform (ローカル空間 [-0.5, +0.5]、天面 y = +0.5)")]
        [SerializeField] private Transform boardRoot;
        [SerializeField] private Material dotMaterial;
        [SerializeField] private Color dotColor = new Color(1f, 0.2f, 0.9f, 0.9f);
        [Tooltip("ドットの半径 (盤 1 辺に対する比)")]
        [SerializeField, Range(0.002f, 0.05f)] private float dotRadius = 0.012f;
        [Tooltip("天面からの高さ (盤のローカル単位)")]
        [SerializeField] private float heightAboveTop = 0.05f;
        [SerializeField] private bool visibleOnStart = false;

        private const string ContainerName = "CalibrationDots_Container";

        /// <summary>ドットの位置 (盤のローカル XZ): 左奥, 右奥, 右手前, 左手前, 中央</summary>
        public static readonly Vector2[] DotPositions =
        {
            new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, -0.5f), new Vector2(-0.5f, -0.5f), Vector2.zero
        };

        private Transform _container;
        private Mesh _mesh;
        private bool _visible;

        public bool Visible => _visible;

        public Transform BoardRoot
        {
            get => boardRoot;
            set
            {
                if (boardRoot == value) return;
                DestroyContainer();
                boardRoot = value;
                if (_visible) SetVisible(true);
            }
        }

        private void Start()
        {
            SetVisible(visibleOnStart);
        }

        private void OnDestroy()
        {
            DestroyContainer();
            if (_mesh != null) DestroyObj(_mesh);
        }

        public void Toggle() => SetVisible(!_visible);

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (boardRoot == null)
            {
                return;
            }
            if (visible) EnsureDots();
            if (_container != null) _container.gameObject.SetActive(visible);
        }

        private void EnsureDots()
        {
            if (_container != null) return;
            Transform existing = boardRoot.Find(ContainerName);
            if (existing != null) DestroyObj(existing.gameObject);

            if (_mesh == null) _mesh = BoardGeometry.BuildRingMesh(0f, dotRadius, 24);
            if (dotMaterial == null) dotMaterial = Resources.Load<Material>(ExperimentGuideView.DefaultMaterialResource);

            var root = new GameObject(ContainerName);
            root.transform.SetParent(boardRoot, false);
            _container = root.transform;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", dotColor);
            mpb.SetColor("_Color", dotColor);

            string[] names = { "Dot_FarLeft", "Dot_FarRight", "Dot_NearRight", "Dot_NearLeft", "Dot_Center" };
            for (int i = 0; i < DotPositions.Length; i++)
            {
                var go = new GameObject(names[i]);
                go.transform.SetParent(_container, false);
                go.transform.localPosition = new Vector3(DotPositions[i].x, BoardGeometry.TopSurfaceY + heightAboveTop, DotPositions[i].y);
                go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = dotMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.SetPropertyBlock(mpb);
            }
        }

        private void DestroyContainer()
        {
            if (_container != null) DestroyObj(_container.gameObject);
            _container = null;
        }

        private static void DestroyObj(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
