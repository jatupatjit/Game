#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using CoopGame.SceneDecor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools.Decor
{
    /// <summary>
    /// Dresses the open scene with the Assets/Prefabs/Decor prefabs:
    /// ground decor (grass, flowers, bushes, mushrooms, big rocks at island rims) on every grass top,
    /// and a sky (cloud sea below, fog rings round each island, drifting clouds, balloons, landmark islets far out).
    /// Ground decor goes into "Decor_Ground" children of the island it sits on, so it moves with sinking/lifting islands.
    /// Safe to run again: it removes only what it made before ("Decor_Ground" objects and the "L2_Decor" root).
    /// Decor never sits on or next to gameplay objects: any collider or trigger nearby blocks a spot.
    /// </summary>
    public static class Level02DecorPlacer
    {
        private const string SkyRootName = "L2_Decor";
        private const string GroundHolderName = "Decor_Ground";
        private const string P = DecorPrefabBuilder.PrefabRoot + "/";
        private static readonly string[] GroundKeywords = { "grass", "หญ้า" };

        [MenuItem("Tools/Decor/Dress Open Scene (ground + sky)")]
        public static void Menu() => Debug.Log(Dress(true, true));

        [MenuItem("Tools/Decor/Remove Decor From Open Scene")]
        public static void RemoveMenu() { RemoveOld(EditorSceneManager.GetActiveScene()); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); }

        private class Island { public Transform Root; public List<Collider> Grounds = new(); public Bounds Bounds; }

        public static string Dress(bool ground, bool sky, int seed = 2026)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[Decor] Stop Play mode first.";
            var scene = EditorSceneManager.GetActiveScene();
            if (AssetDatabase.LoadAssetAtPath<GameObject>(P + "Nature/Decor_GrassClump_A.prefab") == null) DecorPrefabBuilder.BuildAll();
            RemoveOld(scene);
            Physics.SyncTransforms();

            var islands = FindIslands(scene);
            if (islands.Count == 0) return "[Decor] No grass ground found in " + scene.path;
            var groundSet = new HashSet<Collider>(islands.SelectMany(i => i.Grounds));
            var rng = new System.Random(seed);
            var log = new List<string>();
            int total = 0;

            if (ground)
                foreach (var island in islands)
                {
                    int n = DressIsland(scene, island, groundSet, rng);
                    total += n;
                    log.Add(island.Root.name + ": " + n);
                }

            if (sky) log.Add(BuildSky(scene, islands, rng));
            EditorSceneManager.MarkSceneDirty(scene);
            return "[Decor] " + scene.name + ": " + total + " ground props on " + islands.Count + " islands\n" + string.Join("\n", log);
        }

        // ---------- ground ----------

        private struct Kind
        {
            public string Prefab; public float PerSqM; public float Clear; public float Spacing; public bool Rim; public float MinScale, MaxScale;
            public Kind(string p, float d, float clear, float spacing, bool rim, float s0, float s1) { Prefab = p; PerSqM = d; Clear = clear; Spacing = spacing; Rim = rim; MinScale = s0; MaxScale = s1; }
        }

        // George 2026-11-09: the first pass was too busy. Small props (grass, flowers, small rocks, bushes) at 40%, big rocks at 60%.
        private const float SmallDecorDensity = .4f, BigRockDensity = .6f;

        private static readonly Kind[] Kinds =
        {
            new("Nature/Decor_Rock_Big_A", 1f / 260f, 4f, 9f, true, .8f, 1.3f),
            new("Nature/Decor_Rock_Big_B", 1f / 300f, 4.5f, 9f, true, .8f, 1.2f),
            new("Nature/Decor_Rock_Big_C", 1f / 360f, 5f, 10f, true, .8f, 1.15f),
            new("Nature/Decor_Bush_A", 1f / 110f, 1.2f, 4f, false, .8f, 1.3f),
            new("Nature/Decor_Bush_B", 1f / 140f, 1.2f, 4f, false, .8f, 1.3f),
            new("Nature/Decor_Mushrooms", 1f / 180f, .6f, 5f, false, .9f, 1.4f),
            new("Nature/Decor_Rocks_Small", 1f / 160f, .8f, 5f, false, .8f, 1.3f),
            new("Nature/Decor_FlowerPatch_A", 1f / 45f, .7f, 3.2f, false, .8f, 1.25f),
            new("Nature/Decor_FlowerPatch_B", 1f / 45f, .7f, 3.2f, false, .8f, 1.25f),
            new("Nature/Decor_FlowerPatch_C", 1f / 55f, .7f, 3.2f, false, .8f, 1.25f),
            new("Nature/Decor_FlowerPatch_D", 1f / 45f, .7f, 3.2f, false, .8f, 1.25f),
            new("Nature/Decor_GrassClump_A", 1f / 22f, .4f, 2.6f, false, .8f, 1.3f),
            new("Nature/Decor_GrassClump_B", 1f / 22f, .4f, 2.6f, false, .8f, 1.3f),
            new("Nature/Decor_GrassClump_C", 1f / 22f, .4f, 2.6f, false, .8f, 1.3f),
        };

        private static int DressIsland(UnityEngine.SceneManagement.Scene scene, Island island, HashSet<Collider> groundSet, System.Random rng)
        {
            Transform holder = null;
            var placed = new List<(Vector3 p, float r)>();
            int count = 0;
            float area = island.Grounds.Sum(g => g.bounds.size.x * g.bounds.size.z);
            Vector3 centre = island.Bounds.center;
            foreach (var kind in Kinds)
            {
                if (kind.Rim && area < 120f) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(P + kind.Prefab + ".prefab");
                if (prefab == null) continue;
                int want = Mathf.RoundToInt(area * kind.PerSqM * (kind.Rim ? BigRockDensity : SmallDecorDensity));
                for (int attempt = 0, got = 0; attempt < want * 8 && got < want; attempt++)
                {
                    var g = island.Grounds[rng.Next(island.Grounds.Count)];
                    var b = g.bounds;
                    var start = new Vector3(Lerp(rng, b.min.x, b.max.x), b.max.y + 3f, Lerp(rng, b.min.z, b.max.z));
                    if (!Physics.Raycast(start, Vector3.down, out var hit, b.size.y + 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!groundSet.Contains(hit.collider) || hit.normal.y < .86f || !IsGrass(hit)) continue;
                    if (placed.Any(q => (q.p - hit.point).sqrMagnitude < Mathf.Pow(Mathf.Max(kind.Spacing, q.r), 2))) continue;
                    if (Blocked(hit.point, kind.Clear, groundSet)) continue;
                    if (kind.Rim && !NearRim(hit.point, centre, groundSet)) continue;
                    if (!kind.Rim && !InsideTop(hit.point, centre, groundSet)) continue;

                    if (holder == null) holder = Holder(island.Root);
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    go.transform.SetParent(holder, true);
                    float s = Lerp(rng, kind.MinScale, kind.MaxScale);
                    go.transform.SetPositionAndRotation(hit.point, Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, hit.normal, .5f)) * Quaternion.Euler(0, Lerp(rng, 0, 360), 0));
                    SetWorldScale(go.transform, s);
                    placed.Add((hit.point, kind.Spacing * .5f));
                    got++; count++;
                }
            }
            return count;
        }

        private static bool IsGrass(RaycastHit hit)
        {
            var r = hit.collider.GetComponent<Renderer>();
            if (r == null) return true; // collider made only for the grass top
            var mats = r.sharedMaterials;
            if (mats.Length == 1) return IsGrassMat(mats[0]);
            if (hit.collider is MeshCollider mc && mc.sharedMesh != null && hit.triangleIndex >= 0 && mc.sharedMesh.isReadable)
            {
                var mesh = mc.sharedMesh;
                int tri = hit.triangleIndex * 3;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    int len = (int)mesh.GetIndexCount(s);
                    if (tri >= mesh.GetIndexStart(s) && tri < mesh.GetIndexStart(s) + len) return IsGrassMat(mats[s]);
                }
            }
            return mats.Any(IsGrassMat);
        }

        private static bool IsGrassMat(Material m)
        {
            if (m == null) return false;
            string n = m.name.ToLowerInvariant();
            if (GroundKeywords.Any(k => n.Contains(k))) return true;
            // Imported islands (e.g. เกาะใหญ่) use default-named materials: a clearly green base colour counts as grass.
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.black;
            return c.g > .25f && c.g > c.r * 1.25f && c.g > c.b * 1.25f;
        }

        /// <summary>True when any collider or trigger that is not ground is within radius of the spot.</summary>
        private static bool Blocked(Vector3 p, float radius, HashSet<Collider> groundSet)
        {
            var hits = Physics.OverlapSphere(p + Vector3.up * (radius + .15f), radius + .1f, ~0, QueryTriggerInteraction.Collide);
            foreach (var h in hits)
                if (!groundSet.Contains(h) && h.GetComponentInParent<DecorTag>() == null) return true;
            // A spot right under something (bridge, beam, trap arm) is also a no.
            return Physics.Raycast(p + Vector3.up * .2f, Vector3.up, 6f, ~0, QueryTriggerInteraction.Collide);
        }

        /// <summary>Ground ends within ~4 m further out from the island centre.</summary>
        private static bool NearRim(Vector3 p, Vector3 centre, HashSet<Collider> groundSet)
        {
            Vector3 dir = p - centre; dir.y = 0;
            if (dir.sqrMagnitude < .01f) return false;
            dir.Normalize();
            return !GroundAt(p + dir * 4f, p.y, groundSet);
        }

        /// <summary>Not hanging over an edge: ground continues ~0.8 m round the spot.</summary>
        private static bool InsideTop(Vector3 p, Vector3 centre, HashSet<Collider> groundSet)
        {
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * .5f;
                if (!GroundAt(p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .8f, p.y, groundSet)) return false;
            }
            return true;
        }

        private static bool GroundAt(Vector3 p, float y, HashSet<Collider> groundSet) =>
            Physics.Raycast(new Vector3(p.x, y + 2f, p.z), Vector3.down, out var h, 4f, ~0, QueryTriggerInteraction.Ignore) && groundSet.Contains(h.collider);

        // ---------- sky ----------

        private static string BuildSky(UnityEngine.SceneManagement.Scene scene, List<Island> islands, System.Random rng)
        {
            var root = new GameObject(SkyRootName);
            root.AddComponent<DecorTag>();
            SceneManagerMove(root, scene);
            var all = islands[0].Bounds;
            foreach (var i in islands) all.Encapsulate(i.Bounds);
            Vector3 c = all.center;
            float reach = Mathf.Max(all.extents.x, all.extents.z);
            float low = islands.Min(i => i.Bounds.min.y), high = all.max.y;

            // Cloud sea under everything.
            var sea = Place(scene, "Sky/Decor_CloudSea", root.transform, new Vector3(c.x, low - 25f, c.z), 0f);
            sea.transform.localScale = new Vector3(reach * 2.2f + 250f, 1f, reach * 2.2f + 250f);

            // A fog ring hugging the underside of each island.
            var fogParent = Group("FogRings", root.transform);
            foreach (var island in islands)
            {
                float top = island.Grounds.Max(g => g.bounds.max.y);
                float r = Mathf.Max(island.Bounds.extents.x, island.Bounds.extents.z) * 1.08f + 1f;
                float h = Mathf.Clamp(r * 1.4f, 10f, 30f);
                var ring = Place(scene, "Sky/Decor_FogRing", fogParent, new Vector3(island.Bounds.center.x, top - h - 1.5f, island.Bounds.center.z), Lerp(rng, 0, 360));
                ring.transform.localScale = new Vector3(r, h, r);
                ring.name = "FogRing_" + island.Root.name;
            }

            // Clouds in a wide band round the level: some below the islands, some level with them, a few above.
            var cloudParent = Group("Clouds", root.transform);
            string[] clouds = { "Sky/Decor_Cloud_A", "Sky/Decor_Cloud_B", "Sky/Decor_Cloud_C" };
            int made = 0;
            for (int attempt = 0; attempt < 400 && made < 46; attempt++)
            {
                float a = Lerp(rng, 0, Mathf.PI * 2), d = reach + Lerp(rng, 25f, 170f);
                float y = Lerp(rng, 0, 1) < .4f ? Lerp(rng, low - 22f, low - 4f) : Lerp(rng, low, high + 45f);
                var p = new Vector3(c.x + Mathf.Cos(a) * d, y, c.z + Mathf.Sin(a) * d);
                if (islands.Any(i => Expanded(i.Bounds, 22f).Contains(p))) continue;
                var cl = Place(scene, clouds[rng.Next(3)], cloudParent, p, Lerp(rng, 0, 360));
                cl.transform.localScale = Vector3.one * Lerp(rng, 1.6f, 4.2f);
                made++;
            }
            // A few small clouds between islands, below the walking height, so gaps don't look empty.
            for (int attempt = 0, extra = 0; attempt < 200 && extra < 14; attempt++)
            {
                var p = new Vector3(Lerp(rng, all.min.x, all.max.x), Lerp(rng, low - 18f, low - 6f), Lerp(rng, all.min.z, all.max.z));
                if (islands.Any(i => Expanded(i.Bounds, 8f).Contains(p))) continue;
                var cl = Place(scene, clouds[rng.Next(3)], cloudParent, p, Lerp(rng, 0, 360));
                cl.transform.localScale = Vector3.one * Lerp(rng, 1f, 2f);
                extra++; made++;
            }

            // Landmark islets far out: windmill, lighthouse, tree islets; plus balloons.
            var far = Group("FarIslets", root.transform);
            string[] landmarks = { "Landmarks/Decor_Windmill", "Landmarks/Decor_Lighthouse", null, "Landmarks/Decor_Windmill", null, "Landmarks/Decor_Lighthouse" };
            var isletMesh = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/เกาะเล็ก.fbx");
            var bigIsletMesh = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/เกาะใหญ่.fbx");
            var round = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ต้นไม้กลม.fbx");
            var pine = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ต้นสน.fbx");
            int islets = 0;
            for (int k = 0; k < landmarks.Length; k++)
            {
                float a = (k + .3f) / landmarks.Length * Mathf.PI * 2f + Lerp(rng, -.25f, .25f);
                float d = reach + Lerp(rng, 45f, 85f);
                var p = new Vector3(c.x + Mathf.Cos(a) * d, Lerp(rng, low + 2f, high - 4f), c.z + Mathf.Sin(a) * d);
                var islet = new GameObject("FarIslet_" + k).transform;
                islet.SetParent(far, false);
                islet.position = p;
                var src = (k % 2 == 0 ? bigIsletMesh : isletMesh) ?? isletMesh;
                if (src != null)
                {
                    var body = (GameObject)PrefabUtility.InstantiatePrefab(src, scene);
                    body.transform.SetParent(islet, false);
                    body.transform.localScale = Vector3.one * Lerp(rng, 1.2f, 1.6f);
                    body.transform.localEulerAngles = new Vector3(0, Lerp(rng, 0, 360), 0);
                    foreach (var col in body.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                }
                if (landmarks[k] != null) Place(scene, landmarks[k], islet, p + Vector3.up * .05f, Lerp(rng, 0, 360));
                for (int t = 0; t < 4; t++)
                {
                    var tree = t % 2 == 0 ? round : pine;
                    if (tree == null) continue;
                    float ta = Lerp(rng, 0, Mathf.PI * 2), td = Lerp(rng, 5f, 9f);
                    var tr = (GameObject)PrefabUtility.InstantiatePrefab(tree, scene);
                    tr.transform.SetParent(islet, false);
                    tr.transform.position = p + new Vector3(Mathf.Cos(ta) * td, 0, Mathf.Sin(ta) * td);
                    tr.transform.localEulerAngles = new Vector3(0, Lerp(rng, 0, 360), 0);
                    foreach (var col in tr.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                }
                var fr = Place(scene, "Sky/Decor_FogRing", islet, p + Vector3.down * 18f, 0);
                fr.transform.localScale = new Vector3(14f, 17f, 14f);
                islets++;
            }

            var balloons = Group("Balloons", root.transform);
            for (int k = 0; k < 4; k++)
            {
                float a = Lerp(rng, 0, Mathf.PI * 2), d = reach + Lerp(rng, 20f, 60f);
                var bl = Place(scene, "Landmarks/Decor_HotAirBalloon", balloons, new Vector3(c.x + Mathf.Cos(a) * d, high + Lerp(rng, 5f, 25f), c.z + Mathf.Sin(a) * d), Lerp(rng, 0, 360));
                bl.transform.localScale = Vector3.one * Lerp(rng, 1.4f, 2.2f);
            }
            return "Sky: cloud sea, " + islands.Count + " fog rings, " + made + " clouds, " + islets + " far islets, 4 balloons";
        }

        // ---------- helpers ----------

        private static List<Island> FindIslands(UnityEngine.SceneManagement.Scene scene)
        {
            var byRoot = new Dictionary<Transform, Island>();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == SkyRootName) continue;
                foreach (var col in root.GetComponentsInChildren<Collider>())
                {
                    if (col.isTrigger || !col.enabled || !col.gameObject.activeInHierarchy) continue;
                    if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
                    var r = col.GetComponent<Renderer>();
                    if (r == null || !r.sharedMaterials.Any(IsGrassMat)) continue;
                    if (col.bounds.size.x < 3f || col.bounds.size.z < 3f) continue;
                    // Colour-only matches (default-named materials) must be island-sized, so tree crowns never count as ground.
                    if (!r.sharedMaterials.Any(m => m != null && GroundKeywords.Any(k => m.name.ToLowerInvariant().Contains(k))) &&
                        (col.bounds.size.x < 10f || col.bounds.size.z < 10f)) continue;
                    var owner = IslandRoot(col.transform);
                    if (!byRoot.TryGetValue(owner, out var island)) { island = new Island { Root = owner, Bounds = col.bounds }; byRoot[owner] = island; }
                    island.Grounds.Add(col);
                    island.Bounds.Encapsulate(col.bounds);
                }
            }
            foreach (var island in byRoot.Values)
                foreach (var r in island.Root.GetComponentsInChildren<Renderer>())
                    if (r.GetComponentInParent<DecorTag>() == null && r.bounds.size.magnitude < 200f) island.Bounds.Encapsulate(r.bounds);
            return byRoot.Values.ToList();
        }

        /// <summary>Nearest ancestor with an even scale, so props parented under it keep their shape.</summary>
        private static Transform IslandRoot(Transform t)
        {
            for (var x = t; x != null; x = x.parent)
            {
                var s = x.lossyScale;
                if (Mathf.Abs(s.x - s.y) < .01f * Mathf.Abs(s.x) && Mathf.Abs(s.x - s.z) < .01f * Mathf.Abs(s.x)) return x;
            }
            return t.root;
        }

        private static Transform Holder(Transform islandRoot)
        {
            var go = new GameObject(GroundHolderName);
            go.AddComponent<DecorTag>();
            go.transform.SetParent(islandRoot, false);
            return go.transform;
        }

        private static void RemoveOld(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == SkyRootName) { Object.DestroyImmediate(root); continue; }
                foreach (var tag in root.GetComponentsInChildren<DecorTag>(true).ToArray())
                    if (tag != null && tag.gameObject.name == GroundHolderName) Object.DestroyImmediate(tag.gameObject);
            }
        }

        private static GameObject Place(UnityEngine.SceneManagement.Scene scene, string prefab, Transform parent, Vector3 pos, float yaw)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(P + prefab + ".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, scene);
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            return go;
        }

        private static Transform Group(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }

        private static void SetWorldScale(Transform t, float s)
        {
            var parent = t.parent != null ? t.parent.lossyScale.x : 1f;
            t.localScale = Vector3.one * (s / Mathf.Max(.0001f, parent));
        }

        private static Bounds Expanded(Bounds b, float m) { b.Expand(m * 2f); return b; }
        private static float Lerp(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    }
}
#endif
