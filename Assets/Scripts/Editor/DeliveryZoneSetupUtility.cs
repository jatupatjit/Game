using CoopGame.CarrySystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.EditorTools
{
    /// <summary>Refreshes delivery assets without rebuilding the configured prefab or mission.</summary>
    public static class DeliveryZoneSetupUtility
    {
        [MenuItem("CoopGame/Open Lobby")]
        public static void OpenLobby()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene("Assets/Scenes/Lobby.unity");
        }

        [MenuItem("CoopGame/Setup Delivery Zone & Place in Level01")]
        public static void SetupAndPlaceInLevel01()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Shader shader = Shader.Find("DontDropIt/DeliveryZoneRing");
            if (shader == null)
            {
                Debug.LogError("[DeliveryZoneSetup] DeliveryZoneRing shader is missing.");
                return;
            }
            const string materialPath = "Assets/Materials/DeliveryZone_Mat.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                    AssetDatabase.CreateFolder("Assets", "Materials");
                material = new Material(shader);
                material.SetColor("_Color", new Color(1f, 0.15f, 0.15f));
                material.SetFloat("_InnerAlpha", 0.1f);
                material.SetFloat("_RingThickness", 0.06f);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            const string prefabPath = "Assets/Prefabs/DeliveryZone.prefab";
            bool existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
            GameObject root = existingPrefab
                ? PrefabUtility.LoadPrefabContents(prefabPath) : new GameObject("DeliveryZone");
            GameObject prefab;
            try
            {
                DeliveryZone zone = root.GetComponent<DeliveryZone>();
                if (zone == null) zone = root.AddComponent<DeliveryZone>();
                zone.EnsureVisuals();
                Transform ring = root.transform.Find("Zone_Circle_Visual");
                if (ring != null && ring.TryGetComponent(out Renderer renderer))
                    renderer.sharedMaterial = material;
                if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                    AssetDatabase.CreateFolder("Assets", "Prefabs");
                prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                if (existingPrefab) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            SceneSetup[] previousScenes = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/Level01.unity");
                DeliveryZone zone = Object.FindAnyObjectByType<DeliveryZone>();
                if (zone == null)
                {
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    zone = instance.GetComponent<DeliveryZone>();
                    Undo.RegisterCreatedObjectUndo(instance, "Create delivery zone");
                }
                GameObject ground = GameObject.Find("floating Ground (13)");
                if (ground != null && ground.TryGetComponent(out Collider groundCollider))
                {
                    Bounds bounds = groundCollider.bounds;
                    zone.transform.position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
                }
                zone.EnsureVisuals();
                // Keep the mission's wooden crate; never resurrect the removed package.
                var mission = Object.FindAnyObjectByType<CoopGame.Network.LevelMission>();
                FragileCargo cargo = mission != null ? mission.Cargo : null;
                if (cargo == null) cargo = Object.FindAnyObjectByType<FragileCargo>();
                if (cargo != null)
                {
                    zone.TargetDeliveryObject = cargo.gameObject;
                    zone.DetectionMode = DeliveryDetectionMode.SpecificObjectOnly;
                }
                else Debug.LogWarning("[DeliveryZoneSetup] No cargo in Level01; assign a target before playing.");
                EditorUtility.SetDirty(zone);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("[DeliveryZoneSetup] Refreshed Level01 delivery zone and preserved mission references.");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
            }
        }
    }
}
