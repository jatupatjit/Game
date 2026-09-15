using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CoopGame.CarrySystem;
using CoopGame.Player;
using CoopGame.Network;

namespace CoopGame.EditorTools
{
    public static class ApplyArmReachAndUIUpdates
    {
        [MenuItem("CoopGame/Apply Arm Reach & UI Updates")]
        public static void ApplyAndVerify()
        {
            Debug.Log("[ApplyArmReachAndUIUpdates] Starting updates...");

            // 1. Update CarryablePackage Prefab
            string packagePath = "Assets/Prefabs/Items/CarryablePackage.prefab";
            GameObject packagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(packagePath);
            if (packagePrefab != null)
            {
                var carryable = packagePrefab.GetComponent<CarryableObject>();
                if (carryable != null)
                {
                    SerializedObject so = new SerializedObject(carryable);
                    var fwdProp = so.FindProperty("_carryForwardDistance");
                    var hProp = so.FindProperty("_carryHeightOffset");
                    if (fwdProp != null) fwdProp.floatValue = 0.72f;
                    if (hProp != null) hProp.floatValue = 0.0f;
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(packagePrefab);
                    Debug.Log($"[ApplyArmReachAndUIUpdates] CarryablePackage updated: forward={fwdProp?.floatValue}, height={hProp?.floatValue}");
                }
            }

            // 2. Update Player Prefab
            string playerPath = "Assets/Prefabs/Player/Player.prefab";
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath);
            if (playerPrefab != null)
            {
                var wallclimb = playerPrefab.GetComponent<Wallclimb>();
                if (wallclimb != null)
                {
                    SerializedObject so = new SerializedObject(wallclimb);
                    var reachProp = so.FindProperty("_handReachDistance");
                    var normProp = so.FindProperty("_normalHangDistance");
                    var minProp = so.FindProperty("_minHangDistance");
                    var maxProp = so.FindProperty("_maxHangDistance");
                    if (reachProp != null) reachProp.floatValue = 0.85f;
                    if (normProp != null) normProp.floatValue = 0.48f;
                    if (minProp != null) minProp.floatValue = 0.15f;
                    if (maxProp != null) maxProp.floatValue = 0.65f;
                    so.ApplyModifiedProperties();
                    Debug.Log($"[ApplyArmReachAndUIUpdates] Wallclimb updated: reach={reachProp?.floatValue}, norm={normProp?.floatValue}, min={minProp?.floatValue}, max={maxProp?.floatValue}");
                }

                var playerCarry = playerPrefab.GetComponent<PlayerCarry>();
                if (playerCarry != null)
                {
                    SerializedObject so = new SerializedObject(playerCarry);
                    var grabProp = so.FindProperty("_grabContactDistance");
                    if (grabProp != null) grabProp.floatValue = 0.95f;
                    so.ApplyModifiedProperties();
                    Debug.Log($"[ApplyArmReachAndUIUpdates] PlayerCarry updated: grabContact={grabProp?.floatValue}");
                }
                EditorUtility.SetDirty(playerPrefab);
            }

            // 3. Open SampleScene and build UI in scene
            string scenePath = "Assets/Scenes/SampleScene.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            var lobbyUI = Object.FindAnyObjectByType<LobbyUI>(FindObjectsInactive.Include);
            if (lobbyUI != null)
            {
                LobbyUIBuilder.Build(lobbyUI);
                Debug.Log("[ApplyArmReachAndUIUpdates] LobbyUI rebuilt in SampleScene.");
            }

            var pauseMenu = Object.FindAnyObjectByType<PauseMenu>(FindObjectsInactive.Include);
            if (pauseMenu != null)
            {
                PauseMenuBuilder.Build(pauseMenu);
                Debug.Log("[ApplyArmReachAndUIUpdates] PauseMenu rebuilt in SampleScene.");
            }

            var roomHUD = Object.FindAnyObjectByType<RoomCodeHUD>(FindObjectsInactive.Include);
            if (roomHUD != null)
            {
                RoomCodeHUDBuilder.Build(roomHUD);
                Debug.Log("[ApplyArmReachAndUIUpdates] RoomCodeHUD rebuilt in SampleScene.");
            }

            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ApplyArmReachAndUIUpdates] SampleScene saved.");

            // 4. Update PauseMenu prefab in Resources
            string pausePrefabPath = "Assets/Resources/PauseMenu.prefab";
            GameObject pausePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pausePrefabPath);
            if (pausePrefab != null)
            {
                var pm = pausePrefab.GetComponent<PauseMenu>();
                if (pm != null)
                {
                    PauseMenuBuilder.Build(pm);
                    EditorUtility.SetDirty(pausePrefab);
                    Debug.Log("[ApplyArmReachAndUIUpdates] PauseMenu.prefab updated.");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[ApplyArmReachAndUIUpdates] All updates applied and assets saved successfully! ✅");
        }
    }
}
