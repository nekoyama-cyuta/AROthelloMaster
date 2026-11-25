using UnityEngine;

namespace AROthello.Experiment
{
    /// <summary>
    /// 盤面ローカル座標の計算。既存の AR 盤 (OthelloBoard_AR_Target) と同じく、
    /// 盤の Transform のローカル空間 [-0.5, +0.5] に盤全体が収まり、天面が y = +0.5 である前提。
    /// row 0 (座標 "x1") が +Z 側 (奥)、col 0 ("A") が -X 側 (左)。
    /// </summary>
    public static class BoardGeometry
    {
        /// <summary>マス 1 辺の長さ (ローカル単位)</summary>
        public const float CellSize = 1f / BoardCoord.Size;
        /// <summary>天面の y (ローカル単位)</summary>
        public const float TopSurfaceY = 0.5f;

        /// <summary>マス中心のローカル座標。heightAboveTop は天面からの高さ (ローカル単位)。</summary>
        public static Vector3 CellCenterLocal(int col, int row, float heightAboveTop = 0f)
        {
            float x = -0.5f + (col + 0.5f) * CellSize;
            float z = +0.5f - (row + 0.5f) * CellSize;
            return new Vector3(x, TopSurfaceY + heightAboveTop, z);
        }

        /// <summary>
        /// XZ 平面上の円環 (annulus) メッシュ。innerRadius = 0 なら円盤。法線は +Y。
        /// </summary>
        public static Mesh BuildRingMesh(float innerRadius, float outerRadius, int segments = 48)
        {
            segments = Mathf.Max(3, segments);
            innerRadius = Mathf.Max(0f, innerRadius);
            outerRadius = Mathf.Max(innerRadius + 1e-5f, outerRadius);

            bool isDisc = innerRadius <= 0f;
            int ringVerts = segments + 1;
            var vertices = new Vector3[isDisc ? ringVerts + 1 : ringVerts * 2];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * (isDisc ? 3 : 6)];

            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[i] = dir * outerRadius;
                uvs[i] = new Vector2((float)i / segments, 1f);
                if (!isDisc)
                {
                    vertices[ringVerts + i] = dir * innerRadius;
                    uvs[ringVerts + i] = new Vector2((float)i / segments, 0f);
                }
            }
            if (isDisc)
            {
                vertices[ringVerts] = Vector3.zero;
                uvs[ringVerts] = new Vector2(0.5f, 0f);
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                if (isDisc)
                {
                    // 上から見て時計回り (Unity の表面 = +Y から見える向き)
                    triangles[t++] = ringVerts;
                    triangles[t++] = i + 1;
                    triangles[t++] = i;
                }
                else
                {
                    int o0 = i, o1 = i + 1, i0 = ringVerts + i, i1 = ringVerts + i + 1;
                    triangles[t++] = i0; triangles[t++] = o1; triangles[t++] = o0;
                    triangles[t++] = i0; triangles[t++] = i1; triangles[t++] = o1;
                }
            }

            var mesh = new Mesh { name = isDisc ? "GuideDisc" : "GuideRing" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
