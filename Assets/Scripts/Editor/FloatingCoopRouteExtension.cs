#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using CoopGame.CarrySystem;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Extends the existing floating route, preserving its training section and old scene geometry.</summary>
public static class FloatingCoopRouteExtension
{
    private const string Evidence = "E:/Unity/Verification/FloatingHard-20261008";
    private static Material _wood, _stone, _blue, _gold, _warning, _safe;
    private static Transform _root;
    private static FragileCargo _cargo;
    private static LevelMission _mission;

    [MenuItem("Tools/Expedition/Extend Floating Level01 With Climb Switches")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing the route.");
        Scene scene = SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/Scenes/Level01.unity", OpenSceneMode.Additive);
        var route = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "FloatingCoopRoute");
        if (route == null || route.transform.Find("ExtendedCoopRoute") != null)
            throw new InvalidOperationException("Requires the original floating route without an existing extension.");
        _cargo = scene.GetRootGameObjects().Select(x => x.GetComponent<FragileCargo>()).FirstOrDefault(x => x != null);
        _mission = scene.GetRootGameObjects().Select(x => x.GetComponent<LevelMission>()).FirstOrDefault(x => x != null);
        if (_cargo == null || _mission == null) throw new InvalidOperationException("Cargo and mission must exist.");
        Directory.CreateDirectory(Evidence);
        if (!EditorSceneManager.SaveScene(scene, Evidence + "/Level01.BeforeExtension.unity", true))
            throw new IOException("Could not preserve the pre-extension scene.");
        SceneManager.SetActiveScene(scene);
        _root = new GameObject("ExtendedCoopRoute").transform;
        _root.SetParent(route.transform, false);
        Undo.RegisterCreatedObjectUndo(_root.gameObject, "Extend floating co-op route");
        FloatingCoopLevelBuilder.UseExistingAssets(_root);
        _wood = Material("BridgeWood"); _stone = Material("Stone"); _blue = Material("RelayBlue"); _gold = Material("GoalGold");
        _warning = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/TrapWarning.mat");
        if (_warning == null)
        {
            _warning = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "TrapWarning" };
            _warning.SetColor("_BaseColor", new Color(1f, .42f, .04f));
            AssetDatabase.CreateAsset(_warning, "Assets/Environment/FloatingCoop/TrapWarning.mat");
        }
        _safe = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/TrapSafe.mat");
        if (_safe == null)
        {
            _safe = new Material(_warning) { name = "TrapSafe" };
            _safe.SetColor("_BaseColor", new Color(.15f, .85f, .35f));
            AssetDatabase.CreateAsset(_safe, "Assets/Environment/FloatingCoop/TrapSafe.mat");
        }

        Vector3[] centers = { new(11, 0, 120), new(-20, 0, 151), new(10, 0, 183), new(-15, 0, 215), new(11, 0, 250) };
        Vector2[] radii = { new(16, 14), new(14, 12), new(14, 12), new(13, 11), new(17, 15) };
        string[] names = { "05_RelayRest", "06_WallRelay", "07_TrapCrossing", "08_HighSwitch", "09_FinalDelivery" };
        for (int i = 1; i < centers.Length; i++)
        {
            FloatingCoopLevelBuilder.Island(names[i], centers[i], radii[i], 119 + i * 13);
            FloatingCoopLevelBuilder.Parking(names[i] + "_Rest", centers[i] + new Vector3(0, 0, -4));
            FloatingCoopLevelBuilder.Flag(names[i] + "_Flag", centers[i] + new Vector3(radii[i].x * .72f, 0, 0), i == 4 ? _gold : _blue);
            // Outer trees stay away from the diagonal bridge approaches and wall ledges.
            FloatingCoopLevelBuilder.Tree(names[i] + "_Tree", centers[i] + new Vector3(radii[i].x * .65f, 0, -radii[i].y * .4f));
        }
        var rest = route.transform.Find("05_DeliveryIsland");
        if (rest != null) { Undo.RecordObject(rest.gameObject, "Rename intermediate island"); rest.name = names[0]; }
        FloatingCoopLevelBuilder.Parking("05_RelayRest_Pad", centers[0] + new Vector3(0, 0, -3));
        FloatingCoopLevelBuilder.Sign("RestBeforeWalls", centers[0] + new Vector3(-6, 3, 0),
            "REST THE CRATE\nNEXT: CLIMB TO OPEN THE TEAM GATE", 180);
        var bridges = new GameObject[4];
        float[] widths = { 4.2f, 3.6f, 3.8f, 3.8f };
        for (int i = 0; i < bridges.Length; i++)
            bridges[i] = FloatingCoopLevelBuilder.Bridge($"0{i + 5}_CargoBridge", centers[i], radii[i], centers[i + 1], radii[i + 1], widths[i], _root);

        CreateClimbGate("WallSwitch_A", centers[1], centers[2], 4.5f);
        CreateClimbGate("WallSwitch_B", centers[3], centers[4], 5.6f);
        CreateTrap("FlameTrap_A", bridges[1].transform, widths[1], 0f);
        CreateTrap("FlameTrap_B", bridges[3].transform, widths[3], 2f);
        // Alternating posts leave a cargo-width lane; teammates must turn together.
        for (int side = -1; side <= 1; side += 2)
        {
            var bridge = bridges[0].transform;
            Vector3 position = bridge.TransformPoint(new Vector3(side * 1.5f, .75f, side * 2.4f));
            var post = FloatingCoopLevelBuilder.Cube("BridgeChicanePost", position, new Vector3(1.1f, 1.5f, .7f), _stone, _root);
            post.transform.rotation = bridge.rotation;
        }
        FloatingCoopLevelBuilder.Rock("SlalomExtra_A", centers[2] + new Vector3(-4.5f, 0, -5), new Vector3(3, 2, 2.8f));
        FloatingCoopLevelBuilder.Rock("SlalomExtra_B", centers[2] + new Vector3(3, 0, 4), new Vector3(3, 2.6f, 2.8f));
        FloatingCoopLevelBuilder.Sign("TrapInstructions", centers[2] + new Vector3(5, 3, -3),
            "GREEN = CROSS | ORANGE = WAIT\nRED FLAMES DAMAGE THE CRATE\nONE HIT PER PULSE - KEEP THE TEAM TOGETHER", 180);

        Vector3 delta = centers[4] - centers[0];
        // Move existing finish objects and their references as one destination.
        foreach (Transform item in route.transform)
        {
            if (item.name == "CastleTower" || item.name == "Battlement" || item.name == "CastleWall" ||
                item.name.StartsWith("DeliveryLanding", StringComparison.Ordinal) || item.name.StartsWith("DeliveryFlag", StringComparison.Ordinal))
            { Undo.RecordObject(item, "Move finish landmark"); item.position += delta; }
        }
        foreach (var item in scene.GetRootGameObjects())
            if (item.name == "DeliveryZone" || item.name == "DeliveryExitPortal_Level02")
            { Undo.RecordObject(item.transform, "Move delivery and exit"); item.transform.position += delta; }
        var missionData = new SerializedObject(_mission);
        missionData.FindProperty("_objective").stringValue = "Deliver the crate across nine islands - climb to unlock the gates";
        missionData.ApplyModifiedProperties();
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 440f;
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save extended Level01.");
        AssetDatabase.SaveAssets(); Selection.activeGameObject = route;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(0, -1, 120), Quaternion.Euler(48, -30, 0), 160f);
    }

    private static void CreateClimbGate(string name, Vector3 island, Vector3 next, float height)
    {
        var root = new GameObject(name); root.transform.SetParent(_root, false);
        Vector3 direction = (next - island).normalized;
        Vector3 doorPosition = island + direction * 9.8f;
        Quaternion facing = Quaternion.LookRotation(direction);
        var gate = root.AddComponent<ClimbSwitchGate>();
        var barrier = FloatingCoopLevelBuilder.Cube("LockedCargoPassage", doorPosition + Vector3.up * 1.8f, new Vector3(5.4f, 3.6f, .5f), _wood, root.transform);
        barrier.transform.rotation = facing;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 lateral = facing * Vector3.right * side;
            FloatingCoopLevelBuilder.Cube("GatePillar", doorPosition + lateral * 3.1f + Vector3.up * 2f,
                new Vector3(.6f, 4, .6f), _stone, root.transform);
            var fence = FloatingCoopLevelBuilder.Cube("GateFenceWing", doorPosition + lateral * 6.2f + Vector3.up * 1.4f,
                new Vector3(5.7f, 2.8f, .5f), _wood, root.transform);
            fence.transform.rotation = facing;
        }
        Vector3 wallPosition = island + new Vector3(-6, 0, -1);
        var wall = FloatingCoopLevelBuilder.Cube("ClimbWall", wallPosition + Vector3.up * height * .5f,
            new Vector3(8.5f, height, 1f), _stone, root.transform);
        wall.layer = 3;
        var ledge = FloatingCoopLevelBuilder.Cube("SwitchLedge", wallPosition + new Vector3(0, height - .09f, 0),
            new Vector3(8.5f, .3f, 2.5f), _wood, root.transform);
        ledge.layer = 3;
        // A blue strip identifies the climbable face without adding a snagging collider.
        FloatingCoopLevelBuilder.Cube("ClimbStripe", wallPosition + new Vector3(0, height * .5f, -.515f),
            new Vector3(.5f, height - .2f, .02f), _blue, root.transform, false);
        var button = FloatingCoopLevelBuilder.Cube("HighButton", wallPosition + new Vector3(0, height + .3f, -.65f),
            new Vector3(.8f, .5f, .25f), _warning, root.transform, false);
        var label = FloatingCoopLevelBuilder.Sign(name + "_Prompt", button.transform.position + Vector3.up * 1.4f,
            "CLIMB TO THE LEDGE\nPRESS E", 180);
        label.transform.SetParent(root.transform, true);
        FloatingCoopLevelBuilder.Sign(name + "_GroundInstructions", island + new Vector3(3, 3, -4),
            "ONE PLAYER CLIMBS TO THE BLUE STRIPE\nPRESS E ABOVE - OPEN THE CARGO GATE\nFRIENDS WAIT WITH THE CRATE", 180);
        var data = new SerializedObject(gate);
        Set(data, "_button", button.transform); Set(data, "_barrier", barrier); Set(data, "_label", label);
        Set(data, "_indicator", button.GetComponent<Renderer>()); Set(data, "_mission", _mission);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateTrap(string name, Transform bridge, float width, float offset)
    {
        var root = new GameObject(name); root.transform.SetParent(_root, false);
        root.transform.SetPositionAndRotation(bridge.position, bridge.rotation);
        var trap = root.AddComponent<TimedCargoTrap>();
        var zone = root.AddComponent<BoxCollider>(); zone.isTrigger = true;
        zone.center = new Vector3(0, 1.5f, 0); zone.size = new Vector3(width, 3f, 2.4f);
        var marker = FloatingCoopLevelBuilder.Cube("PulseMarker", bridge.position + Vector3.up * .095f,
            new Vector3(width - .1f, .025f, 2.4f), _safe, root.transform, false);
        marker.transform.rotation = bridge.rotation;
        var flames = new GameObject("PulseFlames"); flames.transform.SetParent(root.transform, false);
        // Stylized flames are visual only. Cargo HP uses the authoritative overlap zone,
        // preventing accidental collision damage plus scripted damage for one pulse.
        for (int i = 0; i < 5; i++)
        {
            Vector3 local = new Vector3(Mathf.Lerp(-width * .4f, width * .4f, i / 4f), .65f, 0);
            var flame = FloatingCoopLevelBuilder.Cube("Flame", root.transform.TransformPoint(local),
                new Vector3(.25f, 1.2f + (i % 2) * .4f, 1.8f), _warning, flames.transform, false);
            flame.transform.rotation = bridge.rotation;
        }
        flames.SetActive(false);
        var label = FloatingCoopLevelBuilder.Sign(name + "_Status", bridge.position + Vector3.up * 3.2f,
            "GREEN - CROSS TOGETHER", 180);
        label.transform.SetParent(root.transform, true);
        var data = new SerializedObject(trap);
        Set(data, "_zone", zone); Set(data, "_flames", flames); Set(data, "_marker", marker.GetComponent<Renderer>());
        Set(data, "_label", label); Set(data, "_cargo", _cargo);
        data.FindProperty("_phaseOffset").floatValue = offset;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/" + name + ".mat");
    private static void Set(SerializedObject data, string field, UnityEngine.Object value) => data.FindProperty(field).objectReferenceValue = value;
}
#endif

