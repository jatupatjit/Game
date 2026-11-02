using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the Lobby tutorial stations as standalone prefabs (Assets/Prefabs/Tutorial) and a small scene that shows
/// them all (Assets/Scenes/Test/TutorialStations.unity). Each station is a wooden sign with the controls in Thai and
/// a practice prop (target ring, throw target, climbing wall). Menu: Tools/Expedition/Build Tutorial Stations.
/// Practice crates are not inside the prefabs (network objects): place a CarryablePackage next to a station.
/// </summary>
public static class TutorialStationBuilder
{
    private const string PrefabFolder = "Assets/Prefabs/Tutorial";
    private const string ScenePath = "Assets/Scenes/Test/TutorialStations.unity";

    private static readonly (string file, string title, string body, string prop)[] Stations =
    {
        ("Station_Lift", "ยกลัง", "เล็งลังแล้ว คลิกซ้าย = ยกสองมือ\nยกคนเดียวช้าและเหนื่อย ชวนเพื่อนมาช่วย!", "crateSpot"),
        ("Station_Place", "วางลังเบาๆ", "คลิกขวา = วางลังลงพื้นเบาๆ\nวางบนวงสีเขียวให้ได้", "ring"),
        ("Station_Throw", "โยนลัง", "กด Q = โยนลังไปข้างหน้า\nโยนแรงลังเสียหายได้นะ!", "target"),
        ("Station_Climb", "ปีนกำแพง", "คลิกซ้าย/ขวา = จับด้วยมือซ้าย/ขวา\nW = ดึงตัวขึ้น  Space = ดีดตัวขึ้น", "wall"),
        ("Station_Coop", "ยกด้วยกัน", "สองคนยกเร็วขึ้นและไม่เสียแรง\nเดินพร้อมกัน ลังจะไม่โยก", "crateSpot"),
        ("Station_Ping", "คุยกับเพื่อน", "Z = มานี่!  X = ช่วยยก!  T = ระวัง!\nG = โบกมือ  M = แผนที่  K = ร้านหมวก", "none"),
    };

    [MenuItem("Tools/Expedition/Build Tutorial Stations")]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Tutorial");
        Material wood = SaveMaterial("TutorialWood", new Color(.62f, .43f, .26f));
        Material board = SaveMaterial("TutorialBoard", new Color(.96f, .91f, .8f));
        Material green = SaveMaterial("TutorialGreen", new Color(.45f, .85f, .45f));
        Material red = SaveMaterial("TutorialRed", new Color(.95f, .35f, .3f));
        Material stone = SaveMaterial("TutorialStone", new Color(.6f, .6f, .62f));
        var prefabs = new GameObject[Stations.Length];
        for (int i = 0; i < Stations.Length; i++)
        {
            var (file, title, body, prop) = Stations[i];
            var root = new GameObject(file);
            BuildSign(root.transform, title, body, wood, board);
            BuildProp(root.transform, prop, green, red, stone, wood);
            prefabs[i] = PrefabUtility.SaveAsPrefabAsset(root, PrefabFolder + "/" + file + ".prefab");
            Object.DestroyImmediate(root);
        }
        BuildScene(prefabs);
        AssetDatabase.SaveAssets();
        Debug.Log("[TutorialStationBuilder] Built " + prefabs.Length + " stations and " + ScenePath);
    }

    private static Material SaveMaterial(string name, Color color)
    {
        string folder = "Assets/Materials/Tutorial";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Materials", "Tutorial");
        string path = folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>Two posts and a board facing -Z (towards a player walking up), text on a world-space canvas.</summary>
    private static void BuildSign(Transform root, string title, string body, Material wood, Material board)
    {
        Cube(root, "PostL", new Vector3(-.9f, .9f, 0f), new Vector3(.12f, 1.8f, .12f), wood);
        Cube(root, "PostR", new Vector3(.9f, .9f, 0f), new Vector3(.12f, 1.8f, .12f), wood);
        Cube(root, "Board", new Vector3(0f, 1.55f, 0f), new Vector3(2.1f, 1.1f, .08f), board, false);
        var canvasGo = new GameObject("Text", typeof(RectTransform), typeof(Canvas));
        canvasGo.transform.SetParent(root, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)canvasGo.transform;
        rect.sizeDelta = new Vector2(1000f, 520f);
        rect.localScale = Vector3.one * .002f;
        rect.localPosition = new Vector3(0f, 1.55f, -.05f);
        var titleText = UITheme.MakeText(canvasGo.transform, "Title", title, 96, UITextRole.Primary, TextAnchor.MiddleCenter);
        titleText.color = new Color(.35f, .2f, .1f);
        UITheme.SetWeight(titleText, UITheme.FontBold);
        ((RectTransform)titleText.transform).sizeDelta = new Vector2(980f, 140f);
        ((RectTransform)titleText.transform).anchoredPosition = new Vector2(0f, 150f);
        var bodyText = UITheme.MakeText(canvasGo.transform, "Body", body, 52, UITextRole.Primary, TextAnchor.MiddleCenter);
        bodyText.color = new Color(.25f, .18f, .12f);
        ((RectTransform)bodyText.transform).sizeDelta = new Vector2(980f, 300f);
        ((RectTransform)bodyText.transform).anchoredPosition = new Vector2(0f, -70f);
    }

    private static void BuildProp(Transform root, string prop, Material green, Material red, Material stone, Material wood)
    {
        switch (prop)
        {
            case "ring": // flat green pad 2.5 m in front of the sign
                Cube(root, "PlacePad", new Vector3(0f, .02f, -2.5f), new Vector3(2f, .04f, 2f), green);
                break;
            case "target": // red/white target 6 m away, standing
                Cube(root, "TargetBack", new Vector3(0f, 1f, -7f), new Vector3(2f, 2f, .1f), wood);
                Cube(root, "TargetRing", new Vector3(0f, 1f, -7.06f), new Vector3(1.4f, 1.4f, .05f), red, false);
                Cube(root, "TargetCentre", new Vector3(0f, 1f, -7.1f), new Vector3(.5f, .5f, .05f), green, false);
                break;
            case "wall": // 2.4 m climbing wall with hand holds, beside the sign
                Cube(root, "ClimbWall_Tutorial", new Vector3(0f, 1.2f, -2.2f), new Vector3(3f, 2.4f, .6f), stone);
                for (int i = 0; i < 6; i++)
                    Cube(root, "Hold" + i, new Vector3((i % 2 == 0 ? -.6f : .6f), .5f + i * .35f, -2.55f), new Vector3(.3f, .1f, .15f), wood);
                break;
            case "crateSpot": // painted square where a practice crate goes
                Cube(root, "CrateSpot", new Vector3(0f, .015f, -2.2f), new Vector3(1.8f, .03f, 1.8f), green, false);
                break;
        }
    }

    private const string CoursePath = PrefabFolder + "/TutorialCourse.prefab";
    private const string TutorialScenePath = "Assets/Scenes/Tutorial.unity";
    private const float StationSpacing = 9f;

    /// <summary>
    /// The training ground: the stations along one walkway (start sign, fences, finish arch) saved as a single prefab
    /// (no network objects, so it drops into the Lobby or any level), and Assets/Scenes/Tutorial.unity built around
    /// it with a player start, practice crates and an exit portal back to the Lobby. Menu: Tools/Expedition/Build
    /// Tutorial Course (builds the stations first).
    /// </summary>
    [MenuItem("Tools/Expedition/Build Tutorial Course")]
    public static void BuildCourse()
    {
        Build();
        Material wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tutorial/TutorialWood.mat");
        Material board = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tutorial/TutorialBoard.mat");
        Material path = SaveMaterial("TutorialPath", new Color(.82f, .7f, .5f));
        Material grass = SaveMaterial("TutorialGrass", new Color(.45f, .72f, .35f));
        var root = new GameObject("TutorialCourse");
        float length = StationSpacing * (Stations.Length + 1) + 6f;
        Cube(root.transform, "Grass", new Vector3(0f, -.5f, length * .5f - 6f), new Vector3(24f, 1f, length + 12f), grass);
        Cube(root.transform, "Walkway", new Vector3(0f, .01f, length * .5f - 6f), new Vector3(5f, .02f, length + 8f), path, false);
        for (int side = -1; side <= 1; side += 2)
            Cube(root.transform, "Fence" + (side < 0 ? "L" : "R"), new Vector3(side * 11.5f, .5f, length * .5f - 6f), new Vector3(.2f, 1f, length + 12f), wood);
        var start = new GameObject("StartSign");
        start.transform.SetParent(root.transform, false);
        start.transform.localPosition = new Vector3(-5f, 0f, 3f);
        BuildSign(start.transform, "สนามฝึก", "เดินตามทางไปทีละป้าย ลองทำตาม\nสุดทางมีประตูกลับ Lobby", wood, board);
        for (int i = 0; i < Stations.Length; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + Stations[i].file + ".prefab");
            var station = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            station.transform.SetParent(root.transform, false);
            // Signs alternate sides of the walkway; their props sit towards the walkway's approach.
            station.transform.localPosition = new Vector3(i % 2 == 0 ? 5.5f : -5.5f, 0f, StationSpacing * (i + 1) + 4f);
        }
        float end = StationSpacing * (Stations.Length + 1) + 2f;
        Cube(root.transform, "FinishPostL", new Vector3(-2.6f, 1.6f, end), new Vector3(.3f, 3.2f, .3f), wood);
        Cube(root.transform, "FinishPostR", new Vector3(2.6f, 1.6f, end), new Vector3(.3f, 3.2f, .3f), wood);
        Cube(root.transform, "FinishBeam", new Vector3(0f, 3.3f, end), new Vector3(5.6f, .4f, .4f), wood);
        var course = PrefabUtility.SaveAsPrefabAsset(root, CoursePath);
        Object.DestroyImmediate(root);
        BuildTutorialScene(course, end);
        AssetDatabase.SaveAssets();
        Debug.Log("[TutorialStationBuilder] Built " + CoursePath + " and " + TutorialScenePath);
    }

    private static void BuildTutorialScene(GameObject course, float end)
    {
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        PrefabUtility.InstantiatePrefab(course, scene);
        new GameObject("PlayerStart").transform.position = new Vector3(0f, 1.05f, -2f);
        // Practice crates next to the lift, place, throw and co-op stations (in-scene network objects).
        var crate = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Items/CarryablePackage.prefab");
        foreach (int station in new[] { 0, 1, 2, 4 })
        {
            float x = station % 2 == 0 ? 5.5f : -5.5f;
            var c = (GameObject)PrefabUtility.InstantiatePrefab(crate, scene);
            c.transform.position = new Vector3(x, .6f, StationSpacing * (station + 1) + 4f - 2.2f);
        }
        // Exit portal at the finish arch: press E at the button, then walk through (LobbyPortalGate -> Lobby).
        var portal = new GameObject("TutorialExitPortal", typeof(Unity.Netcode.NetworkObject));
        portal.transform.position = new Vector3(0f, 0f, end);
        var button = Cube(portal.transform, "Button", new Vector3(3.6f, .6f, -1.2f), new Vector3(.5f, 1.2f, .5f),
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tutorial/TutorialRed.mat"));
        var zone = new GameObject("EntryZone", typeof(BoxCollider));
        zone.transform.SetParent(portal.transform, false);
        zone.transform.localPosition = new Vector3(0f, 1.5f, .8f);
        var box = zone.GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(4.8f, 3f, 1.4f);
        var glow = Cube(portal.transform, "PortalGlow", new Vector3(0f, 1.6f, 0f), new Vector3(4.8f, 3f, .05f),
            AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Tutorial/TutorialGreen.mat"), false);
        var gate = portal.AddComponent<LobbyPortalGate>();
        var so = new SerializedObject(gate);
        so.FindProperty("_interactionPoint").objectReferenceValue = button.transform;
        so.FindProperty("_portalEntryZone").objectReferenceValue = box;
        so.FindProperty("_portalVisualRoot").objectReferenceValue = glow;
        so.FindProperty("_destinationSceneName").stringValue = "Lobby";
        so.ApplyModifiedPropertiesWithoutUndo();
        glow.SetActive(false);
        EditorSceneManager.SaveScene(scene, TutorialScenePath);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
        EditorSceneManager.CloseScene(scene, true);
        // Right after the Lobby in the build list.
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == TutorialScenePath);
        int lobby = scenes.FindIndex(s => s.path.EndsWith("/Lobby.unity"));
        scenes.Insert(lobby + 1, new EditorBuildSettingsScene(TutorialScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void BuildScene(GameObject[] prefabs)
    {
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, -.5f, 0f);
        ground.transform.localScale = new Vector3(50f, 1f, 30f);
        new GameObject("PlayerStart").transform.position = new Vector3(0f, 1.05f, -10f);
        for (int i = 0; i < prefabs.Length; i++)
        {
            var station = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
            station.transform.position = new Vector3(-17.5f + i * 7f, 0f, 4f);
        }
        EditorSceneManager.SaveScene(scene, ScenePath);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(previous);
        EditorSceneManager.CloseScene(scene, true);
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
    }
}
