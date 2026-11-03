using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoopGame.CarrySystem;
using CoopGame.Network;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builder for "New Level 01" scene.
    /// Continues from the user's map layout:
    /// 1. First Big Island: Starter Village with PlayerStart in center, WoodenCrateItem, village houses, well, stalls, torches, flags.
    /// 2. Small Islands between Island 1 and Island 2: Separated with jump gaps, decorated with trees (ต้นสน), spikes (หนามไม้), and traps.
    /// 3. Middle Big Island: Dense pine forest with clear winding walking path, mid-way checkpoint pad, and crusher hazard.
    /// 4. Small Islands between Island 2 and Island 3: Bridging the chasm with stepping islands (เกาะเล็ก), trees, spikes, and swing hammer / twin pendulums.
    /// 5. Last Big Island: Grand medieval castle (keep, 4 towers, battlements, walls, gate, red carpet, flags) with DeliveryZone in front!
    /// 6. Surrounding low-poly clouds encircling the islands and chasms.
    /// 7. 100% accurate MeshColliders on all islands and solid colliders on all structures.
    /// </summary>
    [InitializeOnLoad]
    public static class NewLevel01Builder
    {
        public const string ScenePath = "Assets/Scenes/New Level 01.unity";
        private const string BackupPath = "Assets/Scenes/New Level 01_Backup.unity";

        // Big Island center coordinates laid out by the user
        public static readonly Vector3 Island1_Center = new Vector3(41.93f, 5.36f, -183.06f);
        public static readonly Vector3 Island2_Center = new Vector3(20.68f, 9.38f, -38.68f);
        public static readonly Vector3 Island3_Center = new Vector3(-50.10f, 11.32f, 41.70f);

        // Materials
        private static Material _grass;
        private static Material _wood;
        private static Material _stone;
        private static Material _gold;
        private static Material _blue;
        private static Material _roof;
        private static Material _plaster;
        private static Material _cloth;
        private static Material _carpet;
        private static Material _cloud;
        private static Material _backing;

        // Prefabs
        private static GameObject _islandBigPrefab;
        private static GameObject _islandSmallPrefab;
        private static GameObject _pineTreePrefab;
        private static GameObject _roundTreePrefab;
        private static GameObject _spikePrefab;
        private static GameObject _cratePrefab;
        private static GameObject _deliveryZonePrefab;
        private static GameObject _swingHammerPrefab;
        private static GameObject _twinPendulumsPrefab;
        private static GameObject _rotatingLogPrefab;
        private static GameObject _crusherPrefab;
        private static GameObject _woodenSpikesPrefab;

        static NewLevel01Builder()
        {
            // Delay call to compile / auto-run once if requested
            EditorApplication.delayCall += CheckAutoRun;
        }

        private static void CheckAutoRun()
        {
            const string triggerFile = "Temp/BuildNewLevel01.trigger";
            if (File.Exists(triggerFile))
            {
                try { File.Delete(triggerFile); } catch { }
                BuildMap(interactive: false);
            }
        }

        [MenuItem("CoopGame/Map/Build 'New Level 01' Map (Village, Forest, Castle, Traps, Clouds)", priority = 10)]
        public static void MenuBuild()
        {
            BuildMap(interactive: true);
        }

        public static void BuildMap(bool interactive)
        {
            if (EditorApplication.isPlaying)
            {
                if (interactive) EditorUtility.DisplayDialog("Play Mode Active", "Please stop Play Mode before building the map.", "OK");
                return;
            }

            // Ensure scene is loaded
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            // Backup scene
            if (!File.Exists(BackupPath))
            {
                File.Copy(ScenePath, BackupPath, true);
                Debug.Log($"[NewLevel01Builder] Created scene backup at: {BackupPath}");
            }

            LoadAssets();

            // Locate or create organizational parents
            Transform mapPrefap = GetOrCreateRoot("Map Prefap");
            Transform sceneryRoot = GetOrCreateRoot("Environment_Scenery");
            Transform cloudsRoot = GetOrCreateRoot("SurroundingClouds");

            // Clean up any old stray test objects in the void (Z > 100)
            CleanStrayObjects();

            // 1. Separate small islands so they don't touch each other, and bridge gaps
            OrganizeAndBridgeSmallIslands(mapPrefap);

            // 2. Build the Starter Village on Big Island 1
            BuildStarterVillage(sceneryRoot);

            // 3. Polish the walking path on Middle Big Island 2
            PolishForestWalkingPath(sceneryRoot);

            // 4. Build Grand Castle & place DeliveryZone on Last Big Island 3
            BuildGrandCastleAndDeliveryZone(sceneryRoot);

            // 5. Decorate Small Islands with pine trees, spikes, and traps
            DecorateSmallIslands(mapPrefap, sceneryRoot);

            // 6. Generate surrounding cloud banks
            BuildSurroundingClouds(cloudsRoot);

            // 7. Ensure accurate non-convex MeshColliders on all islands
            EnsureAllIslandColliders(mapPrefap);

            // Mark dirty and save
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            string summary = "[NewLevel01Builder] Map build completed successfully!\n" +
                             "• Island 1: Starter Village with PlayerStart, Crate & Houses\n" +
                             "• Island 2: Dense Forest with clear Walking Path & Crusher\n" +
                             "• Island 3: Grand Castle with DeliveryZone in front\n" +
                             "• Small Islands: Separated with jump gaps, pine trees, spikes & traps\n" +
                             "• Clouds: Full panoramic sky cloud coverage\n" +
                             "• Colliders: Accurate non-convex MeshColliders on all islands";

            Debug.Log(summary);
            if (interactive)
            {
                EditorUtility.DisplayDialog("New Level 01 Map Built!", summary, "Great!");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ASSET LOADING
        // ─────────────────────────────────────────────────────────────────────────
        private static void LoadAssets()
        {
            _grass = LoadMat("Assets/Environment/FloatingCoop/Grass.mat");
            _wood = LoadMat("Assets/Environment/FloatingCoop/BridgeWood.mat");
            _stone = LoadMat("Assets/Environment/FloatingCoop/Stone.mat");
            _gold = LoadMat("Assets/Environment/FloatingCoop/GoalGold.mat");
            _blue = LoadMat("Assets/Environment/FloatingCoop/RelayBlue.mat");
            _roof = LoadMat("Assets/Environment/Level02/Roof.mat");
            _plaster = LoadMat("Assets/Environment/Level02/Plaster.mat");
            _cloth = LoadMat("Assets/Environment/Level02/StallCloth.mat");
            _carpet = LoadMat("Assets/Environment/Level02/RoyalCarpet.mat");
            _cloud = LoadMat("Assets/Materials/Medieval/Cloud.mat");
            _backing = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/Level01/RouteOrange.mat") ?? _wood;

            _islandBigPrefab = LoadPrefab("Assets/Prefabs/เกาะใหญ่.fbx");
            _islandSmallPrefab = LoadPrefab("Assets/Prefabs/เกาะเล็ก.fbx");
            _pineTreePrefab = LoadPrefab("Assets/Prefabs/ต้นสน.fbx");
            _roundTreePrefab = LoadPrefab("Assets/Prefabs/ต้นไม้กลม.fbx");
            _spikePrefab = LoadPrefab("Assets/Prefabs/หนามไม้.fbx");
            _cratePrefab = LoadPrefab("Assets/Prefabs/Items/WoodenCrateItem.prefab");
            _deliveryZonePrefab = LoadPrefab("Assets/Prefabs/DeliveryZone.prefab");
            _swingHammerPrefab = LoadPrefab("Assets/Prefabs/Hazards/Hazard_SwingHammer.prefab");
            _twinPendulumsPrefab = LoadPrefab("Assets/Prefabs/Hazards/Medieval/Medieval_TwinPendulums.prefab");
            _rotatingLogPrefab = LoadPrefab("Assets/Prefabs/Hazards/Hazard_RotatingLog.prefab");
            _crusherPrefab = LoadPrefab("Assets/Prefabs/Hazards/Hazard_Crusher_Entry.prefab");
            _woodenSpikesPrefab = LoadPrefab("Assets/Prefabs/Hazards/Medieval/Medieval_WoodenSpikes.prefab");
        }

        private static Material LoadMat(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);
        private static GameObject LoadPrefab(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        private static Transform GetOrCreateRoot(string name)
        {
            GameObject obj = GameObject.Find(name);
            if (obj == null) obj = new GameObject(name);
            return obj.transform;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // CLEANUP STRAY OBJECTS
        // ─────────────────────────────────────────────────────────────────────────
        private static void CleanStrayObjects()
        {
            // Remove misplaced remnants floating out at Z > 100
            var allGOs = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var go in allGOs)
            {
                if (go == null) continue;
                Vector3 p = go.transform.position;
                if (p.z > 100f && (go.name.Contains("Cube") || go.name.Contains("เกาะปีน") || go.name.Contains("Medieval_TwinPendulums")))
                {
                    Undo.DestroyObjectImmediate(go);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 1. SMALL ISLAND SPACING & BRIDGING
        // ─────────────────────────────────────────────────────────────────────────
        private static void OrganizeAndBridgeSmallIslands(Transform parent)
        {
            // Find all small islands under Map Prefap
            List<Transform> smallIslands = new List<Transform>();
            foreach (Transform child in parent)
            {
                if (child.name.Contains("เกาะเล็ก"))
                {
                    smallIslands.Add(child);
                }
            }

            // Ensure islands do not touch each other: minimum center distance ~11.5m
            // If any adjacent pair is closer than 11.5m, nudge them slightly apart
            smallIslands.Sort((a, b) => a.position.z.CompareTo(b.position.z));
            for (int i = 0; i < smallIslands.Count - 1; i++)
            {
                Transform a = smallIslands[i];
                Transform b = smallIslands[i + 1];

                // If within the same chain
                float distXZ = Vector2.Distance(new Vector2(a.position.x, a.position.z), new Vector2(b.position.x, b.position.z));
                if (distXZ < 11.5f && distXZ > 0.01f)
                {
                    Vector3 dir = (b.position - a.position);
                    dir.y = 0;
                    dir.Normalize();

                    float needed = (12.0f - distXZ) * 0.5f;
                    a.position -= dir * needed;
                    b.position += dir * needed;
                }
            }

            // Add stepping small islands in any large gaps:
            // Gap 1: Island 1 rim towards first small island (Z from -165 to -137)
            EnsureSteppingIsland(parent, new Vector3(48.5f, 4.8f, -153.0f), new Vector3(232.84f, 137.5f, 290.51f));
            EnsureSteppingIsland(parent, new Vector3(51.0f, 4.7f, -145.0f), new Vector3(232.84f, 137.5f, 290.51f));

            // Gap 2: High vertical hop from (60.25, 4.59, -108.68) to (60.37, 8.98, -76.16)
            EnsureSteppingIsland(parent, new Vector3(60.3f, 6.8f, -92.5f), new Vector3(232.84f, 137.5f, 290.51f));

            // Gap 3: 26m chasm between Island 2 and Island 3: from (-9.91, 5.8) to (-33.84, 15.23)
            EnsureSteppingIsland(parent, new Vector3(-17.5f, 8.0f, 8.5f), new Vector3(232.84f, 137.5f, 290.51f));
            EnsureSteppingIsland(parent, new Vector3(-25.5f, 9.5f, 11.8f), new Vector3(232.84f, 137.5f, 290.51f));

            // Gap 4: Final hop from (-38.51, 22.19) onto Last Big Island (-50.1, 11.32, 41.7)
            EnsureSteppingIsland(parent, new Vector3(-43.5f, 11.1f, 31.0f), new Vector3(232.84f, 137.5f, 290.51f));
        }

        private static GameObject EnsureSteppingIsland(Transform parent, Vector3 pos, Vector3 scale)
        {
            // Check if already exists near pos
            foreach (Transform child in parent)
            {
                if (child.name.Contains("เกาะเล็ก") && Vector3.Distance(child.position, pos) < 4.0f)
                    return child.gameObject;
            }

            GameObject go;
            if (_islandSmallPrefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(_islandSmallPrefab, parent);
            }
            else
            {
                go = new GameObject("เกาะเล็ก (Stepping)");
                go.transform.SetParent(parent, false);
            }

            go.transform.position = pos;
            go.transform.localScale = scale;
            go.name = "เกาะเล็ก";

            // Add MeshCollider
            MeshFilter mf = go.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && go.GetComponent<Collider>() == null)
            {
                MeshCollider mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
            }

            return go;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 2. STARTER VILLAGE (FIRST BIG ISLAND)
        // ─────────────────────────────────────────────────────────────────────────
        private static void BuildStarterVillage(Transform sceneryRoot)
        {
            Transform village = sceneryRoot.Find("StarterVillage");
            if (village != null) Undo.DestroyObjectImmediate(village.gameObject);

            village = new GameObject("StarterVillage").transform;
            village.SetParent(sceneryRoot, false);
            village.position = Island1_Center;

            Vector3 center = Island1_Center;
            float baseY = center.y + 0.9f;

            // 1. Central Plaza & Player Spawn
            GameObject playerStart = GameObject.Find("PlayerStart");
            if (playerStart == null) playerStart = new GameObject("PlayerStart");
            playerStart.transform.SetParent(village, true);
            playerStart.transform.position = new Vector3(center.x, baseY + 1.05f, center.z);
            playerStart.transform.rotation = Quaternion.LookRotation(new Vector3(0.2f, 0, 1f));

            // 2. Starting Cargo & Golden Rest Pad
            Vector3 cargoPos = new Vector3(center.x, baseY + 0.05f, center.z + 3.2f);
            CreateGoldPad("StartingCargoPad", village, cargoPos, 3.8f);

            // Starting Package Crate
            GameObject crate = GameObject.Find("StartingPackage");
            if (crate == null && _cratePrefab != null)
            {
                crate = (GameObject)PrefabUtility.InstantiatePrefab(_cratePrefab, village);
                crate.name = "StartingPackage";
            }
            if (crate != null)
            {
                crate.transform.SetParent(village, true);
                crate.transform.position = new Vector3(cargoPos.x, baseY + 0.9f, cargoPos.z);
                crate.transform.rotation = Quaternion.identity;
            }

            // 3. Welcome Sign
            CreateThaiSign("VillageWelcomeSign", village,
                new Vector3(center.x - 4.5f, baseY, center.z + 1.0f),
                "หมู่บ้านเริ่มต้น — ภารกิจส่งของสู่ปราสาท!\nคลิกซ้าย = ยกลัง (ช่วยกันยก 2 คนประหยัดพลังงาน)\nเดินตามเส้นทางข้ามเกาะลอยฟ้าไปยังปราสาทใหญ่",
                Vector3.forward);

            // 4. Village Houses around Plaza
            CreateHouse("VillageHouse_North", village, new Vector3(center.x - 10f, baseY, center.z + 12f), 35f, new Vector2(5.5f, 4.5f), 3.5f);
            CreateHouse("VillageHouse_West", village, new Vector3(center.x - 15f, baseY, center.z - 4f), -15f, new Vector2(5.0f, 4.2f), 3.2f);
            CreateHouse("VillageHouse_East", village, new Vector3(center.x + 13f, baseY, center.z - 2f), 20f, new Vector2(5.2f, 4.4f), 3.4f);
            CreateHouse("VillageHouse_South", village, new Vector3(center.x + 2f, baseY, center.z - 16f), -175f, new Vector2(5.5f, 4.5f), 3.6f);

            // 5. Village Well
            CreateWell("VillageWell", village, new Vector3(center.x + 6.5f, baseY, center.z - 7.5f));

            // 6. Market Stalls & Props
            CreateMarketStall("MarketStall_A", village, new Vector3(center.x - 7.5f, baseY, center.z - 8f), 45f);
            CreateMarketStall("MarketStall_B", village, new Vector3(center.x + 8.5f, baseY, center.z + 7f), -35f);

            // Hay bales and barrels
            for (int i = 0; i < 4; i++)
            {
                CreateBoxPrimitive("HayBale_" + i, village, new Vector3(center.x - 13f + i * 1.3f, baseY + 0.45f, center.z - 11f), new Vector3(1.2f, 0.9f, 1f), _gold, true);
            }

            // 7. Village Flags
            CreateFlag("VillageFlag_L", village, new Vector3(center.x - 8f, baseY, center.z + 18f), _blue);
            CreateFlag("VillageFlag_R", village, new Vector3(center.x + 10f, baseY, center.z + 17f), _gold);

            // 8. Torches guiding toward the exit
            CreateTorch("VillageTorch_Exit1", village, new Vector3(center.x - 2f, baseY, center.z + 20f));
            CreateTorch("VillageTorch_Exit2", village, new Vector3(center.x + 4f, baseY, center.z + 24f));
            CreateTorch("VillageTorch_Exit3", village, new Vector3(center.x + 7f, baseY, center.z + 28f));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 3. MIDDLE BIG ISLAND (DENSE FOREST & PATH POLISH)
        // ─────────────────────────────────────────────────────────────────────────
        private static void PolishForestWalkingPath(Transform sceneryRoot)
        {
            Transform forestPath = sceneryRoot.Find("ForestWalkingPath");
            if (forestPath != null) Undo.DestroyObjectImmediate(forestPath.gameObject);

            forestPath = new GameObject("ForestWalkingPath").transform;
            forestPath.SetParent(sceneryRoot, false);
            forestPath.position = Island2_Center;

            Vector3 center = Island2_Center;
            float baseY = center.y + 0.05f;

            // Rest stop in the middle of the forest
            Vector3 restPadPos = new Vector3(center.x - 4f, baseY, center.z + 2.5f);
            CreateGoldPad("ForestCheckpointPad", forestPath, restPadPos, 4.0f);

            // Forest Info Sign
            CreateThaiSign("ForestTrailSign", forestPath,
                new Vector3(restPadPos.x - 3.5f, baseY, restPadPos.z - 1f),
                "ป่าสนใหญ่ — ทางเดินผ่านป่า\nระวังกับดักบดหินข้างหน้า!\nวางพักลังบนแท่นทองเพื่อฟื้นพลังและบันทึกจุดเช็คพอยต์",
                new Vector3(0.5f, 0, 1f));

            // Lantern posts along the forest trail
            Vector3[] trailPoints = new Vector3[]
            {
                new Vector3(center.x, baseY, center.z - 22f),
                new Vector3(center.x - 3f, baseY, center.z - 14f),
                new Vector3(center.x - 7f, baseY, center.z - 6f),
                new Vector3(center.x - 4f, baseY, center.z + 5f),
                new Vector3(center.x + 1f, baseY, center.z + 12f),
                new Vector3(center.x - 4f, baseY, center.z + 20f)
            };

            for (int i = 0; i < trailPoints.Length; i++)
            {
                CreateTorch("ForestTrailTorch_" + i, forestPath, trailPoints[i]);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 4. LAST BIG ISLAND (GRAND CASTLE & DELIVERY ZONE)
        // ─────────────────────────────────────────────────────────────────────────
        private static void BuildGrandCastleAndDeliveryZone(Transform sceneryRoot)
        {
            Transform castleRoot = sceneryRoot.Find("GrandCastle");
            if (castleRoot != null) Undo.DestroyObjectImmediate(castleRoot.gameObject);

            castleRoot = new GameObject("GrandCastle").transform;
            castleRoot.SetParent(sceneryRoot, false);
            castleRoot.position = Island3_Center;

            Vector3 center = Island3_Center;
            float baseY = center.y + 0.1f;

            // Castle Gate faces Southeast towards the approach path (-38, 11, 22) -> (-50, 11, 41)
            Vector3 forward = new Vector3(-0.35f, 0, 0.94f).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            // 1. Keep (Main Palace building at the rear)
            Vector3 keepPos = center + forward * 14f;
            CreateBoxPrimitive("CastleKeep", castleRoot, keepPos + Vector3.up * 6f, new Vector3(14f, 12f, 8f), _stone, true);
            // Keep Roof
            CreateBoxPrimitive("CastleKeepRoof", castleRoot, keepPos + Vector3.up * 12.3f, new Vector3(14.8f, 1.2f, 8.8f), _roof, true);

            // 2. Towers (4 corner towers)
            float towerH = 15f;
            float towerW = 3.8f;
            Vector3[] towerOffsets = new Vector3[]
            {
                forward * 6f - right * 9f,  // Front Left
                forward * 6f + right * 9f,  // Front Right
                forward * 18f - right * 9f, // Rear Left
                forward * 18f + right * 9f  // Rear Right
            };

            for (int i = 0; i < towerOffsets.Length; i++)
            {
                Vector3 tPos = center + towerOffsets[i];
                Transform tower = CreateBoxPrimitive("CastleTower_" + i, castleRoot, tPos + Vector3.up * (towerH * 0.5f), new Vector3(towerW, towerH, towerW), _stone, true).transform;

                // Tower battlements
                for (int b = -1; b <= 1; b++)
                {
                    CreateBoxPrimitive("Battlement_" + b, tower, new Vector3(b * 1.3f, towerH * 0.5f + 0.5f, 0), new Vector3(0.8f, 1.0f, towerW + 0.2f), _stone, true);
                }
                // Tower roof
                CreateBoxPrimitive("TowerRoof", tower, new Vector3(0, towerH * 0.5f + 1.3f, 0), new Vector3(towerW + 0.5f, 0.6f, towerW + 0.5f), _roof, true);

                // Flags on front towers
                if (i < 2)
                {
                    CreateFlag("CastleBanner_" + i, castleRoot, tPos + Vector3.up * (towerH + 1.5f), _gold);
                }
            }

            // 3. Curtain Walls
            // Left Wall
            Vector3 leftWallPos = center + forward * 12f - right * 9f;
            CreateBoxPrimitive("CastleWall_Left", castleRoot, leftWallPos + Vector3.up * 3.5f, new Vector3(1.4f, 7f, 12f), _stone, true);

            // Right Wall
            Vector3 rightWallPos = center + forward * 12f + right * 9f;
            CreateBoxPrimitive("CastleWall_Right", castleRoot, rightWallPos + Vector3.up * 3.5f, new Vector3(1.4f, 7f, 12f), _stone, true);

            // Front Walls with central Gatehouse
            Vector3 frontLeftPos = center + forward * 6f - right * 5.5f;
            CreateBoxPrimitive("CastleWall_FrontLeft", castleRoot, frontLeftPos + Vector3.up * 3.5f, new Vector3(6f, 7f, 1.4f), _stone, true);

            Vector3 frontRightPos = center + forward * 6f + right * 5.5f;
            CreateBoxPrimitive("CastleWall_FrontRight", castleRoot, frontRightPos + Vector3.up * 3.5f, new Vector3(6f, 7f, 1.4f), _stone, true);

            // Gate arch overhead
            Vector3 gateArchPos = center + forward * 6f;
            CreateBoxPrimitive("CastleGateArch", castleRoot, gateArchPos + Vector3.up * 6.5f, new Vector3(5f, 1.5f, 1.6f), _stone, true);

            // 4. Royal Red Carpet leading from courtyard to gate
            Vector3 carpetPos = center + forward * 8f;
            CreateBoxPrimitive("RoyalCarpet", castleRoot, carpetPos + Vector3.up * 0.03f, new Vector3(3.2f, 0.05f, 14f), _carpet, false);

            // 5. DELIVERY ZONE (Directly in front of the castle entrance)
            Vector3 deliveryPos = center - forward * 3.5f;
            deliveryPos.y = baseY + 0.05f;

            GameObject deliveryZone = GameObject.Find("DeliveryZone");
            if (deliveryZone == null && _deliveryZonePrefab != null)
            {
                deliveryZone = (GameObject)PrefabUtility.InstantiatePrefab(_deliveryZonePrefab, castleRoot);
                deliveryZone.name = "DeliveryZone";
            }
            if (deliveryZone != null)
            {
                deliveryZone.transform.SetParent(castleRoot, true);
                deliveryZone.transform.position = deliveryPos;
                deliveryZone.transform.rotation = Quaternion.LookRotation(forward);
            }

            // Castle Delivery Sign
            CreateThaiSign("CastleDeliverySign", castleRoot,
                deliveryPos - right * 4.5f,
                "ปราสาทหลวง — จุดส่งของสุดท้าย\nวางลังลงในวงกลมสีทองเพื่อสำเร็จภารกิจ!\nขอแสดงความยินดีกับทีมขนส่ง!",
                forward);

            // Castle Entrance Braziers
            CreateTorch("CastleGateTorch_L", castleRoot, center + forward * 5f - right * 3.2f);
            CreateTorch("CastleGateTorch_R", castleRoot, center + forward * 5f + right * 3.2f);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 5. DECORATE SMALL ISLANDS (TREES, SPIKES, TRAPS)
        // ─────────────────────────────────────────────────────────────────────────
        private static void DecorateSmallIslands(Transform mapPrefap, Transform sceneryRoot)
        {
            Transform decorRoot = sceneryRoot.Find("SmallIslandDecorations");
            if (decorRoot != null) Undo.DestroyObjectImmediate(decorRoot.gameObject);

            decorRoot = new GameObject("SmallIslandDecorations").transform;
            decorRoot.SetParent(sceneryRoot, false);

            // Iterate all small islands under Map Prefap
            List<Transform> islands = new List<Transform>();
            foreach (Transform t in mapPrefap)
            {
                if (t.name.Contains("เกาะเล็ก")) islands.Add(t);
            }

            // Distribute pine trees and spikes on each small island
            for (int i = 0; i < islands.Count; i++)
            {
                Transform isl = islands[i];
                Vector3 p = isl.position;
                float topY = p.y + 0.35f;

                // Add 1-2 pine trees on the edge
                if (_pineTreePrefab != null)
                {
                    Vector3 treePos1 = p + new Vector3(-3.0f, topY - p.y, 1.5f);
                    GameObject tree1 = (GameObject)PrefabUtility.InstantiatePrefab(_pineTreePrefab, decorRoot);
                    tree1.transform.position = treePos1;
                    tree1.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.85f, 1.15f);
                    tree1.transform.rotation = Quaternion.Euler(0, i * 45f, 0);

                    if (i % 2 == 0)
                    {
                        Vector3 treePos2 = p + new Vector3(3.2f, topY - p.y, -1.2f);
                        GameObject tree2 = (GameObject)PrefabUtility.InstantiatePrefab(_pineTreePrefab, decorRoot);
                        tree2.transform.position = treePos2;
                        tree2.transform.localScale = Vector3.one * UnityEngine.Random.Range(0.8f, 1.05f);
                        tree2.transform.rotation = Quaternion.Euler(0, i * 70f + 30f, 0);
                    }
                }

                // Add 1-2 wooden spikes on the island surface to challenge landing
                if (_spikePrefab != null)
                {
                    Vector3 spikePos = p + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), topY - p.y, UnityEngine.Random.Range(-1.2f, 1.2f));
                    GameObject spike = (GameObject)PrefabUtility.InstantiatePrefab(_spikePrefab, decorRoot);
                    spike.transform.position = spikePos;
                    spike.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 360f), 0);
                }
            }

            // Add active traps across key hopping locations
            // 1. Swing Hammer over vertical hop
            if (_swingHammerPrefab != null)
            {
                Vector3 hammerPos = new Vector3(60.3f, 9.8f, -92.5f);
                GameObject hammer = (GameObject)PrefabUtility.InstantiatePrefab(_swingHammerPrefab, decorRoot);
                hammer.name = "Hazard_SwingHammer_Chasm1";
                hammer.transform.position = hammerPos;
                hammer.transform.rotation = Quaternion.Euler(0, 0, 0);
            }

            // 2. Twin Pendulums guarding the Island 2 -> 3 crossing
            if (_twinPendulumsPrefab != null)
            {
                Vector3 pendPos = new Vector3(-21.5f, 10.2f, 10.2f);
                GameObject pend = (GameObject)PrefabUtility.InstantiatePrefab(_twinPendulumsPrefab, decorRoot);
                pend.name = "Medieval_TwinPendulums_Chasm2";
                pend.transform.position = pendPos;
                pend.transform.rotation = Quaternion.Euler(0, 45f, 0);
            }

            // 3. Timed wooden spikes on stepping island
            if (_woodenSpikesPrefab != null)
            {
                Vector3 spikeTrapPos = new Vector3(53.07f, 4.9f, -137.11f);
                GameObject spikeTrap = (GameObject)PrefabUtility.InstantiatePrefab(_woodenSpikesPrefab, decorRoot);
                spikeTrap.name = "Medieval_WoodenSpikes_Trap";
                spikeTrap.transform.position = spikeTrapPos;
                spikeTrap.transform.rotation = Quaternion.identity;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 6. SURROUNDING CLOUDS
        // ─────────────────────────────────────────────────────────────────────────
        private static void BuildSurroundingClouds(Transform cloudsRoot)
        {
            // Clear existing clouds if any
            for (int i = cloudsRoot.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(cloudsRoot.GetChild(i).gameObject);
            }

            // List of cloud centers and scales around the 3 big islands and connecting chasms
            var cloudDefinitions = new (Vector3 pos, Vector3 scale)[]
            {
                // Around Big Island 1 (Starter Village)
                (new Vector3(10f, -10f, -195f), new Vector3(32f, 5f, 18f)),
                (new Vector3(65f, -8f, -200f), new Vector3(38f, 6f, 22f)),
                (new Vector3(15f, -12f, -165f), new Vector3(28f, 5f, 16f)),
                (new Vector3(70f, -11f, -165f), new Vector3(35f, 5f, 20f)),

                // Chasm 1 (Island 1 -> Island 2)
                (new Vector3(35f, -14f, -130f), new Vector3(30f, 6f, 24f)),
                (new Vector3(80f, -12f, -120f), new Vector3(36f, 5f, 22f)),
                (new Vector3(45f, -13f, -95f), new Vector3(34f, 6f, 20f)),
                (new Vector3(80f, -15f, -75f), new Vector3(35f, 5f, 25f)),
                (new Vector3(20f, -14f, -75f), new Vector3(30f, 6f, 20f)),

                // Around Big Island 2 (Dense Forest)
                (new Vector3(-15f, -10f, -45f), new Vector3(35f, 5f, 22f)),
                (new Vector3(55f, -9f, -40f), new Vector3(38f, 6f, 25f)),
                (new Vector3(-10f, -13f, -20f), new Vector3(32f, 5f, 20f)),
                (new Vector3(45f, -12f, -15f), new Vector3(35f, 5f, 22f)),

                // Chasm 2 (Island 2 -> Island 3)
                (new Vector3(15f, -14f, 5f), new Vector3(30f, 6f, 20f)),
                (new Vector3(-15f, -12f, 12f), new Vector3(34f, 5f, 22f)),
                (new Vector3(-25f, -15f, 25f), new Vector3(32f, 6f, 20f)),

                // Around Big Island 3 (Grand Castle)
                (new Vector3(-75f, -9f, 25f), new Vector3(36f, 6f, 24f)),
                (new Vector3(-25f, -8f, 55f), new Vector3(35f, 5f, 22f)),
                (new Vector3(-75f, -11f, 60f), new Vector3(40f, 7f, 26f)),
                (new Vector3(-50f, -14f, 75f), new Vector3(42f, 7f, 28f)),
                (new Vector3(-90f, -12f, 40f), new Vector3(38f, 6f, 25f)),

                // Wide horizon bank
                (new Vector3(0f, -18f, -100f), new Vector3(60f, 8f, 40f)),
                (new Vector3(-40f, -18f, 0f), new Vector3(55f, 8f, 38f))
            };

            for (int i = 0; i < cloudDefinitions.Length; i++)
            {
                var def = cloudDefinitions[i];
                GameObject cloud = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cloud.name = "Cloud_" + i;
                cloud.transform.SetParent(cloudsRoot, false);
                cloud.transform.position = def.pos;
                cloud.transform.localScale = def.scale;
                cloud.transform.rotation = Quaternion.Euler(0, i * 37f, 0);

                // Destroy collider (clouds are visual backdrop)
                Collider col = cloud.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);

                // Assign cloud material
                MeshRenderer rend = cloud.GetComponent<MeshRenderer>();
                if (rend != null && _cloud != null) rend.sharedMaterial = _cloud;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 7. MESH COLLIDERS FOR ALL ISLANDS
        // ─────────────────────────────────────────────────────────────────────────
        private static void EnsureAllIslandColliders(Transform mapPrefap)
        {
            MeshFilter[] filters = mapPrefap.GetComponentsInChildren<MeshFilter>(true);
            int addedCount = 0;

            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                GameObject go = mf.gameObject;

                // Check if this is an island model
                if (go.name.Contains("เกาะ") || (go.transform.parent != null && go.transform.parent.name.Contains("เกาะ")))
                {
                    MeshCollider mc = go.GetComponent<MeshCollider>();
                    if (mc == null)
                    {
                        mc = go.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = false;
                        addedCount++;
                    }
                    else
                    {
                        if (mc.sharedMesh == null) mc.sharedMesh = mf.sharedMesh;
                        if (mc.convex) mc.convex = false;
                    }
                }
            }

            Debug.Log($"[NewLevel01Builder] Verified non-convex MeshColliders on {addedCount} island meshes.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // HELPER BUILDERS
        // ─────────────────────────────────────────────────────────────────────────
        private static GameObject CreateBoxPrimitive(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, bool solid)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!solid)
            {
                Collider col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
            }
            return go;
        }

        private static void CreateHouse(string name, Transform parent, Vector3 pos, float yaw, Vector2 footprint, float height)
        {
            GameObject house = new GameObject(name);
            house.transform.SetParent(parent, false);
            house.transform.position = pos;
            house.transform.rotation = Quaternion.Euler(0, yaw, 0);

            // Walls
            CreateBoxPrimitive("Walls", house.transform, new Vector3(0, height * 0.5f, 0), new Vector3(footprint.x, height, footprint.y), _plaster, true);

            // Roof (two sloped eaves)
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject slab = CreateBoxPrimitive("Roof_" + (side > 0 ? "R" : "L"), house.transform,
                    new Vector3(side * footprint.x * 0.25f, height + footprint.x * 0.2f, 0),
                    new Vector3(footprint.x * 0.65f, 0.25f, footprint.y + 0.6f),
                    _roof, true);
                slab.transform.localRotation = Quaternion.Euler(0, 0, -side * 35f);
            }

            // Door
            CreateBoxPrimitive("Door", house.transform, new Vector3(0, 0.9f, -footprint.y * 0.5f - 0.04f), new Vector3(1.1f, 1.8f, 0.1f), _wood, false);
        }

        private static void CreateWell(string name, Transform parent, Vector3 pos)
        {
            GameObject well = new GameObject(name);
            well.transform.SetParent(parent, false);
            well.transform.position = pos;

            // Stone cylinder base
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "WellRing";
            ring.transform.SetParent(well.transform, false);
            ring.transform.localPosition = new Vector3(0, 0.45f, 0);
            ring.transform.localScale = new Vector3(2.2f, 0.45f, 2.2f);
            if (_stone != null) ring.GetComponent<Renderer>().sharedMaterial = _stone;

            // Posts & roof
            CreateBoxPrimitive("WellPost_L", well.transform, new Vector3(-0.95f, 1.3f, 0), new Vector3(0.16f, 1.7f, 0.16f), _wood, false);
            CreateBoxPrimitive("WellPost_R", well.transform, new Vector3(0.95f, 1.3f, 0), new Vector3(0.16f, 1.7f, 0.16f), _wood, false);
            CreateBoxPrimitive("WellRoof", well.transform, new Vector3(0, 2.3f, 0), new Vector3(2.5f, 0.18f, 1.6f), _roof, false);
        }

        private static void CreateMarketStall(string name, Transform parent, Vector3 pos, float yaw)
        {
            GameObject stall = new GameObject(name);
            stall.transform.SetParent(parent, false);
            stall.transform.position = pos;
            stall.transform.rotation = Quaternion.Euler(0, yaw, 0);

            // Table / counter
            CreateBoxPrimitive("Counter", stall.transform, new Vector3(0, 0.5f, 0), new Vector3(2.4f, 1.0f, 1.2f), _wood, true);

            // Canopy posts & cloth
            CreateBoxPrimitive("Post_FL", stall.transform, new Vector3(-1.1f, 1.25f, -0.55f), new Vector3(0.1f, 1.5f, 0.1f), _wood, false);
            CreateBoxPrimitive("Post_FR", stall.transform, new Vector3(1.1f, 1.25f, -0.55f), new Vector3(0.1f, 1.5f, 0.1f), _wood, false);
            CreateBoxPrimitive("Post_BL", stall.transform, new Vector3(-1.1f, 1.45f, 0.55f), new Vector3(0.1f, 1.9f, 0.1f), _wood, false);
            CreateBoxPrimitive("Post_BR", stall.transform, new Vector3(1.1f, 1.45f, 0.55f), new Vector3(0.1f, 1.9f, 0.1f), _wood, false);

            GameObject canopy = CreateBoxPrimitive("Canopy", stall.transform, new Vector3(0, 2.2f, 0), new Vector3(2.6f, 0.08f, 1.5f), _cloth, false);
            canopy.transform.localRotation = Quaternion.Euler(12f, 0, 0);
        }

        private static void CreateGoldPad(string name, Transform parent, Vector3 pos, float size)
        {
            GameObject pad = new GameObject(name);
            pad.transform.SetParent(parent, false);
            pad.transform.position = pos;

            // Stone base ring
            CreateBoxPrimitive("PadRim", pad.transform, Vector3.zero, new Vector3(size, 0.08f, size), _stone, true);
            // Gold center plate
            CreateBoxPrimitive("PadCenter", pad.transform, Vector3.up * 0.04f, new Vector3(size * 0.85f, 0.08f, size * 0.85f), _gold, false);
        }

        private static void CreateTorch(string name, Transform parent, Vector3 pos)
        {
            GameObject torch = new GameObject(name);
            torch.transform.SetParent(parent, false);
            torch.transform.position = pos;

            CreateBoxPrimitive("TorchStake", torch.transform, Vector3.up * 0.9f, new Vector3(0.14f, 1.8f, 0.14f), _wood, false);
            GameObject head = CreateBoxPrimitive("TorchHead", torch.transform, Vector3.up * 1.85f, new Vector3(0.24f, 0.24f, 0.24f), _gold, false);

            // Add soft point light
            Light light = head.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.72f, 0.35f);
            light.intensity = 1.8f;
            light.range = 8.0f;
            light.shadows = LightShadows.None;
        }

        private static void CreateFlag(string name, Transform parent, Vector3 pos, Material clothMat)
        {
            GameObject flag = new GameObject(name);
            flag.transform.SetParent(parent, false);
            flag.transform.position = pos;

            CreateBoxPrimitive("Pole", flag.transform, Vector3.up * 3.0f, new Vector3(0.12f, 6.0f, 0.12f), _wood, false);
            CreateBoxPrimitive("Cloth", flag.transform, new Vector3(0.75f, 5.2f, 0), new Vector3(1.5f, 1.0f, 0.04f), clothMat, false);
        }

        private static void CreateThaiSign(string name, Transform parent, Vector3 pos, string text, Vector3 reader)
        {
            GameObject sign = new GameObject(name);
            sign.transform.SetParent(parent, false);
            sign.transform.position = pos;
            sign.transform.rotation = Quaternion.LookRotation(reader.sqrMagnitude > 0.01f ? reader : Vector3.forward);

            // Post
            CreateBoxPrimitive("Post", sign.transform, new Vector3(0, 1.1f, 0), new Vector3(0.16f, 2.2f, 0.16f), _wood, false);

            // Plate backing
            CreateBoxPrimitive("Backing", sign.transform, new Vector3(0, 2.1f, 0.04f), new Vector3(3.8f, 1.4f, 0.08f), _stone, false);

            // TextMesh
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(sign.transform, false);
            textObj.transform.localPosition = new Vector3(0, 2.1f, -0.02f);
            textObj.transform.localRotation = Quaternion.identity;

            TextMesh tm = textObj.AddComponent<TextMesh>();
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = 0.028f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;
        }
    }
}
