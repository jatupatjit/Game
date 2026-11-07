#if UNITY_EDITOR
using System.IO;
using CoopGame.CarrySystem;
using CoopGame.Obstacles;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builder utility for creating and registering the See-Saw and Counterweight prefabs.
    /// Menu items:
    /// - Tools/SeeSaw/Build SeeSaw Prefabs
    /// - Tools/SeeSaw/Spawn SeeSaw In Current Scene
    /// </summary>
    [InitializeOnLoad]
    public static class SeeSawBuilder
    {
        private const string PrimaryFolder = "Assets/prefap";
        private const string StandardFolder = "Assets/Prefabs/SeeSaw";

        static SeeSawBuilder()
        {
            EditorApplication.delayCall += AutoBuildIfMissing;
        }

        [InitializeOnLoadMethod]
        private static void InitOnLoad()
        {
            EditorApplication.delayCall += AutoBuildIfMissing;
        }

        private static void AutoBuildIfMissing()
        {
            string seeSawPath = $"{PrimaryFolder}/SeeSaw.prefab";
            string weightPath = $"{PrimaryFolder}/SeeSawWeight.prefab";
            if (!File.Exists(seeSawPath) || !File.Exists(weightPath))
            {
                BuildAllPrefabs();
            }
        }

        private const string WoodMatPath = "Assets/Materials/Medieval/Wood.mat";
        private const string StoneMatPath = "Assets/Materials/Medieval/Stone.mat";
        private const string MetalMatPath = "Assets/Materials/CargoVariants/HeavyMetal.mat";
        private const string GlowMatPath = "Assets/Materials/Level01BackRoute/DirectionCyan.mat";
        private const string GoldMatPath = "Assets/Materials/Level01BackRoute/LandingGold.mat";
        private const string StripeMatPath = "Assets/Materials/CargoVariants/Stripe.mat";
        private const string HazardMatPath = "Assets/Materials/Level01BackRoute/ObstacleOrange.mat";

        private const string ImpactAudioPath = "Assets/Resources/Audio/Impact.wav";
        private const string HeavyDropAudioPath = "Assets/Resources/Audio/SFX/HeavyDrop.wav";
        private const string LiftAudioPath = "Assets/Resources/Audio/Lift.wav";

        [MenuItem("Tools/SeeSaw/Build SeeSaw Prefabs")]
        public static void BuildAllPrefabs()
        {
            EnsureFolders();

            Material woodMat = LoadMaterial(WoodMatPath, new Color(0.62f, 0.43f, 0.26f));
            Material stoneMat = LoadMaterial(StoneMatPath, new Color(0.55f, 0.55f, 0.58f));
            Material metalMat = LoadMaterial(MetalMatPath, new Color(0.28f, 0.28f, 0.32f));
            Material glowMat = LoadMaterial(GlowMatPath, new Color(0.2f, 0.85f, 1f));
            Material goldMat = LoadMaterial(GoldMatPath, new Color(0.9f, 0.75f, 0.2f));
            Material stripeMat = LoadMaterial(StripeMatPath, new Color(0.85f, 0.7f, 0.15f));
            Material hazardMat = LoadMaterial(HazardMatPath, new Color(0.95f, 0.45f, 0.1f));

            AudioClip impactClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ImpactAudioPath);
            AudioClip heavyDropClip = AssetDatabase.LoadAssetAtPath<AudioClip>(HeavyDropAudioPath);
            AudioClip liftClip = AssetDatabase.LoadAssetAtPath<AudioClip>(LiftAudioPath);

            // 1. Build SeeSaw Plank
            GameObject seeSawGO = ConstructSeeSawGameObject(woodMat, stoneMat, metalMat, glowMat, goldMat, stripeMat, hazardMat, impactClip, heavyDropClip);
            GameObject seeSawPrimary = PrefabUtility.SaveAsPrefabAsset(seeSawGO, $"{PrimaryFolder}/SeeSaw.prefab");
            PrefabUtility.SaveAsPrefabAsset(seeSawGO, $"{StandardFolder}/SeeSaw.prefab");
            Object.DestroyImmediate(seeSawGO);

            // 2. Build SeeSaw Weight
            GameObject weightGO = ConstructWeightGameObject(metalMat, stoneMat, glowMat, goldMat, stripeMat, heavyDropClip, liftClip);
            GameObject weightPrimary = PrefabUtility.SaveAsPrefabAsset(weightGO, $"{PrimaryFolder}/SeeSawWeight.prefab");
            PrefabUtility.SaveAsPrefabAsset(weightGO, $"{StandardFolder}/SeeSawWeight.prefab");
            Object.DestroyImmediate(weightGO);

            // 3. Register to NetworkPrefabsList
            RegisterToNetworkPrefabs(seeSawPrimary);
            RegisterToNetworkPrefabs(weightPrimary);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SeeSawBuilder] Successfully created and registered high-detail SeeSaw prefabs in '{PrimaryFolder}' and '{StandardFolder}'!");
        }

        [MenuItem("Tools/SeeSaw/Spawn SeeSaw In Current Scene")]
        public static void SpawnInCurrentScene()
        {
            GameObject seeSawPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrimaryFolder}/SeeSaw.prefab");
            GameObject weightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrimaryFolder}/SeeSawWeight.prefab");

            if (seeSawPrefab == null || weightPrefab == null)
            {
                BuildAllPrefabs();
                seeSawPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrimaryFolder}/SeeSaw.prefab");
                weightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrimaryFolder}/SeeSawWeight.prefab");
            }

            Vector3 spawnOrigin = Vector3.zero;
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
            {
                spawnOrigin = view.camera.transform.position + view.camera.transform.forward * 8f;
                if (Physics.Raycast(spawnOrigin, Vector3.down, out RaycastHit hit, 20f))
                {
                    spawnOrigin = hit.point;
                }
            }

            GameObject instanceSeeSaw = (GameObject)PrefabUtility.InstantiatePrefab(seeSawPrefab);
            instanceSeeSaw.transform.position = spawnOrigin;
            Undo.RegisterCreatedObjectUndo(instanceSeeSaw, "Spawn SeeSaw");

            GameObject instanceWeight = (GameObject)PrefabUtility.InstantiatePrefab(weightPrefab);
            instanceWeight.transform.position = spawnOrigin + Vector3.back * 3.5f + Vector3.up * 1.5f;
            Undo.RegisterCreatedObjectUndo(instanceWeight, "Spawn SeeSaw Weight");

            // Create empty GameObject spawn point for the counterweight
            GameObject spawnPointGO = new GameObject("SeeSawWeight_SpawnPoint");
            spawnPointGO.transform.position = instanceWeight.transform.position;
            spawnPointGO.transform.rotation = instanceWeight.transform.rotation;
            Undo.RegisterCreatedObjectUndo(spawnPointGO, "Spawn SeeSaw Weight Spawn Point");

            SeeSawWeight weightComp = instanceWeight.GetComponent<SeeSawWeight>();
            if (weightComp != null)
            {
                weightComp.RespawnPoint = spawnPointGO.transform;
            }

            Selection.activeGameObject = instanceSeeSaw;
            Debug.Log("[SeeSawBuilder] Spawned SeeSaw, SeeSawWeight, and SeeSawWeight_SpawnPoint in scene at " + spawnOrigin);
        }

        [MenuItem("Tools/SeeSaw/Create Weight Spawn Point (Empty GameObject)")]
        public static void CreateSpawnPointForSelectedWeight()
        {
            GameObject selected = Selection.activeGameObject;
            Vector3 pos = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            SeeSawWeight weightComp = null;
            if (selected != null)
            {
                pos = selected.transform.position;
                rot = selected.transform.rotation;
                weightComp = selected.GetComponent<SeeSawWeight>();
            }
            else
            {
                weightComp = Object.FindFirstObjectByType<SeeSawWeight>();
                if (weightComp != null)
                {
                    pos = weightComp.transform.position;
                    rot = weightComp.transform.rotation;
                }
            }

            GameObject spawnPointGO = new GameObject("SeeSawWeight_SpawnPoint");
            spawnPointGO.transform.position = pos;
            spawnPointGO.transform.rotation = rot;
            Undo.RegisterCreatedObjectUndo(spawnPointGO, "Create SeeSaw Weight Spawn Point");

            if (weightComp != null)
            {
                weightComp.RespawnPoint = spawnPointGO.transform;
                EditorUtility.SetDirty(weightComp);
            }

            Selection.activeGameObject = spawnPointGO;
            Debug.Log($"[SeeSawBuilder] Created empty spawn point GameObject '{spawnPointGO.name}' at {pos}!");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/prefap"))
                AssetDatabase.CreateFolder("Assets", "prefap");

            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/SeeSaw"))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                    AssetDatabase.CreateFolder("Assets", "Prefabs");
                AssetDatabase.CreateFolder("Assets/Prefabs", "SeeSaw");
            }
        }

        private static Material LoadMaterial(string path, Color fallbackColor)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", fallbackColor);
            }
            return mat;
        }

        private static GameObject ConstructSeeSawGameObject(
            Material woodMat,
            Material stoneMat,
            Material metalMat,
            Material glowMat,
            Material goldMat,
            Material stripeMat,
            Material hazardMat,
            AudioClip impactClip,
            AudioClip creakClip)
        {
            GameObject root = new GameObject("SeeSaw");

            // NetworkObject
            NetworkObject netObj = root.AddComponent<NetworkObject>();

            // AudioSource
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 2f;
            audio.maxDistance = 25f;

            // ──────────────── Fulcrum Stand (Base) ────────────────
            GameObject fulcrum = new GameObject("Fulcrum");
            fulcrum.transform.SetParent(root.transform, false);

            // Heavy masonry base pillar
            CreatePrimitive(PrimitiveType.Cube, "BasePillar", fulcrum.transform,
                new Vector3(0f, 0.65f, 0f), new Vector3(2.6f, 1.3f, 0.9f), stoneMat);

            // Steel capping plate across the pillar top
            CreatePrimitive(PrimitiveType.Cube, "PillarCap", fulcrum.transform,
                new Vector3(0f, 1.32f, 0f), new Vector3(2.7f, 0.1f, 0.95f), metalMat, hasCollider: false);

            // A-Frame diagonal steel side supports
            GameObject leftSupport = CreatePrimitive(PrimitiveType.Cube, "LeftSupport", fulcrum.transform,
                new Vector3(-1.38f, 0.7f, 0f), new Vector3(0.18f, 1.3f, 0.45f), metalMat, hasCollider: false);
            leftSupport.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);

            GameObject rightSupport = CreatePrimitive(PrimitiveType.Cube, "RightSupport", fulcrum.transform,
                new Vector3(1.38f, 0.7f, 0f), new Vector3(0.18f, 1.3f, 0.45f), metalMat, hasCollider: false);
            rightSupport.transform.localRotation = Quaternion.Euler(0f, 0f, -18f);

            // Central heavy steel axle cylinder
            GameObject axle = CreatePrimitive(PrimitiveType.Cylinder, "AxlePin", fulcrum.transform,
                new Vector3(0f, 1.4f, 0f), new Vector3(0.24f, 1.55f, 0.24f), metalMat, hasCollider: false);
            axle.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Left & Right Axle Flange Hub Discs
            GameObject hubL = CreatePrimitive(PrimitiveType.Cylinder, "Hub_Left", fulcrum.transform,
                new Vector3(-1.48f, 1.4f, 0f), new Vector3(0.48f, 0.08f, 0.48f), metalMat, hasCollider: false);
            hubL.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            GameObject hubR = CreatePrimitive(PrimitiveType.Cylinder, "Hub_Right", fulcrum.transform,
                new Vector3(1.48f, 1.4f, 0f), new Vector3(0.48f, 0.08f, 0.48f), metalMat, hasCollider: false);
            hubR.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // Center gold axle nut caps
            GameObject nutL = CreatePrimitive(PrimitiveType.Cylinder, "Nut_Left", fulcrum.transform,
                new Vector3(-1.54f, 1.4f, 0f), new Vector3(0.24f, 0.06f, 0.24f), goldMat, hasCollider: false);
            nutL.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            GameObject nutR = CreatePrimitive(PrimitiveType.Cylinder, "Nut_Right", fulcrum.transform,
                new Vector3(1.54f, 1.4f, 0f), new Vector3(0.24f, 0.06f, 0.24f), goldMat, hasCollider: false);
            nutR.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

            // ──────────────── Plank (Tilting Board) ────────────────
            GameObject plank = new GameObject("Plank");
            plank.transform.SetParent(root.transform, false);
            plank.transform.localPosition = new Vector3(0f, 1.4f, 0f);

            Rigidbody plankRb = plank.AddComponent<Rigidbody>();
            plankRb.isKinematic = true;
            plankRb.useGravity = false;
            plankRb.interpolation = RigidbodyInterpolation.Interpolate;

            // Main walking deck (has clean physics BoxCollider)
            CreatePrimitive(PrimitiveType.Cube, "Board", plank.transform,
                Vector3.zero, new Vector3(2.4f, 0.25f, 10.0f), woodMat);

            // Heavy central iron saddle plate across the fulcrum
            CreatePrimitive(PrimitiveType.Cube, "CenterSaddle", plank.transform,
                new Vector3(0f, 0.13f, 0f), new Vector3(2.44f, 0.03f, 0.9f), metalMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "CenterGoldStrap", plank.transform,
                new Vector3(0f, 0.14f, 0f), new Vector3(0.35f, 0.02f, 0.94f), goldMat, hasCollider: false);

            // Heavy under-spine girder running the length of the board
            CreatePrimitive(PrimitiveType.Cube, "UnderSpine", plank.transform,
                new Vector3(0f, -0.25f, 0f), new Vector3(0.45f, 0.25f, 9.6f), metalMat, hasCollider: false);

            // Side Guide Curbs / Safety Rails
            CreatePrimitive(PrimitiveType.Cube, "RailLeft", plank.transform,
                new Vector3(-1.16f, 0.22f, 0f), new Vector3(0.12f, 0.24f, 10.0f), woodMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "RailRight", plank.transform,
                new Vector3(1.16f, 0.22f, 0f), new Vector3(0.12f, 0.24f, 10.0f), woodMat, hasCollider: false);

            // Iron Corner Trim Caps along the side rails
            CreatePrimitive(PrimitiveType.Cube, "RailCap_Left", plank.transform,
                new Vector3(-1.16f, 0.35f, 0f), new Vector3(0.14f, 0.03f, 10.02f), metalMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "RailCap_Right", plank.transform,
                new Vector3(1.16f, 0.35f, 0f), new Vector3(0.14f, 0.03f, 10.02f), metalMat, hasCollider: false);

            // Anti-slip step slats across the plank (visual only, smooth walking)
            float[] slatPositions = { -4.2f, -3.2f, -2.2f, -1.2f, 1.2f, 2.2f, 3.2f, 4.2f };
            for (int i = 0; i < slatPositions.Length; i++)
            {
                CreatePrimitive(PrimitiveType.Cube, $"Slat_{i}", plank.transform,
                    new Vector3(0f, 0.13f, slatPositions[i]), new Vector3(2.15f, 0.02f, 0.14f), woodMat, hasCollider: false);
            }

            // ──────────────── Side A (Weight Docking Zone) ────────────────
            // Heavy steel docking pad
            CreatePrimitive(PrimitiveType.Cube, "SideA_WeightPad", plank.transform,
                new Vector3(0f, 0.13f, -3.6f), new Vector3(1.6f, 0.02f, 1.6f), metalMat, hasCollider: false);

            // Hazard warning border around docking zone
            CreatePrimitive(PrimitiveType.Cube, "SideA_HazardBorder", plank.transform,
                new Vector3(0f, 0.14f, -3.6f), new Vector3(1.7f, 0.01f, 1.7f), stripeMat, hasCollider: false);

            // Glowing cyan target crosshair / ring
            CreatePrimitive(PrimitiveType.Cube, "SideA_TargetRing", plank.transform,
                new Vector3(0f, 0.145f, -3.6f), new Vector3(1.1f, 0.01f, 1.1f), glowMat, hasCollider: false);

            // Corner alignment brackets that cradle the counterweight cube
            CreatePrimitive(PrimitiveType.Cube, "SideA_Bracket_FL", plank.transform,
                new Vector3(-0.65f, 0.19f, -2.95f), new Vector3(0.16f, 0.10f, 0.16f), metalMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "SideA_Bracket_FR", plank.transform,
                new Vector3(0.65f, 0.19f, -2.95f), new Vector3(0.16f, 0.10f, 0.16f), metalMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "SideA_Bracket_BL", plank.transform,
                new Vector3(-0.65f, 0.19f, -4.25f), new Vector3(0.16f, 0.10f, 0.16f), metalMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "SideA_Bracket_BR", plank.transform,
                new Vector3(0.65f, 0.19f, -4.25f), new Vector3(0.16f, 0.10f, 0.16f), metalMat, hasCollider: false);

            // ──────────────── Side B (Crossing Runway Chevrons) ────────────────
            float[] chevronZ = { 2.6f, 3.4f, 4.2f };
            for (int i = 0; i < chevronZ.Length; i++)
            {
                CreatePrimitive(PrimitiveType.Cube, $"SideB_Chevron_{i}", plank.transform,
                    new Vector3(0f, 0.135f, chevronZ[i]), new Vector3(1.1f - i * 0.15f, 0.015f, 0.22f), glowMat, hasCollider: false);
            }

            // ──────────────── Shock Absorber Bumpers ────────────────
            CreatePrimitive(PrimitiveType.Cube, "Bumper_A", plank.transform,
                new Vector3(0f, -0.22f, -4.8f), new Vector3(0.6f, 0.25f, 0.6f), metalMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "Bumper_A_Tip", plank.transform,
                new Vector3(0f, -0.36f, -4.8f), new Vector3(0.45f, 0.08f, 0.45f), hazardMat, hasCollider: false);

            CreatePrimitive(PrimitiveType.Cube, "Bumper_B", plank.transform,
                new Vector3(0f, -0.22f, 4.8f), new Vector3(0.6f, 0.25f, 0.6f), metalMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "Bumper_B_Tip", plank.transform,
                new Vector3(0f, -0.36f, 4.8f), new Vector3(0.45f, 0.08f, 0.45f), hazardMat, hasCollider: false);

            // Component setup on Root
            SeeSawPlank seeSaw = root.AddComponent<SeeSawPlank>();
            SerializedObject so = new SerializedObject(seeSaw);
            so.FindProperty("_plankTransform").objectReferenceValue = plank.transform;
            so.FindProperty("_plankRigidbody").objectReferenceValue = plankRb;
            so.FindProperty("_plankLength").floatValue = 10.0f;
            so.FindProperty("_plankWidth").floatValue = 2.4f;
            so.FindProperty("_plankThickness").floatValue = 0.25f;
            so.FindProperty("_maxTiltAngle").floatValue = 16.0f;
            so.FindProperty("_tiltSpeed").floatValue = 24.0f;
            so.FindProperty("_returnToCenterSpeed").floatValue = 6.0f;
            so.FindProperty("_autoCenterWhenEmpty").boolValue = true;
            so.FindProperty("_playerTorqueMass").floatValue = 1.0f;
            so.FindProperty("_audioSource").objectReferenceValue = audio;
            so.FindProperty("_impactSound").objectReferenceValue = impactClip;
            so.FindProperty("_creakSound").objectReferenceValue = creakClip;
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static GameObject ConstructWeightGameObject(
            Material metalMat,
            Material stoneMat,
            Material glowMat,
            Material goldMat,
            Material stripeMat,
            AudioClip latchClip,
            AudioClip unlatchClip)
        {
            GameObject root = new GameObject("SeeSawWeight");

            // Netcode
            NetworkObject netObj = root.AddComponent<NetworkObject>();

            // Rigidbody
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = 20.0f;
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.15f;
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            // NetworkTransform
            NetworkTransform netTransform = root.AddComponent<NetworkTransform>();
            netTransform.Interpolate = true;

            // CarryableOutline
            CarryableOutline outline = root.AddComponent<CarryableOutline>();

            // BoxCollider on root (exact 0.8 x 0.8 x 0.8 cube)
            BoxCollider boxCol = root.AddComponent<BoxCollider>();
            boxCol.size = new Vector3(0.8f, 0.8f, 0.8f);
            boxCol.center = new Vector3(0f, 0.4f, 0f);

            // Visual Model Root
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            // 1. Core Steel Cube Body
            CreatePrimitive(PrimitiveType.Cube, "CoreCube", visual.transform,
                new Vector3(0f, 0.4f, 0f), new Vector3(0.78f, 0.78f, 0.78f), metalMat, hasCollider: false);

            // 2. Corner Armor Brackets (Iron/Stone reinforced framing)
            CreatePrimitive(PrimitiveType.Cube, "Corner_FL", visual.transform,
                new Vector3(-0.35f, 0.4f, -0.35f), new Vector3(0.12f, 0.82f, 0.12f), stoneMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "Corner_FR", visual.transform,
                new Vector3(0.35f, 0.4f, -0.35f), new Vector3(0.12f, 0.82f, 0.12f), stoneMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "Corner_BL", visual.transform,
                new Vector3(-0.35f, 0.4f, 0.35f), new Vector3(0.12f, 0.82f, 0.12f), stoneMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "Corner_BR", visual.transform,
                new Vector3(0.35f, 0.4f, 0.35f), new Vector3(0.12f, 0.82f, 0.12f), stoneMat, hasCollider: false);

            // 3. Side Hazard Stripes (Front and Back inset plates)
            CreatePrimitive(PrimitiveType.Cube, "Stripe_Front", visual.transform,
                new Vector3(0f, 0.4f, -0.395f), new Vector3(0.52f, 0.42f, 0.02f), stripeMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "Stripe_Back", visual.transform,
                new Vector3(0f, 0.4f, 0.395f), new Vector3(0.52f, 0.42f, 0.02f), stripeMat, hasCollider: false);

            // 4. Heavy-duty Top Grab Handle
            CreatePrimitive(PrimitiveType.Cube, "Handle_Bar", visual.transform,
                new Vector3(0f, 0.86f, 0f), new Vector3(0.48f, 0.06f, 0.08f), goldMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "Handle_PostL", visual.transform,
                new Vector3(-0.21f, 0.81f, 0f), new Vector3(0.06f, 0.08f, 0.08f), metalMat, hasCollider: false);
            CreatePrimitive(PrimitiveType.Cube, "Handle_PostR", visual.transform,
                new Vector3(0.21f, 0.81f, 0f), new Vector3(0.06f, 0.08f, 0.08f), metalMat, hasCollider: false);

            // 5. Graviton Power Core (Glowing cyan central energy cell)
            CreatePrimitive(PrimitiveType.Cube, "PowerCore", visual.transform,
                new Vector3(0f, 0.795f, 0f), new Vector3(0.28f, 0.02f, 0.28f), glowMat, hasCollider: false);

            // 6. Anchor Indicator Base Ring (illuminates when latched to see-saw)
            GameObject anchorIndicator = CreatePrimitive(PrimitiveType.Cube, "AnchorIndicator", visual.transform,
                new Vector3(0f, 0.015f, 0f), new Vector3(0.82f, 0.03f, 0.82f), glowMat, hasCollider: false);
            anchorIndicator.SetActive(false);

            // Sockets for holding (Front, Back, Left, Right)
            Transform[] sockets = new Transform[4];
            sockets[0] = CreateSocket("Socket_Front", root.transform, new Vector3(0f, 0.4f, 0.52f));
            sockets[1] = CreateSocket("Socket_Back", root.transform, new Vector3(0f, 0.4f, -0.52f));
            sockets[2] = CreateSocket("Socket_Left", root.transform, new Vector3(-0.52f, 0.4f, 0f));
            sockets[3] = CreateSocket("Socket_Right", root.transform, new Vector3(0.52f, 0.4f, 0f));

            // SeeSawWeight Component
            SeeSawWeight weightComp = root.AddComponent<SeeSawWeight>();
            SerializedObject so = new SerializedObject(weightComp);
            so.FindProperty("_weightMass").floatValue = 2.5f;
            so.FindProperty("_anchorLatchSound").objectReferenceValue = latchClip;
            so.FindProperty("_unanchorSound").objectReferenceValue = unlatchClip;
            so.FindProperty("_anchorVisualIndicator").objectReferenceValue = anchorIndicator;
            so.FindProperty("_carryForwardDistance").floatValue = 0.75f;
            so.FindProperty("_carryHeightOffset").floatValue = 0.85f;
            so.FindProperty("_soloSpeedMultiplier").floatValue = 0.65f;
            so.FindProperty("_coopSpeedMultiplier").floatValue = 0.95f;
            so.FindProperty("_requiresTwoCarriers").boolValue = false; // solo carrier allowed

            SerializedProperty socketsProp = so.FindProperty("_sockets");
            socketsProp.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                socketsProp.GetArrayElementAtIndex(i).objectReferenceValue = sockets[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static GameObject CreatePrimitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPos,
            Vector3 localScale,
            Material material,
            bool hasCollider = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;

            if (!hasCollider)
            {
                Collider col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }

            MeshRenderer mr = go.GetComponent<MeshRenderer>();
            if (mr != null && material != null)
            {
                mr.sharedMaterial = material;
            }

            return go;
        }

        private static Transform CreateSocket(string name, Transform parent, Vector3 localPos)
        {
            GameObject sock = new GameObject(name);
            sock.transform.SetParent(parent, false);
            sock.transform.localPosition = localPos;
            return sock.transform;
        }

        private static void RegisterToNetworkPrefabs(GameObject prefab)
        {
            if (prefab == null) return;
            string listPath = "Assets/DefaultNetworkPrefabs.asset";
            NetworkPrefabsList prefabsList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(listPath);
            if (prefabsList == null) return;

            SerializedObject so = new SerializedObject(prefabsList);
            SerializedProperty listProp = so.FindProperty("List");

            bool alreadyRegistered = false;
            for (int i = 0; i < listProp.arraySize; i++)
            {
                SerializedProperty item = listProp.GetArrayElementAtIndex(i);
                SerializedProperty p = item.FindPropertyRelative("Prefab");
                if (p != null && p.objectReferenceValue == prefab)
                {
                    alreadyRegistered = true;
                    break;
                }
            }

            if (!alreadyRegistered)
            {
                int index = listProp.arraySize;
                listProp.InsertArrayElementAtIndex(index);
                SerializedProperty newItem = listProp.GetArrayElementAtIndex(index);
                SerializedProperty p = newItem.FindPropertyRelative("Prefab");
                if (p != null) p.objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(prefabsList);
                Debug.Log($"[SeeSawBuilder] Registered {prefab.name} in NetworkPrefabsList.");
            }
        }
    }
}
#endif
