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

/// <summary>Builds an editable five-island cargo route without deleting the previous layout.</summary>
public static class FloatingCoopLevelBuilder
{
    private const string Folder = "Assets/Environment/FloatingCoop";
    private static Material _grass, _cliff, _wood, _stone, _blue, _gold, _leaves;
    private static Transform _root;

    [MenuItem("Tools/Expedition/Build Floating Co-op Level01")]
    public static void BuildLevel01() => Build("Assets/Scenes/Level01.unity");

    public static string Build(string scenePath)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before building the level.");
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        if (FindRoot(scene, "FloatingCoopRoute") != null)
            throw new InvalidOperationException("FloatingCoopRoute already exists; edit it directly instead of overwriting it.");
        SceneManager.SetActiveScene(scene);
        Directory.CreateDirectory("E:/Unity/Verification/FloatingCoop-20261008");
        File.Copy(scenePath, "E:/Unity/Verification/FloatingCoop-20261008/" + Path.GetFileNameWithoutExtension(scenePath) + ".Before.unity", true);
        EnsureFolder("Assets/Environment"); EnsureFolder(Folder);
        _grass = Mat("Grass", new Color(.35f, .66f, .40f));
        _cliff = Mat("Cliff", new Color(.30f, .21f, .15f));
        _wood = Mat("BridgeWood", new Color(.55f, .33f, .16f));
        _stone = Mat("Stone", new Color(.48f, .51f, .49f));
        _blue = Mat("RelayBlue", new Color(.10f, .65f, .90f));
        _gold = Mat("GoalGold", new Color(.95f, .71f, .17f));
        _leaves = Mat("Leaves", new Color(.20f, .46f, .28f));

        var legacy = new GameObject("PreviousObstacleTest_Preserved");
        Undo.RegisterCreatedObjectUndo(legacy, "Preserve old test route");
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go.name == "Level01_Ground" || go.name == "Ground Group" || go.name == "Level01_BackRoute_Coop" ||
                go.name == "MedievalScenery" || go.name == "wall" || go.name == "wall (1)" || go.name == "เกาะเล็ก")
                Undo.SetTransformParent(go.transform, legacy.transform, "Preserve geometry");
        }
        legacy.SetActive(false);
        _root = new GameObject("FloatingCoopRoute").transform;
        Undo.RegisterCreatedObjectUndo(_root.gameObject, "Build floating route");
        Vector3[] centers = { Vector3.zero, new Vector3(0, 0, 31), new Vector3(23, 0, 60), new Vector3(-1, 0, 89), new Vector3(11, 0, 120) };
        Vector2[] radii = { new Vector2(12, 13), new Vector2(12, 10), new Vector2(13, 12), new Vector2(11, 11), new Vector2(16, 14) };
        string[] names = { "01_StartVillage", "02_RelayIsland", "03_ForestSlalom", "04_TeamGate", "05_DeliveryIsland" };
        for (int i = 0; i < centers.Length; i++) Island(names[i], centers[i], radii[i], 37 + i * 11);
        Bridge("01_WideTrainingBridge", centers[0], radii[0], centers[1], radii[1], 4.8f, _root);
        GameObject relayDeck = Bridge("02_RelayBridgeDeck", centers[1], radii[1], centers[2], radii[2], 4.4f, _root);
        Bridge("03_ForestBridge", centers[2], radii[2], centers[3], radii[3], 4.2f, _root);
        Bridge("04_FinalBridge", centers[3], radii[3], centers[4], radii[4], 4.5f, _root);

        var cargoRoot = FindRoot(scene, "WoodenCrateItem");
        if (cargoRoot == null) throw new InvalidOperationException("The scene needs WoodenCrateItem.");
        FragileCargo cargo = cargoRoot.GetComponent<FragileCargo>();
        Undo.RecordObject(cargoRoot.transform, "Place cargo");
        cargoRoot.transform.SetPositionAndRotation(new Vector3(0, 1.25f, 2), Quaternion.identity);
        Transform start = FindRoot(scene, "PlayerStart")?.transform;
        if (start == null) start = new GameObject("PlayerStart").transform;
        Undo.RecordObject(start, "Place player start");
        start.SetPositionAndRotation(new Vector3(0, 1.05f, -7), Quaternion.identity);

        Parking("01_CargoRest", new Vector3(0, 0, 6));
        Parking("02_CargoRest", new Vector3(0, 0, 29));
        Parking("03_CargoRest", centers[2] + new Vector3(0, 0, -3));
        Parking("04_CargoRest", centers[3] + new Vector3(0, 0, -3));
        Sign("StartInstructions", new Vector3(0, 3, -1), "FLOATING ISLANDS\nFIND THE CRATE - CARRY TOGETHER", 180);
        Sign("VillageTip", new Vector3(-6, 2.5f, 6), "2-4 PLAYERS\nTAKE DIFFERENT SIDES\nREST THE CRATE ON GOLD PADS", 160);

        var relay = new GameObject("RelayBridge_TwoPlayerUnlock");
        relay.transform.SetParent(_root, false);
        relay.AddComponent<CoopRelayBridge>();
        BoxCollider leftPad = Pad("BluePad_Left", new Vector3(-6, 0, 32));
        BoxCollider rightPad = Pad("BluePad_Right", new Vector3(6, 0, 32));
        var relayText = Sign("RelayInstructions", new Vector3(0, 3.5f, 35), "STAND ON BOTH BLUE PADS\nUNLOCK THE BRIDGE TOGETHER", 180);
        var relayData = new SerializedObject(relay.GetComponent<CoopRelayBridge>());
        Ref(relayData, "_leftPad", leftPad); Ref(relayData, "_rightPad", rightPad);
        Ref(relayData, "_bridge", relayDeck); Ref(relayData, "_label", relayText);
        relayData.ApplyModifiedPropertiesWithoutUndo(); relayDeck.SetActive(false);

        // Rocks make two readable lanes; the crate can be set down before turning.
        Rock("SlalomA", centers[2] + new Vector3(-4, 0, -6), new Vector3(4, 2.4f, 3));
        Rock("SlalomB", centers[2] + new Vector3(3, 0, 0), new Vector3(4, 3.3f, 3));
        Rock("SlalomC", centers[2] + new Vector3(-4, 0, 6), new Vector3(4, 2.6f, 3));
        Sign("ForestInstructions", centers[2] + new Vector3(7, 3, -4), "TURN THE CRATE TOGETHER\nUSE THE WIDE SIDE OF EACH ROCK", 190);

        CreateGate(centers[3], centers[4], radii[3], cargo);
        for (int i = 0; i < centers.Length; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                Vector3 point = centers[i] + new Vector3((j % 2 == 0 ? -1 : 1) * radii[i].x * .74f, 0,
                    (j < 2 ? -1 : 1) * radii[i].y * .4f);
                Tree(names[i] + "_Tree" + j, point);
            }
            Flag(names[i] + "_Flag", centers[i] + new Vector3(-radii[i].x * .7f, 0, 0), i == 4 ? _gold : _blue);
        }
        CreateFinish(scene, centers[4]);
        var mission = FindRoot(scene, "LevelMission")?.GetComponent<LevelMission>();
        if (mission != null)
        {
            var missionData = new SerializedObject(mission);
            missionData.FindProperty("_objective").stringValue = "Deliver the wooden crate across the floating islands";
            missionData.ApplyModifiedProperties();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = _root.gameObject;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(10, -1, 58), Quaternion.Euler(55, -25, 0), 98f);
        AssetDatabase.SaveAssets();
        return scenePath + ": five islands, four bridges, relay pads, team gate, cargo/goal/portal connected; previous geometry preserved inactive.";
    }

    private static void CreateGate(Vector3 center, Vector3 next, Vector2 radius, FragileCargo cargo)
    {
        Vector3 direction = (next - center).normalized;
        Vector3 at = center + direction * 8.4f;
        var root = new GameObject("TeamPassageGate"); root.transform.SetParent(_root, false);
        root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(direction));
        var gate = root.AddComponent<TeamLeverGate>();
        var barrier = Cube("ClosedGate", at + Vector3.up * 1.8f, new Vector3(5.2f, 3.6f, .4f), _wood, root.transform);
        barrier.transform.rotation = root.transform.rotation;
        for (int i = -1; i <= 1; i += 2)
            Cube("StoneGatePost", at + root.transform.right * i * 2.9f + Vector3.up * 2, new Vector3(.6f, 4, .7f), _stone, root.transform);
        var lever = Cube("LeverStand", center + new Vector3(-6, 1, 2), new Vector3(1, 2, 1), _blue, root.transform);
        var safety = new GameObject("GateSafety"); safety.transform.SetParent(root.transform, false);
        var volume = safety.AddComponent<BoxCollider>(); volume.center = new Vector3(0, 1.7f, 0); volume.size = new Vector3(6, 4, 4); volume.isTrigger = true;
        var label = Sign("GateInstructions", center + new Vector3(-6, 3.5f, 2), "HOLD E AT THE BLUE LEVER\nFRIENDS BRING THE CRATE THROUGH", 180);
        var data = new SerializedObject(gate);
        Ref(data, "_lever", lever.transform); Ref(data, "_barrier", barrier); Ref(data, "_safetyVolume", volume);
        Ref(data, "_label", label); Ref(data, "_cargo", cargo);
        data.FindProperty("_openSeconds").floatValue = 12f;
        data.ApplyModifiedPropertiesWithoutUndo();
        // Fence wings prevent simply walking around the gate with cargo.
        for (int i = -1; i <= 1; i += 2)
        {
            Vector3 p = at + root.transform.right * i * 5.8f + Vector3.up * 1.2f;
            var wing = Cube("FenceWing", p, new Vector3(5.2f, 2.4f, .5f), _wood, root.transform);
            wing.transform.rotation = root.transform.rotation;
        }
    }

    private static void CreateFinish(Scene scene, Vector3 center)
    {
        var delivery = FindRoot(scene, "DeliveryZone");
        var portal = FindRoot(scene, "DeliveryExitPortal_Level02");
        if (delivery == null || portal == null) throw new InvalidOperationException("DeliveryZone and Level02 portal must already exist.");
        Undo.RecordObject(delivery.transform, "Move delivery zone");
        delivery.transform.position = center + new Vector3(-3, .04f, 1);
        Undo.RecordObject(portal.transform, "Move exit portal");
        portal.transform.SetPositionAndRotation(center + new Vector3(8, 0, 2), Quaternion.identity);
        Parking("DeliveryLanding", center + new Vector3(-3, 0, 1), 6);
        Flag("DeliveryFlagL", center + new Vector3(-7, 0, 4), _gold);
        Flag("DeliveryFlagR", center + new Vector3(1, 0, 4), _gold);
        // Small skyline landmark, with the delivery area in front of it.
        for (int i = -1; i <= 1; i += 2)
        {
            var tower = Cube("CastleTower", center + new Vector3(i * 4, 3.5f, 9), new Vector3(3, 7, 3), _stone, _root);
            for (int j = -1; j <= 1; j++) Cube("Battlement", tower.transform.position + new Vector3(j, 4, 0), new Vector3(.7f, 1, 3), _stone, _root);
        }
        Cube("CastleWall", center + new Vector3(0, 2, 10), new Vector3(6, 4, 1.5f), _stone, _root);
    }

    private static BoxCollider Pad(string name, Vector3 point)
    {
        Cube(name + "_Visible", point + Vector3.up * .025f, new Vector3(2.5f, .05f, 2.5f), _blue, _root);
        var obj = new GameObject(name + "_Occupancy"); obj.transform.SetParent(_root, false); obj.transform.position = point;
        var pad = obj.AddComponent<BoxCollider>(); pad.isTrigger = true; pad.center = new Vector3(0, .5f, 0); pad.size = new Vector3(2.5f, 2, 2.5f);
        return pad;
    }

    internal static void Parking(string name, Vector3 point, float width = 4f)
    {
        // Thin flush marker: a resting spot, not an extra obstacle under the crate.
        Cube(name, point + Vector3.up * .018f, new Vector3(width, .036f, width), _gold, _root);
        for (int i = -1; i <= 1; i += 2)
            Cube(name + "_Edge", point + new Vector3(i * width * .5f, .04f, 0), new Vector3(.12f, .08f, width), _wood, _root);
    }

    internal static GameObject Bridge(string name, Vector3 a, Vector2 ar, Vector3 b, Vector2 br, float width, Transform parent)
    {
        Vector3 d = (b - a).normalized;
        float Radius(Vector2 r) => 1f / Mathf.Sqrt(d.x * d.x / (r.x * r.x) + d.z * d.z / (r.y * r.y));
        Vector3 from = a + d * (Radius(ar) * .88f - .8f);
        Vector3 to = b - d * (Radius(br) * .88f - .8f);
        var bridge = new GameObject(name); bridge.transform.SetParent(parent, false);
        bridge.transform.SetPositionAndRotation((from + to) * .5f, Quaternion.LookRotation(d));
        float length = Vector3.Distance(from, to);
        var deck = Cube("SolidCargoDeck", bridge.transform.position - Vector3.up * .16f, new Vector3(width, .44f, length), _wood, bridge.transform);
        deck.transform.rotation = bridge.transform.rotation;
        int count = Mathf.CeilToInt(length / 1.6f);
        for (int i = 0; i <= count; i++)
        {
            float z = Mathf.Lerp(-length * .5f, length * .5f, (float)i / count);
            var plank = Cube("PlankSeam", bridge.transform.TransformPoint(new Vector3(0, .075f, z)), new Vector3(width, .025f, .05f), _cliff, bridge.transform, false);
            plank.transform.rotation = bridge.transform.rotation;
            if (i % 2 != 0) continue;
            for (int side = -1; side <= 1; side += 2)
                Cube("EdgePost", bridge.transform.TransformPoint(new Vector3(side * (width * .5f + .12f), .5f, z)), new Vector3(.18f, 1, .18f), _wood, bridge.transform);
        }
        // Low edges help cargo recovery without blocking players alongside the box.
        for (int side = -1; side <= 1; side += 2)
        {
            var edge = Cube("SafetyEdge", bridge.transform.TransformPoint(new Vector3(side * (width * .5f + .1f), .18f, 0)), new Vector3(.18f, .36f, length), _wood, bridge.transform);
            edge.transform.rotation = bridge.transform.rotation;
        }
        return bridge;
    }

    internal static void Island(string name, Vector3 point, Vector2 radii, int seed)
    {
        var random = new System.Random(seed);
        const int count = 28;
        Vector3[] rim = new Vector3[count], lower = new Vector3[count], bottom = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            float r = .96f + (float)random.NextDouble() * .07f;
            rim[i] = new Vector3(Mathf.Cos(angle) * radii.x * r, 0, Mathf.Sin(angle) * radii.y * r);
            lower[i] = rim[i] * .95f - Vector3.up * (.9f + (float)random.NextDouble() * .5f);
            bottom[i] = new Vector3(rim[i].x * .35f, -5f - (float)random.NextDouble(), rim[i].z * .35f);
        }
        var vertices = new List<Vector3>(); var top = new List<int>(); var sides = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, List<int> triangles)
        { int n = vertices.Count; vertices.Add(a); vertices.Add(b); vertices.Add(c); triangles.Add(n); triangles.Add(n + 1); triangles.Add(n + 2); }
        for (int i = 0; i < count; i++)
        {
            int n = (i + 1) % count;
            Tri(Vector3.zero, rim[n], rim[i], top);
            Tri(rim[i], rim[n], lower[n], sides); Tri(rim[i], lower[n], lower[i], sides);
            Tri(lower[i], lower[n], bottom[n], sides); Tri(lower[i], bottom[n], bottom[i], sides);
            Tri(bottom[i], bottom[n], new Vector3(0, -7, 0), sides);
        }
        var mesh = new Mesh { name = name, subMeshCount = 2 };
        mesh.SetVertices(vertices); mesh.SetTriangles(top, 0); mesh.SetTriangles(sides, 1); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Folder + "/" + name + ".asset");
        var island = new GameObject(name); island.transform.SetParent(_root, false); island.transform.position = point;
        island.AddComponent<MeshFilter>().sharedMesh = mesh;
        island.AddComponent<MeshRenderer>().sharedMaterials = new[] { _grass, _cliff };
        island.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    internal static void Tree(string name, Vector3 point)
    {
        Cube(name + "_Trunk", point + Vector3.up * 1.2f, new Vector3(.55f, 2.4f, .55f), _wood, _root);
        var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere); crown.name = name + "_Crown"; crown.transform.SetParent(_root, false);
        crown.transform.position = point + Vector3.up * 3.2f; crown.transform.localScale = new Vector3(3.2f, 3, 3.2f);
        crown.GetComponent<Renderer>().sharedMaterial = _leaves;
        UnityEngine.Object.DestroyImmediate(crown.GetComponent<Collider>());
    }

    internal static void Rock(string name, Vector3 point, Vector3 size)
    {
        var rock = Cube(name, point + Vector3.up * size.y * .5f, size, _stone, _root);
        rock.transform.rotation = Quaternion.Euler(0, name.EndsWith("B") ? -20 : 15, 0);
    }

    internal static void Flag(string name, Vector3 point, Material material)
    {
        Cube(name + "_Pole", point + Vector3.up * 2, new Vector3(.12f, 4, .12f), _wood, _root);
        Cube(name + "_Cloth", point + new Vector3(.75f, 3.4f, 0), new Vector3(1.5f, 1, .06f), material, _root, false);
    }

    internal static TextMesh Sign(string name, Vector3 point, string text, float yaw)
    {
        var obj = new GameObject(name); obj.transform.SetParent(_root, false); obj.transform.SetPositionAndRotation(point, Quaternion.Euler(0, yaw - 180f, 0));
        var label = obj.AddComponent<TextMesh>(); label.text = text; label.fontSize = 56; label.characterSize = .075f;
        label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = Color.white;
        return label;
    }

    internal static GameObject Cube(string name, Vector3 point, Vector3 size, Material material, Transform parent, bool solid = true)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = name; obj.transform.SetParent(parent, false);
        obj.transform.position = point; obj.transform.localScale = size; obj.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) UnityEngine.Object.DestroyImmediate(obj.GetComponent<Collider>());
        return obj;
    }

    private static Material Mat(string name, Color color)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", .08f); EditorUtility.SetDirty(material); return material;
    }

    internal static void UseExistingAssets(Transform root)
    {
        _root = root;
        _grass = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Grass.mat");
        _cliff = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Cliff.mat");
        _wood = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/BridgeWood.mat");
        _stone = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Stone.mat");
        _blue = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/RelayBlue.mat");
        _gold = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/GoalGold.mat");
        _leaves = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Leaves.mat");
        if (_grass == null || _cliff == null || _wood == null || _stone == null || _blue == null || _gold == null || _leaves == null)
            throw new InvalidOperationException("The existing floating route materials are required.");
    }

    private static void EnsureFolder(string path)
    { if (!AssetDatabase.IsValidFolder(path)) { string parent = Path.GetDirectoryName(path).Replace('\\', '/'); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); } }
    private static GameObject FindRoot(Scene scene, string name)
    { foreach (var obj in scene.GetRootGameObjects()) if (obj.name == name) return obj; return null; }
    private static void Ref(SerializedObject target, string name, UnityEngine.Object value) => target.FindProperty(name).objectReferenceValue = value;
}
#endif
