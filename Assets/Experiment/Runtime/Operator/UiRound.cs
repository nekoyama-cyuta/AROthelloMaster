using System;
using UnityEngine;
using UnityEngine.UI;

namespace AROthello.Experiment
{
    /// <summary>
    /// 角丸・ピル型の Image にする。9 スライスの角丸スプライト (UiSprites.Rounded) の角の大きさを、
    /// Image.pixelsPerUnitMultiplier で実際の大きさに合わせる。Pill なら短辺の半分 = 両端が半円になる。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Image))]
    public class UiRound : MonoBehaviour
    {
        [Tooltip("角の半径 (Canvas の単位)")]
        public float Radius = 24f;
        [Tooltip("両端を半円にする (半径 = 短辺の半分)")]
        public bool Pill;

        private Image _image;

        private void OnEnable() => Apply();
        private void OnRectTransformDimensionsChange() => Apply();

        public void Apply()
        {
            if (_image == null) _image = GetComponent<Image>();
            if (_image == null || _image.sprite == null) return;
            UnityEngine.Rect r = ((RectTransform)transform).rect;
            float half = Mathf.Min(r.width, r.height) * 0.5f;
            float radius = Pill ? half : Mathf.Min(Radius, half);
            if (radius < 0.5f) radius = 0.5f;
            float unit = _image.pixelsPerUnit; // スプライトの ppu / Canvas の referencePixelsPerUnit
            if (unit <= 0f) unit = 1f;
            _image.pixelsPerUnitMultiplier = UiSprites.CornerPx / (radius * unit);
        }
    }

    /// <summary>実験者画面で使う、コードで作るスプライト。</summary>
    public static class UiSprites
    {
        /// <summary>角丸スプライトの角の半径 (ピクセル)。9 スライスの境界も同じ。</summary>
        public const int CornerPx = 40;
        private const int StrokePx = 3;

        private static Sprite _rounded, _stroke, _circle, _triangle, _check, _cross, _sliders;

        /// <summary>塗りの角丸 (9 スライス)。</summary>
        public static Sprite Rounded => _rounded != null ? _rounded : (_rounded = MakeRounded(false));
        /// <summary>枠線だけの角丸 (9 スライス)。</summary>
        public static Sprite Stroke => _stroke != null ? _stroke : (_stroke = MakeRounded(true));

        public static Sprite Circle => _circle != null ? _circle : (_circle = Make(64, (x, y) =>
        {
            float dx = x - 31.5f, dy = y - 31.5f;
            return Mathf.Clamp01(31.5f - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
        }));

        /// <summary>上向きの三角形 (矢印)。回転して向きを変える。</summary>
        public static Sprite Triangle => _triangle != null ? _triangle : (_triangle = Make(64, (x, y) =>
        {
            if (y < 8 || y > 56) return 0f;
            float half = (56f - y) / 48f * 26f;
            return Mathf.Clamp01(half - Mathf.Abs(x - 31.5f) + 0.5f);
        }));

        /// <summary>チェック (レ点)。</summary>
        public static Sprite Check => _check != null ? _check : (_check = Make(64, (x, y) =>
            Mathf.Clamp01(5f - Mathf.Min(Seg(x, y, 14, 33, 27, 20), Seg(x, y, 27, 20, 50, 46)) + 0.5f)));

        /// <summary>バツ。</summary>
        public static Sprite Cross => _cross != null ? _cross : (_cross = Make(64, (x, y) =>
            Mathf.Clamp01(5f - Mathf.Min(Seg(x, y, 17, 17, 47, 47), Seg(x, y, 17, 47, 47, 17)) + 0.5f)));

        /// <summary>設定 (つまみ付きの横線 2 本)。</summary>
        public static Sprite Sliders => _sliders != null ? _sliders : (_sliders = Make(64, (x, y) =>
        {
            float a = Mathf.Clamp01(3.2f - Mathf.Min(Seg(x, y, 8, 44, 56, 44), Seg(x, y, 8, 20, 56, 20)) + 0.5f);
            float k1 = Mathf.Sqrt((x - 40) * (x - 40) + (y - 44) * (y - 44));
            float k2 = Mathf.Sqrt((x - 24) * (x - 24) + (y - 20) * (y - 20));
            float knob = Mathf.Clamp01(8f - Mathf.Min(k1, k2) + 0.5f);
            return Mathf.Max(a, knob);
        }));

        private static float Seg(float px, float py, float ax, float ay, float bx, float by)
        {
            px += 0.5f; py += 0.5f;
            float vx = bx - ax, vy = by - ay;
            float t = Mathf.Clamp01(((px - ax) * vx + (py - ay) * vy) / (vx * vx + vy * vy));
            float dx = px - (ax + t * vx), dy = py - (ay + t * vy);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static Sprite MakeRounded(bool strokeOnly)
        {
            int r = CornerPx, n = r * 2 + 2;
            Texture2D tex = NewTexture(n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float cx = x + 0.5f, cy = y + 0.5f;
                    float dx = Mathf.Max(r - cx, 0f, cx - (n - r));
                    float dy = Mathf.Max(r - cy, 0f, cy - (n - r));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float outer = Mathf.Clamp01(r - d + 0.5f);
                    // r - d = 外周から内側への距離 (直線部でも角でも同じ)。枠線はその距離が StrokePx 以内だけ塗る
                    float a = strokeOnly ? outer * Mathf.Clamp01(d - (r - StrokePx) + 0.5f) : outer;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            Sprite s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(r, r, r, r));
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        private static Sprite Make(int n, Func<int, int, float> alpha)
        {
            Texture2D tex = NewTexture(n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x, y)) * 255f));
            tex.SetPixels32(px);
            tex.Apply();
            Sprite s = Sprite.Create(tex, new UnityEngine.Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        private static Texture2D NewTexture(int n)
        {
            return new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }
}
