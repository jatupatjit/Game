#if UNITY_EDITOR
using System.Collections.Generic;
using CoopGame.CarrySystem;
using CoopGame.EditorTools.Traps;
using CoopGame.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Level02 climb island: places Coop_CapstanLift (capstan on SpikeIslet 3, platform against the climb island cliff),
/// gives the climb island a collider, and adds the level's two checkpoints (start village, big forest).
/// Safe to run again: it replaces only the objects it made.
/// </summary>
public static class Level02CapstanSetup
{
    private const string ScenePath = "Assets/Scenes/Level02.unity";
    private const string Shared = "Assets/Environment/FloatingCoop/";
    private static readonly Vector3 CapstanPosition = new(21.66f, 7.6f, 18.2f);
    private static readonly Vector3 PlatformPosition = new(22.6f, 7.6f, 23.2f);
    private static readonly Vector3 VillagePad = new(10.5f, 8f, -1.5f);
    private static readonly Vector3 ForestPad = new(14f, 14.9f, 52.5f);

    [MenuItem("Tools/Expedition/Level02 Capstan + Checkpoints")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[Level02CapstanSetup] Stop Play mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) return "[Level02CapstanSetup] Open " + ScenePath + " first.";
        var log = new List<string>();

        foreach (string name in new[] { "L2_CapstanLift", "L2_Checkpoint_1", "L2_Checkpoint_2", "L2_CheckpointManager" })
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == name) Object.DestroyImmediate(go);

        // The capstan islet grows a little so the walk ring fits; its pivot is on the top face, so the top stays put.
        var islet = GameObject.Find("L2_02_SpikeIslet (3)");
        if (islet != null) { islet.transform.localScale = Vector3.one * .8f; log.Add("SpikeIslet 3 scale 0.8"); }
        var climb = GameObject.Find("เกาะปีน");
        if (climb != null && climb.GetComponent<Collider>() == null)
        {
            climb.AddComponent<MeshCollider>();
            log.Add("climb island collider");
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CapstanLiftBuilder.PrefabPath);
        if (prefab == null) return "[Level02CapstanSetup] Build the capstan prefab first (Tools/Traps/Build Coop Capstan Lift).";
        var lift = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        lift.name = "L2_CapstanLift";
        lift.transform.SetPositionAndRotation(CapstanPosition, Quaternion.identity);
        Vector3 offset = PlatformPosition - CapstanPosition;
        var platform = lift.transform.Find("Platform");
        var frame = lift.transform.Find("LiftFrame");
        platform.localPosition = offset;
        frame.localPosition = new Vector3(offset.x, 0f, offset.z);
        CapstanLiftBuilder.FitWindRope(lift);
        log.Add("capstan at " + CapstanPosition + ", platform at " + PlatformPosition);

        var cargo = Object.FindAnyObjectByType<FragileCargo>();
        var pads = new[]
        {
            MakePad(1, VillagePad, Vector3.right, cargo),
            MakePad(2, ForestPad, Vector3.forward, cargo),
        };
        var managerGo = new GameObject("L2_CheckpointManager");
        SceneManager(managerGo, scene);
        managerGo.AddComponent<NetworkObject>();
        var manager = managerGo.AddComponent<CheckpointManager>();
        var ms = new SerializedObject(manager);
        var list = ms.FindProperty("_pads");
        list.arraySize = pads.Length;
        for (int i = 0; i < pads.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = pads[i];
        ms.ApplyModifiedPropertiesWithoutUndo();
        log.Add("checkpoints: village " + VillagePad + ", forest " + ForestPad);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "[Level02CapstanSetup] " + string.Join("; ", log);
    }

    /// <summary>
    /// The FBX art George placed in Level02 has no colliders. Pine trunks and the wooden barricade logs get convex
    /// mesh colliders; the round trees (one mesh with the canopy) get a trunk capsule only, so the canopy stays open.
    /// </summary>
    [MenuItem("Tools/Expedition/Level02 Art Colliders")]
    public static void ArtCollidersMenu() => Debug.Log(AddArtColliders());

    public static string AddArtColliders()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "[Level02CapstanSetup] Stop Play mode first.";
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) return "[Level02CapstanSetup] Open " + ScenePath + " first.";
        int added = 0;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.GetComponent<Collider>() != null) continue;
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer.gameObject);
                string file = source != null ? System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(source)) : "";
                if (file == "ต้นสน.fbx" && renderer.name == "ทรงกระบอก.001" ||
                    file == "หนามไม้.fbx" && renderer.name.StartsWith("ทรงกระบอก"))
                {
                    renderer.gameObject.AddComponent<MeshCollider>().convex = true;
                    added++;
                }
                else if (file == "ต้นไม้กลม.fbx")
                {
                    AddTrunkCapsule(renderer);
                    added++;
                }
            }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return "[Level02CapstanSetup] Added " + added + " art colliders.";
    }

    private static void AddTrunkCapsule(MeshRenderer tree)
    {
        Transform t = tree.transform;
        Bounds world = tree.bounds;
        const float radius = .45f, height = 3.4f;
        Vector3 up = t.InverseTransformDirection(Vector3.up);
        int axis = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? (Mathf.Abs(up.x) > Mathf.Abs(up.z) ? 0 : 2) : (Mathf.Abs(up.y) > Mathf.Abs(up.z) ? 1 : 2);
        Vector3 scale = t.lossyScale;
        float along = Mathf.Abs(scale[axis]);
        float across = Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
        var capsule = tree.gameObject.AddComponent<CapsuleCollider>();
        capsule.direction = axis;
        capsule.center = t.InverseTransformPoint(new Vector3(world.center.x, world.min.y + height * .5f, world.center.z));
        capsule.height = height / along;
        capsule.radius = radius / across;
    }

    private static void SceneManager(GameObject go, UnityEngine.SceneManagement.Scene scene) =>
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);

    private static CheckpointPad MakePad(int number, Vector3 point, Vector3 route, FragileCargo cargo)
    {
        var gray = AssetDatabase.LoadAssetAtPath<Material>(Shared + "InactivePressurePad.mat");
        var gold = AssetDatabase.LoadAssetAtPath<Material>(Shared + "GoalGold.mat");
        var wood = AssetDatabase.LoadAssetAtPath<Material>(Shared + "BridgeWood.mat");
        var root = new GameObject("L2_Checkpoint_" + number).transform;
        root.SetPositionAndRotation(point, Quaternion.LookRotation(route));
        root.gameObject.AddComponent<NetworkObject>();
        var pad = root.gameObject.AddComponent<CheckpointPad>();
        var lamps = new List<Object> { Box("PadSlab", root, new Vector3(0, .04f, 0), new Vector3(4.6f, .08f, 4.6f), gray).GetComponent<Renderer>() };
        for (int cx = -1; cx <= 1; cx += 2)
            for (int cz = -1; cz <= 1; cz += 2)
                lamps.Add(Box("PadLamp", root, new Vector3(cx * 2.05f, .35f, cz * 2.05f), new Vector3(.3f, .7f, .3f), gray).GetComponent<Renderer>());
        Box("FlagPole", root, new Vector3(-2.3f, 1.5f, 2.3f), new Vector3(.1f, 3f, .1f), wood);
        var cloth = Box("FlagCloth", root, new Vector3(-1.75f, .5f, 2.3f), new Vector3(1.1f, .7f, .05f), gray);
        lamps.Add(cloth.GetComponent<Renderer>());
        var cargoPoint = new GameObject("CargoPoint").transform;
        cargoPoint.SetParent(root, false); cargoPoint.localPosition = new Vector3(0, .9f, 0);
        var playerPoint = new GameObject("PlayerPoint").transform;
        playerPoint.SetParent(root, false); playerPoint.localPosition = new Vector3(0, .15f, -3.4f);
        var labelGo = new GameObject("PadLabel");
        labelGo.transform.SetParent(root, false);
        labelGo.transform.localPosition = new Vector3(0, 2.7f, 0);
        var label = labelGo.AddComponent<TextMesh>();
        label.text = "เช็คพอยต์ " + number; label.fontSize = 112; label.characterSize = .018f;
        label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = Color.white;

        var so = new SerializedObject(pad);
        so.FindProperty("_cargo").objectReferenceValue = cargo;
        so.FindProperty("_number").intValue = number;
        so.FindProperty("_cargoPoint").objectReferenceValue = cargoPoint;
        so.FindProperty("_playerPoint").objectReferenceValue = playerPoint;
        var lampList = so.FindProperty("_lamps");
        lampList.arraySize = lamps.Count;
        for (int i = 0; i < lamps.Count; i++) lampList.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
        so.FindProperty("_inactiveMaterial").objectReferenceValue = gray;
        so.FindProperty("_activeMaterial").objectReferenceValue = gold;
        so.FindProperty("_flag").objectReferenceValue = cloth.transform;
        so.FindProperty("_flagDownY").floatValue = .5f;
        so.FindProperty("_flagUpY").floatValue = 2.5f;
        so.FindProperty("_label").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();
        return pad;
    }

    private static GameObject Box(string name, Transform parent, Vector3 local, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }
}
#endif
