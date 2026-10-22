using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;

public static class Level01HierarchyOrganizer
{
    private const string ScenePath = "Assets/Scenes/Level01.unity";

    private static readonly float[] IslandZ = { 0f, 78f, 158f, 238f, 322f, 408f, 492f, 578f, 670f };
    private static readonly float[] BridgeZ = { 39f, 118f, 198f, 280f, 365f, 450f, 535f, 624f };

    private static readonly HashSet<string> BuilderContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "FloatingCoopRoute",
        "Level01_Additions",
        "ExtendedCoopRoute",
        "ExpandedLayout_v1",
        "ChallengeUpgrade_v2",
        "HardcoreUpgrade_v3",
        "IslandCoopGauntlet_v5",
        "ExpandedIslandComposition_v6",
        "IslandRouteComposition_v7",
        "IslandRoutePolish_v8"
    };

    private static readonly string[] HazardWords =
    {
        "hazard", "trap", "hammer", "crusher", "spinner", "rotating", "pendulum",
        "spike", "fire", "flame", "saw", "sweeper", "slalom", "collapse", "vanish",
        "falling", "rising", "timed", "clamp", "gate", "sweep"
    };

    private static readonly string[] GameplayWords =
    {
        "button", "switch", "lever", "pressure", "plate", "checkpoint", "spawn",
        "mission", "system", "trigger", "cargo", "climb"
    };

    private static readonly string[] DecorWords =
    {
        "tree", "flag", "rock", "fence", "banner", "torch", "battlement", "tower",
        "castle", "grass", "bush", "flower", "barrel", "village", "tip", "instruction", "scenery"
    };

    private sealed class Layout
    {
        public Transform Root;
        public Transform CoreRendering;
        public Transform CoreUi;
        public Transform CoreInput;
        public Transform GameplaySpawnCargo;
        public Transform GameplaySystems;
        public Transform SharedDecor;
        public Transform Archive;
        public readonly Transform[,] Islands = new Transform[9, 7];
        public readonly Transform[,] Bridges = new Transform[8, 3];
    }

    [MenuItem("Tools/Coop Game/Organize Level01 Hierarchy")]
    public static void OrganizeLevel01()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Level01Hierarchy] Level01 could not be loaded.");
            return;
        }

        GameObject[] beforeObjects = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .ToArray();
        int beforeNetworkObjects = beforeObjects.Count(go => go.GetComponent<NetworkObject>() != null);
        Dictionary<GameObject, Matrix4x4> worldBefore = beforeObjects.ToDictionary(go => go, go => go.transform.localToWorldMatrix);

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Organize Level01 Hierarchy");

        Layout layout = BuildLayout(scene);
        MoveKnownRoots(scene, layout);
        DrainRouteBuilders(scene, layout);
        ReclassifyOrganizedContent(layout);
        MoveRemainingRoots(scene, layout);
        SortChildren(layout);

        int changedWorldTransforms = CountChangedWorldTransforms(beforeObjects, worldBefore);
        GameObject[] afterObjects = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(transform => transform.gameObject)
            .ToArray();
        int afterNetworkObjects = afterObjects.Count(go => go.GetComponent<NetworkObject>() != null);

        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = EditorSceneManager.SaveScene(scene);
        Undo.CollapseUndoOperations(undoGroup);

        bool valid = saved
            && changedWorldTransforms == 0
            && afterObjects.Length >= beforeObjects.Length
            && beforeNetworkObjects == afterNetworkObjects
            && scene.GetRootGameObjects().Length == 1
            && scene.GetRootGameObjects()[0].name == "LEVEL01";

        string message =
            $"[Level01Hierarchy] {(valid ? "PASS" : "CHECK REQUIRED")} | " +
            $"content {beforeObjects.Length} | organizer {afterObjects.Length - beforeObjects.Length} | " +
            $"NetworkObjects {beforeNetworkObjects}->{afterNetworkObjects} | " +
            $"world transform changes {changedWorldTransforms} | roots {scene.rootCount} | saved {saved}";

        if (valid)
        {
            Debug.Log(message);
        }
        else
        {
            Debug.LogError(message);
        }

        Selection.activeGameObject = layout.Root.gameObject;
        EditorGUIUtility.PingObject(layout.Root.gameObject);
    }

    private static Layout BuildLayout(Scene scene)
    {
        Layout layout = new Layout();
        layout.Root = GetOrCreate(scene, null, "LEVEL01");

        Transform core = GetOrCreate(scene, layout.Root, "00_CORE");
        layout.CoreRendering = GetOrCreate(scene, core, "Rendering");
        layout.CoreUi = GetOrCreate(scene, core, "UI");
        layout.CoreInput = GetOrCreate(scene, core, "Input");

        Transform gameplay = GetOrCreate(scene, layout.Root, "01_GAMEPLAY");
        layout.GameplaySpawnCargo = GetOrCreate(scene, gameplay, "Spawn_And_Cargo");
        layout.GameplaySystems = GetOrCreate(scene, gameplay, "Mission_And_Systems");

        Transform islands = GetOrCreate(scene, layout.Root, "10_ISLANDS");
        string[] islandNames =
        {
            "Island_01_Start", "Island_02", "Island_03", "Island_04", "Island_05",
            "Island_06", "Island_07", "Island_08", "Island_09_Goal"
        };
        string[] islandFolders = { "Geometry", "Gameplay", "Hazards", "Decor", "Castle", "Delivery", "Exit_Portal" };
        for (int island = 0; island < islandNames.Length; island++)
        {
            Transform islandRoot = GetOrCreate(scene, islands, islandNames[island]);
            for (int folder = 0; folder < islandFolders.Length; folder++)
            {
                if (island != 8 && folder >= 4)
                {
                    continue;
                }

                layout.Islands[island, folder] = GetOrCreate(scene, islandRoot, islandFolders[folder]);
            }
        }

        Transform bridges = GetOrCreate(scene, layout.Root, "20_BRIDGES");
        for (int bridge = 0; bridge < 8; bridge++)
        {
            Transform bridgeRoot = GetOrCreate(scene, bridges, $"Bridge_{bridge + 1:00}_{bridge + 2:00}");
            layout.Bridges[bridge, 0] = GetOrCreate(scene, bridgeRoot, "Geometry");
            layout.Bridges[bridge, 1] = GetOrCreate(scene, bridgeRoot, "Mechanisms");
            layout.Bridges[bridge, 2] = GetOrCreate(scene, bridgeRoot, "Waiting_Areas");
        }

        layout.SharedDecor = GetOrCreate(scene, layout.Root, "30_SHARED_DECOR");
        layout.Archive = GetOrCreate(scene, layout.Root, "90_DISABLED_ARCHIVE");
        return layout;
    }

    private static void MoveKnownRoots(Scene scene, Layout layout)
    {
        MoveRoot(scene, "Main Camera", layout.CoreRendering);
        MoveRoot(scene, "Directional Light", layout.CoreRendering);
        MoveRoot(scene, "EventSystem", layout.CoreInput);
        MoveRoot(scene, "PauseMenu", layout.CoreUi);
        MoveRoot(scene, "LevelSessionHUD", layout.CoreUi);
        MoveRoot(scene, "ExpeditionHUD", layout.CoreUi);

        MoveRoot(scene, "PlayerStart", layout.GameplaySpawnCargo);
        MoveRoot(scene, "WoodenCrateItem", layout.GameplaySpawnCargo);
        MoveRoot(scene, "LevelMission", layout.GameplaySystems);

        MoveRoot(scene, "DeliveryZone", layout.Islands[8, 5]);
        MoveRoot(scene, "DeliveryExitPortal_Level02", layout.Islands[8, 6]);
        MoveRoot(scene, "PreviousObstacleTest_Preserved", layout.Archive);
    }

    private static void DrainRouteBuilders(Scene scene, Layout layout)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        foreach (GameObject root in roots)
        {
            if (root == layout.Root.gameObject || !BuilderContainers.Contains(root.name))
            {
                continue;
            }

            DrainBuilder(root.transform, layout);
        }
    }

    private static void DrainBuilder(Transform builder, Layout layout)
    {
        Transform[] children = builder.Cast<Transform>().ToArray();
        foreach (Transform child in children)
        {
            if (BuilderContainers.Contains(child.name))
            {
                DrainBuilder(child, layout);
                MovePreservingWorld(child, layout.Archive);
                continue;
            }

            MovePreservingWorld(child, ClassifyTarget(child, layout));
        }

        MovePreservingWorld(builder, layout.Archive);
    }

    private static void MoveRemainingRoots(Scene scene, Layout layout)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root == layout.Root.gameObject)
            {
                continue;
            }

            MovePreservingWorld(root.transform, ClassifyTarget(root.transform, layout));
        }
    }

    private static void ReclassifyOrganizedContent(Layout layout)
    {
        var content = new List<Transform>();
        for (int island = 0; island < 9; island++)
        {
            for (int folder = 0; folder < 7; folder++)
            {
                Transform category = layout.Islands[island, folder];
                if (category != null)
                {
                    content.AddRange(category.Cast<Transform>());
                }
            }
        }

        for (int bridge = 0; bridge < 8; bridge++)
        {
            for (int folder = 0; folder < 3; folder++)
            {
                content.AddRange(layout.Bridges[bridge, folder].Cast<Transform>());
            }
        }

        foreach (Transform item in content.Distinct().ToArray())
        {
            MovePreservingWorld(item, ClassifyTarget(item, layout));
        }
    }

    private static Transform ClassifyTarget(Transform item, Layout layout)
    {
        string name = item.name.ToLowerInvariant();

        if (name == "systems" || name.Contains("manager") || name.Contains("networkprobe"))
        {
            return layout.GameplaySystems;
        }

        if (!item.gameObject.activeSelf && (name.Contains("preserved") || name.Contains("prototype") || name.Contains("old_")))
        {
            return layout.Archive;
        }

        int explicitBridge = GetExplicitBridgeIndex(name);
        int explicitIsland = GetExplicitIslandIndex(name);
        bool bridgeByPosition = IsBridgePosition(item.position.z, out int positionalBridge);
        int bridge = explicitBridge >= 0 ? explicitBridge : positionalBridge;

        if (explicitBridge >= 0 || (explicitIsland < 0 && bridgeByPosition))
        {
            if (ContainsAny(name, "waiting", "_rest", "rest_", "bay", "landing"))
            {
                return layout.Bridges[bridge, 2];
            }

            if (ContainsAny(name, HazardWords) ||
                (ContainsAny(name, GameplayWords) && !name.Contains("cargobridge")))
            {
                return layout.Bridges[bridge, 1];
            }

            return layout.Bridges[bridge, 0];
        }

        int island = explicitIsland >= 0 ? explicitIsland : NearestIndex(item.position.z, IslandZ);
        if (island == 8)
        {
            if (ContainsAny(name, "delivery", "goal"))
            {
                return layout.Islands[island, 5];
            }

            if (ContainsAny(name, "portal", "exit"))
            {
                return layout.Islands[island, 6];
            }

            if (ContainsAny(name, "castle", "tower", "battlement"))
            {
                return layout.Islands[island, 4];
            }
        }

        if (ContainsAny(name, "waiting", "_rest", "rest_", "bay", "landing"))
        {
            return layout.Islands[island, 1];
        }

        if (ContainsAny(name, DecorWords))
        {
            return layout.Islands[island, 3];
        }

        if (ContainsAny(name, HazardWords))
        {
            return layout.Islands[island, 2];
        }

        if (ContainsAny(name, GameplayWords))
        {
            return layout.Islands[island, 1];
        }

        return layout.Islands[island, 0];
    }

    private static bool IsBridgePosition(float z, out int bridgeIndex)
    {
        int island = NearestIndex(z, IslandZ);
        bridgeIndex = NearestIndex(z, BridgeZ);
        float islandDistance = Mathf.Abs(z - IslandZ[island]);
        float bridgeDistance = Mathf.Abs(z - BridgeZ[bridgeIndex]);
        return bridgeDistance + 8f < islandDistance;
    }

    private static int GetExplicitIslandIndex(string name)
    {
        for (int i = 1; i <= 9; i++)
        {
            string twoDigits = i.ToString("00");
            if (name.Contains($"island_{twoDigits}") || name.Contains($"island{twoDigits}") ||
                name.StartsWith(twoDigits + "_") || name.Contains($"island {i}"))
            {
                return i - 1;
            }
        }

        return -1;
    }

    private static int GetExplicitBridgeIndex(string name)
    {
        if (!name.Contains("bridge"))
        {
            return -1;
        }

        for (int i = 1; i <= 8; i++)
        {
            string twoDigits = i.ToString("00");
            if (name.Contains($"bridge_{twoDigits}") || name.Contains($"bridge{twoDigits}") ||
                name.StartsWith(twoDigits + "_") || name.Contains($"bridge {i}"))
            {
                return i - 1;
            }
        }

        return -1;
    }

    private static int NearestIndex(float value, float[] values)
    {
        int result = 0;
        float best = Mathf.Abs(value - values[0]);
        for (int i = 1; i < values.Length; i++)
        {
            float distance = Mathf.Abs(value - values[i]);
            if (distance < best)
            {
                best = distance;
                result = i;
            }
        }

        return result;
    }

    private static bool ContainsAny(string value, params string[] words)
    {
        return words.Any(value.Contains);
    }

    private static Transform GetOrCreate(Scene scene, Transform parent, string name)
    {
        Transform existing = parent == null
            ? scene.GetRootGameObjects().Select(go => go.transform).FirstOrDefault(t => t.name == name)
            : parent.Cast<Transform>().FirstOrDefault(t => t.name == name);
        if (existing != null)
        {
            return existing;
        }

        GameObject created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "Create hierarchy folder");
        if (parent == null)
        {
            SceneManager.MoveGameObjectToScene(created, scene);
        }
        else
        {
            created.transform.SetParent(parent, false);
        }

        created.transform.localPosition = Vector3.zero;
        created.transform.localRotation = Quaternion.identity;
        created.transform.localScale = Vector3.one;
        return created.transform;
    }

    private static void MoveRoot(Scene scene, string name, Transform target)
    {
        GameObject root = scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);
        if (root != null)
        {
            MovePreservingWorld(root.transform, target);
        }
    }

    private static void MovePreservingWorld(Transform item, Transform target)
    {
        if (item == null || target == null || item == target || item.parent == target)
        {
            return;
        }

        Undo.SetTransformParent(item, target, "Organize Level01 hierarchy");
    }

    private static int CountChangedWorldTransforms(IEnumerable<GameObject> objects, Dictionary<GameObject, Matrix4x4> before)
    {
        int changed = 0;
        foreach (GameObject item in objects)
        {
            if (item == null || !before.TryGetValue(item, out Matrix4x4 oldMatrix))
            {
                continue;
            }

            Matrix4x4 current = item.transform.localToWorldMatrix;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (Mathf.Abs(oldMatrix[row, column] - current[row, column]) > 0.0001f)
                    {
                        changed++;
                        row = 4;
                        break;
                    }
                }
            }
        }

        return changed;
    }

    private static void SortChildren(Layout layout)
    {
        SortDirectChildren(layout.Root);
        foreach (Transform mainGroup in layout.Root)
        {
            SortDirectChildren(mainGroup);
            if (mainGroup.name != "10_ISLANDS" && mainGroup.name != "20_BRIDGES")
            {
                continue;
            }

            foreach (Transform section in mainGroup)
            {
                SortDirectChildren(section);
            }
        }
    }

    private static void SortDirectChildren(Transform parent)
    {
        Transform[] children = parent.Cast<Transform>()
            .OrderBy(child => child.name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(child => child.GetSiblingIndex())
            .ToArray();
        for (int i = 0; i < children.Length; i++)
        {
            children[i].SetSiblingIndex(i);
        }
    }
}
