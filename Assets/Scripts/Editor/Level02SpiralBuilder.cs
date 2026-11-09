#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using CoopGame.CarrySystem;
using CoopGame.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Level02 middle section (2026-11-07): from George's island 29 to island (4). Fork island with a cargo-chosen branch
/// (easy main route or a harder route to a free repair shop; the unchosen branch sinks away), then a climb-switch lift
/// up to island (4). The far section (island 4 .. castle) is moved down once to meet the lift. Re-running rebuilds the
/// section; the far section is only moved while it is still high.
/// </summary>
public static class Level02SpiralBuilder
{
    private const string ScenePath = "Assets/Scenes/Level02.unity";
    private const string RootName = "L2_Mid_Spiral";
    private const string FloatMat = "Assets/Environment/FloatingCoop/";
    private const string L2Mat = "Assets/Environment/Level02/";

    // Island tops (pivot = walkable top). Scale 1 islet = 16.86 m across.
    private static readonly Vector3 Fork = new(-42.27f, 30.4f, 108f), M1 = new(-53.62f, 31f, 111.92f), M2 = new(-58.33f, 31.6f, 122.63f),
        M3 = new(-53.45f, 32.2f, 133.27f), M4 = new(-42.35f, 32.8f, 140.53f), B2 = new(-40.21f, 30.6f, 124.47f), Shop = new(-30.73f, 31.2f, 130.79f);
    private const float LiftRise = 7f;

    private static Material _wood, _gold, _gray, _cloth, _roof, _plaster, _green;

    // Menu removed 2026-11-07: George rebuilt this section by hand; use Level02ForkWiring instead.
    public static void Menu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        _wood = AssetDatabase.LoadAssetAtPath<Material>(FloatMat + "BridgeWood.mat");
        _gold = AssetDatabase.LoadAssetAtPath<Material>(FloatMat + "GoalGold.mat");
        _gray = AssetDatabase.LoadAssetAtPath<Material>(FloatMat + "InactivePressurePad.mat");
        _green = AssetDatabase.LoadAssetAtPath<Material>(FloatMat + "Grass.mat");
        _cloth = AssetDatabase.LoadAssetAtPath<Material>(L2Mat + "StallCloth.mat");
        _roof = AssetDatabase.LoadAssetAtPath<Material>(L2Mat + "Roof.mat");
        _plaster = AssetDatabase.LoadAssetAtPath<Material>(L2Mat + "Plaster.mat");

        var sky = GameObject.Find("Level02_SkyKingdom_v1").transform;
        var template = sky.Find("L2_02_SpikeIslet (29)").gameObject;
        var cargo = Object.FindFirstObjectByType<FragileCargo>();

        RemoveBuilt();

        var root = new GameObject(RootName).transform;
        var main = Group("Branch_Main", root);
        var repair = Group("Branch_Repair", root);

        // Islands
        Islet(template, "L2_Mid_Fork", Fork, .75f, 0, root);
        var m1 = Islet(template, "L2_Mid_M1_Conveyor", M1, .9f, 40, main.transform);
        Islet(template, "L2_Mid_M2_Rest", M2, .7f, 110, main.transform);
        Islet(template, "L2_Mid_M3_Turntable", M3, .9f, 200, main.transform);
        Islet(template, "L2_Mid_M4_Lift", M4, .95f, 300, root);
        Islet(template, "L2_Mid_B2_Roller", B2, .8f, 70, repair.transform);
        Islet(template, "L2_Mid_Shop", Shop, .75f, 160, repair.transform);

        // Main: conveyor across M1 pushing toward the outer edge, turntable on M3.
        Vector3 path1 = Flat(M2 - Fork).normalized, side = Vector3.Cross(Vector3.up, path1);
        var conveyor = Prefab("Moving/Trap_Conveyor", M1, Quaternion.LookRotation(-side), main);
        conveyor.name = "M1_Conveyor";
        Prefab("Moving/Trap_Turntable", M3, Quaternion.identity, main).name = "M3_Turntable";

        // Repair: tilt plate from the fork to B2, spike roller + bumpers on B2, flame bridge from the shop to M4.
        var tilt = Prefab("Obstacles/Obstacle_TiltPlate", Vector3.Lerp(Fork, B2, .5f) + Vector3.down * 1.05f,
            Quaternion.LookRotation(Flat(B2 - Fork)), repair);
        tilt.name = "B1_TiltPlate";
        Vector3 bDir = Flat(Shop - Fork).normalized, bSide = Vector3.Cross(Vector3.up, bDir);
        Prefab("Moving/Trap_SpikeRoller", B2, Quaternion.LookRotation(bDir), repair).name = "B2_SpikeRoller";
        Prefab("Moving/Trap_Bumper", B2 + bSide * 3.6f + bDir * 2.2f, Quaternion.identity, repair).name = "B2_Bumper";
        Prefab("Moving/Trap_Bumper", B2 - bSide * 3.6f - bDir * 2.2f, Quaternion.identity, repair).name = "B2_Bumper";
        var flameBridge = Plank("B3_FlameBridge", Shop, 4.2f, M4, 5.5f, 3.2f, repair.transform);
        for (int i = -1; i <= 1; i += 2)
        {
            var f = Prefab("Classic/Hazard_FlameTrap_Bridge", flameBridge.TransformPoint(new Vector3(0, .25f, i * 1.6f)), flameBridge.rotation, repair);
            f.transform.SetParent(flameBridge, true);
            f.name = "FlameTrap";
        }

        // Repair shop: stall + pad. The station (NetworkObject) sits outside the branch so hiding the branch is safe.
        Vector3 shopIn = Flat(Shop - B2).normalized;
        var stall = Group("ShopStall", repair.transform);
        stall.transform.SetPositionAndRotation(Shop + shopIn * 2.6f, Quaternion.LookRotation(-shopIn));
        Box("Counter", stall.transform, new Vector3(0, .55f, 0), new Vector3(3.6f, 1.1f, 1.1f), _wood, true);
        for (int sx = -1; sx <= 1; sx += 2)
            Box("Post", stall.transform, new Vector3(sx * 1.7f, 1.4f, .5f), new Vector3(.18f, 2.8f, .18f), _wood, true);
        Box("Canopy", stall.transform, new Vector3(0, 2.85f, .2f), new Vector3(4.2f, .12f, 2.2f), _cloth, false);
        Box("Planks", stall.transform, new Vector3(-1f, 1.25f, 0), new Vector3(1.2f, .2f, .5f), _wood, false);
        Box("Hammer", stall.transform, new Vector3(.9f, 1.2f, 0), new Vector3(.7f, .15f, .2f), _gray, false);

        var stationGo = new GameObject("L2_RepairStation");
        stationGo.transform.SetPositionAndRotation(Shop - shopIn * 1.2f, Quaternion.LookRotation(shopIn));
        stationGo.AddComponent<NetworkObject>();
        var station = stationGo.AddComponent<RepairStation>();
        var pad = Group("RepairPad", repair.transform).transform;
        pad.SetPositionAndRotation(stationGo.transform.position, stationGo.transform.rotation);
        var lamps = new List<Object> { Box("PadSlab", pad, new Vector3(0, .04f, 0), new Vector3(4.6f, .08f, 4.6f), _green, false).GetComponent<Renderer>() };
        for (int cx = -1; cx <= 1; cx += 2)
            for (int cz = -1; cz <= 1; cz += 2)
                lamps.Add(Box("PadLamp", pad, new Vector3(cx * 2.05f, .35f, cz * 2.05f), new Vector3(.3f, .7f, .3f), _green, false).GetComponent<Renderer>());
        var label = Label("ShopLabel", stall.transform, new Vector3(0, 3.6f, 0), "ร้านช่างลัง ซ่อมฟรี\nวางลังบนแท่น +30 HP");
        Set(station, so =>
        {
            so.FindProperty("_cargo").objectReferenceValue = cargo;
            so.FindProperty("_healAmount").intValue = 30;
            so.FindProperty("_pad").objectReferenceValue = pad;
            so.FindProperty("_branchRoot").objectReferenceValue = repair;
            var list = so.FindProperty("_lamps"); list.arraySize = lamps.Count;
            for (int i = 0; i < lamps.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
            so.FindProperty("_readyMaterial").objectReferenceValue = _green;
            so.FindProperty("_usedMaterial").objectReferenceValue = _gray;
            so.FindProperty("_label").objectReferenceValue = label;
        });

        // Fork sign + choice.
        var choiceGo = new GameObject("L2_BranchChoice");
        choiceGo.transform.position = Fork;
        choiceGo.AddComponent<NetworkObject>();
        var choice = choiceGo.AddComponent<BranchChoice>();
        var signPost = Group("ForkSign", root).transform;
        Vector3 toFork = Flat(Fork - new Vector3(-35.6f, 0, 96.1f)).normalized;
        signPost.SetPositionAndRotation(Fork + toFork * 2.5f, Quaternion.LookRotation(toFork));
        Box("SignPole", signPost, new Vector3(0, 1.3f, 0), new Vector3(.15f, 2.6f, .15f), _wood, true);
        Box("SignBoard", signPost, new Vector3(0, 2.5f, -.1f), new Vector3(3.6f, 1.1f, .08f), _wood, false);
        var signText = Label("SignText", signPost, new Vector3(0, 2.5f, -.16f), "");
        signText.transform.localRotation = Quaternion.identity;
        signText.characterSize = .012f;
        Arrow("ArrowMain", root, Fork, M1, _gold);
        Arrow("ArrowRepair", root, Fork, B2, _green);
        var mainEntry = new GameObject("MainEntry").transform; mainEntry.SetParent(choiceGo.transform, false); mainEntry.position = M1;
        var repairEntry = new GameObject("RepairEntry").transform; repairEntry.SetParent(choiceGo.transform, false); repairEntry.position = tilt.transform.position;
        Set(choice, so =>
        {
            so.FindProperty("_cargo").objectReferenceValue = cargo;
            so.FindProperty("_mainRoot").objectReferenceValue = main;
            so.FindProperty("_repairRoot").objectReferenceValue = repair;
            so.FindProperty("_mainEntry").objectReferenceValue = mainEntry;
            so.FindProperty("_repairEntry").objectReferenceValue = repairEntry;
            so.FindProperty("_entryRadius").floatValue = 4f;
            so.FindProperty("_sign").objectReferenceValue = signText;
        });

        // Lift on M4: deck heads north toward island (4).
        var lift = Prefab("Coop/Coop_ClimbSwitchLift", M4, Quaternion.Euler(0, 180, 0), null);
        lift.name = "L2_Mid_ClimbLift";
        var liftComp = lift.GetComponent("CoopSwitchLift");
        if (liftComp != null) Set(liftComp, so => { var p = so.FindProperty("_rise"); if (p != null) p.vector3Value = new Vector3(0, LiftRise - .2f, 0); });

        string moved = MoveFarSection(sky);
        string fixes = FixCheckpoint2() + FixExitPortal();

        EditorSceneManager.MarkSceneDirty(scene);
        return $"Level02 mid section built ({RootName}, L2_BranchChoice, L2_RepairStation, L2_Mid_ClimbLift). {moved} {fixes}";
    }

    private static void RemoveBuilt()
    {
        foreach (var old in new[] { RootName, "L2_BranchChoice", "L2_RepairStation", "L2_Mid_ClimbLift" })
        {
            var go = GameObject.Find(old);
            while (go != null) { Object.DestroyImmediate(go); go = GameObject.Find(old); }
        }
    }

    /// <summary>
    /// Undo this builder in the open (unsaved) scene: removes its objects and puts the far section and exit portal
    /// parts back where the saved scene file has them. Other objects are left alone.
    /// </summary>
    public static string RestoreMovedFromDisk()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        const string copy = "Assets/Scenes/__L2_disk_copy.unity";
        File.Copy(ScenePath, copy, true);
        AssetDatabase.ImportAsset(copy);
        var active = EditorSceneManager.GetActiveScene();
        var disk = EditorSceneManager.OpenScene(copy, OpenSceneMode.Additive);
        var paths = new List<(List<int> path, string name, Vector3 pos)>();
        var diskRoots = disk.GetRootGameObjects();
        for (int i = 0; i < diskRoots.Length; i++)
        {
            var t = diskRoots[i].transform;
            if (t.name == "L2_DeliveryExitPortal_Level01")
                for (int c = 0; c < t.childCount; c++) paths.Add((new List<int> { i, c }, t.GetChild(c).name, t.GetChild(c).position));
            else CollectHigh(t, new List<int> { i }, paths);
        }
        EditorSceneManager.CloseScene(disk, true);
        AssetDatabase.DeleteAsset(copy);
        EditorSceneManager.SetActiveScene(active);

        var roots = active.GetRootGameObjects();
        int restored = 0, skipped = 0;
        foreach (var (path, name, pos) in paths)
        {
            if (path[0] >= roots.Length) { skipped++; continue; }
            Transform t = roots[path[0]].transform;
            for (int k = 1; k < path.Count && t != null; k++) t = path[k] < t.childCount ? t.GetChild(path[k]) : null;
            if (t == null || t.name != name) { skipped++; continue; }
            t.position = pos; restored++;
        }
        RemoveBuilt();
        EditorSceneManager.MarkSceneDirty(active);
        return $"restored {restored} transforms from the saved scene, skipped {skipped}, removed built objects.";
    }

    private static void CollectHigh(Transform t, List<int> path, List<(List<int>, string, Vector3)> into)
    {
        if (t.GetComponent<RectTransform>() != null) return;
        bool hasLeaf = false;
        if (AllHigh(t, ref hasLeaf) && hasLeaf) { into.Add((new List<int>(path), t.name, t.position)); return; }
        for (int i = 0; i < t.childCount; i++) { path.Add(i); CollectHigh(t.GetChild(i), path, into); path.RemoveAt(path.Count - 1); }
    }

    /// <summary>Moves island (4) .. castle down to the top of the lift, once.</summary>
    private static string MoveFarSection(Transform sky)
    {
        var island4 = sky.Find("L2_02_SpikeIslet (4)");
        if (island4 == null) return "island (4) not found, far section not moved.";
        if (island4.position.y < 55f) return "far section already moved.";
        Vector3 target = M4 + new Vector3(0, LiftRise, 14.2f);
        Vector3 offset = target - island4.position;
        int count = 0;
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var r in scene.GetRootGameObjects())
        {
            if (r.name == RootName || r.name == "L2_Mid_ClimbLift" || r.name == "L2_BranchChoice" || r.name == "L2_RepairStation") continue;
            count += MoveHigh(r.transform, offset);
        }
        return $"far section moved by {offset} ({count} objects).";
    }

    private static int MoveHigh(Transform t, Vector3 offset)
    {
        if (t.GetComponent<RectTransform>() != null) return 0;
        bool hasLeaf = false;
        if (AllHigh(t, ref hasLeaf) && hasLeaf) { Undo.RecordObject(t, "Move far section"); t.position += offset; return 1; }
        int n = 0;
        for (int i = 0; i < t.childCount; i++) n += MoveHigh(t.GetChild(i), offset);
        return n;
    }

    private static bool AllHigh(Transform t, ref bool hasLeaf)
    {
        if (t.childCount == 0) { hasLeaf = true; return t.position.y > 50f; }
        for (int i = 0; i < t.childCount; i++)
            if (!AllHigh(t.GetChild(i), ref hasLeaf)) return false;
        return true;
    }

    private static string FixCheckpoint2()
    {
        var cp = GameObject.Find("L2_Checkpoint_2");
        if (cp == null) return "";
        var slab = cp.transform.Find("PadSlab");
        var cargoPoint = cp.transform.Find("CargoPoint");
        var playerPoint = cp.transform.Find("PlayerPoint");
        if (slab == null || cargoPoint == null || playerPoint == null) return "";
        cargoPoint.position = slab.position + Vector3.up * .9f;
        playerPoint.position = slab.position + new Vector3(0, .15f, -3.4f);
        return " CP2 respawn points moved onto the pad.";
    }

    private static string FixExitPortal()
    {
        var portal = GameObject.Find("L2_DeliveryExitPortal_Level01");
        if (portal == null) return "";
        var barrier = portal.transform.Find("ClosedBarrier");
        var model = portal.transform.Find("NewPortalModel");
        if (barrier == null || model == null || Vector3.Distance(barrier.position, model.position) < 8f) return "";
        var zone = GameObject.Find("DeliveryZone");
        float floor = zone != null ? zone.transform.position.y : barrier.position.y - 2.8f;
        Vector3 delta = new Vector3(barrier.position.x, floor, barrier.position.z) - model.position;
        for (int i = 0; i < portal.transform.childCount; i++)
        {
            var c = portal.transform.GetChild(i);
            if (c != barrier) c.position += delta;
        }
        return " exit portal parts moved to the castle barrier.";
    }

    // ---------- helpers ----------
    private static GameObject Islet(GameObject template, string name, Vector3 top, float scale, float yaw, Transform parent)
    {
        var go = Object.Instantiate(template, parent);
        go.name = name;
        go.transform.SetPositionAndRotation(top, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    private static GameObject Prefab(string path, Vector3 pos, Quaternion rot, GameObject parent)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Traps/" + path + ".prefab");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        if (parent != null) go.transform.SetParent(parent.transform, false);
        go.transform.SetPositionAndRotation(pos, rot);
        return go;
    }

    private static Transform Plank(string name, Vector3 a, float ar, Vector3 b, float br, float width, Transform parent)
    {
        Vector3 d = Flat(b - a).normalized;
        Vector3 from = a + d * (ar - .6f), to = b - d * (br - .6f);
        var t = new GameObject(name).transform;
        t.SetParent(parent, false);
        t.SetPositionAndRotation((from + to) * .5f, Quaternion.LookRotation(to - from));
        float length = Vector3.Distance(from, to);
        Box("Deck", t, new Vector3(0, -.2f, 0), new Vector3(width, .4f, length), _wood, true);
        for (int s = -1; s <= 1; s += 2)
            Box("SafetyEdge", t, new Vector3(s * (width * .5f + .1f), .15f, 0), new Vector3(.18f, .36f, length), _wood, true);
        return t;
    }

    private static void Arrow(string name, Transform parent, Vector3 from, Vector3 to, Material mat)
    {
        Vector3 d = Flat(to - from).normalized;
        var t = Group(name, parent).transform;
        t.SetPositionAndRotation(from + d * 3.6f + Vector3.up * .03f, Quaternion.LookRotation(d));
        Box("Shaft", t, Vector3.zero, new Vector3(.35f, .04f, 1.6f), mat, false);
        var l = Box("HeadL", t, new Vector3(-.25f, 0, .7f), new Vector3(.3f, .04f, .9f), mat, false); l.transform.localRotation = Quaternion.Euler(0, 40, 0);
        var r = Box("HeadR", t, new Vector3(.25f, 0, .7f), new Vector3(.3f, .04f, .9f), mat, false); r.transform.localRotation = Quaternion.Euler(0, -40, 0);
    }

    private static TextMesh Label(string name, Transform parent, Vector3 local, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localRotation = Quaternion.Euler(0, 180, 0);
        var tm = go.AddComponent<TextMesh>();
        tm.text = text; tm.fontSize = 112; tm.characterSize = .018f;
        tm.anchor = TextAnchor.MiddleCenter; tm.alignment = TextAlignment.Center; tm.color = Color.white;
        return tm;
    }

    private static GameObject Group(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private static GameObject Box(string name, Transform parent, Vector3 local, Vector3 scale, Material mat, bool collide)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collide) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static void Set(Object target, Action<SerializedObject> edit)
    {
        var so = new SerializedObject(target);
        edit(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
#endif
