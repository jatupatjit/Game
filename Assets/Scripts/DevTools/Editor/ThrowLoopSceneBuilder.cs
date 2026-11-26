#if UNITY_EDITOR
using System.Linq;
using CoopGame.DevTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools/Expedition/Build Throw Loop Scene: catch-practice test scene <c>Scenes/Test/ThrowLoop.unity</c>. A flat
/// ground, the player start, the level crate and a box-built pitcher (<see cref="ThrowLoopPitcher"/>) that throws
/// the crate at the player every few seconds. Re-running rebuilds the scene. Play it directly (Play From Any Scene
/// boots the Lobby host and loads it), stand still and hold F to catch.
/// </summary>
public static class ThrowLoopSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Test/ThrowLoop.unity";
    private const string CratePath = "Assets/Prefabs/Items/WoodenCrateItem.prefab";

    [MenuItem("Tools/Expedition/Build Throw Loop Scene")]
    public static void Build()
    {
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.72f, .82f, .95f);
        RenderSettings.ambientEquatorColor = new Color(.6f, .66f, .62f);
        RenderSettings.ambientGroundColor = new Color(.35f, .38f, .3f);

        Material grass = Lit("ThrowLoop_Grass", new Color(.42f, .72f, .38f));
        Material wood = Lit("ThrowLoop_Wood", new Color(.55f, .38f, .22f));
        Material skin = Lit("ThrowLoop_Pitcher", new Color(.98f, .62f, .25f));
        Material dark = Lit("ThrowLoop_Dark", new Color(.15f, .14f, .16f));

        Box("Ground", null, new Vector3(0f, -.5f, 4f), new Vector3(40f, 1f, 40f), grass);
        // Low rails round the practice area so a dropped crate does not roll far.
        Box("Rail_N", null, new Vector3(0f, .25f, 16f), new Vector3(16f, .5f, .3f), wood);
        Box("Rail_S", null, new Vector3(0f, .25f, -6f), new Vector3(16f, .5f, .3f), wood);
        Box("Rail_E", null, new Vector3(8f, .25f, 5f), new Vector3(.3f, .5f, 22f), wood);
        Box("Rail_W", null, new Vector3(-8f, .25f, 5f), new Vector3(.3f, .5f, 22f), wood);

        var start = new GameObject("PlayerStart");
        start.transform.SetPositionAndRotation(new Vector3(0f, .05f, 0f), Quaternion.identity); // faces +Z, the pitcher

        // Pitcher 7 m in front of the player, facing them.
        var pitcher = new GameObject("Pitcher");
        pitcher.transform.SetPositionAndRotation(new Vector3(0f, 0f, 7f), Quaternion.Euler(0f, 180f, 0f));
        Box("Body", pitcher.transform, new Vector3(0f, 1.0f, 0f), new Vector3(.7f, 1.0f, .45f), skin, local: true);
        Box("LegL", pitcher.transform, new Vector3(-.18f, .25f, 0f), new Vector3(.22f, .5f, .25f), skin, local: true);
        Box("LegR", pitcher.transform, new Vector3(.18f, .25f, 0f), new Vector3(.22f, .5f, .25f), skin, local: true);
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(pitcher.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.82f, 0f);
        head.transform.localScale = Vector3.one * .62f;
        head.GetComponent<Renderer>().sharedMaterial = skin;
        Object.DestroyImmediate(head.GetComponent<Collider>());
        Box("EyeL", pitcher.transform, new Vector3(-.11f, 1.88f, .29f), new Vector3(.07f, .12f, .03f), dark, local: true);
        Box("EyeR", pitcher.transform, new Vector3(.11f, 1.88f, .29f), new Vector3(.07f, .12f, .03f), dark, local: true);
        // Arms raised forward, holding the crate overhead.
        var armL = Box("ArmL", pitcher.transform, new Vector3(-.42f, 1.75f, .25f), new Vector3(.18f, .7f, .18f), skin, local: true);
        armL.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
        var armR = Box("ArmR", pitcher.transform, new Vector3(.42f, 1.75f, .25f), new Vector3(.18f, .7f, .18f), skin, local: true);
        armR.transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
        foreach (var c in pitcher.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c); // the crate must fly free
        var hands = new GameObject("Hands").transform;
        hands.SetParent(pitcher.transform, false);
        hands.localPosition = new Vector3(0f, 2.55f, .7f);

        var cratePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CratePath);
        GameObject crate = cratePrefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(cratePrefab, scene) : null;
        if (crate != null) crate.transform.SetPositionAndRotation(new Vector3(0f, .6f, 5.5f), Quaternion.Euler(0f, 180f, 0f));
        else Debug.LogError($"[ThrowLoopSceneBuilder] {CratePath} not found.");

        var loop = pitcher.AddComponent<ThrowLoopPitcher>();
        var so = new SerializedObject(loop);
        so.FindProperty("_cargo").objectReferenceValue = crate != null ? crate.GetComponent<CoopGame.CarrySystem.FragileCargo>() : null;
        so.FindProperty("_hands").objectReferenceValue = hands;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuild(ScenePath);
        if (previous.IsValid()) SceneManager.SetActiveScene(previous);
        EditorSceneManager.CloseScene(scene, true);
        Debug.Log($"[ThrowLoopSceneBuilder] Saved {ScenePath}");
    }

    private static Material Lit(string name, Color color)
    {
        string path = $"Assets/Scenes/Test/ThrowLoop_Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        if (!AssetDatabase.IsValidFolder("Assets/Scenes/Test/ThrowLoop_Materials"))
            AssetDatabase.CreateFolder("Assets/Scenes/Test", "ThrowLoop_Materials");
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        material.SetColor("_BaseColor", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool local = false)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        if (local) go.transform.localPosition = position; else go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static void AddToBuild(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == path)) return;
        scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
#endif
