#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using CoopGame.CarrySystem;
using CoopGame.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;


    /// <summary>
    /// Builds and integrates hardcore extreme hazards into Level01.unity:
    /// 1. Synchronized Double-Blade Pendulum on 06_CargoBridge
    /// 2. Server-Time Rotating Log Bridge on 05_CargoBridge
    /// 3. Collapsing Stepping Stones Gauntlet (5 precision stones)
    /// 4. Two-Player Timed Switch Gate with co-op drawbridge on 08_CargoBridge
    /// </summary>
    public static class HardcoreObstacleCourse
    {
        private const string RootName = "HardcoreUpgrade_v3";
        private static Transform _root;
        private static Material _wood, _stone, _gold, _warning, _blue;
        private static Font _font;
        private static FragileCargo _cargo;

        [MenuItem("Tools/Expedition/Add Hardcore Extreme Obstacles")]
        public static string Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
            if (!scene.isLoaded) throw new InvalidOperationException("Open Level01 first.");

            var roots = scene.GetRootGameObjects();
            var route = roots.Single(g => g.name == "FloatingCoopRoute").transform;

            Transform existing = route.Find(RootName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            _wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/BridgeWood.mat");
            _stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/Stone.mat");
            _gold = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/GoalGold.mat");
            _warning = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/TrapWarning.mat");
            _blue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/RelayBlue.mat");
            _font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/GameThai.ttf");
            _cargo = roots.Select(r => r.GetComponent<FragileCargo>()).FirstOrDefault(c => c != null);

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Hardcore Extreme Obstacles");

            try
            {
                var rootObj = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(rootObj, "Create Hardcore Root");
                _root = rootObj.transform;
                _root.SetParent(route, false);

                var scale = route.lossyScale;
                _root.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);

                var all = route.GetComponentsInChildren<Transform>(true);
                Transform bridge05 = all.FirstOrDefault(t => t.name == "05_CargoBridge");
                Transform bridge06 = all.FirstOrDefault(t => t.name == "06_CargoBridge");
                Transform bridge08 = all.FirstOrDefault(t => t.name == "08_CargoBridge");

                // 1. Build Synchronized Double Pendulum on bridge 06
                if (bridge06 != null)
                {
                    BuildDoublePendulum(bridge06);
                }

                // 2. Build Rotating Log Hazard on bridge 05
                if (bridge05 != null)
                {
                    BuildRotatingLog(bridge05);
                }

                // 3. Build Collapsing Stepping Stones Gauntlet
                BuildSteppingStonesGauntlet();

                // 4. Build Two-Player Timed Switch Gate on bridge 08
                if (bridge08 != null)
                {
                    BuildTwoPlayerTimedGate(bridge08);
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Undo.CollapseUndoOperations(group);
                Selection.activeGameObject = _root.gameObject;

                return "Hardcore Extreme Obstacles successfully created in Level01!";
            }
            catch (Exception ex)
            {
                Undo.RevertAllDownToGroup(group);
                Debug.LogException(ex);
                throw;
            }
        }

        private static void BuildDoublePendulum(Transform bridge)
        {
            var hazardObj = new GameObject("DoublePendulum_Relay");
            hazardObj.transform.SetParent(_root, false);
            hazardObj.transform.position = bridge.position + Vector3.up * 1.5f;
            hazardObj.transform.rotation = bridge.rotation;

            hazardObj.AddComponent<NetworkObject>();
            var hazard = hazardObj.AddComponent<NetworkRouteHazard>();

            const float width = 5.2f;
            const float pivotHeight = 5.2f;

            // Frame posts & beam
            for (int side = -1; side <= 1; side += 2)
            {
                CreateLocalBox("PendulumPost", hazardObj.transform, new Vector3(side * (width * 0.5f + 0.6f), pivotHeight * 0.5f, 0), new Vector3(0.5f, pivotHeight, 0.5f), _wood);
            }
            CreateLocalBox("PendulumBeam", hazardObj.transform, new Vector3(0, pivotHeight + 0.2f, 0), new Vector3(width + 1.8f, 0.4f, 0.8f), _wood);

            // Pivot 1 (Primary)
            var pivot1 = new GameObject("PendulumPivot_1").transform;
            pivot1.SetParent(hazardObj.transform, false);
            pivot1.localPosition = new Vector3(-0.8f, pivotHeight, 0);
            var rb1 = AddBody(pivot1.gameObject);
            CreateLocalBox("PendulumArm_1", pivot1, new Vector3(0, -2.0f, 0), new Vector3(0.2f, 4.0f, 0.2f), _wood, false);
            var head1 = CreateLocalBox("PendulumBlade_1", pivot1, new Vector3(0, -4.0f, 0), new Vector3(2.0f, 1.4f, 0.5f), _stone);

            // Pivot 2 (Secondary)
            var pivot2 = new GameObject("PendulumPivot_2").transform;
            pivot2.SetParent(hazardObj.transform, false);
            pivot2.localPosition = new Vector3(0.8f, pivotHeight, 0);
            var rb2 = AddBody(pivot2.gameObject);
            CreateLocalBox("PendulumArm_2", pivot2, new Vector3(0, -2.0f, 0), new Vector3(0.2f, 4.0f, 0.2f), _wood, false);
            var head2 = CreateLocalBox("PendulumBlade_2", pivot2, new Vector3(0, -4.0f, 0), new Vector3(2.0f, 1.4f, 0.5f), _stone);

            // Configure SerializedObject
            var so = new SerializedObject(hazard);
            so.FindProperty("_kind").enumValueIndex = (int)NetworkRouteHazard.HazardKind.DoublePendulum;
            so.FindProperty("_primary").objectReferenceValue = rb1;
            so.FindProperty("_secondary").objectReferenceValue = rb2;
            so.FindProperty("_hitA").objectReferenceValue = head1.GetComponent<BoxCollider>();
            so.FindProperty("_hitB").objectReferenceValue = head2.GetComponent<BoxCollider>();
            so.FindProperty("_cargo").objectReferenceValue = _cargo;
            so.FindProperty("_swingAngle").floatValue = 68f;
            so.FindProperty("_activeSeconds").floatValue = 4.0f;
            so.FindProperty("_safeSeconds").floatValue = 5.5f;
            so.FindProperty("_warningSeconds").floatValue = 2.0f;

            var lamp = CreateLocalBox("WarningLamp", hazardObj.transform, new Vector3(-width * 0.5f - 1.2f, 2.5f, -3f), new Vector3(0.5f, 0.7f, 0.5f), _warning, false);
            so.FindProperty("_indicator").objectReferenceValue = lamp.GetComponent<Renderer>();

            var sign = CreateSign("PendulumSign", hazardObj.transform.position + new Vector3(-width * 0.5f - 2.5f, 2.0f, -4.5f),
                "ระวังลูกตุ้มคู่ตัดขวาง!\nรอจังหวะเปิดแล้วรีบวิ่ง", hazardObj.transform, 0, 0.055f);
            so.FindProperty("_label").objectReferenceValue = sign;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildRotatingLog(Transform bridge)
        {
            var logHazardObj = new GameObject("RotatingLog_Bridge");
            logHazardObj.transform.SetParent(_root, false);
            Vector3 centerPos = bridge.position + Vector3.up * 0.6f;
            logHazardObj.transform.position = centerPos;
            logHazardObj.transform.rotation = bridge.rotation;

            logHazardObj.AddComponent<NetworkObject>();
            var hazard = logHazardObj.AddComponent<RotatingLogHazard>();

            // The rotating cylinder log
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "RollingLogVisual";
            cylinder.transform.SetParent(logHazardObj.transform, false);
            cylinder.transform.localPosition = Vector3.zero;
            // Align cylinder along forward axis (rotate 90 on X)
            cylinder.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cylinder.transform.localScale = new Vector3(1.4f, 6.0f, 1.4f);

            var rend = cylinder.GetComponent<Renderer>();
            if (_wood != null) rend.sharedMaterial = _wood;

            var rb = cylinder.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var col = cylinder.GetComponent<CapsuleCollider>();
            col.radius = 0.5f;
            col.height = 2f;

            // Wire serialized fields
            var so = new SerializedObject(hazard);
            so.FindProperty("_logRigidbody").objectReferenceValue = rb;
            so.FindProperty("_axis").enumValueIndex = (int)RotatingLogHazard.RotationAxis.Z; // roll around Z (bridge forward)
            so.FindProperty("_rotationSpeedDegrees").floatValue = 75f;
            so.FindProperty("_cargo").objectReferenceValue = _cargo;

            var lamp = CreateLocalBox("LogWarningLamp", logHazardObj.transform, new Vector3(-2.8f, 1.8f, -4f), new Vector3(0.5f, 0.6f, 0.5f), _warning, false);
            so.FindProperty("_indicatorRenderer").objectReferenceValue = lamp.GetComponent<Renderer>();

            CreateSign("LogSign", logHazardObj.transform.position + new Vector3(-3.2f, 2.0f, -5.5f),
                "สะพานท่อนซุงหมุนวน!\nทรงตัวดีๆ อย่าให้ตกข้างทาง", logHazardObj.transform, 0, 0.055f);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildSteppingStonesGauntlet()
        {
            var gauntlet = new GameObject("CollapsingSteppingStones_Gauntlet");
            gauntlet.transform.SetParent(_root, false);
            gauntlet.transform.position = new Vector3(2.5f, 5.0f, 210f);

            // Create 5 precision jumping stones across a chasm
            Vector3 startPos = new Vector3(-12f, 5.5f, 205f);
            Vector3 endPos = new Vector3(6f, 5.0f, 222f);

            CreateSign("SteppingStoneSign", startPos + new Vector3(-2f, 2f, -2f),
                "หินยุบตัวต่อเนื่อง!\nกระโดดข้ามทันที ห้ามหยุดนิ่ง", gauntlet.transform, 0, 0.055f);

            for (int i = 0; i < 5; i++)
            {
                float t = (i + 1) / 6.0f;
                Vector3 pos = Vector3.Lerp(startPos, endPos, t);
                // Stagger slightly left and right for challenging jump arc
                pos.x += ((i % 2 == 0) ? 1.2f : -1.2f);
                pos.y += Mathf.Sin(t * Mathf.PI) * 0.8f;

                var stoneObj = new GameObject($"SteppingStone_{i + 1}");
                stoneObj.transform.SetParent(gauntlet.transform, false);
                stoneObj.transform.position = pos;

                stoneObj.AddComponent<NetworkObject>();
                var stone = stoneObj.AddComponent<CollapsingSteppingStone>();

                var stoneVis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                stoneVis.name = "StoneVisual";
                stoneVis.transform.SetParent(stoneObj.transform, false);
                stoneVis.transform.localPosition = Vector3.zero;
                stoneVis.transform.localScale = new Vector3(1.6f, 0.45f, 1.6f);

                if (_stone != null) stoneVis.GetComponent<Renderer>().sharedMaterial = _stone;

                var rb = stoneVis.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                var so = new SerializedObject(stone);
                so.FindProperty("_rigidbody").objectReferenceValue = rb;
                so.FindProperty("_solidCollider").objectReferenceValue = stoneVis.GetComponent<Collider>();
                so.FindProperty("_stoneRenderer").objectReferenceValue = stoneVis.GetComponent<Renderer>();
                so.FindProperty("_warningSeconds").floatValue = 0.65f;
                so.FindProperty("_collapsedSeconds").floatValue = 2.4f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void BuildTwoPlayerTimedGate(Transform bridge)
        {
            var gateObj = new GameObject("TwoPlayerTimedSwitchGate_Overpass");
            gateObj.transform.SetParent(_root, false);
            gateObj.transform.position = bridge.position;
            gateObj.transform.rotation = bridge.rotation;

            gateObj.AddComponent<NetworkObject>();
            var gate = gateObj.AddComponent<TwoPlayerTimedSwitchGate>();

            // Drawbridge slab that moves up/forward to bridge the gap
            var drawbridge = CreateLocalBox("CoopDrawbridge", gateObj.transform, new Vector3(0, -0.2f, 0), new Vector3(4.5f, 0.4f, 8.0f), _wood);
            var rb = drawbridge.AddComponent<Rigidbody>();
            rb.isKinematic = true;

            // Pressure Plate A (Left Cliff)
            var plateA = CreateLocalBox("PressurePlate_A", gateObj.transform, new Vector3(-4.5f, 0.2f, -3.5f), new Vector3(1.8f, 0.2f, 1.8f), _blue);
            var plateACol = plateA.GetComponent<BoxCollider>();

            // Pressure Plate B (Right Cliff)
            var plateB = CreateLocalBox("PressurePlate_B", gateObj.transform, new Vector3(4.5f, 0.2f, -3.5f), new Vector3(1.8f, 0.2f, 1.8f), _blue);
            var plateBCol = plateB.GetComponent<BoxCollider>();

            var sign = CreateSign("GateSign", gateObj.transform.position + new Vector3(0, 3.2f, -5.0f),
                "สวิตช์ร่วมใจสองผู้เล่น!\nเหยียบสองฝั่งพร้อมกันเพื่อเปิดสะพาน", gateObj.transform, 0, 0.055f);

            var so = new SerializedObject(gate);
            so.FindProperty("_gateRigidbody").objectReferenceValue = rb;
            so.FindProperty("_closedPosition").vector3Value = drawbridge.transform.position + Vector3.down * 4.5f;
            so.FindProperty("_openPosition").vector3Value = drawbridge.transform.position;
            so.FindProperty("_plateACollider").objectReferenceValue = plateACol;
            so.FindProperty("_plateBCollider").objectReferenceValue = plateBCol;
            so.FindProperty("_plateARenderer").objectReferenceValue = plateA.GetComponent<Renderer>();
            so.FindProperty("_plateBRenderer").objectReferenceValue = plateB.GetComponent<Renderer>();
            so.FindProperty("_syncWindowSeconds").floatValue = 4.5f;
            so.FindProperty("_statusLabel").objectReferenceValue = sign;

            // Start closed
            drawbridge.transform.position += Vector3.down * 4.5f;

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject CreateLocalBox(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static Rigidbody AddBody(GameObject go)
        {
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return rb;
        }

        private static TextMesh CreateSign(string name, Vector3 worldPos, string text, Transform parent, int layer = 0, float charSize = 0.05f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.LookRotation(Vector3.forward);

            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 42;
            mesh.characterSize = charSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = Color.white;
            if (_font != null) mesh.font = _font;

            var rend = go.GetComponent<MeshRenderer>();
            if (_font != null && _font.material != null) rend.sharedMaterial = _font.material;

            return mesh;
        }
    }
#endif
