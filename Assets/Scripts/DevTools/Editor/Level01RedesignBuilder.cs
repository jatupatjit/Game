#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using CoopGame.CarrySystem;
using CoopGame.Network;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the Level01 teaching route (GAME_RULES section 7): eight islands plus an optional repair-kit branch, with
/// checkpoint pads, a climb switch, a two-player pad gate, the "place the crate, swap roles" puzzle, three hazard types,
/// orange route markers and a castle finish. Re-running replaces only the generated root; the old route
/// (FloatingCoopRoute and its stacked v1..v8 layers) is parked inactive, never deleted.
/// </summary>
public static class Level01RedesignBuilder
{
    private const string ScenePath = "Assets/Scenes/Level01.unity";
    private const string RootName = "Level01_Teaching_v9";
    private const string MaterialFolder = "Assets/Environment/Level01";
    private const string Evidence = "E:/Unity/Verification/level01-redesign";

    private static Transform _root;
    private static Material _grass, _wood, _stone, _blue, _gold, _leaves, _warning, _safe, _gray, _roof, _plaster, _cloth, _carpet, _orange, _backing;
    private static FragileCargo _cargo;
    private static LevelMission _mission;
    private static readonly List<GameObject> StaticCandidates = new List<GameObject>();
    private static readonly List<Vector3> NoMarks = new List<Vector3>();

    // ---------------------------------------------------------------- layout (world space)
    private static readonly Vector3 Village = new Vector3(0, 0, 0), Plank = new Vector3(2, -.3f, 38), Climb = new Vector3(-5, .8f, 76),
        Hammer = new Vector3(1, .8f, 112), Branch = new Vector3(-30, .3f, 114), Gate = new Vector3(5, 1.6f, 150),
        Puzzle = new Vector3(16, 2.2f, 192), Gauntlet = new Vector3(4, 1.4f, 232), Castle = new Vector3(0, .4f, 274);
    private static readonly Vector2 VillageR = new Vector2(16, 14), PlankR = new Vector2(8, 8), ClimbR = new Vector2(11, 11),
        HammerR = new Vector2(8, 8), BranchR = new Vector2(7, 7), GateR = new Vector2(12, 11), PuzzleR = new Vector2(14, 12),
        GauntletR = new Vector2(8, 8), CastleR = new Vector2(18, 16);

    [MenuItem("Tools/Expedition/Build Level01 Teaching Route v9")]
    public static void Menu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building Level01.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SceneManager.SetActiveScene(scene);
        Directory.CreateDirectory(Evidence);
        string before = Evidence + "/Level01.before-builder.unity";
        if (!File.Exists(before)) File.Copy(ScenePath, before);

        var previous = FindRoot(scene, RootName);
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
        StaticCandidates.Clear(); NoMarks.Clear();

        var old = FindRoot(scene, "FloatingCoopRoute");
        if (old != null) old.SetActive(false);
        var legacy = FindRoot(scene, "PreviousObstacleTest_Preserved");
        if (legacy != null) legacy.SetActive(false);

        _root = new GameObject(RootName).transform;
        SceneManager.MoveGameObjectToScene(_root.gameObject, scene);
        FloatingCoopLevelBuilder.UseExistingAssets(_root);
        LoadMaterials();

        var cargoRoot = FindRoot(scene, "WoodenCrateItem") ?? throw new InvalidOperationException("Level01 needs WoodenCrateItem.");
        _cargo = cargoRoot.GetComponent<FragileCargo>();
        _mission = FindRoot(scene, "LevelMission")?.GetComponent<LevelMission>();

        string[] names = { "01_Village", "02_PlankIslet", "03_ClimbIsland", "04_HammerRest", "05_RepairBranch", "06_GateIsland", "07_PuzzleIsland", "08_Gauntlet", "09_Castle" };
        Vector3[] centers = { Village, Plank, Climb, Hammer, Branch, Gate, Puzzle, Gauntlet, Castle };
        Vector2[] radii = { VillageR, PlankR, ClimbR, HammerR, BranchR, GateR, PuzzleR, GauntletR, CastleR };
        for (int i = 0; i < names.Length; i++) FloatingCoopLevelBuilder.Island("L1v9_" + names[i], centers[i], radii[i], 301 + i * 17);
        Physics.SyncTransforms();

        // ---- bridges (generated first so the route reads end to end)
        BridgeOf("L1_Bridge_VillageToPlank", Village, VillageR, Plank, PlankR, 4.8f, true);
        BridgeOf("L1_Bridge_NarrowPlank", Plank, PlankR, Climb, ClimbR, 2.4f, false);
        BridgeOf("L1_Bridge_HammerBridge", Climb, ClimbR, Hammer, HammerR, 4.8f, true);
        BridgeOf("L1_Bridge_HammerToGate", Hammer, HammerR, Gate, GateR, 4.8f, true);
        BridgeOf("L1_Bridge_GateToPuzzle", Gate, GateR, Puzzle, PuzzleR, 4.8f, true);
        var puzzleExit = PuzzleExitBridge();
        BridgeOf("L1_Bridge_GauntletToCastle", Gauntlet, GauntletR, Castle, CastleR, 4.8f, true);

        // ---- islands
        BuildVillage(scene, cargoRoot);
        BuildPlankIslet();
        BuildClimbIsland();
        BuildHammerRest();
        BuildBranch();
        BuildGateIsland();
        BuildPuzzleIsland(puzzleExit);
        BuildGauntlet();
        BuildCastle(scene);

        // ---- shared systems
        WireSystems(scene);

        // ---- route markers last (needs colliders in place)
        Physics.SyncTransforms();
        int marks = BuildRouteMarks();
        ApplyStaticBatching();

        if (_mission != null)
        {
            var data = new SerializedObject(_mission);
            var objective = data.FindProperty("_objective");
            if (objective != null) objective.stringValue = "ขนลังไปส่งที่ปราสาท";
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return $"{ScenePath}: {names.Length} islands, route markers {marks}, old route parked inactive. Backup: {before}";
    }

    // ---------------------------------------------------------------- materials
    private static void LoadMaterials()
    {
        const string shared = "Assets/Environment/FloatingCoop/";
        _grass = Load(shared + "Grass.mat"); _wood = Load(shared + "BridgeWood.mat"); _stone = Load(shared + "Stone.mat");
        _blue = Load(shared + "RelayBlue.mat"); _gold = Load(shared + "GoalGold.mat"); _leaves = Load(shared + "Leaves.mat");
        _warning = Load(shared + "TrapWarning.mat"); _gray = Load(shared + "InactivePressurePad.mat");
        _safe = AssetDatabase.LoadAssetAtPath<Material>(shared + "TrapSafe.mat");
        _roof = Load("Assets/Environment/Level02/Roof.mat"); _plaster = Load("Assets/Environment/Level02/Plaster.mat");
        _cloth = Load("Assets/Environment/Level02/StallCloth.mat"); _carpet = Load("Assets/Environment/Level02/RoyalCarpet.mat");
        if (!AssetDatabase.IsValidFolder("Assets/Environment")) AssetDatabase.CreateFolder("Assets", "Environment");
        if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Environment", "Level01");
        _orange = Mat("RouteOrange", new Color(1f, .5f, .08f));
        _backing = FindBacking();
    }

    private static Material FindBacking()
    {
        foreach (string guid in AssetDatabase.FindAssets("ThaiSignBacking t:Material"))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null) return m;
        }
        return Mat("SignBacking", new Color(.12f, .09f, .08f));
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

    // ---------------------------------------------------------------- helpers
    private static Vector3 Dir(Vector3 from, Vector3 to) { Vector3 d = to - from; d.y = 0; return d.normalized; }

    private static Transform Group(string name, Transform parent, Vector3 world, Quaternion rotation)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(world, rotation);
        return go.transform;
    }

    private static GameObject Box(string name, Transform parent, Vector3 local, Vector3 size, Material material, bool solid = true, bool batch = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = local; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        if (batch) StaticCandidates.Add(go);
        return go;
    }

    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 local, Vector3 size, Material material, bool solid)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name; go.transform.SetParent(parent, false);
        go.transform.localPosition = local; go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        StaticCandidates.Add(go);
        return go;
    }

    private static void SetupBody(Rigidbody rb)
    {
        rb.isKinematic = true; rb.useGravity = false;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }

    private static void Ref(SerializedObject so, string name, UnityEngine.Object value)
    {
        var p = so.FindProperty(name) ?? throw new InvalidOperationException("Missing serialized field " + name + " on " + so.targetObject.GetType().Name);
        p.objectReferenceValue = value;
    }

    private static void Refs(SerializedObject so, string name, UnityEngine.Object[] values)
    {
        var p = so.FindProperty(name) ?? throw new InvalidOperationException("Missing serialized field " + name);
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (var go in scene.GetRootGameObjects()) if (go.name == name) return go;
        return null;
    }

    /// <summary>Thai sign on a dark plate. <paramref name="reader"/> is the direction a reader looks while facing it.</summary>
    private static TextMesh Sign(string name, Vector3 world, string text, Vector3 reader, float size = .045f, float height = 2.1f)
    {
        // A sign on a post at the side of the route: the plate sits 'height' above the ground under world.xz.
        float ground = GroundAt(world);
        var label = Label(name, new Vector3(world.x, ground + height, world.z), text, reader, size);
        int lines = text.Split('\n').Length;
        float plateHeight = lines * .82f * size / .075f + .45f;
        float postLength = height - plateHeight * .5f;
        var post = Box("Post", label.transform, new Vector3(0, -(height + plateHeight * .5f) * .5f, .14f), new Vector3(.16f, postLength, .16f), _wood, false);
        post.transform.localRotation = Quaternion.identity;
        return label;
    }

    /// <summary>Text on a dark plate with no post (hung from a structure, or a dynamic status label).</summary>
    private static TextMesh Label(string name, Vector3 world, string text, Vector3 reader, float size = .045f)
    {
        var label = FloatingCoopLevelBuilder.Sign(name, world, text, 0);
        // Same world size as fontSize 56 / characterSize 'size' but twice the glyph resolution (less shimmer at range).
        label.fontSize = 112;
        label.characterSize = size * 56f / 112f;
        label.transform.rotation = Quaternion.LookRotation(reader.sqrMagnitude > .001f ? reader : Vector3.forward);
        string[] lines = text.Split('\n');
        int longest = 0; foreach (var l in lines) longest = Mathf.Max(longest, VisibleChars(l));
        float scale = size / .075f;
        var plate = Box(name + "_Plate", label.transform, new Vector3(0, 0, .06f), new Vector3((longest * .34f + .9f) * scale, lines.Length * .82f * scale + .45f, .08f), _backing, false);
        plate.transform.localRotation = Quaternion.identity;
        return label;
    }

    private static float GroundAt(Vector3 p)
    {
        if (Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore)) return hit.point.y;
        Debug.LogWarning("[Level01RedesignBuilder] no ground under sign/pad at " + p + "; using nearest island height.");
        return GroundY(p);
    }

    // Thai vowel/tone marks sit above or below a letter and add no width.
    private static int VisibleChars(string line)
    {
        int n = 0;
        foreach (char c in line)
            if (!(c == '\u0E31' || (c >= '\u0E34' && c <= '\u0E3A') || (c >= '\u0E47' && c <= '\u0E4E'))) n++;
        return n;
    }

    // ---------------------------------------------------------------- bridges
    private static float Radius(Vector3 d, Vector2 r) => 1f / Mathf.Sqrt(d.x * d.x / (r.x * r.x) + d.z * d.z / (r.y * r.y));

    private static Transform BridgeOf(string name, Vector3 a, Vector2 ar, Vector3 b, Vector2 br, float width, bool rails)
    {
        Vector3 d = Dir(a, b);
        Vector3 from = a + d * (Radius(d, ar) * .88f - .8f), to = b - d * (Radius(d, br) * .88f - .8f);
        from.y = a.y; to.y = b.y;
        return BridgeSpan(name, from, to, width, rails);
    }

    private static Transform BridgeSpan(string name, Vector3 from, Vector3 to, float width, bool rails)
    {
        var bridge = new GameObject(name).transform;
        bridge.SetParent(_root, false);
        Quaternion rot = Quaternion.LookRotation(to - from);
        bridge.SetPositionAndRotation((from + to) * .5f, rot);
        float length = Vector3.Distance(from, to);
        Box("Deck", bridge, new Vector3(0, -.2f, 0), new Vector3(width, .4f, length), _wood);
        if (rails)
            for (int side = -1; side <= 1; side += 2)
                Box("Rail", bridge, new Vector3(side * (width * .5f + .1f), .35f, 0), new Vector3(.2f, .7f, length), _wood);
        // posts every ~6 m give the span a readable rhythm; they carry no collider
        int count = Mathf.Max(2, Mathf.RoundToInt(length / 6f));
        for (int i = 0; i <= count; i++)
        {
            float z = Mathf.Lerp(-length * .5f, length * .5f, (float)i / count);
            for (int side = -1; side <= 1; side += 2)
                Box("Post", bridge, new Vector3(side * (width * .5f + .1f), rails ? .9f : .35f, z), new Vector3(.24f, rails ? 1.8f : .7f, .24f), _wood, false);
        }
        return bridge;
    }

    private static (Transform near, Transform far, Vector3 from, Vector3 gapA, Vector3 gapB) PuzzleExitBridge()
    {
        Vector3 d = Dir(Puzzle, Gauntlet);
        Vector3 from = Puzzle + d * (Radius(d, PuzzleR) * .88f - .8f), to = Gauntlet - d * (Radius(d, GauntletR) * .88f - .8f);
        from.y = Puzzle.y; to.y = Gauntlet.y;
        float total = Vector3.Distance(from, to);
        const float gap = 9f;
        float near = (total - gap) * .4f;                        // short fixed stub, then the 9 m chasm, then the far span
        Vector3 along = (to - from).normalized;
        Vector3 gapA = from + along * near, gapB = gapA + along * gap;
        var a = BridgeSpan("L1_Bridge_PuzzleExitNear", from, gapA, 4.8f, true);
        var b = BridgeSpan("L1_Bridge_PuzzleExitFar", gapB, to, 4.8f, true);
        return (a, b, from, gapA, gapB);
    }

    // ---------------------------------------------------------------- island 1: village (carry, place, throw)
    private static void BuildVillage(Scene scene, GameObject cargoRoot)
    {
        Vector3 route = Dir(Village, Plank);
        var start = FindRoot(scene, "PlayerStart")?.transform ?? new GameObject("PlayerStart").transform;
        start.SetPositionAndRotation(new Vector3(0, 1.05f, -9.5f), Quaternion.LookRotation(Vector3.forward));
        cargoRoot.transform.SetPositionAndRotation(new Vector3(0, 1.3f, -4.2f), Quaternion.LookRotation(Vector3.forward));

        var village = Group("L1_VillageDecor", _root, Village, Quaternion.identity);
        House(village, "House_A", new Vector3(-11, 0, 4), 25, new Vector2(4.5f, 4f), 3.2f);
        House(village, "House_B", new Vector3(10, 0, -6), -30, new Vector2(4f, 3.6f), 2.8f);
        Well(village, new Vector3(8, 0, 6));
        for (int i = 0; i < 3; i++) Box("HayBale", village, new Vector3(-12 + i * 1.3f, .45f, -8), new Vector3(1.2f, .9f, 1f), _gold);
        FloatingCoopLevelBuilder.Flag("L1_VillageFlag", Village + new Vector3(-13, 0, -2), _blue);
        FloatingCoopLevelBuilder.Tree("L1_VillageTree0", Village + new Vector3(12, 0, 9));
        FloatingCoopLevelBuilder.Tree("L1_VillageTree1", Village + new Vector3(-8, 0, 10));

        Vector3 read = Vector3.forward;
        Sign("L1_Sign_Welcome", new Vector3(-6.5f, 0, -6.5f), "ยินดีต้อนรับ!\nขนลังไปส่งที่ปราสาท", read, .055f);
        Sign("L1_Sign_Lift", new Vector3(5.5f, 0, -6.5f), "คลิกซ้าย = ยกลัง\nเดินเข้าไปชิดลังก่อน", read);
        Sign("L1_Sign_Place", new Vector3(5.8f, 0, 3.5f), "คลิกขวา = วางลังเบา ๆ\nวางบนแท่นทอง = เซฟจุดเช็คพอยต์", read);
        Sign("L1_Sign_Throw", new Vector3(-6.8f, 0, 8f), "Q = โยน (กดค้างเพื่อชาร์จแรง)\nอย่าโยนลังแตกง่าย เสียเลือดเยอะ!", read);
        NoMarks.Add(new Vector3(0, 0, 9));
        MakePad(1, new Vector3(0, 0, 9), route);
        FloatingCoopLevelBuilder.Parking("L1_StartCargoPad", new Vector3(0, 0, -4.2f), 3.6f);
    }

    private static void House(Transform parent, string name, Vector3 local, float yaw, Vector2 footprint, float height)
    {
        var house = Group(name, parent, parent.TransformPoint(local), Quaternion.Euler(0, yaw, 0));
        Box("Walls", house, new Vector3(0, height * .5f, 0), new Vector3(footprint.x, height, footprint.y), _plaster);
        for (int side = -1; side <= 1; side += 2)
        {
            var slab = Box("Roof", house, new Vector3(side * footprint.x * .25f, height + footprint.x * .2f, 0), new Vector3(footprint.x * .62f, .25f, footprint.y + .6f), _roof);
            slab.transform.localRotation = Quaternion.Euler(0, 0, -side * 35f);
        }
        Box("Door", house, new Vector3(0, .9f, -footprint.y * .5f - .04f), new Vector3(1f, 1.8f, .08f), _wood, false);
    }

    private static void Well(Transform parent, Vector3 local)
    {
        var well = Group("Well", parent, parent.TransformPoint(local), Quaternion.identity);
        Primitive(PrimitiveType.Cylinder, "WellRing", well, new Vector3(0, .45f, 0), new Vector3(2f, .45f, 2f), _stone, true);
        for (int side = -1; side <= 1; side += 2) Box("WellPost", well, new Vector3(side * .9f, 1.3f, 0), new Vector3(.15f, 1.7f, .15f), _wood, false);
        Box("WellRoof", well, new Vector3(0, 2.25f, 0), new Vector3(2.4f, .15f, 1.4f), _roof, false);
    }

    // ---------------------------------------------------------------- island 2: plank (balance)
    private static void BuildPlankIslet()
    {
        Vector3 route = Dir(Plank, Climb);
        MakePad(2, Plank + new Vector3(-.4f, 0, -2.5f), route);
        FloatingCoopLevelBuilder.Flag("L1_PlankFlag", Plank + new Vector3(5.5f, 0, 1), _blue);
        FloatingCoopLevelBuilder.Tree("L1_PlankTree", Plank + new Vector3(-5.5f, 0, 2.5f));
        Sign("L1_Sign_Plank", Plank + route * 3.5f + Vector3.Cross(Vector3.up, route) * -5f, "สะพานแคบ! เดินตรง ๆ\nลังตกเหว = กลับมาที่แท่นเช็คพอยต์\n(เลือดลังไม่ฟื้น)", route, .045f);
    }

    // ---------------------------------------------------------------- island 3: climb switch
    private static void BuildClimbIsland()
    {
        Vector3 route = Dir(Climb, Hammer);
        MakePad(3, Climb + new Vector3(0, 0, -6.5f), route);
        FloatingCoopLevelBuilder.Flag("L1_ClimbFlag", Climb + new Vector3(6.5f, 0, -7), _blue);
        ClimbGate("L1_ClimbGate", Climb + new Vector3(0, 0, 1.5f), route);
        Sign("L1_Sign_Climb", Climb + new Vector3(-7.5f, 0, -5f), "วางลังบนแท่นเช็คพอยต์ก่อน!\nถือลังปีนไม่ได้\nปีนหอไปกดปุ่มฟ้า (กด E)", route, .045f);
    }

    private static void ClimbGate(string name, Vector3 at, Vector3 travel)
    {
        var t = Group(name, _root, at, Quaternion.LookRotation(travel));
        var gate = t.gameObject.AddComponent<ClimbSwitchGate>();
        var barrier = Box("LockedPassage", t, new Vector3(0, 2f, 3f), new Vector3(5.6f, 4f, .5f), _wood, true, false);
        for (int side = -1; side <= 1; side += 2)
            Box("GateWall", t, new Vector3(side * 7.1f, 2f, 3f), new Vector3(8.6f, 4f, .8f), _stone);
        const float height = 6f; float step = height / 4f;
        var climb = Group("ClimbTower", t, t.TransformPoint(new Vector3(-5.2f, 0, -1.5f)), t.rotation);
        for (int j = 0; j < 4; j++)
        {
            float x = j % 2 == 0 ? -1.1f : 1.1f;
            var wall = Box("ClimbStage_" + j, climb, new Vector3(x, step * (j + .5f), 0), new Vector3(3.6f, step, 1.2f), _stone); wall.layer = 3;
            Box("ClimbStripe", climb, new Vector3(x, step * (j + .5f), -.615f), new Vector3(.25f, step - .2f, .025f), _blue, false);
            var ledge = Box(j == 3 ? "TopSwitchLanding" : "RestLedge_" + j, climb, new Vector3(x, step * (j + 1) - .15f, -1.3f), new Vector3(3.6f, .3f, 2.6f), _wood); ledge.layer = 3;
        }
        var button = Box("HighButton", climb, new Vector3(1.1f, height + .35f, -.7f), new Vector3(1f, .65f, .25f), _blue, false, false);
        var label = Label(name + "_Label", t.TransformPoint(new Vector3(0, 4.6f, 2.5f)), "ปีนหอคอยไปกดปุ่มสีฟ้า\nเพื่อเปิดประตู", travel, .045f);
        var so = new SerializedObject(gate);
        Ref(so, "_button", button.transform); Ref(so, "_barrier", barrier); Ref(so, "_label", label);
        Ref(so, "_indicator", button.GetComponent<Renderer>()); Ref(so, "_mission", _mission);
        so.FindProperty("_interactionDistance").floatValue = 1.8f; so.FindProperty("_requiredHeightOffset").floatValue = .2f;
        so.ApplyModifiedPropertiesWithoutUndo();
        NoMarks.Add(t.TransformPoint(new Vector3(0, 0, 3f)));
    }

    // ---------------------------------------------------------------- island 4: hammer bridge exit, branch start
    private static void BuildHammerRest()
    {
        Vector3 route = Dir(Hammer, Gate);
        MakePad(4, Hammer + new Vector3(2.2f, 0, -3.2f), route);
        FloatingCoopLevelBuilder.Flag("L1_HammerRestFlag", Hammer + new Vector3(4.5f, 0, 3.5f), _blue);
        // The swing hammer stands in the middle of the bridge that leads here from the climb island.
        Vector3 back = Dir(Climb, Hammer);
        Vector3 mid = Vector3.Lerp(Climb, Hammer, .5f);
        mid.y = Mathf.Lerp(Climb.y, Hammer.y, .5f);
        SwingHammer("L1_SwingHammer_Bridge", mid, back, 0f);
        Vector3 west = Dir(Hammer, Branch);
        Sign("L1_Sign_Branch", Hammer + new Vector3(-4.8f, 3f, -1.5f), "ทางอ้อม: ชุดซ่อม +20 เลือด\nหินถล่ม! นำลังไปด้วย ★ ภารกิจเสริม", west);
        CollapsingStones("L1_CollapsingStones", Hammer, HammerR, Branch, BranchR);
    }

    // ---------------------------------------------------------------- branch island: repair kit
    private static void BuildBranch()
    {
        var kitRoot = Group("L1_RepairKit", _root, Branch + new Vector3(-2f, 0, 0), Quaternion.identity);
        var visual = Group("KitVisual", kitRoot, kitRoot.position, Quaternion.identity);
        Box("KitBox", visual, new Vector3(0, .5f, 0), new Vector3(1.1f, .8f, .8f), _gold, false, false);
        Box("KitCrossH", visual, new Vector3(0, .93f, 0), new Vector3(.8f, .06f, .2f), _blue, false, false);
        Box("KitCrossV", visual, new Vector3(0, .93f, 0), new Vector3(.2f, .06f, .8f), _blue, false, false);
        visual.localRotation = Quaternion.identity;
        var kit = kitRoot.gameObject.AddComponent<RepairKit>();
        var label = Sign("L1_Sign_Kit", kitRoot.position + new Vector3(0, 0, 0), "ชุดซ่อม +20 เลือดลัง (ใช้ได้ครั้งเดียว)\nนำลังมาใกล้ ๆ แล้วเดินเข้าไปหา", Dir(Hammer, Branch), .045f);
        var so = new SerializedObject(kit);
        Ref(so, "_cargo", _cargo); Ref(so, "_visual", visual.gameObject); Ref(so, "_label", label);
        so.ApplyModifiedPropertiesWithoutUndo();
        FloatingCoopLevelBuilder.Flag("L1_BranchFlag", Branch + new Vector3(3, 0, 3), _gold);
        FloatingCoopLevelBuilder.Tree("L1_BranchTree0", Branch + new Vector3(-3.5f, 0, 3));
        FloatingCoopLevelBuilder.Parking("L1_BranchCargoPad", Branch + new Vector3(2f, 0, -1f), 3.6f);
    }

    // ---------------------------------------------------------------- island 5: two-player pad gate
    private static void BuildGateIsland()
    {
        Vector3 route = Dir(Gate, Puzzle);
        Vector3 right = Vector3.Cross(Vector3.up, route);
        MakePad(5, Gate + route * -5.5f, route);
        FloatingCoopLevelBuilder.Flag("L1_GateFlag", Gate + new Vector3(-7, 0, -4), _blue);
        var root = Group("L1_PressureGate", _root, Gate + route * 3f, Quaternion.LookRotation(route));
        var gate = root.gameObject.AddComponent<CoopPressureGate>();
        var blocker = Box("Portcullis", root, new Vector3(0, 2.2f, 0), new Vector3(6f, 4.4f, .6f), _wood, true, false);
        for (int side = -1; side <= 1; side += 2)
            Box("GateWall", root, new Vector3(side * 7.7f, 2.2f, 0), new Vector3(9.4f, 4.4f, .9f), _stone);
        var open = Box("OpenArch", root, new Vector3(0, 4.6f, 0), new Vector3(6.4f, .35f, .5f), _gold, false, false);
        open.SetActive(false);
        Vector3 leftPos = right * -4.5f + route * -5f, rightPos = right * 4.5f + route * -5f;
        var leftVisible = Box("PadLeft_Visible", root, root.InverseTransformVector(leftPos) + Vector3.up * .03f, new Vector3(3f, .06f, 3f), _blue, false, false);
        var rightVisible = Box("PadRight_Visible", root, root.InverseTransformVector(rightPos) + Vector3.up * .03f, new Vector3(3f, .06f, 3f), _blue, false, false);
        var leftPad = PadVolume("PadLeft_Occupancy", root, leftVisible.transform.localPosition);
        var rightPad = PadVolume("PadRight_Occupancy", root, rightVisible.transform.localPosition);
        var status = Label("L1_GateStatus", root.TransformPoint(new Vector3(0, 5.4f, -.8f)), "ประตู 2 คน", route, .045f);
        status.transform.rotation = Quaternion.LookRotation(route);
        var sign = Sign("L1_Sign_Gate", root.TransformPoint(new Vector3(-6.8f, 0, -9.5f)),
            "ประตู 2 คน: เหยียบแผ่นฟ้า 'ทั้งสอง' แผ่น\nโหมดสาธิตเล่นคนเดียว:\nแผ่นเทาไม่ต้องใช้ ยืนแผ่นฟ้าแผ่นเดียวพอ", route, .045f);
        var so = new SerializedObject(gate);
        Ref(so, "_leftPad", leftPad); Ref(so, "_rightPad", rightPad); Ref(so, "_blocker", blocker);
        Ref(so, "_leftIndicator", leftVisible.GetComponent<Renderer>()); Ref(so, "_rightIndicator", rightVisible.GetComponent<Renderer>());
        Ref(so, "_idleMaterial", _blue); Ref(so, "_activeMaterial", _gold); Ref(so, "_disabledMaterial", _gray);
        Ref(so, "_openIndicator", open); Ref(so, "_statusText", status);
        so.FindProperty("_holdSeconds").floatValue = 1.5f;
        so.ApplyModifiedPropertiesWithoutUndo();
        NoMarks.Add(root.position);
    }

    private static BoxCollider PadVolume(string name, Transform parent, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true; box.center = new Vector3(0, .5f, 0); box.size = new Vector3(3f, 2f, 3f);
        return box;
    }

    // ---------------------------------------------------------------- island 6: place the crate, swap roles
    private static void BuildPuzzleIsland((Transform near, Transform far, Vector3 from, Vector3 gapA, Vector3 gapB) exit)
    {
        Vector3 route = Dir(Puzzle, Gauntlet);
        Vector3 right = Vector3.Cross(Vector3.up, route);
        Vector3 padAt = Puzzle + route * -4f + right * 4f;
        MakePad(6, padAt, route);
        FloatingCoopLevelBuilder.Flag("L1_PuzzleFlag", Puzzle + new Vector3(-9, 0, -5), _blue);
        FloatingCoopLevelBuilder.Tree("L1_PuzzleTree", Puzzle + new Vector3(10, 0, 4));

        // the chasm: a retractable bridge section that appears when the plate is weighted down
        Vector3 along = (exit.gapB - exit.gapA).normalized;
        var gapBridge = BridgeSpan("L1_Bridge_PuzzleGap", exit.gapA - along * .6f, exit.gapB + along * .6f, 4.8f, true);
        gapBridge.SetParent(_root, true);

        // weight plate on the island edge just before the chasm
        Vector3 plateAt = exit.from - along * 3.2f + right * -3.4f; plateAt.y = Puzzle.y;
        var plateRoot = Group("L1_WeightPlate", _root, plateAt, Quaternion.LookRotation(route));
        var plate = plateRoot.gameObject.AddComponent<WeightPlate>();
        var slab = Box("PlateSlab", plateRoot, new Vector3(0, .04f, 0), new Vector3(3.4f, .08f, 3.4f), _blue, false, false);
        Box("PlateRim", plateRoot, new Vector3(0, .03f, 0), new Vector3(3.8f, .05f, 3.8f), _stone, false, false);
        var plateLabel = Label("L1_Label_Plate", plateRoot.position + new Vector3(0, 2.4f, 0), "วางหินหนักบนแผ่นนี้\nสะพานจะยื่นออกมา", route, .045f);
        gapBridge.gameObject.SetActive(false);

        // heavy stone (RequiresTwoCarriers): two players carry it, a solo player drags it slowly
        Vector3 stoneAt = Puzzle + right * -8f + route * -3f; stoneAt.y = Puzzle.y + .8f;
        var stone = MakeStone("L1_HeavyStone", stoneAt, Quaternion.LookRotation(route));
        FloatingCoopLevelBuilder.Parking("L1_StoneSpot", new Vector3(stoneAt.x, Puzzle.y, stoneAt.z), 3.6f);

        var so = new SerializedObject(plate);
        Ref(so, "_object", stone); Ref(so, "_bridge", gapBridge.gameObject);
        Refs(so, "_lamps", new UnityEngine.Object[] { slab.GetComponent<Renderer>() });
        Ref(so, "_idleMaterial", _blue); Ref(so, "_pressedMaterial", _gold); Ref(so, "_label", plateLabel);
        so.ApplyModifiedPropertiesWithoutUndo();

        Sign("L1_Sign_Puzzle", Puzzle + new Vector3(-6.5f, 0, -9f),
            "ปริศนา: ทางขาด!\n1) วางลังบนแท่นเช็คพอยต์ก่อน\n2) ยกหินหนักไปวางบนแผ่นฟ้า (2 คนยกพร้อมกัน)\nโหมดสาธิตเล่นคนเดียว: จับหินแล้วลากช้า ๆ", route, .045f);
        NoMarks.Add(plateAt);
    }

    private static CarryableObject MakeStone(string name, Vector3 world, Quaternion rotation)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.SetParent(_root, false);
        go.transform.SetPositionAndRotation(world, rotation);
        go.transform.localScale = new Vector3(1.9f, 1.4f, 1.9f);
        go.GetComponent<Renderer>().sharedMaterial = _stone;
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 12f; rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        go.AddComponent<NetworkObject>();
        var crateTransform = _cargo.GetComponent<NetworkTransform>();
        if (crateTransform != null && UnityEditorInternal.ComponentUtility.CopyComponent(crateTransform))
            UnityEditorInternal.ComponentUtility.PasteComponentAsNew(go);
        else go.AddComponent<NetworkTransform>();
        var carryable = go.AddComponent<CarryableObject>();
        carryable.RequiresTwoCarriers = true;
        go.AddComponent<CarryableHome>();
        return carryable;
    }

    // ---------------------------------------------------------------- island 7: spike gauntlet
    private static void BuildGauntlet()
    {
        Vector3 route = Dir(Gauntlet, Castle);
        FloatingCoopLevelBuilder.Flag("L1_GauntletFlag", Gauntlet + new Vector3(5, 0, -4), _blue);
        SpikeTrap("L1_SpikeTrap_A", Gauntlet + route * -2f, route, 0f);
        SpikeTrap("L1_SpikeTrap_B", Gauntlet + route * 3.2f, route, 3.4f);
        Sign("L1_Sign_Spikes", Gauntlet + new Vector3(-4.2f, 0, -4.2f), "หนามขึ้นเป็นจังหวะ\nรอแผ่นเขียว/ส้ม แล้วข้ามตอนหนามลง", route);
        // second swing hammer guards the last bridge to the castle
        Vector3 mid = Vector3.Lerp(Gauntlet, Castle, .55f);
        mid.y = Mathf.Lerp(Gauntlet.y, Castle.y, .55f);
        SwingHammer("L1_SwingHammer_Final", mid, route, 1.7f);
    }

    // ---------------------------------------------------------------- island 8: castle
    private static void BuildCastle(Scene scene)
    {
        Vector3 toEntrance = Dir(Castle, Gauntlet);
        var castle = Group("L1_Castle", _root, Castle, Quaternion.LookRotation(-toEntrance));
        const float half = 8f, height = 4.2f, thick = 1.1f, gateHalf = 3.4f;
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
        Box("Carpet", castle, new Vector3(0, .02f, -2.5f), new Vector3(2.4f, .02f, 11f), _carpet, false);
        FloatingCoopLevelBuilder.Flag("L1_CastleFlagL", castle.TransformPoint(new Vector3(-half, height * 2 + 1f, -half)), _gold);
        FloatingCoopLevelBuilder.Flag("L1_CastleFlagR", castle.TransformPoint(new Vector3(half, height * 2 + 1f, -half)), _gold);

        var delivery = FindRoot(scene, "DeliveryZone") ?? throw new InvalidOperationException("Level01 needs DeliveryZone.");
        Vector3 goal = castle.TransformPoint(new Vector3(0, 0, -.5f));
        delivery.transform.position = goal + Vector3.up * .04f;
        FloatingCoopLevelBuilder.Parking("L1_DeliveryLanding", goal, 6f);
        NoMarks.Add(goal);

        var portal = FindRoot(scene, "DeliveryExitPortal_Level02");
        if (portal != null)
        {
            // In the courtyard against the keep, behind the delivery ring: deliver first, then step through the portal.
            portal.transform.SetPositionAndRotation(castle.TransformPoint(new Vector3(0, 0, 5.8f)) + Vector3.up * .02f, castle.rotation);
        }
        Sign("L1_Sign_Finish", castle.TransformPoint(new Vector3(-6.2f, 0, -9.3f)), "วางลังในวงกลมสีแดง\nสำเร็จแล้ววงกลมจะเป็นสีเขียว", castle.forward);
    }

    // ---------------------------------------------------------------- hazards (same components Level02 uses)
    private static void SpikeTrap(string name, Vector3 at, Vector3 travel, float phaseOffset)
    {
        var t = Group(name, _root, at, Quaternion.LookRotation(travel));
        var trap = t.gameObject.AddComponent<TimedCargoTrap>();
        var plate = Box("SpikePlate", t, new Vector3(0, .03f, 0), new Vector3(5.2f, .04f, 2.6f), _warning, false, false);
        var zone = Group("DamageZone", t, at, t.rotation).gameObject;
        var box = zone.AddComponent<BoxCollider>();
        box.isTrigger = true; box.center = Vector3.up * 1.3f; box.size = new Vector3(5.2f, 2.6f, 2.6f);
        var spikes = Group("Spikes", t, at, t.rotation);
        for (float x = -2.2f; x <= 2.21f; x += .55f)
            for (float z = -1f; z <= 1.01f; z += .5f)
            {
                var spike = Box("Spike", spikes, new Vector3(x, .5f, z), new Vector3(.16f, 1f, .16f), _stone, false, false);
                spike.transform.localRotation = Quaternion.Euler(0, 45, 0);
            }
        spikes.gameObject.SetActive(false);
        for (int side = -1; side <= 1; side += 2) Box("SpikeHousing", t, new Vector3(side * 2.9f, .25f, 0), new Vector3(.4f, .5f, 2.8f), _stone);
        var so = new SerializedObject(trap);
        Ref(so, "_zone", box); Ref(so, "_flames", spikes.gameObject); Ref(so, "_marker", plate.GetComponent<Renderer>()); Ref(so, "_cargo", _cargo);
        so.FindProperty("_safeSeconds").floatValue = 4.5f; so.FindProperty("_warningSeconds").floatValue = 1.4f;
        so.FindProperty("_activeSeconds").floatValue = 1.8f; so.FindProperty("_phaseOffset").floatValue = phaseOffset;
        so.FindProperty("_damage").intValue = 10;
        so.ApplyModifiedPropertiesWithoutUndo();
        NoMarks.Add(at);
    }

    private static void SwingHammer(string name, Vector3 at, Vector3 travel, float phaseOffset)
    {
        var t = Group(name, _root, at, Quaternion.LookRotation(travel));
        var hazard = t.gameObject.AddComponent<NetworkRouteHazard>();
        for (int side = -1; side <= 1; side += 2) Box("SweepPost", t, new Vector3(side * 5f, 2.9f, 0), new Vector3(.45f, 5.8f, .5f), _wood);
        Box("SweepBeam", t, new Vector3(0, 5.9f, 0), new Vector3(10.5f, .4f, .6f), _wood);
        var pivot = Group("SwingPivot", t, t.TransformPoint(Vector3.up * 5.6f), t.rotation);
        var rb = pivot.gameObject.AddComponent<Rigidbody>(); SetupBody(rb);
        Box("SwingArm", pivot, new Vector3(0, -2.1f, 0), new Vector3(.25f, 4.2f, .25f), _wood, false, false);
        var head = Box("SwingHead", pivot, new Vector3(0, -4.2f, 0), new Vector3(1.6f, 1.8f, 1.5f), _stone, true, false);
        pivot.localRotation = Quaternion.Euler(0, 0, 70f);
        var lamp = Box("WarningLamp", t, new Vector3(-4.4f, 2.2f, -2f), new Vector3(.5f, .7f, .5f), _warning, false, false);
        Box("SweepZone", t, new Vector3(0, .05f, 0), new Vector3(4.6f, .03f, 2f), _warning, false, false);
        var so = new SerializedObject(hazard);
        so.FindProperty("_kind").enumValueIndex = (int)NetworkRouteHazard.HazardKind.SwingHammer;
        Ref(so, "_primary", rb); Ref(so, "_hitA", head.GetComponent<BoxCollider>()); Ref(so, "_cargo", _cargo);
        Ref(so, "_indicator", lamp.GetComponent<Renderer>());
        so.FindProperty("_swingAngle").floatValue = 70f;
        so.FindProperty("_safeSeconds").floatValue = 4.5f; so.FindProperty("_warningSeconds").floatValue = 1.5f;
        so.FindProperty("_activeSeconds").floatValue = 3.5f; so.FindProperty("_phaseOffset").floatValue = phaseOffset;
        so.FindProperty("_damage").intValue = 10;
        so.ApplyModifiedPropertiesWithoutUndo();
        Label(name + "_Sign", t.TransformPoint(new Vector3(0, 4.45f, -1.3f)), "ค้อนแกว่ง! รอจังหวะ\nไฟเขียว = ข้ามได้", travel, .04f);
        NoMarks.Add(at);
    }

    private static void CollapsingStones(string name, Vector3 a, Vector2 ar, Vector3 b, Vector2 br)
    {
        Vector3 d = Dir(a, b);
        Vector3 from = a + d * (Radius(d, ar) * .9f), to = b - d * (Radius(d, br) * .9f);
        from.y = a.y; to.y = b.y;
        var group = Group(name, _root, (from + to) * .5f, Quaternion.LookRotation(d));
        const int count = 3; const float gap = .35f;
        float span = Vector3.Distance(new Vector3(from.x, 0, from.z), new Vector3(to.x, 0, to.z));
        float length = (span - gap * (count + 1)) / count;
        for (int i = 0; i < count; i++)
        {
            float along = gap + length * .5f + i * (length + gap);
            Vector3 p = Vector3.Lerp(from, to, along / span);
            var stoneRoot = Group("CollapsingStone_" + (i + 1), group, p, group.rotation);
            var stone = stoneRoot.gameObject.AddComponent<CollapsingSteppingStone>();
            var slab = Box("StoneSlab", stoneRoot, new Vector3(0, -.25f, 0), new Vector3(4.2f, .5f, length), _stone, true, false);
            var rb = slab.AddComponent<Rigidbody>(); SetupBody(rb);
            var sensor = stoneRoot.gameObject.AddComponent<BoxCollider>();
            sensor.isTrigger = true; sensor.center = new Vector3(0, .45f, 0); sensor.size = new Vector3(4f, .7f, length - .1f);
            var so = new SerializedObject(stone);
            Ref(so, "_rigidbody", rb); Ref(so, "_solidCollider", slab.GetComponent<Collider>()); Ref(so, "_stoneRenderer", slab.GetComponent<Renderer>());
            so.FindProperty("_warningSeconds").floatValue = 2.6f; so.FindProperty("_collapsedSeconds").floatValue = 3.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        Sign(name + "_Sign", from - d * 2.2f + Vector3.Cross(Vector3.up, d) * 3f, "หินถล่ม! เดินต่อเนื่อง ห้ามหยุด", d);
    }

    // ---------------------------------------------------------------- checkpoint pad
    private static CheckpointPad MakePad(int number, Vector3 groundPoint, Vector3 route)
    {
        Vector3 p = groundPoint;
        if (Physics.Raycast(new Vector3(p.x, 40f, p.z), Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) p.y = hit.point.y;
        else p.y = GroundY(p);
        var root = Group("L1_Checkpoint_" + number, _root, p, Quaternion.LookRotation(route));
        var pad = root.gameObject.AddComponent<CheckpointPad>();
        var slab = Box("PadSlab", root, new Vector3(0, .04f, 0), new Vector3(4.6f, .08f, 4.6f), _gray, false, false);
        var lamps = new List<UnityEngine.Object> { slab.GetComponent<Renderer>() };
        for (int cx = -1; cx <= 1; cx += 2)
            for (int cz = -1; cz <= 1; cz += 2)
            {
                var lamp = Box("PadLamp", root, new Vector3(cx * 2.05f, .35f, cz * 2.05f), new Vector3(.3f, .7f, .3f), _gray, false, false);
                lamps.Add(lamp.GetComponent<Renderer>());
            }
        Box("FlagPole", root, new Vector3(-2.3f, 1.5f, 2.3f), new Vector3(.1f, 3f, .1f), _wood, false, false);
        var cloth = Box("FlagCloth", root, new Vector3(-1.75f, .5f, 2.3f), new Vector3(1.1f, .7f, .05f), _gray, false, false);
        lamps.Add(cloth.GetComponent<Renderer>());
        var cargoPoint = Group("CargoPoint", root, p + Vector3.up * .9f, root.rotation);
        var playerPoint = Group("PlayerPoint", root, p + root.rotation * new Vector3(0, .15f, -3.4f), root.rotation);
        var label = Label("PadLabel", p + Vector3.up * 2.7f, "เช็คพอยต์", route, .036f);
        label.transform.SetParent(root, true);
        label.transform.rotation = Quaternion.LookRotation(route);
        var so = new SerializedObject(pad);
        Ref(so, "_cargo", _cargo);
        so.FindProperty("_number").intValue = number;
        Ref(so, "_cargoPoint", cargoPoint); Ref(so, "_playerPoint", playerPoint);
        Refs(so, "_lamps", lamps.ToArray());
        Ref(so, "_inactiveMaterial", _gray); Ref(so, "_activeMaterial", _gold);
        Ref(so, "_flag", cloth.transform); so.FindProperty("_flagDownY").floatValue = .5f; so.FindProperty("_flagUpY").floatValue = 2.5f;
        Ref(so, "_label", label);
        so.ApplyModifiedPropertiesWithoutUndo();
        NoMarks.Add(p);
        return pad;
    }

    private static float GroundY(Vector3 p)
    {
        Vector3[] centers = { Village, Plank, Climb, Hammer, Branch, Gate, Puzzle, Gauntlet, Castle };
        float best = float.MaxValue, y = 0;
        foreach (var c in centers) { float d = (new Vector2(c.x, c.z) - new Vector2(p.x, p.z)).sqrMagnitude; if (d < best) { best = d; y = c.y; } }
        return y;
    }

    // ---------------------------------------------------------------- systems
    private static void WireSystems(Scene scene)
    {
        var pads = _root.GetComponentsInChildren<CheckpointPad>(true);
        Array.Sort(pads, (a, b) => a.Number.CompareTo(b.Number));
        var systems = new GameObject("L1_Systems").transform;
        systems.SetParent(_root, false);
        var managerGo = new GameObject("CheckpointManager");
        managerGo.transform.SetParent(systems, false);
        var manager = managerGo.AddComponent<CheckpointManager>();
        var ms = new SerializedObject(manager);
        Refs(ms, "_pads", pads);
        ms.ApplyModifiedPropertiesWithoutUndo();

        var runGo = new GameObject("LevelRunState");
        runGo.transform.SetParent(systems, false);
        var run = runGo.AddComponent<LevelRunState>();
        var start = FindRoot(scene, "PlayerStart");
        var delivery = FindRoot(scene, "DeliveryZone")?.GetComponent<DeliveryPoint>();
        var branchMark = new GameObject("BranchIslandTrigger").transform;
        branchMark.SetParent(systems, false);
        branchMark.position = Branch;
        var rs = new SerializedObject(run);
        Ref(rs, "_cargo", _cargo); Ref(rs, "_delivery", delivery);
        Ref(rs, "_startPoint", start != null ? start.transform : null); Ref(rs, "_branchPoint", branchMark);
        rs.FindProperty("_startRadius").floatValue = 21f;
        rs.FindProperty("_branchRadius").floatValue = 8f;
        rs.FindProperty("_sideLimitSeconds").floatValue = 270f;
        rs.FindProperty("_bonusCoins").intValue = 50;
        rs.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- route markers (orange)
    private static int BuildRouteMarks()
    {
        Vector3[] route = {
            new Vector3(0, 0, -6), Village + new Vector3(0, 0, 11), Plank + new Vector3(0, 0, -6), Plank + new Vector3(0, 0, 0), Climb + new Vector3(0, 0, -9),
            Climb + new Vector3(0, 0, -1), Hammer + new Vector3(0, 0, -6), Hammer + new Vector3(0, 0, 2), Gate + new Vector3(0, 0, -7),
            Gate + new Vector3(0, 0, 0), Puzzle + new Vector3(0, 0, -8), Puzzle + new Vector3(0, 0, 4), Gauntlet + new Vector3(0, 0, -7),
            Gauntlet + new Vector3(0, 0, 8), Castle + new Vector3(0, 0, -12), Castle + new Vector3(0, 0, -3) };
        var group = new GameObject("L1_RouteMarks").transform;
        group.SetParent(_root, false);
        int count = 0;
        for (int i = 0; i < route.Length - 1; i++)
        {
            Vector3 a = route[i], b = route[i + 1];
            Vector3 dir = Dir(a, b);
            float len = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
            for (float d = 2f; d < len - 1f; d += 5f)
            {
                Vector3 p = a + dir * d;
                bool skip = false;
                foreach (var n in NoMarks) { Vector3 q = new Vector3(n.x, 0, n.z); if ((new Vector3(p.x, 0, p.z) - q).sqrMagnitude < 3.6f * 3.6f) { skip = true; break; } }
                if (skip) continue;
                if (!Physics.Raycast(new Vector3(p.x, 40f, p.z), Vector3.down, out var hit, 80f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var tile = Box("RouteMark", group, Vector3.zero, new Vector3(.55f, .04f, 1.5f), _orange, false);
                tile.transform.SetPositionAndRotation(hit.point + Vector3.up * .035f, Quaternion.LookRotation(dir));
                var tip = Box("RouteMarkTip", tile.transform, new Vector3(0, 0, .55f), new Vector3(1.6f, 1f, .3f), _orange, false);
                count += 2;
            }
        }
        return count;
    }

    private static void ApplyStaticBatching()
    {
        foreach (var go in StaticCandidates)
        {
            if (go == null) continue;
            if (go.GetComponentInParent<Rigidbody>() != null) continue;
            if (go.GetComponentInParent<NetworkObject>() != null) continue;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }
    }
}
#endif
