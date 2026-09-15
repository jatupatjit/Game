#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CoopGame.Player;
using CoopGame.Network;

[InitializeOnLoad]
public static class PlayerModelSetupEditor
{
    static PlayerModelSetupEditor()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Setup();
            }
        };
    }

    [MenuItem("CoopGame/Setup Player Model and PauseMenu")]
    public static void Setup()
    {
        EnsurePauseMenuPrefab();
        SetupPlayerPrefab();
        EnsurePauseMenuInCurrentScene();
    }

    public static void EnsurePauseMenuPrefab()
    {
        string path = "Assets/Resources/PauseMenu.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
        {
            var go = new GameObject("PauseMenu");
            var pause = go.AddComponent<PauseMenu>();
            PauseMenuBuilder.Build(pause);
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log("[PlayerModelSetup] Created Assets/Resources/PauseMenu.prefab! ✅");
        }
    }

    public static void SetupPlayerPrefab()
    {
        string prefabPath = "Assets/Prefabs/Player/Player.prefab";
        string modelPath = "Assets/Models/No bone_character.fbx";

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
        {
            Debug.LogError($"[PlayerModelSetup] Model not found at {modelPath}");
            return;
        }

        // Open prefab for editing using PrefabUtility
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
        {
            GameObject playerRoot = editScope.prefabContentsRoot;

            // 1. Disable/clear primitive capsule mesh on root
            MeshFilter rootMF = playerRoot.GetComponent<MeshFilter>();
            if (rootMF != null)
            {
                rootMF.sharedMesh = null;
            }

            MeshRenderer rootMR = playerRoot.GetComponent<MeshRenderer>();
            if (rootMR != null)
            {
                rootMR.enabled = false;
            }

            // 2. Check or create CharacterVisual child
            Transform existingVisual = playerRoot.transform.Find("CharacterVisual");
            if (existingVisual != null)
            {
                Object.DestroyImmediate(existingVisual.gameObject);
            }

            GameObject visualGO = Object.Instantiate(modelAsset);
            visualGO.name = "CharacterVisual";
            visualGO.transform.SetParent(playerRoot.transform, false);

            // Calculate model bounds
            Renderer[] childRenderers = visualGO.GetComponentsInChildren<Renderer>();
            if (childRenderers.Length == 0)
            {
                Debug.LogError("[PlayerModelSetup] No renderers found in model!");
                return;
            }

            Bounds combinedBounds = childRenderers[0].bounds;
            for (int i = 1; i < childRenderers.Length; i++)
            {
                combinedBounds.Encapsulate(childRenderers[i].bounds);
            }

            // Target height for player: 2.0 meters (CharacterController height)
            float currentHeight = combinedBounds.size.y;
            float scaleFactor = 1.0f;
            if (currentHeight > 0.001f)
            {
                scaleFactor = 1.95f / currentHeight;
            }

            visualGO.transform.localScale = Vector3.one * scaleFactor;

            // Re-calculate bounds after scaling to align bottom to y = -1.0 (ground)
            combinedBounds = childRenderers[0].bounds;
            for (int i = 1; i < childRenderers.Length; i++)
            {
                combinedBounds.Encapsulate(childRenderers[i].bounds);
            }

            // CharacterController bottom is at y = -1.0 (since center is 0, height is 2)
            float targetBottomY = -1.0f;
            float offsetY = targetBottomY - combinedBounds.min.y;
            float offsetX = -combinedBounds.center.x;
            float offsetZ = -combinedBounds.center.z;

            visualGO.transform.localPosition = new Vector3(offsetX, offsetY, offsetZ);
            visualGO.transform.localRotation = Quaternion.identity;

            // 3. Attach BonelessCharacterPhysics component
            BonelessCharacterPhysics boneless = visualGO.GetComponent<BonelessCharacterPhysics>();
            if (boneless == null)
            {
                boneless = visualGO.AddComponent<BonelessCharacterPhysics>();
            }

            // 4. Find the main body renderer (body.002) for player tinting and camera clipping
            Renderer bodyRenderer = null;
            foreach (var r in childRenderers)
            {
                if (r.name.Contains("body") || r.name.Contains("BODY"))
                {
                    bodyRenderer = r;
                    break;
                }
            }
            if (bodyRenderer == null && childRenderers.Length > 0)
            {
                bodyRenderer = childRenderers[0];
            }

            // Wire NetworkPlayer._playerRenderer
            NetworkPlayer netPlayer = playerRoot.GetComponent<NetworkPlayer>();
            if (netPlayer != null && bodyRenderer != null)
            {
                SerializedObject soNet = new SerializedObject(netPlayer);
                SerializedProperty propRend = soNet.FindProperty("_playerRenderer");
                if (propRend != null)
                {
                    propRend.objectReferenceValue = bodyRenderer;
                    soNet.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // Wire PlayerCameraController._playerBodyRenderer
            PlayerCameraController camCtrl = playerRoot.GetComponent<PlayerCameraController>();
            if (camCtrl != null && bodyRenderer != null)
            {
                SerializedObject soCam = new SerializedObject(camCtrl);
                SerializedProperty propCamRend = soCam.FindProperty("_playerBodyRenderer");
                if (propCamRend != null)
                {
                    propCamRend.objectReferenceValue = bodyRenderer;
                    soCam.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            Debug.Log($"[PlayerModelSetup] Player.prefab successfully updated with No bone_character and BonelessCharacterPhysics! Scale: {scaleFactor:F2}, Pos: {visualGO.transform.localPosition} ✅");
        }

        AssetDatabase.SaveAssets();
    }

    public static void EnsurePauseMenuInCurrentScene()
    {
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!activeScene.isLoaded) return;

        var pause = Object.FindFirstObjectByType<PauseMenu>();
        if (pause == null)
        {
            var go = new GameObject("PauseMenu");
            pause = go.AddComponent<PauseMenu>();
            PauseMenuBuilder.Build(pause);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
            Debug.Log("[PlayerModelSetup] PauseMenu added and built in active scene! ✅");
        }
    }
}
#endif
