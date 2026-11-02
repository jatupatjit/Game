using System.Collections.Generic;
using CoopGame.CarrySystem;
using UnityEngine;

namespace CoopGame.Network
{
    /// <summary>
    /// Crack overlays on every <see cref="FragileCargo"/> as it loses HP (each screen, from the synced HP): below 75 HP
    /// the first cracks show, below 50 more, below 25 all of them. Quads with a generated crack texture sit just outside
    /// the crate's box faces; no prefab or material of the crate is changed.
    /// </summary>
    public sealed class CrateCracks : MonoBehaviour
    {
        private const float ScanInterval = 1f;
        private static readonly int[] Thresholds = { 75, 50, 25 };
        private static Material _material;
        private readonly Dictionary<FragileCargo, Renderer[][]> _crates = new();
        private float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("CrateCracks") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<CrateCracks>();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanInterval;
                var dead = new List<FragileCargo>();
                foreach (var pair in _crates) if (pair.Key == null) dead.Add(pair.Key);
                foreach (var cargo in dead) _crates.Remove(cargo);
                foreach (var cargo in FindObjectsByType<FragileCargo>())
                    if (!_crates.ContainsKey(cargo)) _crates[cargo] = Build(cargo);
            }
            foreach (var pair in _crates)
            {
                if (pair.Key == null) continue;
                int hp = pair.Key.CurrentHP.Value;
                for (int stage = 0; stage < pair.Value.Length; stage++)
                {
                    bool show = hp < Thresholds[stage] && hp > 0;
                    foreach (var quad in pair.Value[stage])
                        if (quad != null && quad.enabled != show) quad.enabled = show;
                }
            }
        }

        /// <summary>Three stages of crack quads on the crate's side and top faces (from its colliders' box).</summary>
        private static Renderer[][] Build(FragileCargo cargo)
        {
            var stages = new[] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
            Transform t = cargo.transform;
            bool any = false;
            Bounds box = default;
            foreach (var c in cargo.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || c is not BoxCollider b) continue;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = b.transform.TransformPoint(b.center + Vector3.Scale(b.size * .5f,
                        new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1)));
                    Vector3 local = t.InverseTransformPoint(corner);
                    if (!any) { box = new Bounds(local, Vector3.zero); any = true; } else box.Encapsulate(local);
                }
            }
            if (!any) return new[] { new Renderer[0], new Renderer[0], new Renderer[0] };
            Vector3 e = box.extents;
            // Face centre, outward normal and face size (local space). Each face gets a crack at a different stage.
            var faces = new (Vector3 n, Vector2 size)[]
            {
                (Vector3.forward, new Vector2(e.x, e.y)), (Vector3.back, new Vector2(e.x, e.y)),
                (Vector3.right, new Vector2(e.z, e.y)), (Vector3.left, new Vector2(e.z, e.y)), (Vector3.up, new Vector2(e.x, e.z))
            };
            int[] stageOf = { 0, 1, 1, 2, 2 };
            var random = new System.Random(cargo.GetHashCode());
            for (int f = 0; f < faces.Length; f++)
            {
                var (n, size) = faces[f];
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(quad.GetComponent<Collider>());
                quad.name = "Crack";
                quad.transform.SetParent(t, false);
                Vector3 depth = Vector3.Scale(n, e);
                Vector3 jitter = new Vector3((float)random.NextDouble() - .5f, (float)random.NextDouble() - .5f, 0f) * .3f;
                quad.transform.localRotation = Quaternion.LookRotation(-n, n == Vector3.up ? Vector3.forward : Vector3.up);
                quad.transform.localPosition = box.center + depth * 1.01f + quad.transform.localRotation * Vector3.Scale(jitter, new Vector3(size.x, size.y, 0f));
                float scale = Mathf.Min(size.x, size.y) * 1.3f;
                quad.transform.localScale = new Vector3(scale, scale, 1f);
                var renderer = quad.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = Material();
                if (renderer.sharedMaterial == null) { Destroy(quad); continue; }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.enabled = false;
                stages[stageOf[f]].Add(renderer);
            }
            return new[] { stages[0].ToArray(), stages[1].ToArray(), stages[2].ToArray() };
        }

        /// <summary>Transparent dark crack lines (random branching walks), drawn once.</summary>
        private static Material Material()
        {
            if (_material != null) return _material;
            const int n = 256;
            var pixels = new Color32[n * n];
            var random = new System.Random(7);
            void Walk(float x, float y, float angle, int steps, int width)
            {
                for (int i = 0; i < steps; i++)
                {
                    angle += ((float)random.NextDouble() - .5f) * .9f;
                    x += Mathf.Cos(angle) * 2f; y += Mathf.Sin(angle) * 2f;
                    for (int dx = -width; dx <= width; dx++)
                        for (int dy = -width; dy <= width; dy++)
                        {
                            int px = (int)x + dx, py = (int)y + dy;
                            if (px >= 0 && py >= 0 && px < n && py < n) pixels[py * n + px] = new Color32(40, 25, 15, 230);
                        }
                    if (random.NextDouble() < .04 && width > 0) Walk(x, y, angle + 1.1f * (random.NextDouble() < .5 ? 1 : -1), steps / 2, width - 1);
                }
            }
            for (int r = 0; r < 4; r++) Walk(n / 2, n / 2, r * Mathf.PI * .5f + .4f, 60, 2);
            var texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply();
            // Resources/CrateCrack.mat (URP Unlit, transparent) keeps the shader and its variant in the build.
            var template = Resources.Load<Material>("CrateCrack");
            if (template == null) { Debug.LogWarning("[CrateCracks] Resources/CrateCrack material missing."); return null; }
            _material = new Material(template) { name = "CrateCracks (generated)" };
            _material.SetTexture("_BaseMap", texture);
            return _material;
        }
    }
}
