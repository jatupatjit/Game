#if UNITY_EDITOR
using System.Linq;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Tools/Expedition/Build Coop Islands Scene: a test route <c>Scenes/Test/CoopIslands.unity</c> where nothing can be
/// done alone (George 2026-11-27, rule C). Sky islets in a row, start to goal:
///  1. Wobbly bridge: two carry the crate across together (pulling apart = tug-of-war jolt, rule A1).
///  2. Throw-up cliff (4.5 m): climbing needs empty hands, so one climbs and catches (hold F), the other throws (Q).
///  3. Wall walkway: a 2 m wide walkway over the void blocked by a 3.2 m wall wider than the walkway. Players climb
///     over it; the crate can only be thrown over it to a team-mate waiting on the far side.
///  4. Lever bridge: one climbs the lever tower and pulls the lever (E); the bridge over the next gap comes down.
///  5. Rope hoist (needs 2+): the crate goes in the basket, a team-mate on top holds E at the winch.
/// Built from existing prefabs (Obstacle_WobblyBridge, Climb_*) on the Level01 islet mesh; the prefabs' text signs
/// are switched off (no text signs in levels). Re-running rebuilds the scene. Play it directly (Play From Any Scene).
/// To move a section into a level: copy its "Section_*" object (islet + pieces) into the level scene.
/// </summary>
public static class CoopIslandsSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Test/CoopIslands.unity";
    private const string CratePath = "Assets/Prefabs/Items/WoodenCrateItem.prefab";
    private const string IsletMeshPath = "Assets/Environment/FloatingCoop/L2_02_SpikeIslet.asset";
    private const string GrassPath = "Assets/Environment/FloatingCoop/Grass.mat";
    private const string CliffPath = "Assets/Environment/FloatingCoop/Cliff.mat";
    private const string BridgePath = "Assets/Prefabs/Traps/Obstacles/Obstacle_WobblyBridge.prefab";
    private const string Climb = "Assets/Prefabs/Traps/Climb/";
    private const string Medieval = "Assets/Prefabs/Traps/Materials/Medieval/";

    private static Material _stone, _wood, _woodLight, _grip, _flag;

    [MenuItem("Tools/Expedition/Build Coop Islands Scene")]
    public static void BuildMenu() => Debug.Log(Build());

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[CoopIslands] Stop Play mode first.";
        _stone = AssetDatabase.LoadAssetAtPath<Material>(Medieval + "Medieval_Stone.mat");
        _wood = AssetDatabase.LoadAssetAtPath<Material>(Medieval + "Medieval_Wood.mat");
        _woodLight = AssetDatabase.LoadAssetAtPath<Material>(Medieval + "Medieval_WoodLight.mat");
        _grip = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/Traps/Materials/Climb_Grip.mat") ?? _woodLight;
        _flag = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/Traps/Obstacles/WindFlagCloth.mat") ?? _woodLight;

        Scene previous = SceneManager.GetActiveScene();
        string previousPath = previous.path;
        if (previous.isDirty && !string.IsNullOrEmpty(previousPath)) EditorSceneManager.SaveScene(previous);
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        foreach (var go in scene.GetRootGameObjects()) if (go.GetComponent<Camera>() != null) Object.DestroyImmediate(go);
        Random.InitState(7);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.72f, .82f, .95f);
        RenderSettings.ambientEquatorColor = new Color(.62f, .68f, .66f);
        RenderSettings.ambientGroundColor = new Color(.38f, .4f, .36f);

        // 0. Start islet: spawn and the crate.
        var start = Section("Section_0_Start");
        Islet(start, "Islet_Start", new Vector3(0f, 0f, 0f), 1.6f);
        var spawn = new GameObject("PlayerStart");
        spawn.transform.SetParent(start, false);
        spawn.transform.position = new Vector3(0f, 1.1f, -5f); // faces +Z, the route (root ~1 m above the ground)
        Prefab(CratePath, start, new Vector3(0f, .6f, 1.5f), Quaternion.identity);

        // 1. Wobbly bridge to the cliff islet (carry together).
        var s1 = Section("Section_1_WobblyBridge");
        Prefab(BridgePath, s1, new Vector3(0f, 0f, 10.5f), Quaternion.identity);

        // 2. Throw-up cliff: the islet foot at y 0, plateau 4.5 m up.
        var s2 = Section("Section_2_ThrowUpCliff");
        Islet(s2, "Islet_Cliff", new Vector3(0f, 0f, 34f), 1.6f);
        Prefab(Climb + "Climb_ThrowShortcut.prefab", s2, new Vector3(0f, 0f, 36f), Quaternion.identity);

        // 3. Wall walkway at plateau height (4.5): climb over the wall; the crate must be thrown over it.
        var s3 = Section("Section_3_ThrowOverWall");
        const float top = 4.5f;
        Box("Walkway", s3, new Vector3(0f, top - .25f, 48.5f), new Vector3(2.2f, .5f, 13f), _wood);
        Box("Rail_Post_L", s3, new Vector3(-1.05f, top + .45f, 43f), new Vector3(.15f, .9f, .15f), _woodLight, false);
        Box("Rail_Post_R", s3, new Vector3(1.05f, top + .45f, 43f), new Vector3(.15f, .9f, .15f), _woodLight, false);
        var wall = Box("ClimbWall", s3, new Vector3(0f, top + 1.6f, 48.5f), new Vector3(4.2f, 3.2f, 1f), _stone);
        Grips(wall.transform.parent, new Vector3(0f, top, 48.5f), 4.2f, 3.2f, .5f);
        Grips(wall.transform.parent, new Vector3(0f, top, 48.5f), 4.2f, 3.2f, -.5f);
        Box("WallCap", s3, new Vector3(0f, top + 3.25f, 48.5f), new Vector3(4.5f, .15f, 1.3f), _woodLight);

        // 4. Lever islet + lever bridge to the hoist islet.
        var s4 = Section("Section_4_LeverBridge");
        Islet(s4, "Islet_Lever", new Vector3(0f, top, 63f), 1.4f);
        var lever = Prefab(Climb + "Climb_WallSwitch.prefab", s4, new Vector3(-5f, top, 64f), Quaternion.Euler(0f, -90f, 0f)); // climb face towards the islet centre
        var bridge = Prefab(Climb + "Climb_SwitchBridge.prefab", s4, new Vector3(0f, top, 71.2f), Quaternion.identity);
        LinkSwitch(bridge, lever.GetComponent<ClimbWallSwitch>());

        // 5. Rope hoist up to the goal (needs 2+).
        var s5 = Section("Section_5_RopeHoist");
        Islet(s5, "Islet_Hoist", new Vector3(0f, top, 84f), 1.5f);
        Prefab(Climb + "Climb_RopeHoist.prefab", s5, new Vector3(0f, top, 84f), Quaternion.identity);
        var goal = new GameObject("Goal").transform;
        goal.SetParent(s5, false);
        goal.position = new Vector3(1.5f, top + 6f, 88.5f);
        Box("FlagPole", goal, new Vector3(0f, 1.6f, 0f), new Vector3(.12f, 3.2f, .12f), _wood, false, local: true);
        Box("Flag", goal, new Vector3(.55f, 2.85f, 0f), new Vector3(1f, .6f, .04f), _flag, false, local: true);

        // No text signs in levels: switch off every TextMesh the prefabs brought.
        int hidden = 0;
        foreach (var text in Object.FindObjectsByType<TextMesh>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var r = text.GetComponent<Renderer>();
            if (r != null && r.enabled) { r.enabled = false; hidden++; }
            // A sign board (parent named Sign) goes too.
            if (text.transform.parent != null && text.transform.parent.name == "Sign") text.transform.parent.gameObject.SetActive(false);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        var scenes = EditorBuildSettings.scenes.ToList();
        if (!scenes.Any(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
        if (!string.IsNullOrEmpty(previousPath)) EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
        return $"[CoopIslands] saved {ScenePath} ({hidden} prefab text labels hidden)";
    }

    private static Transform Section(string name) => new GameObject(name).transform;

    private static void Islet(Transform parent, string name, Vector3 topCentre, float scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = topCentre; // the islet mesh's pivot is its top centre
        go.transform.localScale = new Vector3(scale, 1.1f, scale);
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(IsletMeshPath);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            AssetDatabase.LoadAssetAtPath<Material>(GrassPath), AssetDatabase.LoadAssetAtPath<Material>(CliffPath)
        };
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.isStatic = true;
    }

    private static GameObject Prefab(string path, Transform parent, Vector3 position, Quaternion rotation)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) { Debug.LogError("[CoopIslands] missing " + path); return new GameObject("Missing_" + path); }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent.gameObject.scene);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(position, rotation);
        return go;
    }

    private static void LinkSwitch(GameObject targetRoot, ClimbWallSwitch source)
    {
        foreach (var t in targetRoot.GetComponentsInChildren<SwitchTarget>(true))
        {
            var so = new SerializedObject(t);
            so.FindProperty("_source").objectReferenceValue = source;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>Hand-hold stones on one face (z offset from the wall centre) of a wall standing on <paramref name="baseCentre"/>.</summary>
    private static void Grips(Transform parent, Vector3 baseCentre, float width, float height, float faceZ)
    {
        var grips = new GameObject("Grips").transform;
        grips.SetParent(parent, false);
        var rng = new System.Random(faceZ > 0 ? 31 : 17);
        float side = Mathf.Sign(faceZ);
        for (float y = .8f; y < height - .3f; y += .7f)
            for (float x = -width * .5f + .45f; x < width * .5f - .3f; x += .85f)
            {
                float jx = (float)rng.NextDouble() * .3f - .15f, jy = (float)rng.NextDouble() * .25f - .12f;
                var g = Box("Grip", grips, baseCentre + new Vector3(x + jx, y + jy, faceZ + side * .06f), new Vector3(.3f, .15f, .14f), _grip, false);
                g.transform.localEulerAngles = new Vector3(0f, 0f, (float)rng.NextDouble() * 30f - 15f);
            }
    }

    private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material,
        bool collider = true, bool local = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        if (local) go.transform.localPosition = position; else go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
}
#endif
