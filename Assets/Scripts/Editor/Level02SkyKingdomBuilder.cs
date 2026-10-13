#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using CoopGame.CarrySystem;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Builds Level02 from the user's sketch: village start island, spike/hammer islets,
/// a dense forest island and a castle delivery island, descending gently between them.
/// Re-running replaces only the generated root; earlier Level02 geometry is preserved inactive.</summary>
public static class Level02SkyKingdomBuilder
{
    private const string ScenePath = "Assets/Scenes/Level02.unity";
    private const string RootName = "Level02_SkyKingdom_v1";
    private const string ParkName = "Level02_PreviousLayout_Preserved";
    private const string MaterialFolder = "Assets/Environment/Level02";
    private const string Evidence = "E:/Unity/Verification/Collaboration/20261014-level02-course";
    private static readonly string[] ParkedRoots = { "Level02_Ground", "Level02_Course", "MedievalScenery", "TeamLeverGate", "DeliveryTower" };

    private static Transform _root;
    private static Material _grass, _wood, _stone, _blue, _gold, _leaves, _warning, _roof, _cloth, _carpet, _plaster;
    private static FragileCargo _cargo;

    private static readonly Vector3 Village = new(0, 8f, 0), SpikeA = new(25, 8f, 6), HammerIslet = new(36, 7.2f, 18),
        Forest = new(24, 6f, 40), SpikeB = new(6, 4.5f, 56),
        CrusherIslet = new(-12, 3.5f, 64), SpinnerIslet = new(-30, 2.5f, 72), ClimbIslet = new(-44, 2f, 92),
        DoubleHammerIslet = new(-36, 1f, 112), SpikeC = new(-18, 0f, 124), Castle = new(2, -1.5f, 148);
    private static readonly Vector2 VillageR = new(15, 13), SmallR = new(6, 6), HammerR = new(7, 7), ForestR = new(17, 14), CastleR = new(17, 15),
        MidR = new(8, 8);

    [MenuItem("Tools/Expedition/Build Level02 Sky Kingdom")]
    public static void Menu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building Level02.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);

        Directory.CreateDirectory(Evidence);
        string backup = Evidence + "/Level02.Before.unity";
        if (!File.Exists(backup)) File.Copy(ScenePath, backup);

        var previous = FindRoot(scene, RootName);
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
        ParkOldLayout(scene);

        _root = new GameObject(RootName).transform;
        SceneManager.MoveGameObjectToScene(_root.gameObject, scene);
        FloatingCoopLevelBuilder.UseExistingAssets(_root);
        LoadMaterials();

        var cargoRoot = FindRoot(scene, "WoodenCrateItem") ?? throw new InvalidOperationException("Level02 needs WoodenCrateItem.");
        _cargo = cargoRoot.GetComponent<FragileCargo>();

        FloatingCoopLevelBuilder.Island("L2_01_Village", Village, VillageR, 211);
        FloatingCoopLevelBuilder.Island("L2_02_SpikeIslet", SpikeA, SmallR, 223);
        FloatingCoopLevelBuilder.Island("L2_03_HammerIslet", HammerIslet, HammerR, 227);
        FloatingCoopLevelBuilder.Island("L2_04_Forest", Forest, ForestR, 229);
        FloatingCoopLevelBuilder.Island("L2_05_SpikeIslet", SpikeB, SmallR, 233);
        FloatingCoopLevelBuilder.Island("L2_06_CrusherIslet", CrusherIslet, MidR, 251);
        FloatingCoopLevelBuilder.Island("L2_07_SpinnerIslet", SpinnerIslet, MidR, 257);
        FloatingCoopLevelBuilder.Island("L2_08_ClimbIslet", ClimbIslet, MidR, 263);
        FloatingCoopLevelBuilder.Island("L2_09_DoubleHammerIslet", DoubleHammerIslet, MidR, 269);
        FloatingCoopLevelBuilder.Island("L2_10_SpikeIslet", SpikeC, SmallR, 239);
        FloatingCoopLevelBuilder.Island("L2_11_Castle", Castle, CastleR, 241);

        BridgeWithRamps("L2_Bridge_VillageToSpikes", Village, VillageR, SpikeA, SmallR, 4.8f);
        BridgeWithRamps("L2_Bridge_SpikesToHammer", SpikeA, SmallR, HammerIslet, HammerR, 4.6f);
        BridgeWithRamps("L2_Bridge_HammerToForest", HammerIslet, HammerR, Forest, ForestR, 4.6f);
        BridgeWithRamps("L2_Bridge_ForestToSpikes", Forest, ForestR, SpikeB, SmallR, 4.6f);
        BridgeWithRamps("L2_Bridge_SpikesToCrusher", SpikeB, SmallR, CrusherIslet, MidR, 4.6f);
        BridgeWithRamps("L2_Bridge_CrusherToSpinner", CrusherIslet, MidR, SpinnerIslet, MidR, 4.6f);
        CollapsingStones("L2_CollapsingStones", SpinnerIslet, MidR, ClimbIslet, MidR);
        BridgeWithRamps("L2_Bridge_NarrowPlank", ClimbIslet, MidR, DoubleHammerIslet, MidR, 2.4f);
        BridgeWithRamps("L2_Bridge_HammersToSpikes", DoubleHammerIslet, MidR, SpikeC, SmallR, 4.6f);
        BridgeWithRamps("L2_Bridge_SpikesToCastle", SpikeC, SmallR, Castle, CastleR, 4.8f);

        BuildVillage(scene, cargoRoot);
        SpikeTrap("L2_SpikeTrap_A", SpikeA, Travel(Village, HammerIslet), 0f);
        SwingHammer("L2_SwingHammer", HammerIslet, Travel(SpikeA, Forest), 1.5f);
        int trees = BuildForest();
        SpikeTrap("L2_SpikeTrap_B", SpikeB, Travel(Forest, CrusherIslet), 2.5f);
        Crusher("L2_Crusher", CrusherIslet, Travel(SpikeB, SpinnerIslet), 1f);
        Spinner("L2_SpinningLog", SpinnerIslet, Travel(CrusherIslet, ClimbIslet));
        ClimbGate("L2_ClimbGate", ClimbIslet, Travel(SpinnerIslet, DoubleHammerIslet), mission: FindRoot(scene, "LevelMission")?.GetComponent<LevelMission>());
        SwingHammer("L2_SwingHammer_1", DoubleHammerIslet - Travel(ClimbIslet, SpikeC) * 2.3f, Travel(ClimbIslet, SpikeC), 0f);
        SwingHammer("L2_SwingHammer_2", DoubleHammerIslet + Travel(ClimbIslet, SpikeC) * 2.3f, Travel(ClimbIslet, SpikeC), 2.6f);
        FloatingCoopLevelBuilder.Sign("L2_PlankSign", ClimbIslet + Travel(ClimbIslet, DoubleHammerIslet) * 5f + Vector3.up * 2.6f + Vector3.Cross(Vector3.up, Travel(ClimbIslet, DoubleHammerIslet)) * 2.6f, "สะพานแคบ! เดินตรงๆ", 0);
        SpikeTrap("L2_SpikeTrap_C1", SpikeC + Travel(DoubleHammerIslet, Castle) * -1.8f, Travel(DoubleHammerIslet, Castle), 0f);
        SpikeTrap("L2_SpikeTrap_C2", SpikeC + Travel(DoubleHammerIslet, Castle) * 1.8f, Travel(DoubleHammerIslet, Castle), 3.6f);
        BuildCastle(scene);

        var mission = FindRoot(scene, "LevelMission")?.GetComponent<LevelMission>();
        if (mission != null)
        {
            var data = new SerializedObject(mission);
            var objective = data.FindProperty("_objective");
            if (objective != null) objective.stringValue = "ขนลังจากหมู่บ้าน ผ่านป่า ไปส่งที่ปราสาท";
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(4, 0, 42), Quaternion.Euler(55, -20, 0), 110f);
        return $"{ScenePath}: 11 islands, 9 bridges + collapsing-stone crossing, 4 spike traps, 3 swing hammers, crusher, spinning log, climb-switch gate, {trees} forest trees, castle with lever gate; old layout parked in {ParkName}. Backup: {backup}";
    }

    // Decks run straight between island centres, so the end over a lower island hangs above it
    // as a ledge. A shallow ramp lets the crate be carried down without a drop.
    private static void BridgeWithRamps(string name, Vector3 a, Vector2 ar, Vector3 b, Vector2 br, float width)
    {
        var bridge = FloatingCoopLevelBuilder.Bridge(name, a, ar, b, br, width, _root).transform;
        float length = bridge.Find("SolidCargoDeck").localScale.z;
        for (int end = -1; end <= 1; end += 2)
        {
            Vector3 top = bridge.position + bridge.forward * (end * length * .5f) + bridge.up * .06f;
            float ground = end > 0 ? b.y : a.y;
            float drop = top.y - ground;
            if (drop < .12f) continue;
            Vector3 outward = bridge.forward * end; outward.y = 0; outward.Normalize();
            Vector3 foot = top + outward * (drop / Mathf.Tan(12f * Mathf.Deg2Rad));
            foot.y = ground;
            Vector3 along = foot - top;
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "LandingRamp"; ramp.transform.SetParent(bridge, true);
            ramp.transform.rotation = Quaternion.LookRotation(along);
            ramp.transform.position = (top + foot) * .5f - ramp.transform.up * .15f;
            ramp.transform.localScale = new Vector3(width, .3f, along.magnitude);
            ramp.GetComponent<Renderer>().sharedMaterial = _wood;
        }
    }

    private static Vector3 Travel(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; d.y = 0f;
        return d.normalized;
    }

    private static void ParkOldLayout(Scene scene)
    {
        var park = FindRoot(scene, ParkName);
        if (park == null) { park = new GameObject(ParkName); SceneManager.MoveGameObjectToScene(park, scene); }
        foreach (var go in scene.GetRootGameObjects())
            if (Array.IndexOf(ParkedRoots, go.name) >= 0) go.transform.SetParent(park.transform, true);
        park.SetActive(false);
    }

    private static void LoadMaterials()
    {
        const string shared = "Assets/Environment/FloatingCoop/";
        _grass = Load(shared + "Grass.mat"); _wood = Load(shared + "BridgeWood.mat"); _stone = Load(shared + "Stone.mat");
        _blue = Load(shared + "RelayBlue.mat"); _gold = Load(shared + "GoalGold.mat"); _leaves = Load(shared + "Leaves.mat");
        _warning = Load(shared + "TrapWarning.mat");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Environment", "Level02");
        _roof = Mat("Roof", new Color(.62f, .24f, .16f));
        _cloth = Mat("StallCloth", new Color(.86f, .30f, .26f));
        _carpet = Mat("RoyalCarpet", new Color(.70f, .08f, .10f));
        _plaster = Mat("Plaster", new Color(.92f, .86f, .70f));
    }

    private static Material Load(string path) => AssetDatabase.LoadAssetAtPath<Material>(path) ?? throw new InvalidOperationException("Missing material " + path);

    private static Material Mat(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .08f); EditorUtility.SetDirty(material);
        return material;
    }

    // ---------------- 1. Village (start) ----------------
    private static void BuildVillage(Scene scene, GameObject cargoRoot)
    {
        // Crate starts on the line of the first bridge, long side along the route, carrier behind it.
        Vector3 route = Travel(Village, SpikeA);
        var start = FindRoot(scene, "PlayerStart")?.transform ?? new GameObject("PlayerStart").transform;
        start.SetPositionAndRotation(Village - route * 5f + Vector3.up * 1.05f, Quaternion.LookRotation(route));
        Vector3 cargoAt = Village + route * 2f;
        // The crate model's long (2 m) side runs along its root's forward axis.
        cargoRoot.transform.SetPositionAndRotation(cargoAt + Vector3.up * 1.3f, Quaternion.LookRotation(route));
        FloatingCoopLevelBuilder.Parking("L2_StartCargoPad", cargoAt, 4.6f);

        var village = Group("L2_VillageDecor", _root, Village, Quaternion.identity);
        House(village, "House_A", new Vector3(-9, 0, 6), 20, new Vector2(4.5f, 4f), 3.2f);
        House(village, "House_B", new Vector3(-3, 0, 9), -10, new Vector2(4f, 3.6f), 2.8f);
        House(village, "House_C", new Vector3(-10, 0, -7), 160, new Vector2(4f, 4f), 3f);
        House(village, "House_D", new Vector3(4, 0, 9), 5, new Vector2(3.6f, 3.4f), 2.6f);
        Stall(village, "Stall_A", new Vector3(8, 0, 6.5f), 180);
        Stall(village, "Stall_B", new Vector3(8, 0, -7.5f), 0);
        Well(village, new Vector3(-2, 0, 4.5f));
        for (int i = 0; i < 3; i++) Box("HayBale", village, new Vector3(-12 + i * 1.3f, .45f, 1.5f), new Vector3(1.2f, .9f, 1f), _gold);
        FloatingCoopLevelBuilder.Flag("L2_VillageFlag", Village + new Vector3(-12, 0, -2), _blue);
        // Text reads correctly when viewed along its forward axis, so point it away from the spawn.
        Vector3 signAt = Village + new Vector3(-1, 3.4f, -6);
        Vector3 facing = signAt - start.position; facing.y = 0;
        var startSign = FloatingCoopLevelBuilder.Sign("L2_StartSign", signAt, "ยกลังไปส่งที่ปราสาท\nคลิกซ้ายยก / คลิกขวาวาง", 0);
        startSign.transform.rotation = Quaternion.LookRotation(facing);
        var title = FindRoot(scene, "Level02_Title");
        if (title != null) title.transform.SetPositionAndRotation(signAt + Vector3.up * 1.6f, Quaternion.LookRotation(facing));
    }

    private static void House(Transform parent, string name, Vector3 local, float yaw, Vector2 footprint, float height)
    {
        var house = Group(name, parent, parent.TransformPoint(local), Quaternion.Euler(0, yaw, 0));
        Box("Walls", house, new Vector3(0, height * .5f, 0), new Vector3(footprint.x, height, footprint.y), _plaster);
        for (int side = -1; side <= 1; side += 2)
        {
            Box("Beam", house, new Vector3(side * footprint.x * .5f, height * .5f, -footprint.y * .5f - .02f), new Vector3(.25f, height, .1f), _wood, false);
            var slab = Box("Roof", house, new Vector3(side * footprint.x * .25f, height + footprint.x * .2f, 0),
                new Vector3(footprint.x * .62f, .25f, footprint.y + .6f), _roof);
            slab.transform.localRotation = Quaternion.Euler(0, 0, -side * 35f);
        }
        Box("Door", house, new Vector3(0, .9f, -footprint.y * .5f - .04f), new Vector3(1f, 1.8f, .08f), _wood, false);
        Box("Window", house, new Vector3(footprint.x * .28f, height * .6f, -footprint.y * .5f - .04f), new Vector3(.7f, .7f, .06f), _blue, false);
    }

    private static void Stall(Transform parent, string name, Vector3 local, float yaw)
    {
        var stall = Group(name, parent, parent.TransformPoint(local), Quaternion.Euler(0, yaw, 0));
        Box("Counter", stall, new Vector3(0, .5f, 0), new Vector3(3.2f, 1f, 1.1f), _wood);
        for (int side = -1; side <= 1; side += 2) Box("Pole", stall, new Vector3(side * 1.5f, 1.25f, .45f), new Vector3(.15f, 2.5f, .15f), _wood);
        var canopy = Box("Canopy", stall, new Vector3(0, 2.45f, .1f), new Vector3(3.6f, .1f, 1.8f), _cloth, false);
        canopy.transform.localRotation = Quaternion.Euler(-12, 0, 0);
        for (int i = -1; i <= 1; i++) Box("Goods", stall, new Vector3(i * .9f, 1.15f, 0), new Vector3(.6f, .3f, .5f), i == 0 ? _gold : _leaves, false);
    }

    private static void Well(Transform parent, Vector3 local)
    {
        var well = Group("Well", parent, parent.TransformPoint(local), Quaternion.identity);
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "WellRing"; ring.transform.SetParent(well, false);
        ring.transform.localPosition = new Vector3(0, .45f, 0); ring.transform.localScale = new Vector3(2f, .45f, 2f);
        ring.GetComponent<Renderer>().sharedMaterial = _stone;
        for (int side = -1; side <= 1; side += 2) Box("WellPost", well, new Vector3(side * .9f, 1.3f, 0), new Vector3(.15f, 1.7f, .15f), _wood, false);
        Box("WellRoof", well, new Vector3(0, 2.25f, 0), new Vector3(2.4f, .15f, 1.4f), _roof, false);
    }

    // ---------------- Spike traps (small islets) ----------------
    private static void SpikeTrap(string name, Vector3 at, Vector3 travel, float phaseOffset)
    {
        var t = Group(name, _root, at, Quaternion.LookRotation(travel));
        var trap = t.gameObject.AddComponent<TimedCargoTrap>();
        var plate = Box("SpikePlate", t, new Vector3(0, .03f, 0), new Vector3(5.2f, .04f, 2.6f), _warning, false);
        var zone = Group("DamageZone", t, at, t.rotation).gameObject;
        var box = zone.AddComponent<BoxCollider>();
        box.isTrigger = true; box.center = Vector3.up * 1.3f; box.size = new Vector3(5.2f, 2.6f, 2.6f);
        var spikes = Group("Spikes", t, at, t.rotation);
        for (float x = -2.2f; x <= 2.21f; x += .55f)
            for (float z = -1f; z <= 1.01f; z += .5f)
            {
                var spike = Box("Spike", spikes, new Vector3(x, .5f, z), new Vector3(.16f, 1f, .16f), _stone, false);
                spike.transform.localRotation = Quaternion.Euler(0, 45, 0);
            }
        spikes.gameObject.SetActive(false);
        for (int side = -1; side <= 1; side += 2) Box("SpikeHousing", t, new Vector3(side * 2.9f, .25f, 0), new Vector3(.4f, .5f, 2.8f), _stone);
        var so = new SerializedObject(trap);
        Ref(so, "_zone", box); Ref(so, "_flames", spikes.gameObject); Ref(so, "_marker", plate.GetComponent<Renderer>()); Ref(so, "_cargo", _cargo);
        so.FindProperty("_safeSeconds").floatValue = 4.5f; so.FindProperty("_warningSeconds").floatValue = 1.2f;
        so.FindProperty("_activeSeconds").floatValue = 1.8f; so.FindProperty("_phaseOffset").floatValue = phaseOffset;
        so.FindProperty("_damage").intValue = 15;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------- Swing hammer islet ----------------
    private static void SwingHammer(string name, Vector3 at, Vector3 travel, float phaseOffset)
    {
        var t = Group(name, _root, at, Quaternion.LookRotation(travel));
        var hazard = t.gameObject.AddComponent<NetworkRouteHazard>();
        for (int side = -1; side <= 1; side += 2) Box("SweepPost", t, new Vector3(side * 5f, 2.9f, 0), new Vector3(.45f, 5.8f, .5f), _wood);
        Box("SweepBeam", t, new Vector3(0, 5.9f, 0), new Vector3(10.5f, .4f, .6f), _wood);
        var pivot = Group("SwingPivot", t, t.TransformPoint(Vector3.up * 5.6f), t.rotation);
        var rb = pivot.gameObject.AddComponent<Rigidbody>(); SetupBody(rb);
        Box("SwingArm", pivot, new Vector3(0, -2.1f, 0), new Vector3(.25f, 4.2f, .25f), _wood, false);
        var head = Box("SwingHead", pivot, new Vector3(0, -4.2f, 0), new Vector3(1.6f, 1.8f, 1.5f), _stone);
        pivot.localRotation = Quaternion.Euler(0, 0, 70f);
        var lamp = Box("WarningLamp", t, new Vector3(-4.4f, 2.2f, -2f), new Vector3(.5f, .7f, .5f), _warning, false);
        Box("SweepZone", t, new Vector3(0, .025f, 0), new Vector3(9f, .03f, 2f), _warning, false);
        var so = new SerializedObject(hazard);
        so.FindProperty("_kind").enumValueIndex = (int)NetworkRouteHazard.HazardKind.SwingHammer;
        Ref(so, "_primary", rb); Ref(so, "_hitA", head.GetComponent<BoxCollider>()); Ref(so, "_cargo", _cargo);
        Ref(so, "_indicator", lamp.GetComponent<Renderer>());
        so.FindProperty("_swingAngle").floatValue = 70f;
        so.FindProperty("_safeSeconds").floatValue = 3.5f; so.FindProperty("_warningSeconds").floatValue = 1.5f;
        so.FindProperty("_activeSeconds").floatValue = 4f; so.FindProperty("_phaseOffset").floatValue = phaseOffset;
        so.FindProperty("_damage").intValue = 10;
        so.ApplyModifiedPropertiesWithoutUndo();
        FloatingCoopLevelBuilder.Sign(name + "_Sign", at + Quaternion.LookRotation(travel) * new Vector3(-5.5f, 2.6f, -3.5f), "ค้อนแกว่ง! รอจังหวะ", 0);
    }

    // ---------------- Crusher islet ----------------
    private static void Crusher(string name, Vector3 at, Vector3 travel, float phaseOffset)
    {
        var t = Group(name, _root, at, Quaternion.LookRotation(travel));
        var hazard = t.gameObject.AddComponent<NetworkRouteHazard>();
        var a = Box("LeftCrusher", t, new Vector3(-5.2f, 1.3f, 0), new Vector3(2.4f, 2.6f, 2.4f), _stone);
        var b = Box("RightCrusher", t, new Vector3(5.2f, 1.3f, 0), new Vector3(2.4f, 2.6f, 2.4f), _stone);
        SetupBody(a.AddComponent<Rigidbody>()); SetupBody(b.AddComponent<Rigidbody>());
        var lamp = Box("WarningLamp", t, new Vector3(-4.4f, 2.8f, -2.4f), new Vector3(.5f, .7f, .5f), _warning, false);
        Box("CrushZone", t, new Vector3(0, .028f, 0), new Vector3(8f, .03f, 2.5f), _warning, false);
        for (int side = -1; side <= 1; side += 2) Box("CrusherHousing", t, new Vector3(side * 6.7f, 1.5f, 0), new Vector3(.5f, 3f, 3f), _wood);
        var so = new SerializedObject(hazard);
        so.FindProperty("_kind").enumValueIndex = (int)NetworkRouteHazard.HazardKind.Crusher;
        Ref(so, "_primary", a.GetComponent<Rigidbody>()); Ref(so, "_secondary", b.GetComponent<Rigidbody>());
        Ref(so, "_hitA", a.GetComponent<BoxCollider>()); Ref(so, "_hitB", b.GetComponent<BoxCollider>());
        Ref(so, "_cargo", _cargo); Ref(so, "_indicator", lamp.GetComponent<Renderer>());
        so.FindProperty("_travel").floatValue = 4f;
        so.FindProperty("_primaryRest").vector3Value = a.transform.localPosition;
        so.FindProperty("_secondaryRest").vector3Value = b.transform.localPosition;
        so.FindProperty("_safeSeconds").floatValue = 3.5f; so.FindProperty("_warningSeconds").floatValue = 1.5f;
        so.FindProperty("_activeSeconds").floatValue = 3f; so.FindProperty("_phaseOffset").floatValue = phaseOffset;
        so.FindProperty("_damage").intValue = 15;
        so.ApplyModifiedPropertiesWithoutUndo();
        FloatingCoopLevelBuilder.Sign(name + "_Sign", at + Quaternion.LookRotation(travel) * new Vector3(-6f, 3.2f, -4f), "เครื่องบด! ผ่านตอนไฟเขียว", 0);
    }

    // ---------------- Spinning log islet ----------------
    // Parked along the path (safe), then sweeps one full turn across it.
    private static void Spinner(string name, Vector3 at, Vector3 travel)
    {
        var t = Group(name, _root, at, Quaternion.LookRotation(travel));
        var hazard = t.gameObject.AddComponent<RotatingLogHazard>();
        var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        post.name = "SpinnerPost"; post.transform.SetParent(t, false);
        post.transform.localPosition = new Vector3(0, .9f, 0); post.transform.localScale = new Vector3(.9f, .9f, .9f);
        post.GetComponent<Renderer>().sharedMaterial = _stone;
        var pivot = Group("SpinnerPivot", t, at, t.rotation);
        var rb = pivot.gameObject.AddComponent<Rigidbody>(); SetupBody(rb);
        var bar = Box("SpinningLog", pivot, new Vector3(0, 1f, 0), new Vector3(13f, .6f, .6f), _wood);
        var lamp = Box("WarningLamp", t, new Vector3(-3.2f, 2.8f, -6f), new Vector3(.5f, .7f, .5f), _warning, false);
        var so = new SerializedObject(hazard);
        Ref(so, "_logRigidbody", rb);
        so.FindProperty("_axis").enumValueIndex = (int)RotatingLogHazard.RotationAxis.Y;
        so.FindProperty("_safeSeconds").floatValue = 3f; so.FindProperty("_warningSeconds").floatValue = 1.2f;
        so.FindProperty("_sweepSeconds").floatValue = 3.5f; so.FindProperty("_parkAngle").floatValue = 90f;
        Ref(so, "_cargo", _cargo); so.FindProperty("_cargoImpactDamage").intValue = 15;
        Ref(so, "_impactZone", bar.GetComponent<BoxCollider>()); Ref(so, "_indicatorRenderer", lamp.GetComponent<Renderer>());
        so.ApplyModifiedPropertiesWithoutUndo();
        FloatingCoopLevelBuilder.Sign(name + "_Sign", at + Quaternion.LookRotation(travel) * new Vector3(-4.5f, 3.4f, -6f), "ท่อนซุงหมุน! ข้ามตอนมันจอดนิ่ง", 0);
    }

    // ---------------- Collapsing stone crossing ----------------
    private static void CollapsingStones(string name, Vector3 a, Vector2 ar, Vector3 b, Vector2 br)
    {
        Vector3 d = Travel(a, b);
        float Radius(Vector2 r) => 1f / Mathf.Sqrt(d.x * d.x / (r.x * r.x) + d.z * d.z / (r.y * r.y));
        Vector3 from = a + d * (Radius(ar) * .9f), to = b - d * (Radius(br) * .9f);
        from.y = a.y; to.y = b.y;
        var group = Group(name, _root, (from + to) * .5f, Quaternion.LookRotation(d));
        const int count = 3; const float gap = .3f;
        float span = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(to.x, 0, to.z));
        float length = (span - gap * (count + 1)) / count;
        for (int i = 0; i < count; i++)
        {
            float along = gap + length * .5f + i * (length + gap);
            Vector3 p = Vector3.Lerp(from, to, along / span);
            var stoneRoot = Group("CollapsingStone_" + (i + 1), group, p, group.rotation);
            var stone = stoneRoot.gameObject.AddComponent<CollapsingSteppingStone>();
            var slab = Box("StoneSlab", stoneRoot, new Vector3(0, -.25f, 0), new Vector3(4.2f, .5f, length), _stone);
            var rb = slab.AddComponent<Rigidbody>(); SetupBody(rb);
            // Touch messages reach the component only on its own object, so it owns this sensor.
            var sensor = stoneRoot.gameObject.AddComponent<BoxCollider>();
            sensor.isTrigger = true; sensor.center = new Vector3(0, .45f, 0); sensor.size = new Vector3(4f, .7f, length - .1f);
            var so = new SerializedObject(stone);
            Ref(so, "_rigidbody", rb); Ref(so, "_solidCollider", slab.GetComponent<Collider>()); Ref(so, "_stoneRenderer", slab.GetComponent<Renderer>());
            so.FindProperty("_warningSeconds").floatValue = 1.4f; so.FindProperty("_collapsedSeconds").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        FloatingCoopLevelBuilder.Sign(name + "_Sign", from + Vector3.up * 3f - d * 2.5f + Vector3.Cross(Vector3.up, d) * 3f, "หินถล่ม! เดินต่อเนื่อง ห้ามหยุด", 0);
    }

    // ---------------- Climb tower gate ----------------
    private static void ClimbGate(string name, Vector3 at, Vector3 travel, LevelMission mission)
    {
        var t = Group(name, _root, at, Quaternion.LookRotation(travel));
        var gate = t.gameObject.AddComponent<ClimbSwitchGate>();
        var barrier = Box("LockedPassage", t, new Vector3(0, 2f, 3f), new Vector3(5.6f, 4f, .5f), _wood);
        for (int side = -1; side <= 1; side += 2)
            Box("GateWall", t, new Vector3(side * 5.9f, 2f, 3f), new Vector3(6.2f, 4f, .8f), _stone);
        const float height = 6f; float step = height / 4f;
        var climb = Group("ClimbTower", t, t.TransformPoint(new Vector3(-5.5f, 0, -1f)), t.rotation);
        for (int j = 0; j < 4; j++)
        {
            float x = j % 2 == 0 ? -1.1f : 1.1f;
            var wall = Box("ClimbStage_" + j, climb, new Vector3(x, step * (j + .5f), 0), new Vector3(3.6f, step, 1.2f), _stone); wall.layer = 3;
            Box("ClimbStripe", climb, new Vector3(x, step * (j + .5f), -.615f), new Vector3(.25f, step - .2f, .025f), _blue, false);
            var ledge = Box(j == 3 ? "TopSwitchLanding" : "RestLedge_" + j, climb, new Vector3(x, step * (j + 1) - .15f, -1.3f), new Vector3(3.6f, .3f, 2.6f), _wood); ledge.layer = 3;
        }
        var button = Box("HighButton", climb, new Vector3(1.1f, height + .35f, -.7f), new Vector3(1f, .65f, .25f), _blue, false);
        var label = FloatingCoopLevelBuilder.Sign(name + "_Sign", t.TransformPoint(new Vector3(0, 4.8f, 2.6f)), "ปีนหอคอยไปกดปุ่มสีฟ้า\nเพื่อเปิดประตู", 0);
        label.transform.rotation = t.rotation;
        var so = new SerializedObject(gate);
        Ref(so, "_button", button.transform); Ref(so, "_barrier", barrier); Ref(so, "_label", label);
        Ref(so, "_indicator", button.GetComponent<Renderer>()); Ref(so, "_mission", mission);
        so.FindProperty("_interactionDistance").floatValue = 1.8f; so.FindProperty("_requiredHeightOffset").floatValue = .2f;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------- 4. Forest (trees block the crate) ----------------
    private static int BuildForest()
    {
        Vector3 entry = Forest + Travel(Forest, HammerIslet) * 11f;
        Vector3 exit = Forest + Travel(Forest, SpikeB) * 11f;
        var path = new List<Vector3> { entry, Forest + new Vector3(-3, 0, -5), Forest + new Vector3(5, 0, 4), exit };
        var forest = Group("L2_ForestTrees", _root, Forest, Quaternion.identity);
        var random = new System.Random(4242);
        int count = 0;
        // 1.8 m spacing leaves ~1.25 m between trunks: players slip through, the crate does not.
        for (float x = -ForestR.x; x <= ForestR.x; x += 1.8f)
            for (float z = -ForestR.y; z <= ForestR.y; z += 1.8f)
            {
                float jitterX = ((float)random.NextDouble() - .5f) * .4f, jitterZ = ((float)random.NextDouble() - .5f) * .4f;
                Vector3 p = Forest + new Vector3(x + jitterX, 0, z + jitterZ);
                float ellipse = (x * x) / (ForestR.x * ForestR.x) + (z * z) / (ForestR.y * ForestR.y);
                if (ellipse > .72f || DistanceToPath(p, path) < 3.1f) continue;
                Tree(forest, "Tree_" + count, p, 1f + (float)random.NextDouble() * .5f);
                count++;
            }
        for (int i = 1; i < path.Count - 1; i++) FloatingCoopLevelBuilder.Parking("L2_ForestRest_" + i, path[i], 4.2f);
        FloatingCoopLevelBuilder.Sign("L2_ForestSign", entry + new Vector3(0, 3, 0) + Travel(HammerIslet, Forest) * 2f, "ป่าทึบ! ลังผ่านต้นไม้ไม่ได้\nเดินตามทางโล่ง", 0);
        return count;
    }

    private static float DistanceToPath(Vector3 p, List<Vector3> path)
    {
        float best = float.MaxValue;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2 a = new(path[i].x, path[i].z), b = new(path[i + 1].x, path[i + 1].z), q = new(p.x, p.z);
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(.0001f, ab.sqrMagnitude));
            best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
        }
        return best;
    }

    private static void Tree(Transform parent, string name, Vector3 world, float scale)
    {
        var tree = Group(name, parent, world, Quaternion.Euler(0, (world.x * 37 + world.z * 11) % 360, 0));
        Box("Trunk", tree, new Vector3(0, 1.4f * scale, 0), new Vector3(.55f, 2.8f * scale, .55f), _wood);
        var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        crown.name = "Crown"; crown.transform.SetParent(tree, false);
        crown.transform.localPosition = new Vector3(0, 3.4f * scale, 0); crown.transform.localScale = Vector3.one * 2.6f * scale;
        crown.GetComponent<Renderer>().sharedMaterial = _leaves;
        UnityEngine.Object.DestroyImmediate(crown.GetComponent<Collider>());
    }

    // ---------------- 7. Castle (delivery) ----------------
    private static void BuildCastle(Scene scene)
    {
        Vector3 toEntrance = Travel(Castle, SpikeC);
        var castle = Group("L2_Castle", _root, Castle + new Vector3(0, 0, 0), Quaternion.LookRotation(-toEntrance));
        const float half = 8f, height = 4.2f, thick = 1.1f, gateHalf = 3f;
        Box("BackWall", castle, new Vector3(0, height * .5f, half), new Vector3(half * 2, height, thick), _stone);
        for (int side = -1; side <= 1; side += 2)
        {
            Box("SideWall", castle, new Vector3(side * half, height * .5f, 0), new Vector3(thick, height, half * 2), _stone);
            float span = half - gateHalf;
            Box("FrontWall", castle, new Vector3(side * (gateHalf + span * .5f), height * .5f, -half), new Vector3(span, height, thick), _stone);
            for (int end = -1; end <= 1; end += 2)
            {
                var tower = Box("Tower", castle, new Vector3(side * half, height + .5f, end * half), new Vector3(3f, height * 2 + 1f, 3f), _stone);
                for (int j = -1; j <= 1; j++)
                    Box("Battlement", castle, tower.transform.localPosition + new Vector3(j * 1f, height + 1f, 0), new Vector3(.6f, .9f, 3f), _stone, false);
                Box("TowerRoof", castle, tower.transform.localPosition + new Vector3(0, height + 1.9f, 0), new Vector3(3.4f, .4f, 3.4f), _roof, false);
            }
        }
        Box("Keep", castle, new Vector3(0, 4.5f, half + 2.5f), new Vector3(7f, 9f, 4f), _stone);
        Box("KeepRoof", castle, new Vector3(0, 9.3f, half + 2.5f), new Vector3(7.6f, .6f, 4.6f), _roof, false);
        Box("Carpet", castle, new Vector3(0, .02f, -2.5f), new Vector3(2.4f, .02f, 9f), _carpet, false);
        FloatingCoopLevelBuilder.Flag("L2_CastleFlagL", castle.TransformPoint(new Vector3(-half, height * 2 + 1f, -half)), _gold);
        FloatingCoopLevelBuilder.Flag("L2_CastleFlagR", castle.TransformPoint(new Vector3(half, height * 2 + 1f, -half)), _gold);

        // Gate: a teammate holds the lever outside while the others bring the crate through.
        var gateRoot = Group("L2_CastleGate", castle, castle.TransformPoint(new Vector3(0, 0, -half)), castle.rotation);
        var gate = gateRoot.gameObject.AddComponent<TeamLeverGate>();
        var barrier = Box("ClosedGate", gateRoot, new Vector3(0, 1.9f, 0), new Vector3(gateHalf * 2, 3.8f, .4f), _wood);
        var lever = Box("LeverStand", gateRoot, new Vector3(-gateHalf - 3.5f, 1f, -4f), new Vector3(1f, 2f, 1f), _blue);
        var safety = Group("GateSafety", gateRoot, gateRoot.position, gateRoot.rotation).gameObject;
        var volume = safety.AddComponent<BoxCollider>();
        volume.isTrigger = true; volume.center = new Vector3(0, 1.7f, 0); volume.size = new Vector3(gateHalf * 2 + .5f, 4f, 4f);
        var label = FloatingCoopLevelBuilder.Sign("L2_GateSign", gateRoot.TransformPoint(new Vector3(-gateHalf - 3.5f, 3.4f, -4f)),
            "กด E ค้างที่คันโยกสีฟ้า\nให้เพื่อนขนลังผ่านประตู", gateRoot.eulerAngles.y + 180f);
        label.transform.SetParent(gateRoot, true);
        var data = new SerializedObject(gate);
        Ref(data, "_lever", lever.transform); Ref(data, "_barrier", barrier); Ref(data, "_safetyVolume", volume);
        Ref(data, "_label", label); Ref(data, "_cargo", _cargo);
        data.FindProperty("_openSeconds").floatValue = 12f;
        data.ApplyModifiedPropertiesWithoutUndo();

        var delivery = FindRoot(scene, "DeliveryZone") ?? throw new InvalidOperationException("Level02 needs DeliveryZone.");
        Vector3 goal = castle.TransformPoint(new Vector3(0, 0, 2.5f));
        delivery.transform.position = goal + Vector3.up * .04f;
        FloatingCoopLevelBuilder.Parking("L2_DeliveryLanding", goal, 6f);
    }

    // ---------------- helpers ----------------
    private static Transform Group(string name, Transform parent, Vector3 world, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(world, rotation);
        return go.transform;
    }

    private static GameObject Box(string name, Transform parent, Vector3 local, Vector3 size, Material material, bool solid = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = local; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static void SetupBody(Rigidbody rb)
    {
        rb.isKinematic = true; rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private static void Ref(SerializedObject so, string name, UnityEngine.Object value) => so.FindProperty(name).objectReferenceValue = value;

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (var go in scene.GetRootGameObjects()) if (go.name == name) return go;
        return null;
    }
}
#endif
