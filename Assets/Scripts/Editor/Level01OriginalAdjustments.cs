#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CoopGame.CarrySystem;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Three requested changes to the ORIGINAL Level01 (9 island route), applied idempotently:
///  1. Checkpoint pads (CheckpointPad + one CheckpointManager) on islands 1, 3, 5 and 7.
///  2. Bridges 4-5, 6-7 and 8-9 are replaced by chains of small floating stones (team jump hops).
///  3. The 9 islands (with everything standing on them) are moved to different heights; kept ramp bridges are re-fitted.
/// New objects live under the root "Level01_Additions". Moved objects keep their names. What was changed is written to
/// Level01OriginalAdjustments.record.json (applied height shift per moved object) and to the Verification log.
/// Menu: Tools/Expedition/Adjust Original Level01. Restore: copy the backup scene over Assets/Scenes/Level01.unity
/// (new bridge meshes are written to NEW assets, so the old bridge meshes stay untouched).
/// </summary>
public static class Level01OriginalAdjustments
{
    private const string ScenePath = "Assets/Scenes/Level01.unity";
    private const string RootName = "Level01_Additions";
    private const string RecordPath = "Assets/Scripts/Editor/Level01OriginalAdjustments.record.json";
    private const string MeshFolder = "Assets/Environment/Level01/Adjusted";
    private const string LogPath = "E:/Unity/Verification/level01-original-changes/adjust-log.txt";

    // y of each island (1..9) in the original map and the requested new heights (alternating up/down, spread 12.5 m).
    private static readonly float[] OriginalY = { 0f, 3f, 6f, 4f, 8f, 11f, 7f, 12f, 14f };
    private static readonly float[] TargetY = { 0f, 4f, 8f, 12f, 6f, 10f, 4f, 8f, 2f };
    // Bridge k joins island k and k+1. These three become stepping-stone hops (all descents).
    private static readonly int[] HopBridges = { 4, 6, 8 };
    // Islands that get a checkpoint pad (1-based) and the existing yellow bay each pad sits next to.
    private static readonly int[] PadIslands = { 1, 3, 5, 7 };

    private static readonly string[] IslandNames =
    {
        "01_StartVillage", "02_RelayIsland", "03_ForestSlalom", "04_TeamGate", "05_RelayRest",
        "06_WallRelay", "07_TrapCrossing", "08_HighSwitch", "09_FinalDelivery"
    };
    private static readonly string[] BridgeNames =
    {
        "01_WideTrainingBridge", "02_RelayBridgeDeck", "03_ForestBridge", "04_FinalBridge",
        "05_CargoBridge", "06_CargoBridge", "07_CargoBridge", "08_CargoBridge"
    };
    private static readonly string[] Containers =
    {
        "ExtendedCoopRoute", "ExpandedLayout_v1", "ChallengeUpgrade_v2", "HardcoreUpgrade_v3",
        "IslandCoopGauntlet_v5", "ExpandedIslandComposition_v6", "IslandRouteComposition_v7", "IslandRoutePolish_v8"
    };
    // Mechanisms that stand ON a kept ramp bridge: they follow the deck surface instead of an island.
    private static readonly (string hazard, int bridge)[] BridgeHazards =
    {
        ("ForestHammer_2", 3), ("ForestHammer_3", 3), ("RelaySweep", 3), ("RotatingLog_Bridge", 5), ("FlameTrap_A", 7)
    };

    [Serializable]
    private sealed class Record
    {
        public List<string> ids = new List<string>();
        public List<float> dys = new List<float>();
        public List<string> names = new List<string>();
        public float[] islandY;
        public string note;
    }

    private static Transform _route;
    private static Transform[] _islands;
    private static Vector3[] _after;
    private static Material _wood, _stone, _grass, _cliff, _gold, _gray, _orange, _backing;
    private static StringBuilder _log;

    [MenuItem("Tools/Expedition/Adjust Original Level01")]
    public static void Menu() => Debug.Log("[Level01OriginalAdjustments] " + Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit Mode.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        _log = new StringBuilder();
        _route = scene.GetRootGameObjects().Single(g => g.name == "FloatingCoopRoute").transform;
        _islands = IslandNames.Select(n => _route.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == n && t.GetComponent<MeshFilter>() != null)).ToArray();
        LoadMaterials();

        // --- idempotency record
        Record record = File.Exists(RecordPath) ? JsonUtility.FromJson<Record>(File.ReadAllText(RecordPath)) : null;
        if (record != null && record.islandY != null && record.islandY.Length == 9)
        {
            bool matches = true;
            for (int i = 0; i < 9; i++) matches &= Mathf.Abs(_islands[i].position.y - record.islandY[i]) < .02f;
            if (!matches) { Log("Record does not match the scene; treating the scene as unadjusted."); record = null; }
        }
        else record = null;
        if (record == null)
            for (int i = 0; i < 9; i++)
                if (Mathf.Abs(_islands[i].position.y - OriginalY[i]) > .02f)
                    throw new InvalidOperationException("Island " + (i + 1) + " is at y=" + _islands[i].position.y + ", neither the original nor the recorded adjusted height.");
        var applied = new Dictionary<string, float>();
        if (record != null) for (int i = 0; i < record.ids.Count; i++) applied[record.ids[i]] = record.dys[i];

        // --- remove the previous additions (rebuilt below)
        foreach (var go in scene.GetRootGameObjects().Where(g => g.name == RootName).ToArray()) UnityEngine.Object.DestroyImmediate(go);
        Physics.SyncTransforms();

        // --- 1. sample the old deck surface under mechanisms that stand on kept bridges
        var oldDeckY = new Dictionary<string, float>();
        foreach (var (hazard, bridge) in BridgeHazards)
        {
            var h = FindByName(hazard);
            oldDeckY[hazard] = SampleDeck(BridgeNames[bridge - 1], h.position);
        }

        // --- 2. move islands and everything standing on them
        var newRecord = new Record { islandY = (float[])TargetY.Clone(), note = "Applied height shift per moved object (GlobalObjectId). Written by Level01OriginalAdjustments." };
        var units = CollectUnits();
        _log.AppendLine("== moved units (object -> island, shift) ==");
        int moved = 0;
        foreach (var unit in units)
        {
            Vector3 c = Centroid(unit);
            int island = NearestIsland(c, out float dist, out float second);
            float want = TargetY[island] - OriginalY[island];
            string id = GlobalObjectId.GetGlobalObjectIdSlow(unit.gameObject).ToString();
            applied.TryGetValue(id, out float have);
            float delta = want - have;
            if (Mathf.Abs(delta) > 1e-4f) { unit.position += Vector3.up * delta; moved++; }
            newRecord.ids.Add(id); newRecord.dys.Add(want); newRecord.names.Add(PathOf(unit));
            _log.AppendLine(PathOf(unit) + " -> island " + (island + 1) + " (xz " + dist.ToString("F1") + " m, next " + second.ToString("F1") + " m) dy=" + want.ToString("F2"));
            if (dist > 45f || second - dist < 4f) _log.AppendLine("   WARNING: ambiguous island assignment");
        }
        Physics.SyncTransforms();
        _after = _islands.Select(t => t.position).ToArray();

        // --- 3. kept ramp bridges: rebuild deck, rails and posts to the new endpoints
        Directory.CreateDirectory(MeshFolder);
        if (!AssetDatabase.IsValidFolder("Assets/Environment/Level01/Adjusted")) AssetDatabase.CreateFolder("Assets/Environment/Level01", "Adjusted");
        for (int k = 1; k <= 8; k++)
        {
            var bridge = FindByName(BridgeNames[k - 1]);
            if (HopBridges.Contains(k)) { if (bridge.gameObject.activeSelf) bridge.gameObject.SetActive(false); continue; }
            RebuildBridge(bridge, k - 1);
        }
        Physics.SyncTransforms();
        _log.AppendLine("== mechanisms on kept bridges ==");
        foreach (var (hazard, bridge) in BridgeHazards)
        {
            var h = FindByName(hazard);
            float now = SampleDeck(BridgeNames[bridge - 1], h.position);
            float delta = now - oldDeckY[hazard];
            if (Mathf.Abs(delta) > .005f) h.position += Vector3.up * delta;
            _log.AppendLine(hazard + " on bridge " + bridge + ": deck " + oldDeckY[hazard].ToString("F2") + " -> " + now.ToString("F2") + " (shift " + delta.ToString("F2") + ")");
        }
        Physics.SyncTransforms();

        // --- 4. additions: hop chains, pads, manager
        var root = new GameObject(RootName).transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        foreach (int k in HopBridges) BuildHopChain(root, k);
        BuildCheckpoints(root, scene);

        File.WriteAllText(RecordPath, JsonUtility.ToJson(newRecord, true));
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
        File.WriteAllText(LogPath, _log.ToString());
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "moved " + moved + " of " + units.Count + " units; hop bridges " + string.Join(",", HopBridges) + "; log " + LogPath;
    }

    // ------------------------------------------------------------------ helpers
    private static void Log(string s) { Debug.Log("[Level01OriginalAdjustments] " + s); _log?.AppendLine(s); }

    private static void LoadMaterials()
    {
        const string shared = "Assets/Environment/FloatingCoop/";
        _wood = Load(shared + "BridgeWood.mat"); _stone = Load(shared + "Stone.mat");
        _grass = Load(shared + "Grass.mat"); _cliff = Load(shared + "Cliff.mat");
        _gold = Load(shared + "GoalGold.mat"); _gray = Load(shared + "InactivePressurePad.mat");
        _orange = Load("Assets/Environment/Level01/RouteOrange.mat");
        _backing = null;
        foreach (string guid in AssetDatabase.FindAssets("ThaiSignBacking t:Material"))
        { _backing = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)); if (_backing != null) break; }
        if (_backing == null) throw new InvalidOperationException("Missing ThaiSignBacking material");
    }

    private static Material Load(string path) => AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new InvalidOperationException("Missing material " + path);

    private static Transform FindByName(string n) => _route.GetComponentsInChildren<Transform>(true).Single(t => t.name == n);

    private static string PathOf(Transform t)
    {
        string p = t.name;
        for (var q = t.parent; q != null && q != _route; q = q.parent) p = q.name + "/" + p;
        return p;
    }

    private static float SampleDeck(string bridgeName, Vector3 at)
    {
        var deck = FindByName(bridgeName).Find("ContinuousCargoDeck_7m");
        var col = deck != null ? deck.GetComponent<MeshCollider>() : null;
        if (col == null) throw new InvalidOperationException("No deck collider on " + bridgeName);
        if (col.Raycast(new Ray(new Vector3(at.x, 200f, at.z), Vector3.down), out var hit, 400f)) return hit.point.y;
        throw new InvalidOperationException("Mechanism at " + at + " is not over the deck of " + bridgeName);
    }

    private static Vector3 Centroid(Transform t)
    {
        var all = t.GetComponentsInChildren<Transform>(true);
        Vector3 sum = Vector3.zero;
        foreach (var x in all) sum += x.position;
        return sum / all.Length;
    }

    private static float Spread(Transform t)
    {
        Vector3 c = Centroid(t);
        float max = 0f;
        foreach (var x in t.GetComponentsInChildren<Transform>(true))
            max = Mathf.Max(max, new Vector2(x.position.x - c.x, x.position.z - c.z).magnitude);
        return max;
    }

    private static int NearestIsland(Vector3 p, out float best, out float second)
    {
        best = float.MaxValue; second = float.MaxValue; int index = 0;
        for (int i = 0; i < 9; i++)
        {
            float d = new Vector2(p.x - _islands[i].position.x, p.z - _islands[i].position.z).magnitude;
            if (d < best) { second = best; best = d; index = i; }
            else if (d < second) second = d;
        }
        return index;
    }

    private static bool IsExcluded(Transform t) =>
        BridgeNames.Contains(t.name) || BridgeHazards.Any(h => h.hazard == t.name) || t.name == RootName;

    private static void Collect(Transform t, List<Transform> units)
    {
        if (IsExcluded(t)) return;
        if (t.childCount > 0 && Spread(t) > 40f)
        {
            foreach (Transform child in t) Collect(child, units);
            return;
        }
        units.Add(t);
    }

    private static List<Transform> CollectUnits()
    {
        var units = new List<Transform>();
        foreach (Transform child in _route)
        {
            if (IsExcluded(child)) continue;
            if (Containers.Contains(child.name)) { foreach (Transform c in child) Collect(c, units); }
            else Collect(child, units);
        }
        var scene = _route.gameObject.scene;
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "DeliveryZone" || root.name == "DeliveryExitPortal_Level02" || root.name == "PlayerStart" || root.name == "WoodenCrateItem")
                units.Add(root.transform);
        // A unit must never be a child of another unit (it would move twice).
        return units.Where(u => !units.Any(o => o != u && u.IsChildOf(o))).ToList();
    }

    // ------------------------------------------------------------------ ramp bridges (same shape as ExpandedIslandComposition.RebuildBridge)
    private static void RebuildBridge(Transform bridge, int i)
    {
        foreach (Transform child in bridge.Cast<Transform>().ToArray())
        {
            if (new[] { "ContinuousCargoDeck_7m", "CargoGuardRail", "GuardPost" }.Contains(child.name)) UnityEngine.Object.DestroyImmediate(child.gameObject);
            else if (child.gameObject.activeSelf) child.gameObject.SetActive(false);
        }
        Vector3 direction = _after[i + 1] - _after[i]; direction.y = 0; direction.Normalize();
        float Radius(int j) { var b = _islands[j].GetComponent<Renderer>().bounds; return 1f / Mathf.Sqrt(direction.x * direction.x / (b.extents.x * b.extents.x) + direction.z * direction.z / (b.extents.z * b.extents.z)); }
        Vector3 a = _after[i] + direction * Radius(i) * .72f + Vector3.up * .025f;
        Vector3 b2 = _after[i + 1] - direction * Radius(i + 1) * .72f + Vector3.up * .025f;
        bridge.position = a; bridge.rotation = Quaternion.LookRotation(direction); SetWorldScale(bridge, Vector3.one);
        float length = Vector3.Dot(b2 - a, direction); float rise = b2.y - a.y;
        if (length < 10) throw new InvalidOperationException("Insufficient bridge spacing: " + bridge.name);
        float EdgeProjection(int j, Vector3 toward) => _islands[j].GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => Vector3.Dot(_islands[j].TransformPoint(v) - _after[j], toward));
        float landingA = EdgeProjection(i, direction) - Radius(i) * .72f + 2f;
        float landingB = EdgeProjection(i + 1, -direction) - Radius(i + 1) * .72f + 2f;
        if (landingA + landingB >= length - 2) throw new InvalidOperationException("Bridge ramp needs more space.");
        const float lift = .175f;
        // The ramp rises over the HIGHER island's ground (the deck stays above it), so it starts right after the 1.3 m bevel there;
        // at the LOWER island the deck stays flat until the island edge. Both ends are eased so the crate never meets a sharp kink.
        float ramp0 = rise >= 0f ? 1.3f : landingA;
        float ramp1 = rise >= 0f ? length - landingB : length - 1.3f;
        var pts = new List<Vector3> { Vector3.zero, new Vector3(0, lift, 1.3f) };
        if (ramp0 > 1.35f) pts.Add(new Vector3(0, lift, ramp0));
        int steps = Mathf.Max(4, Mathf.CeilToInt((ramp1 - ramp0) / 1.5f));
        for (int s = 1; s <= steps; s++)
        {
            float u = (float)s / steps; float e = .5f * u + .5f * u * u * (3f - 2f * u);
            pts.Add(new Vector3(0, lift + rise * e, ramp0 + (ramp1 - ramp0) * u));
        }
        if (ramp1 < length - 1.35f) pts.Add(new Vector3(0, rise + lift, length - 1.3f));
        pts.Add(new Vector3(0, rise, length));
        Vector3[] points = pts.ToArray();
        var vertices = new List<Vector3>(); var triangles = new List<int>(); var uv = new List<Vector2>();
        void Quad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            int n = vertices.Count; vertices.AddRange(new[] { v0, v1, v2, v3 });
            uv.AddRange(new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up });
            triangles.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }
        Vector3 x = Vector3.right * 3.5f, down = Vector3.down * .35f;
        for (int j = 0; j < points.Length - 1; j++)
        {
            var p = points[j]; var q = points[j + 1];
            Quad(p - x, q - x, q + x, p + x); Quad(p + x + down, q + x + down, q - x + down, p - x + down);
            Quad(p - x + down, q - x + down, q - x, p - x); Quad(p + x, q + x, q + x + down, p + x + down);
            for (int side = -1; side <= 1; side += 2)
            {
                var offset = Vector3.right * (side * 3.68f) + Vector3.up * .7f;
                Beam("CargoGuardRail", bridge, bridge.TransformPoint(p + offset), bridge.TransformPoint(q + offset), .22f, 1.4f, _wood);
            }
        }
        Quad(points[0] + x, points[0] + x + down, points[0] - x + down, points[0] - x);
        var end = points[points.Length - 1]; Quad(end - x, end - x + down, end + x + down, end + x);
        string path = MeshFolder + "/L01c_" + bridge.name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool created = mesh == null;
        if (created) mesh = new Mesh();
        else mesh.Clear();
        mesh.name = "L01c_" + bridge.name;
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.SetUVs(0, uv); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
        var deckGo = new GameObject("ContinuousCargoDeck_7m"); deckGo.transform.SetParent(bridge, false); deckGo.transform.position = a; SetWorldScale(deckGo.transform, Vector3.one);
        deckGo.AddComponent<MeshFilter>().sharedMesh = mesh; deckGo.AddComponent<MeshRenderer>().sharedMaterial = _wood; deckGo.AddComponent<MeshCollider>().sharedMesh = mesh;
        for (int j = 0; j < points.Length; j++)
        {
            if (j % 2 != 0 && j != points.Length - 1) continue;
            for (int side = -1; side <= 1; side += 2) Box("GuardPost", bridge, points[j] + new Vector3(side * 3.68f, .8f, 0), new Vector3(.4f, 1.6f, .4f), _stone, true);
        }
        _log.AppendLine("bridge " + bridge.name + ": length " + length.ToString("F1") + " rise " + rise.ToString("F2") + " ramp " + (length - landingA - landingB).ToString("F1") + " m");
    }

    private static void Beam(string name, Transform parent, Vector3 a, Vector3 b, float width, float thickness, Material mat)
    {
        var go = Box(name, parent, Vector3.zero, Vector3.one, mat, true);
        go.transform.position = (a + b) * .5f; go.transform.rotation = Quaternion.LookRotation(b - a);
        SetWorldScale(go.transform, new Vector3(width, thickness, Vector3.Distance(a, b)));
    }

    private static GameObject Box(string name, Transform parent, Vector3 local, Vector3 size, Material mat, bool solid)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = local; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static void SetWorldScale(Transform t, Vector3 size)
    {
        var p = t.parent == null ? Vector3.one : t.parent.lossyScale;
        t.localScale = new Vector3(size.x / p.x, size.y / p.y, size.z / p.z);
    }

    private static Transform Group(string name, Transform parent, Vector3 world, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(world, rotation);
        return go.transform;
    }

    // ------------------------------------------------------------------ hop chains
    private struct Layout { public int hops; public float rest, gap, size; }

    private static float IslandEdge(int island, Vector3 from, Vector3 dir, float length, bool fromStart)
    {
        var col = _islands[island].GetComponent<Collider>();
        float y = _islands[island].position.y;
        // Islands can have a trench at the centre, so scan from the middle of the gap toward the island: the first flat hit
        // is the edge facing the gap.
        bool Flat(float s) => col.Raycast(new Ray(from + dir * s + Vector3.up * 80f, Vector3.down), out var h, 200f) && h.point.y >= y - .12f;
        if (fromStart) { for (float s = length * .5f; s >= 0f; s -= .1f) if (Flat(s)) return s; }
        else { for (float s = length * .5f; s <= length; s += .1f) if (Flat(s)) return s; }
        throw new InvalidOperationException("Island edge not found for island " + (island + 1));
    }

    private static void BuildHopChain(Transform root, int bridge)
    {
        int a = bridge - 1, b = bridge;
        Vector3 A = _after[a], B = _after[b];
        Vector3 dir = B - A; dir.y = 0; float L = dir.magnitude; dir /= L;
        Vector3 right = new Vector3(dir.z, 0, -dir.x);
        float edgeA = IslandEdge(a, A, dir, L, true);
        float edgeB = IslandEdge(b, A, dir, L, false);
        float span = edgeB - edgeA;
        Layout best = default; float bestScore = float.MaxValue;
        for (int n = 2; n <= 6; n++)
            foreach (float rest in new[] { 4.2f, 4.6f, 5f, 5.4f, 5.8f })
                for (float g = 1.7f; g <= 2.25f + 1e-3f; g += .05f)
                {
                    float d = (span - 2 * rest - (n + 3) * g) / n;
                    if (d < 2.8f || d > 3.8f) continue;
                    float score = Mathf.Abs(g - 1.95f) * 4f + Mathf.Abs(d - 3.2f) + n * .05f;
                    if (score < bestScore) { bestScore = score; best = new Layout { hops = n, rest = rest, gap = g, size = d }; }
                }
        if (bestScore == float.MaxValue) throw new InvalidOperationException("No hop layout fits span " + span + " on bridge " + bridge);

        var chain = Group("HopChain_" + bridge.ToString("00") + "_island" + a.ToString("0") + "to" + b.ToString("0"), root, A, Quaternion.identity);
        chain.name = "HopChain_Bridge" + bridge.ToString("00") + "_Island" + (a + 1) + "to" + (b + 1);
        var sizes = new List<float> { best.rest };
        for (int i = 0; i < best.hops; i++) sizes.Add(best.size);
        sizes.Add(best.rest);
        float cursor = edgeA;
        var report = new StringBuilder();
        report.Append("hop bridge " + bridge + " (island " + (a + 1) + " -> " + (b + 1) + "): edge-to-edge " + span.ToString("F1") + " m, " + sizes.Count + " stones, gap " + best.gap.ToString("F2") + " m, hop stone " + best.size.ToString("F2") + " m, rest stone " + best.rest.ToString("F1") + " m; stone tops:");
        for (int i = 0; i < sizes.Count; i++)
        {
            cursor += best.gap;
            float center = cursor + sizes[i] * .5f;
            cursor += sizes[i];
            float y = Mathf.Lerp(A.y, B.y, (center - edgeA) / span);
            Vector3 pos = new Vector3(A.x + dir.x * center, y, A.z + dir.z * center);
            bool rest = i == 0 || i == sizes.Count - 1;
            string name = rest ? (i == 0 ? "HopRest_Start" : "HopRest_End") : "HopStone_" + i;
            var stone = MakeStone(chain, name, pos, Quaternion.LookRotation(dir), sizes[i], bridge * 10 + i, rest);
            report.Append(" " + y.ToString("F2"));
            // overlap warning: anything else standing where the stone is
            foreach (var c in Physics.OverlapSphere(pos + Vector3.up * 1.2f, sizes[i] * .6f))
                if (!c.transform.IsChildOf(chain) && c.transform != _islands[a] && c.transform != _islands[b])
                    _log.AppendLine("   WARNING: " + name + " overlaps " + PathOf(c.transform));
        }
        report.Append(" ; descent per jump ~" + (Mathf.Abs(B.y - A.y) / (sizes.Count + 1)).ToString("F2") + " m");
        _log.AppendLine(report.ToString());

        // Short Thai sign on the starting island, facing a player walking toward the hop chain.
        Vector3 signPos = A + dir * (edgeA - 4.2f) + right * 3.4f; signPos.y = A.y;
        MakeSign(chain, "HopSign", signPos, "โดดพร้อมกัน!\nข้ามหินทีละก้อน", dir);
    }

    private static GameObject MakeStone(Transform parent, string name, Vector3 pos, Quaternion rot, float size, int seed, bool rest)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false); go.transform.SetPositionAndRotation(pos, rot);
        var mesh = StoneMesh(name + "_" + seed, size * .5f, seed);
        var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { _grass, _cliff };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        if (rest)
        {
            var pad = Box("RestMark", go.transform, new Vector3(0, .02f, 0), new Vector3(size * .62f, .04f, size * .62f), _gold, false);
        }
        else
        {
            var tile = Box("RouteMark", go.transform, new Vector3(0, .03f, -.15f), new Vector3(.55f, .04f, 1.2f), _orange, false);
            // chevron pointing along the route
            for (int s = -1; s <= 1; s += 2)
            {
                var wing = Box("RouteMarkTip", go.transform, new Vector3(s * .3f, .03f, .5f), new Vector3(.2f, .04f, .85f), _orange, false);
                wing.transform.localRotation = Quaternion.Euler(0, s * 132f, 0);
            }
        }
        return go;
    }

    // Low-poly floating rock: flat octagonal top (submesh 0, grass), tapered cliff underside (submesh 1). Flat faces point along the route.
    private static Mesh StoneMesh(string name, float apothem, int seed)
    {
        string path = MeshFolder + "/L01c_" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool created = mesh == null;
        if (created) mesh = new Mesh(); else mesh.Clear();
        mesh.name = "L01c_" + name;
        var rng = new System.Random(seed * 7919 + 13);
        const int N = 8;
        float vr = apothem / Mathf.Cos(Mathf.PI / N);
        Vector3[] ring0 = new Vector3[N], ring1 = new Vector3[N], ring2 = new Vector3[N];
        for (int i = 0; i < N; i++)
        {
            float ang = (i + .5f) * Mathf.PI * 2f / N;
            float jr = 1f + ((float)rng.NextDouble() - .5f) * .06f;
            var dirv = new Vector3(Mathf.Sin(ang), 0, Mathf.Cos(ang));
            ring0[i] = dirv * vr * jr;
            ring1[i] = dirv * vr * jr * .96f + Vector3.down * .45f;
            float j2 = .45f + ((float)rng.NextDouble() - .5f) * .12f;
            ring2[i] = dirv * vr * j2 + Vector3.down * (2.0f + ((float)rng.NextDouble() - .5f) * .3f);
        }
        Vector3 tip = Vector3.down * (2.7f + (float)rng.NextDouble() * .3f);
        var verts = new List<Vector3>(); var uvs = new List<Vector2>();
        var top = new List<int>(); var side = new List<int>();
        void Tri(List<int> list, Vector3 p, Vector3 q, Vector3 r, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(q - p, r - p), outward) < 0f) { var t = q; q = r; r = t; }
            int n = verts.Count; verts.Add(p); verts.Add(q); verts.Add(r);
            uvs.Add(new Vector2(p.x, p.z)); uvs.Add(new Vector2(q.x, q.z)); uvs.Add(new Vector2(r.x, r.z));
            list.Add(n); list.Add(n + 1); list.Add(n + 2);
        }
        for (int i = 0; i < N; i++)
        {
            int j = (i + 1) % N;
            Tri(top, Vector3.zero, ring0[i], ring0[j], Vector3.up);
            Vector3 mid = (ring0[i] + ring0[j]) * .5f;
            Vector3 o = new Vector3(mid.x, 0, mid.z);
            Tri(side, ring0[i], ring0[j], ring1[j], o); Tri(side, ring0[i], ring1[j], ring1[i], o);
            Tri(side, ring1[i], ring1[j], ring2[j], o); Tri(side, ring1[i], ring2[j], ring2[i], o);
            Tri(side, ring2[i], ring2[j], tip, Vector3.down);
        }
        mesh.SetVertices(verts); mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 2; mesh.SetTriangles(top, 0); mesh.SetTriangles(side, 1);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        if (created) AssetDatabase.CreateAsset(mesh, path); else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ------------------------------------------------------------------ signs
    private static int VisibleChars(string line)
    {
        int n = 0;
        foreach (char c in line)
            if (!(c == '\u0E31' || (c >= '\u0E34' && c <= '\u0E3A') || (c >= '\u0E47' && c <= '\u0E4E'))) n++;
        return n;
    }

    private static TextMesh MakeLabel(Transform parent, string name, Vector3 world, string text, Vector3 reader, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false); go.transform.position = world;
        var label = go.AddComponent<TextMesh>();
        label.text = text; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = Color.white;
        label.fontSize = 112; label.characterSize = size * 56f / 112f;
        go.transform.rotation = Quaternion.LookRotation(reader.sqrMagnitude > .001f ? reader : Vector3.forward);
        string[] lines = text.Split('\n');
        int longest = 0; foreach (var l in lines) longest = Mathf.Max(longest, VisibleChars(l));
        float scale = size / .075f;
        var plate = Box(name + "_Plate", go.transform, new Vector3(0, 0, .06f), new Vector3((longest * .34f + .9f) * scale, lines.Length * .82f * scale + .45f, .08f), _backing, false);
        plate.transform.localRotation = Quaternion.identity;
        return label;
    }

    private static void MakeSign(Transform parent, string name, Vector3 ground, string text, Vector3 reader, float size = .05f, float height = 2.3f)
    {
        var label = MakeLabel(parent, name, new Vector3(ground.x, ground.y + height, ground.z), text, reader, size);
        int lines = text.Split('\n').Length;
        float plateHeight = lines * .82f * size / .075f + .45f;
        float postLength = height - plateHeight * .5f;
        var post = Box("Post", label.transform, new Vector3(0, -(height + plateHeight * .5f) * .5f, .14f), new Vector3(.16f, postLength, .16f), _wood, false);
        post.transform.localRotation = Quaternion.identity;
    }

    // ------------------------------------------------------------------ checkpoints
    private static void BuildCheckpoints(Transform root, Scene scene)
    {
        var cargoRoot = scene.GetRootGameObjects().Single(g => g.name == "WoodenCrateItem");
        var cargo = cargoRoot.GetComponent<FragileCargo>();
        var systems = new GameObject("Systems").transform; systems.SetParent(root, false);
        var pads = new List<CheckpointPad>();
        _log.AppendLine("== checkpoint pads ==");
        for (int n = 0; n < PadIslands.Length; n++)
        {
            int island = PadIslands[n] - 1;
            var bay = FindByName("BridgeWaitingBay_" + PadIslands[n].ToString("00") + "_Start");
            Vector3 dir = _after[island + 1] - _after[island]; dir.y = 0; dir.Normalize();
            Vector3 right = new Vector3(dir.z, 0, -dir.x);
            Vector3 site = FindPadSite(island, bay.position, dir, right, root);
            var pad = MakePad(root, n + 1, site, dir, cargo);
            pads.Add(pad);
            _log.AppendLine("pad " + (n + 1) + " on island " + PadIslands[n] + " (" + IslandNames[island] + "), next to " + bay.name + " " + bay.position.ToString("F1") + " -> pad centre " + site.ToString("F1"));
        }
        var managerGo = new GameObject("CheckpointManager"); managerGo.transform.SetParent(systems, false);
        var manager = managerGo.AddComponent<CheckpointManager>();
        var so = new SerializedObject(manager);
        var p = so.FindProperty("_pads"); p.arraySize = pads.Count;
        for (int i = 0; i < pads.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = pads[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Vector3 FindPadSite(int island, Vector3 bay, Vector3 dir, Vector3 right, Transform ignore)
    {
        var col = _islands[island].GetComponent<Collider>();
        float y = _islands[island].position.y;
        (float along, float lateral)[] candidates = { (-3.5f, 5f), (-3.5f, -5f), (-6f, 5f), (-6f, -5f), (-3.5f, 8f), (-3.5f, -8f), (-8f, 7f), (-8f, -7f) };
        foreach (var (along, lateral) in candidates)
        {
            Vector3 c = bay + dir * along + right * lateral; c.y = y;
            bool ground = true;
            for (int sx = -1; sx <= 1; sx++)
                for (int sz = -1; sz <= 1; sz++)
                {
                    Vector3 q = c + dir * (sz * 2.6f) + right * (sx * 2.6f);
                    ground &= col.Raycast(new Ray(q + Vector3.up * 40f, Vector3.down), out var h, 100f) && Mathf.Abs(h.point.y - y) < .05f;
                    Vector3 back = q - dir * 3.4f;
                    if (sx == 0 && sz == 0) ground &= col.Raycast(new Ray(back + Vector3.up * 40f, Vector3.down), out var h2, 100f) && Mathf.Abs(h2.point.y - y) < .05f;
                }
            if (!ground) continue;
            bool blocked = false;
            foreach (var hit in Physics.OverlapBox(c + Vector3.up * 1.6f, new Vector3(3.1f, 1.5f, 3.1f), Quaternion.LookRotation(dir)))
                if (hit != col && !hit.isTrigger && !hit.transform.IsChildOf(ignore)) { blocked = true; _log.AppendLine("   pad candidate " + c.ToString("F1") + " blocked by " + PathOf(hit.transform)); break; }
            if (!blocked) return c;
        }
        throw new InvalidOperationException("No free flat ground for a checkpoint pad on island " + (island + 1));
    }

    private static CheckpointPad MakePad(Transform parent, int number, Vector3 ground, Vector3 route, FragileCargo cargo)
    {
        var root = Group("Checkpoint_" + number, parent, ground, Quaternion.LookRotation(route));
        var pad = root.gameObject.AddComponent<CheckpointPad>();
        var slab = Box("PadSlab", root, new Vector3(0, .04f, 0), new Vector3(4.6f, .08f, 4.6f), _gray, false);
        var lamps = new List<UnityEngine.Object> { slab.GetComponent<Renderer>() };
        for (int cx = -1; cx <= 1; cx += 2)
            for (int cz = -1; cz <= 1; cz += 2)
            {
                var lamp = Box("PadLamp", root, new Vector3(cx * 2.05f, .35f, cz * 2.05f), new Vector3(.3f, .7f, .3f), _gray, false);
                lamps.Add(lamp.GetComponent<Renderer>());
            }
        Box("FlagPole", root, new Vector3(-2.3f, 1.5f, 2.3f), new Vector3(.1f, 3f, .1f), _wood, false);
        var cloth = Box("FlagCloth", root, new Vector3(-1.75f, .5f, 2.3f), new Vector3(1.1f, .7f, .05f), _gray, false);
        lamps.Add(cloth.GetComponent<Renderer>());
        var cargoPoint = Group("CargoPoint", root, ground + Vector3.up * .9f, root.rotation);
        var playerPoint = Group("PlayerPoint", root, ground + root.rotation * new Vector3(0, .15f, -3.4f), root.rotation);
        var label = MakeLabel(root, "PadLabel", ground + Vector3.up * 2.7f, "เช็คพอยต์", route, .036f);
        label.transform.rotation = Quaternion.LookRotation(route);
        var so = new SerializedObject(pad);
        so.FindProperty("_cargo").objectReferenceValue = cargo;
        so.FindProperty("_number").intValue = number;
        so.FindProperty("_cargoPoint").objectReferenceValue = cargoPoint;
        so.FindProperty("_playerPoint").objectReferenceValue = playerPoint;
        var lp = so.FindProperty("_lamps"); lp.arraySize = lamps.Count;
        for (int i = 0; i < lamps.Count; i++) lp.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
        so.FindProperty("_inactiveMaterial").objectReferenceValue = _gray;
        so.FindProperty("_activeMaterial").objectReferenceValue = _gold;
        so.FindProperty("_flag").objectReferenceValue = cloth.transform;
        so.FindProperty("_flagDownY").floatValue = .5f; so.FindProperty("_flagUpY").floatValue = 2.5f;
        so.FindProperty("_label").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();
        return pad;
    }
}
#endif
