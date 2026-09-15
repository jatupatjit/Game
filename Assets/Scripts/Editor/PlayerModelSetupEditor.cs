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
        string modelPath = "Assets/Models/Rigged_character_.fbx";

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

            // Target height for player: 1.95 meters (CharacterController height 2.0)
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

            // 3. Find SkinnedMeshRenderer (for body tinting & camera shadows)
            Renderer bodyRenderer = visualGO.GetComponentInChildren<SkinnedMeshRenderer>();
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

            // 4. Ensure & Wire ProceduralPlayerArms
            ProceduralPlayerArms procArms = playerRoot.GetComponent<ProceduralPlayerArms>();
            if (procArms == null)
            {
                procArms = playerRoot.AddComponent<ProceduralPlayerArms>();
            }
            procArms.EnsureTargetNodesCreated();

            Transform ikTargetL = playerRoot.transform.Find("IKTarget_Left");
            Transform ikTargetR = playerRoot.transform.Find("IKTarget_Right");

            Transform upperArmL = FindChildRecursive(visualGO.transform, "UpperArmL") ?? FindChildRecursive(visualGO.transform, "UpperArmR");
            Transform lowerArmL = FindChildRecursive(visualGO.transform, "LowerArm.L") ?? FindChildRecursive(visualGO.transform, "LowerArm.R");
            Transform wristL    = FindChildRecursive(visualGO.transform, "Wrist.L") ?? FindChildRecursive(visualGO.transform, "Wrist.R");

            Transform upperArmR = FindChildRecursive(visualGO.transform, "UpperArmR") ?? FindChildRecursive(visualGO.transform, "UpperArmL");
            Transform lowerArmR = FindChildRecursive(visualGO.transform, "LowerArm.R") ?? FindChildRecursive(visualGO.transform, "LowerArm.L");
            Transform wristR    = FindChildRecursive(visualGO.transform, "Wrist.R") ?? FindChildRecursive(visualGO.transform, "Wrist.L");

            SerializedObject soArms = new SerializedObject(procArms);
            soArms.FindProperty("_characterVisual").objectReferenceValue = visualGO.transform;
            soArms.FindProperty("_upperArmL").objectReferenceValue = upperArmL;
            soArms.FindProperty("_lowerArmL").objectReferenceValue = lowerArmL;
            soArms.FindProperty("_wristL").objectReferenceValue = wristL;
            soArms.FindProperty("_upperArmR").objectReferenceValue = upperArmR;
            soArms.FindProperty("_lowerArmR").objectReferenceValue = lowerArmR;
            soArms.FindProperty("_wristR").objectReferenceValue = wristR;
            if (ikTargetL != null) soArms.FindProperty("_leftTargetTransform").objectReferenceValue = ikTargetL;
            if (ikTargetR != null) soArms.FindProperty("_rightTargetTransform").objectReferenceValue = ikTargetR;
            soArms.ApplyModifiedPropertiesWithoutUndo();

            // 5. Ensure & Wire ProceduralPlayerLegs
            ProceduralPlayerLegs procLegs = playerRoot.GetComponent<ProceduralPlayerLegs>();
            if (procLegs == null)
            {
                procLegs = playerRoot.AddComponent<ProceduralPlayerLegs>();
            }

            Transform upperLegL = FindChildRecursive(visualGO.transform, "UpperLeg.L");
            Transform footL     = FindChildRecursive(visualGO.transform, "Foot.L");
            Transform upperLegR = FindChildRecursive(visualGO.transform, "UpperLeg.R");
            Transform footR     = FindChildRecursive(visualGO.transform, "Foot.R");
            Transform hip       = FindChildRecursive(visualGO.transform, "Hip");

            SerializedObject soLegs = new SerializedObject(procLegs);
            soLegs.FindProperty("_characterVisual").objectReferenceValue = visualGO.transform;
            soLegs.FindProperty("_upperLegL").objectReferenceValue = upperLegL;
            soLegs.FindProperty("_footL").objectReferenceValue = footL;
            soLegs.FindProperty("_upperLegR").objectReferenceValue = upperLegR;
            soLegs.FindProperty("_footR").objectReferenceValue = footR;
            soLegs.FindProperty("_hip").objectReferenceValue = hip;
            soLegs.ApplyModifiedPropertiesWithoutUndo();

            // 6. Clean up any orphan old VisualHand child objects that might have sphere meshes
            Transform oldVhL = playerRoot.transform.Find("VisualHand_Left");
            if (oldVhL != null && oldVhL.GetComponent<MeshFilter>() != null) Object.DestroyImmediate(oldVhL.gameObject);
            Transform oldVhR = playerRoot.transform.Find("VisualHand_Right");
            if (oldVhR != null && oldVhR.GetComponent<MeshFilter>() != null) Object.DestroyImmediate(oldVhR.gameObject);

            // 7. Configure Wallclimb arm reach parameters (Restored from commit d8d65af)
            Wallclimb wallclimb = playerRoot.GetComponent<Wallclimb>();
            if (wallclimb != null)
            {
                SerializedObject soWall = new SerializedObject(wallclimb);
                var reachProp = soWall.FindProperty("_handReachDistance");
                var normProp = soWall.FindProperty("_normalHangDistance");
                var minProp = soWall.FindProperty("_minHangDistance");
                var maxProp = soWall.FindProperty("_maxHangDistance");
                var pullProp = soWall.FindProperty("_bodyPullSpeed");
                if (reachProp != null) reachProp.floatValue = 2.0f;
                if (normProp != null) normProp.floatValue = 0.95f;
                if (minProp != null) minProp.floatValue = 0.35f;
                if (maxProp != null) maxProp.floatValue = 1.45f;
                if (pullProp != null) pullProp.floatValue = 10.0f;
                soWall.ApplyModifiedPropertiesWithoutUndo();
            }

            // 8. Configure PlayerCarry contact distance (Restored from commit d8d65af)
            CoopGame.CarrySystem.PlayerCarry playerCarry = playerRoot.GetComponent<CoopGame.CarrySystem.PlayerCarry>();
            if (playerCarry != null)
            {
                SerializedObject soCarry = new SerializedObject(playerCarry);
                var grabProp = soCarry.FindProperty("_grabContactDistance");
                var maxLiftProp = soCarry.FindProperty("_maxLiftHeight");
                var normLiftProp = soCarry.FindProperty("_normalLiftHeight");
                var minLiftProp = soCarry.FindProperty("_minLiftHeight");
                if (grabProp != null) grabProp.floatValue = 1.6f;
                if (maxLiftProp != null) maxLiftProp.floatValue = 1.7f;
                if (normLiftProp != null) normLiftProp.floatValue = 0.85f;
                if (minLiftProp != null) minLiftProp.floatValue = 0.25f;
                soCarry.ApplyModifiedPropertiesWithoutUndo();
            }

            Debug.Log($"[PlayerModelSetup] Player.prefab successfully setup with Rigged_character_ skeleton! Reach: 2.0m, Grab: 1.6m ✅");
        }

        SetupCarryablePackagePrefab();
        AssetDatabase.SaveAssets();
    }

    private static Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        foreach (Transform child in parent)
        {
            Transform found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    public static void SetupCarryablePackagePrefab()
    {
        string packagePath = "Assets/Prefabs/Items/CarryablePackage.prefab";
        using (var editScope = new PrefabUtility.EditPrefabContentsScope(packagePath))
        {
            if (editScope.prefabContentsRoot != null)
            {
                var carryable = editScope.prefabContentsRoot.GetComponent<CoopGame.CarrySystem.CarryableObject>();
                if (carryable != null)
                {
                    SerializedObject so = new SerializedObject(carryable);
                    var fwdProp = so.FindProperty("_carryForwardDistance");
                    var hProp = so.FindProperty("_carryHeightOffset");
                    if (fwdProp != null) fwdProp.floatValue = 0.72f;
                    if (hProp != null) hProp.floatValue = 0.0f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[PlayerModelSetup] CarryablePackage updated: forward=0.72, height=0.0");
                }
            }
        }
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
