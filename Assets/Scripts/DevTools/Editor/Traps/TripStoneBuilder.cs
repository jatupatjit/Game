#if UNITY_EDITOR
using System.Collections.Generic;
using CoopGame.Traps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools.Traps
{
    /// <summary>
    /// Builds the trip stone prefabs (Assets/Prefabs/Traps/Obstacles/Obstacle_TripStone, _TripStones_Cluster) and
    /// scatters them along the Level02 route. Safe to run again: it replaces only its own prefabs and L2_TripStones.
    /// </summary>
    public static class TripStoneBuilder
    {
        public const string SinglePath = "Assets/Prefabs/Traps/Obstacles/Obstacle_TripStone.prefab";
        public const string ClusterPath = "Assets/Prefabs/Traps/Obstacles/Obstacle_TripStones_Cluster.prefab";
        private const string RockMaterial = "Assets/Prefabs/Traps/Materials/Trap_Rock.mat";
        private const string Level02 = "Assets/Scenes/Level02.unity";

        // Flat route spots (x, z); height comes from the ground below. Clusters marked true.
        private static readonly (float x, float z, bool cluster)[] Level02Spots =
        {
            (13f, .5f, false), (18.5f, -.3f, false), (23f, 8.2f, true), (23.6f, 28.6f, false),
            (21f, 35f, false), (23.6f, 37.6f, true), (18.6f, 43f, false), (9f, 60f, true),
            (5f, 66f, false), (3f, 72f, false), (2.5f, 79f, true), (-8f, 95f, false),
            (-13f, 101.5f, false), (-6f, 117f, true), (6f, 126f, false),
        };

        [MenuItem("Tools/Traps/Build Trip Stones")]
        public static void BuildMenu() => Debug.Log(BuildPrefabs());

        [MenuItem("Tools/Expedition/Level02 Trip Stones")]
        public static void PlaceMenu() => Debug.Log(PlaceInLevel02());

        public static string BuildPrefabs()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[TripStoneBuilder] Stop Play mode first.";
            var rock = AssetDatabase.LoadAssetAtPath<Material>(RockMaterial);
            var previous = EditorSceneManager.GetActiveScene();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(temp);
            try
            {
                var single = new GameObject("Obstacle_TripStone");
                Stone(single.transform, Vector3.zero, new Vector3(.5f, .24f, .42f), 20f, rock);
                Save(single, SinglePath);

                var cluster = new GameObject("Obstacle_TripStones_Cluster");
                Stone(cluster.transform, new Vector3(-.55f, 0, .1f), new Vector3(.46f, .22f, .4f), 35f, rock);
                Stone(cluster.transform, new Vector3(.45f, 0, .35f), new Vector3(.36f, .18f, .32f), 110f, rock);
                Stone(cluster.transform, new Vector3(.1f, 0, -.5f), new Vector3(.42f, .2f, .36f), 250f, rock);
                Save(cluster, ClusterPath);
            }
            finally
            {
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(temp, true);
            }
            return "[TripStoneBuilder] Saved " + SinglePath + " and " + ClusterPath;
        }

        public static string PlaceInLevel02()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[TripStoneBuilder] Stop Play mode first.";
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Level02) return "[TripStoneBuilder] Open " + Level02 + " first.";
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "L2_TripStones") Object.DestroyImmediate(go);
            var single = AssetDatabase.LoadAssetAtPath<GameObject>(SinglePath);
            var cluster = AssetDatabase.LoadAssetAtPath<GameObject>(ClusterPath);
            if (single == null || cluster == null) return "[TripStoneBuilder] Build the prefabs first.";
            var root = new GameObject("L2_TripStones");
            var placed = new List<string>();
            foreach (var (x, z, isCluster) in Level02Spots)
            {
                if (!Physics.Raycast(new Vector3(x, 60f, z), Vector3.down, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore))
                    continue;
                var stone = (GameObject)PrefabUtility.InstantiatePrefab(isCluster ? cluster : single, scene);
                stone.transform.SetParent(root.transform, true);
                stone.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0, (x * 37f + z * 11f) % 360f, 0));
                placed.Add(hit.point.ToString("F1") + " on " + hit.collider.name);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return "[TripStoneBuilder] Placed " + placed.Count + ":\n" + string.Join("\n", placed);
        }

        private static void Stone(Transform parent, Vector3 position, Vector3 size, float yaw, Material mat)
        {
            var root = new GameObject("TripStone").transform;
            root.SetParent(parent, false);
            root.localPosition = position;
            root.localEulerAngles = new Vector3(0, yaw, 0);
            root.gameObject.AddComponent<TripStone>();
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mesh.name = "Rock";
            mesh.transform.SetParent(root, false);
            // Sunk a little so it reads as a stone in the ground, not a ball on it.
            mesh.transform.localPosition = new Vector3(0, size.y * .3f, 0);
            mesh.transform.localEulerAngles = new Vector3(8f, 0, -6f);
            mesh.transform.localScale = size;
            mesh.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.AddComponent<MeshCollider>().convex = true;
        }

        private static void Save(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) throw new System.Exception("Could not save " + path);
        }
    }
}
#endif
