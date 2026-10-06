#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CoopGame.Player;
using CoopGame.Network;

public static class PlayerModelSetupEditor
{
    // Setup is intentionally manual. Rebuilding CharacterVisual during every
    // editor launch changes bone file IDs and rest rotations in Player.prefab.
    [MenuItem("CoopGame/Setup Player Model and Hand Animations")]
    [MenuItem("CoopGame/Setup Player Model and PauseMenu")]
    public static void Setup()
    {
        EnsurePauseMenuPrefab();
        SetupPlayerPrefab();
        EnsurePauseMenuInCurrentScene();
    }

    // Imported renderer bounds include animation padding. Bake the rest mesh
    // when positioning the visual so that the visible soles meet the floor.
    public static void AlignFeetToController(GameObject playerRoot)
    {
        var controller = playerRoot.GetComponent<CharacterController>();
        var visual = playerRoot.transform.Find("CharacterVisual");
        if (controller == null || visual == null) return;
        float bottom = float.PositiveInfinity;
        var baked = new Mesh();
        try
        {
            foreach (var renderer in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                baked.Clear();
                renderer.BakeMesh(baked, true);
                foreach (var vertex in baked.vertices)
                    bottom = Mathf.Min(bottom, playerRoot.transform.InverseTransformPoint(
                        renderer.transform.TransformPoint(vertex)).y);
            }
        }
        finally { Object.DestroyImmediate(baked); }
        if (float.IsInfinity(bottom) || float.IsNaN(bottom)) return;
        float floor = controller.center.y - controller.height * 0.5f - controller.skinWidth;
        visual.localPosition += Vector3.up * (floor - bottom);
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
        string modelPath = "Assets/Models/Rigged_Hand_character_ (2).fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null)
        {
            modelPath = "Assets/Models/Rigged_character_.fbx";
        }

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
        {
            Debug.LogError($"[PlayerModelSetup] Model not found at {modelPath}");
            return;
        }

        // Extract and isolate grab animations from Rigged_Hand_character
        ExtractAndSetupGrabAnimations(out AnimationClip leftGrabClip, out AnimationClip rightGrabClip);

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

            // Configure accurate hitbox for CharacterController & CapsuleCollider matching 1.95m model
            CharacterController cc = playerRoot.GetComponent<CharacterController>();
            if (cc != null)
            {
                SerializedObject soCC = new SerializedObject(cc);
                var hProp = soCC.FindProperty("m_Height");
                var rProp = soCC.FindProperty("m_Radius");
                var cProp = soCC.FindProperty("m_Center");
                if (hProp != null) hProp.floatValue = 1.95f;
                if (rProp != null) rProp.floatValue = 0.30f;
                if (cProp != null) cProp.vector3Value = new Vector3(0f, -0.025f, 0f);
                soCC.ApplyModifiedPropertiesWithoutUndo();
            }

            CapsuleCollider capCol = playerRoot.GetComponent<CapsuleCollider>();
            if (capCol != null)
            {
                SerializedObject soCap = new SerializedObject(capCol);
                var hProp = soCap.FindProperty("m_Height");
                var rProp = soCap.FindProperty("m_Radius");
                var cProp = soCap.FindProperty("m_Center");
                if (hProp != null) hProp.floatValue = 1.95f;
                if (rProp != null) rProp.floatValue = 0.30f;
                if (cProp != null) cProp.vector3Value = new Vector3(0f, -0.025f, 0f);
                soCap.ApplyModifiedPropertiesWithoutUndo();
            }

            AlignFeetToController(playerRoot);

            // Rebind appearance after a manual model rebuild, preserving its catalog.
            var appearance = playerRoot.GetComponent<PlayerAppearance>();
            if (appearance != null)
            {
                foreach (var renderer in childRenderers)
                {
                    if (!renderer.name.StartsWith("face_", System.StringComparison.OrdinalIgnoreCase)) continue;
                    renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Models/Material_Char/Mat_Face.mat");
                    var appearanceSettings = new SerializedObject(appearance);
                    appearanceSettings.FindProperty("_faceRenderer").objectReferenceValue = renderer;
                    appearanceSettings.ApplyModifiedPropertiesWithoutUndo();
                    break;
                }
            }

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
                }
                SerializedProperty propModel = soNet.FindProperty("_characterModelPrefab");
                if (propModel != null) propModel.objectReferenceValue = modelAsset;
                soNet.ApplyModifiedPropertiesWithoutUndo();
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
            if (leftGrabClip != null) soArms.FindProperty("_leftGrabClip").objectReferenceValue = leftGrabClip;
            if (rightGrabClip != null) soArms.FindProperty("_rightGrabClip").objectReferenceValue = rightGrabClip;
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
                if (maxLiftProp != null) maxLiftProp.floatValue = 2.2f;
                if (normLiftProp != null) normLiftProp.floatValue = 1.05f;
                if (minLiftProp != null) minLiftProp.floatValue = 0.45f;
                soCarry.ApplyModifiedPropertiesWithoutUndo();
            }

            // 9. Ensure PlayerStamina and PlayerStaminaUI components
            PlayerStamina stamina = playerRoot.GetComponent<PlayerStamina>();
            if (stamina == null)
            {
                stamina = playerRoot.AddComponent<PlayerStamina>();
            }

            PlayerStaminaUI staminaUI = playerRoot.GetComponent<PlayerStaminaUI>();
            if (staminaUI == null)
            {
                staminaUI = playerRoot.AddComponent<PlayerStaminaUI>();
            }

            Debug.Log($"[PlayerModelSetup] Player.prefab successfully setup with Rigged_character_ skeleton, PlayerStamina, & PlayerStaminaUI! Reach: 2.0m, Grab: 1.6m ✅");
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
                    if (fwdProp != null) fwdProp.floatValue = 0.65f;
                    if (hProp != null) hProp.floatValue = 1.05f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"[PlayerModelSetup] CarryablePackage updated: forward=0.65, height=1.05");
                }
            }
        }
    }

    public static void EnsurePauseMenuInCurrentScene()
    {
        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!activeScene.isLoaded) return;

        var pause = Object.FindAnyObjectByType<PauseMenu>();
        if (pause == null)
        {
            var go = new GameObject("PauseMenu");
            pause = go.AddComponent<PauseMenu>();
            PauseMenuBuilder.Build(pause);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(activeScene);
            Debug.Log("[PlayerModelSetup] PauseMenu added and built in active scene! ✅");
        }
    }

    public static void ExtractAndSetupGrabAnimations(out AnimationClip leftGrabClip, out AnimationClip rightGrabClip)
    {
        leftGrabClip = null;
        rightGrabClip = null;

        string modelPath = "Assets/Models/Rigged_Hand_character_ (2).fbx";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(modelPath) == null)
        {
            modelPath = "Assets/Models/Rigged_character_.fbx";
        }

        Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
        // Rig labels are opposite the player's spatial sides: Wrist.R is at -X
        // (player left), Wrist.L at +X (player right), matching LocateBones in IK.
        AnimationClip fbxRightGrab = null; // Player right, rig L finger bones
        AnimationClip fbxLeftGrab = null;  // Player left, rig R finger bones

        foreach (var a in allAssets)
        {
            if (a is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                if (clip.name.EndsWith("RightGrab") || clip.name.Contains("RightGrab"))
                {
                    fbxRightGrab = clip;
                }
                else if (clip.name.EndsWith("LeftGrab") || clip.name.Contains("LeftGrab"))
                {
                    fbxLeftGrab = clip;
                }
            }
        }

        if (fbxRightGrab == null && fbxLeftGrab == null)
        {
            Debug.LogWarning($"[PlayerModelSetup] No grab clips found in {modelPath}");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Animations"))
        {
            AssetDatabase.CreateFolder("Assets", "Animations");
        }

        // 1. Player left uses the source LeftGrab and rig R finger bones.
        string leftClipPath = "Assets/Animations/HandGrab_Left.anim";
        if (fbxLeftGrab != null)
        {
            leftGrabClip = CreateSanitizedFingerClip(fbxLeftGrab, leftClipPath, useRigLeftBones: false);
        }

        // 2. Player right uses the source RightGrab and rig L finger bones.
        string rightClipPath = "Assets/Animations/HandGrab_Right.anim";
        if (fbxRightGrab != null)
        {
            rightGrabClip = CreateSanitizedFingerClip(fbxRightGrab, rightClipPath, useRigLeftBones: true);
        }

        AssetDatabase.SaveAssets();
    }

    private static AnimationClip CreateSanitizedFingerClip(AnimationClip sourceClip, string targetPath, bool useRigLeftBones)
    {
        AnimationClip newClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(targetPath);
        if (newClip == null)
        {
            newClip = new AnimationClip();
            AssetDatabase.CreateAsset(newClip, targetPath);
        }
        else
        {
            newClip.ClearCurves();
        }

        newClip.name = System.IO.Path.GetFileNameWithoutExtension(targetPath);
        newClip.frameRate = sourceClip.frameRate;

        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(sourceClip);
        int copiedCurves = 0;

        foreach (var b in bindings)
        {
            // Only keep curves that animate finger phalanges for the specified hand!
            // Finger keywords: thumb1, thumb2, Index1, Index2, Middle1, Middle2, Pinky1, Pinky2
            bool isFingerJoint = (b.path.IndexOf("thumb1", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  b.path.IndexOf("thumb2", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  b.path.IndexOf("Index1", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  b.path.IndexOf("Index2", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  b.path.IndexOf("Middle1", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  b.path.IndexOf("Middle2", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  b.path.IndexOf("Pinky1", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                                  b.path.IndexOf("Pinky2", System.StringComparison.OrdinalIgnoreCase) >= 0);

            if (!isFingerJoint) continue;

            // Make sure hand matches side
            if (useRigLeftBones && (b.path.Contains(".R") || b.path.Contains("handR"))) continue;
            if (!useRigLeftBones && (b.path.Contains(".L") || b.path.Contains("handL"))) continue;

            AnimationCurve curve = AnimationUtility.GetEditorCurve(sourceClip, b);
            if (curve != null)
            {
                AnimationUtility.SetEditorCurve(newClip, b, curve);
                copiedCurves++;
            }
        }

        EditorUtility.SetDirty(newClip);
        Debug.Log($"[PlayerModelSetup] Created sanitized clip {targetPath} with {copiedCurves} finger curves from {sourceClip.name} ✅");
        return newClip;
    }
}
#endif
