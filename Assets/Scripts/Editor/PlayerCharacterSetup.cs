#if UNITY_EDITOR
using CoopGame.Player;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Puts the player model into Player.prefab: Humanoid import settings, the CharacterVisual child,
/// an Animator Controller with every gameplay parameter and empty states for clips, and an
/// owner-authority NetworkAnimator. Safe to run again after replacing the FBX or adding clips
/// (existing states and their clips are kept).
/// </summary>
public static class PlayerCharacterSetup
{
    public const string PrefabPath = "Assets/Prefabs/Player/Player.prefab";
    public const string ControllerPath = "Assets/Animations/Player/PlayerCharacter.controller";
    public const string UpperBodyMaskPath = "Assets/Animations/Player/UpperBody.mask";
    private const string FaceMaterialPath = "Assets/Models/Material_Char/Mat_Face.mat";
    private const float VisualHeight = 1.95f;

    [MenuItem("CoopGame/Player Character/Setup New Character Model")]
    public static void SetupPlayerCharacter()
    {
        string modelPath = NetworkPlayer.DefaultCharacterModelPath;
        if (!ConfigureImporter(modelPath)) return;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var controller = EnsureController();

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform old = root.transform.Find(CharacterRig.VisualName);
            if (old != null) Object.DestroyImmediate(old.gameObject);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            visual.name = CharacterRig.VisualName;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            var animator = visual.GetComponent<Animator>();
            if (animator == null) animator = visual.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(modelPath))
                if (asset is Avatar avatar) animator.avatar = avatar;
            animator.Rebind();

            float height = MeasureHeight(visual);
            if (height > 0.01f) visual.transform.localScale = Vector3.one * (VisualHeight / height);
            CenterAndGround(root, visual.transform);

            var rig = Ensure<CharacterRig>(root);
            var rigSo = new SerializedObject(rig);
            rigSo.FindProperty("_visual").objectReferenceValue = visual.transform;
            rigSo.ApplyModifiedPropertiesWithoutUndo();
            rig.SetVisual(visual.transform);

            var networkAnimator = Ensure<NetworkAnimator>(root);
            networkAnimator.Animator = animator;
            networkAnimator.AuthorityMode = NetworkAnimator.AuthorityModes.Owner;
            Ensure<PlayerAnimatorDriver>(root);
            Ensure<RagdollPoseSync>(root);
            Ensure<ActiveRagdoll>(root);

            ClearSerializedRefs(root.GetComponent<ProceduralPlayerArms>(), "_characterVisual",
                "_upperArmL", "_lowerArmL", "_wristL", "_upperArmR", "_lowerArmR", "_wristR",
                "_leftGrabClip", "_rightGrabClip");
            ClearSerializedRefs(root.GetComponent<ProceduralPlayerLegs>(), "_characterVisual",
                "_upperLegL", "_lowerLegL", "_upperLegR", "_lowerLegR");
            SetRef(root.GetComponent<ProceduralPlayerArms>(), "_characterVisual", visual.transform);
            SetRef(root.GetComponent<ProceduralPlayerLegs>(), "_characterVisual", visual.transform);
            SetRef(root.GetComponent<PlayerAppearance>(), "_faceRenderer", rig.FaceRenderer);
            SetRef(root.GetComponent<NetworkPlayer>(), "_playerRenderer", rig.BodyRenderer);
            SetRef(root.GetComponent<NetworkPlayer>(), "_characterModelPrefab", model);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[PlayerCharacterSetup] Player.prefab now uses {modelPath} (Humanoid, {controller.layers.Length} animator layers, owner NetworkAnimator).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool ConfigureImporter(string modelPath)
    {
        if (!(AssetImporter.GetAtPath(modelPath) is ModelImporter importer))
        {
            Debug.LogError("[PlayerCharacterSetup] No model at " + modelPath);
            return false;
        }

        bool changed = false;
        void Set<T>(ref T current, T value) { if (!Equals(current, value)) { current = value; changed = true; } }
        bool cams = importer.importCameras, lights = importer.importLights, anim = importer.importAnimation;
        var type = importer.animationType;
        var setup = importer.avatarSetup;
        Set(ref cams, false);
        Set(ref lights, false);
        Set(ref anim, false);
        Set(ref type, ModelImporterAnimationType.Human);
        Set(ref setup, ModelImporterAvatarSetup.CreateFromThisModel);
        importer.importCameras = cams;
        importer.importLights = lights;
        importer.importAnimation = anim;
        importer.animationType = type;
        importer.avatarSetup = setup;

        // The face uses the project's face material (alpha-clipped atlas the 12 face textures are drawn with).
        var faceMaterial = AssetDatabase.LoadAssetAtPath<Material>(FaceMaterialPath);
        if (faceMaterial != null)
        {
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), "Mat_Face");
            importer.GetExternalObjectMap().TryGetValue(id, out Object current);
            if (current != faceMaterial) { importer.AddRemap(id, faceMaterial); changed = true; }
        }

        if (changed) importer.SaveAndReimport();
        return true;
    }

    private static AnimatorController EnsureController()
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ControllerPath));
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        AddParameter(controller, "Speed", AnimatorControllerParameterType.Float);
        AddParameter(controller, "MoveX", AnimatorControllerParameterType.Float);
        AddParameter(controller, "MoveZ", AnimatorControllerParameterType.Float);
        AddParameter(controller, "VerticalSpeed", AnimatorControllerParameterType.Float);
        AddParameter(controller, "Grounded", AnimatorControllerParameterType.Bool, true);
        AddParameter(controller, "Sprinting", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "Carrying", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "CarryHands", AnimatorControllerParameterType.Int);
        AddParameter(controller, "Climbing", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "ChargingThrow", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "Jump", AnimatorControllerParameterType.Trigger);
        AddParameter(controller, "Throw", AnimatorControllerParameterType.Trigger);

        BuildBaseLayer(controller);
        BuildUpperBodyLayer(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    /// <summary>Idle / Locomotion (2D blend tree on MoveX, MoveZ) / Jump / Fall / Climb. Built once; later runs keep George's edits.</summary>
    private static void BuildBaseLayer(AnimatorController controller)
    {
        var sm = controller.layers[0].stateMachine;
        if (FindState(sm, "Idle") != null) return;

        var idle = sm.AddState("Idle", new Vector3(300, 0));
        var tree = new BlendTree
        {
            name = "Locomotion", blendType = BlendTreeType.FreeformDirectional2D,
            blendParameter = "MoveX", blendParameterY = "MoveZ", hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(tree, controller);
        var locomotion = sm.AddState("Locomotion", new Vector3(300, 100));
        locomotion.motion = tree;
        var jump = sm.AddState("Jump", new Vector3(560, -60));
        var fall = sm.AddState("Fall", new Vector3(560, 40));
        var climb = sm.AddState("Climb", new Vector3(560, 160));
        sm.defaultState = idle;

        Transition(idle, locomotion, 0.15f).AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        Transition(locomotion, idle, 0.2f).AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        var toJump = sm.AddAnyStateTransition(jump);
        toJump.duration = 0.05f; toJump.canTransitionToSelf = false;
        toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
        var jumpToFall = Transition(jump, fall, 0.1f);
        jumpToFall.hasExitTime = true; jumpToFall.exitTime = 0.9f;
        var toFall = Transition(idle, fall, 0.15f);
        toFall.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        toFall.AddCondition(AnimatorConditionMode.Less, -2f, "VerticalSpeed");
        var locoToFall = Transition(locomotion, fall, 0.15f);
        locoToFall.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        locoToFall.AddCondition(AnimatorConditionMode.Less, -2f, "VerticalSpeed");
        Transition(fall, idle, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");

        var toClimb = sm.AddAnyStateTransition(climb);
        toClimb.duration = 0.15f; toClimb.canTransitionToSelf = false;
        toClimb.AddCondition(AnimatorConditionMode.If, 0, "Climbing");
        Transition(climb, idle, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Climbing");
    }

    /// <summary>Upper-body override layer for carry and throw. The driver fades its weight in while carrying.</summary>
    private static void BuildUpperBodyLayer(AnimatorController controller)
    {
        foreach (var layer in controller.layers) if (layer.name == "UpperBody") return;

        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                             part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                             part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                             part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, upper);
            }
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
        }

        controller.AddLayer("UpperBody");
        var layers = controller.layers;
        int index = layers.Length - 1;
        layers[index].avatarMask = mask;
        layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[index].defaultWeight = 0f;
        controller.layers = layers;

        var sm = layers[index].stateMachine;
        var empty = sm.AddState("Empty", new Vector3(300, 0));
        var carry = sm.AddState("Carry", new Vector3(560, 0));
        var charge = sm.AddState("ThrowCharge", new Vector3(560, 100));
        var thrown = sm.AddState("Throw", new Vector3(820, 50));
        sm.defaultState = empty;

        Transition(empty, carry, 0.15f).AddCondition(AnimatorConditionMode.If, 0, "Carrying");
        Transition(carry, empty, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, "Carrying");
        Transition(carry, charge, 0.1f).AddCondition(AnimatorConditionMode.If, 0, "ChargingThrow");
        Transition(charge, carry, 0.1f).AddCondition(AnimatorConditionMode.IfNot, 0, "ChargingThrow");
        var toThrow = sm.AddAnyStateTransition(thrown);
        toThrow.duration = 0.05f; toThrow.canTransitionToSelf = false;
        toThrow.AddCondition(AnimatorConditionMode.If, 0, "Throw");
        var throwDone = Transition(thrown, empty, 0.2f);
        throwDone.hasExitTime = true; throwDone.exitTime = 0.9f;
    }

    private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = duration;
        return t;
    }

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (var child in sm.states) if (child.state.name == name) return child.state;
        return null;
    }

    private static void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type, bool defaultBool = false)
    {
        foreach (var p in controller.parameters) if (p.name == name) return;
        controller.AddParameter(new AnimatorControllerParameter { name = name, type = type, defaultBool = defaultBool });
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    private static void ClearSerializedRefs(Object target, params string[] names)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        foreach (var n in names)
        {
            var p = so.FindProperty(n);
            if (p != null && p.propertyType == SerializedPropertyType.ObjectReference) p.objectReferenceValue = null;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetRef(Object target, string name, Object value)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        var p = so.FindProperty(name);
        if (p == null) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static float MeasureHeight(GameObject visual)
    {
        Bounds? bounds = null;
        var baked = new Mesh();
        try
        {
            foreach (var smr in visual.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                baked.Clear();
                smr.BakeMesh(baked, true);
                foreach (var v in baked.vertices)
                {
                    Vector3 p = visual.transform.InverseTransformPoint(smr.transform.TransformPoint(v));
                    if (bounds == null) bounds = new Bounds(p, Vector3.zero);
                    else { var b = bounds.Value; b.Encapsulate(p); bounds = b; }
                }
            }
        }
        finally { Object.DestroyImmediate(baked); }
        return bounds?.size.y ?? 0f;
    }

    /// <summary>Centres the body over the controller and puts the visible soles on its floor.</summary>
    private static void CenterAndGround(GameObject root, Transform visual)
    {
        Transform hips = null;
        var animator = visual.GetComponent<Animator>();
        if (animator != null && animator.isHuman) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null)
        {
            Vector3 local = root.transform.InverseTransformPoint(hips.position);
            visual.localPosition -= new Vector3(local.x, 0f, local.z);
        }
        PlayerModelSetupEditor.AlignFeetToController(root);
    }
}
#endif
