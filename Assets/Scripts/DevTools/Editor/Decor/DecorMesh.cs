#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopGame.EditorTools.Decor
{
    /// <summary>
    /// Small flat-shaded (low-poly) mesh writer used by <see cref="DecorPrefabBuilder"/>.
    /// Each material becomes one sub-mesh, so a whole prop is one renderer.
    /// </summary>
    public class DecorMesh
    {
        private class Part { public readonly List<Vector3> V = new(); public readonly List<Vector2> UV = new(); public readonly List<int> T = new(); }

        private readonly List<Material> _order = new();
        private readonly Dictionary<Material, Part> _parts = new();
        private System.Random _rng;

        public DecorMesh(int seed) { _rng = new System.Random(seed); }

        public float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
        public int Int(int a, int bExclusive) => _rng.Next(a, bExclusive);
        public Material[] Materials => _order.ToArray();

        private Part Get(Material m)
        {
            if (!_parts.TryGetValue(m, out var p)) { p = new Part(); _parts[m] = p; _order.Add(m); }
            return p;
        }

        public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector2 ua = default, Vector2 ub = default, Vector2 uc = default)
        {
            var p = Get(m);
            int i = p.V.Count;
            p.V.Add(a); p.V.Add(b); p.V.Add(c);
            p.UV.Add(ua); p.UV.Add(ub); p.UV.Add(uc);
            p.T.Add(i); p.T.Add(i + 1); p.T.Add(i + 2);
        }

        public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua = default, Vector2 ub = default, Vector2 uc = default, Vector2 ud = default)
        {
            Tri(m, a, b, c, ua, ub, uc);
            Tri(m, a, c, d, ua, uc, ud);
        }

        public void Box(Material m, Vector3 center, Vector3 size, Quaternion rot)
        {
            Vector3 h = size * .5f;
            Vector3 P(float x, float y, float z) => center + rot * new Vector3(x * h.x, y * h.y, z * h.z);
            Quad(m, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
            Quad(m, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
            Quad(m, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
            Quad(m, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
            Quad(m, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
            Quad(m, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        }

        /// <summary>Cone / cylinder / tapered tower along local up. uv.y runs 0 (bottom) to 1 (top).</summary>
        public void Frustum(Material m, Vector3 baseCenter, float rBottom, float rTop, float height, int segments, Quaternion rot, bool capTop = true, bool capBottom = false, float twist = 0f)
        {
            for (int s = 0; s < segments; s++)
            {
                float a0 = (s / (float)segments) * Mathf.PI * 2f, a1 = ((s + 1) / (float)segments) * Mathf.PI * 2f;
                float t0 = a0 + twist, t1 = a1 + twist;
                Vector3 b0 = baseCenter + rot * new Vector3(Mathf.Cos(a0) * rBottom, 0, Mathf.Sin(a0) * rBottom);
                Vector3 b1 = baseCenter + rot * new Vector3(Mathf.Cos(a1) * rBottom, 0, Mathf.Sin(a1) * rBottom);
                Vector3 u0 = baseCenter + rot * new Vector3(Mathf.Cos(t0) * rTop, height, Mathf.Sin(t0) * rTop);
                Vector3 u1 = baseCenter + rot * new Vector3(Mathf.Cos(t1) * rTop, height, Mathf.Sin(t1) * rTop);
                float x0 = s / (float)segments, x1 = (s + 1) / (float)segments;
                if (rTop > 0.0001f) Quad(m, b0, u0, u1, b1, new Vector2(x0, 0), new Vector2(x0, 1), new Vector2(x1, 1), new Vector2(x1, 0));
                else Tri(m, b0, u0, b1, new Vector2(x0, 0), new Vector2(x0, 1), new Vector2(x1, 0));
                Vector3 top = baseCenter + rot * new Vector3(0, height, 0);
                if (capTop && rTop > 0.0001f) Tri(m, top, u1, u0);
                if (capBottom) Tri(m, baseCenter, b0, b1);
            }
        }

        /// <summary>Open ring wall (no caps), uv.y 0 at the bottom, 1 at the top. For fog rings.</summary>
        public void Ring(Material m, float radius, float height, int segments)
        {
            for (int s = 0; s < segments; s++)
            {
                float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 b0 = new(Mathf.Cos(a0) * radius, 0, Mathf.Sin(a0) * radius), b1 = new(Mathf.Cos(a1) * radius, 0, Mathf.Sin(a1) * radius);
                float x0 = s / (float)segments, x1 = (s + 1) / (float)segments;
                Quad(m, b0, b0 + Vector3.up * height, b1 + Vector3.up * height, b1, new Vector2(x0, 0), new Vector2(x0, 1), new Vector2(x1, 1), new Vector2(x1, 0));
            }
        }

        /// <summary>Flat disc facing up, uv (0..1) across it, centre = (.5,.5). For the cloud sea.</summary>
        public void Disc(Material m, float radius, int segments, int rings)
        {
            for (int r = 0; r < rings; r++)
            {
                float r0 = radius * r / rings, r1 = radius * (r + 1) / rings;
                for (int s = 0; s < segments; s++)
                {
                    float a0 = s / (float)segments * Mathf.PI * 2f, a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                    Vector3 p00 = new(Mathf.Cos(a0) * r0, 0, Mathf.Sin(a0) * r0), p01 = new(Mathf.Cos(a1) * r0, 0, Mathf.Sin(a1) * r0);
                    Vector3 p10 = new(Mathf.Cos(a0) * r1, 0, Mathf.Sin(a0) * r1), p11 = new(Mathf.Cos(a1) * r1, 0, Mathf.Sin(a1) * r1);
                    Vector2 U(Vector3 p) => new(.5f + p.x / (2f * radius), .5f + p.z / (2f * radius));
                    if (r == 0) Tri(m, p00, p11, p10, U(p00), U(p11), U(p10));
                    else Quad(m, p00, p01, p11, p10, U(p00), U(p01), U(p11), U(p10));
                }
            }
        }

        /// <summary>Lumpy low-poly blob (rocks, bushes, clouds). flattenBelow cuts the bottom flat (local, -1..1).</summary>
        public void Blob(Material m, Vector3 center, Vector3 radii, Quaternion rot, int subdiv, float jitter, float flattenBelow = -2f)
        {
            var (verts, tris) = Icosphere(subdiv);
            var moved = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i] * (1f + Range(-jitter, jitter));
                if (v.y < flattenBelow) v.y = flattenBelow + (v.y - flattenBelow) * .15f;
                moved[i] = center + rot * Vector3.Scale(v, radii);
            }
            for (int i = 0; i < tris.Count; i += 3) Tri(m, moved[tris[i]], moved[tris[i + 1]], moved[tris[i + 2]]);
        }

        /// <summary>One grass tuft: a fan of thin bent blades.</summary>
        public void Tuft(Material m, Vector3 basePos, int blades, float height, float width)
        {
            for (int b = 0; b < blades; b++)
            {
                float ang = Range(0, Mathf.PI * 2f);
                Vector3 dir = new(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                Vector3 side = Vector3.Cross(Vector3.up, dir) * width * .5f;
                float h = height * Range(.6f, 1.15f);
                Vector3 root = basePos + dir * Range(0, width * 1.2f);
                Vector3 mid = root + Vector3.up * h * .55f + dir * h * .12f;
                Vector3 tip = root + Vector3.up * h + dir * h * Range(.25f, .45f);
                Quad(m, root - side, mid - side * .7f, mid + side * .7f, root + side, new Vector2(0, 0), new Vector2(0, .55f), new Vector2(1, .55f), new Vector2(1, 0));
                Tri(m, mid - side * .7f, tip, mid + side * .7f, new Vector2(0, .55f), new Vector2(.5f, 1), new Vector2(1, .55f));
            }
        }

        /// <summary>A single flower: thin stem, petals round a centre, facing up with a small tilt.</summary>
        public void Flower(Material stem, Material petal, Material middle, Vector3 basePos, float height, float size, int petals)
        {
            Quaternion tilt = Quaternion.Euler(Range(-12, 12), Range(0, 360), Range(-12, 12));
            Vector3 head = basePos + tilt * Vector3.up * height;
            Box(stem, basePos + tilt * Vector3.up * height * .5f, new Vector3(.03f, height, .03f), tilt);
            // Two leaves on the stem.
            for (int l = 0; l < 2; l++)
            {
                Quaternion lr = tilt * Quaternion.Euler(0, l * 180f + Range(-20, 20), 0);
                Vector3 lp = basePos + tilt * Vector3.up * height * Range(.2f, .45f);
                Vector3 o = lr * Vector3.forward, s = lr * Vector3.right * size * .18f;
                Quad(stem, lp, lp + o * size * .35f + s + Vector3.up * size * .12f, lp + o * size * .7f + Vector3.up * size * .25f, lp + o * size * .35f - s + Vector3.up * size * .12f);
            }
            for (int p = 0; p < petals; p++)
            {
                float a = p / (float)petals * 360f;
                Quaternion pr = tilt * Quaternion.Euler(0, a, 0);
                Blob(petal, head + pr * new Vector3(0, 0, size * .5f), new Vector3(size * .26f, size * .07f, size * .42f), pr * Quaternion.Euler(-12, 0, 0), 0, .05f);
            }
            Blob(middle, head + tilt * Vector3.up * size * .05f, new Vector3(size * .22f, size * .14f, size * .22f), tilt, 0, .05f);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            foreach (var m in _order) { verts.AddRange(_parts[m].V); uvs.AddRange(_parts[m].UV); }
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = _order.Count;
            int offset = 0;
            for (int s = 0; s < _order.Count; s++)
            {
                var p = _parts[_order[s]];
                var t = new int[p.T.Count];
                for (int i = 0; i < t.Length; i++) t[i] = p.T[i] + offset;
                mesh.SetTriangles(t, s);
                offset += p.V.Count;
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        private static (List<Vector3>, List<int>) Icosphere(int subdiv)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            var f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int s = 0; s < subdiv; s++)
            {
                var cache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    v.Add(((v[a] + v[b]) * .5f).normalized);
                    cache[key] = v.Count - 1;
                    return v.Count - 1;
                }
                var nf = new List<int>();
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            return (v, f);
        }
    }
}
#endif
