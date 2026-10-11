#if UNITY_EDITOR
using System;
using System.Linq;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class Level01SoloCoopSetup
{
    // Targeted, repeatable migration. Never regenerates the islands or touches Lobby.
    public static string Apply()
    {
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/Level01.unity", OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            Transform Find(string name) => all.Single(t => t.name == name);
            Material Mat(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/" + name + ".mat");
            var wood = Mat("Wood");
            if (wood == null) wood = Find("RelaySweep").Find("SweepBeam").GetComponent<Renderer>().sharedMaterial;
            var blue = Find("BluePad_Left_Visible").GetComponent<Renderer>().sharedMaterial;
            const string grayPath = "Assets/Environment/FloatingCoop/InactivePressurePad.mat";
            var gray = AssetDatabase.LoadAssetAtPath<Material>(grayPath);
            if (gray == null)
            {
                gray = new Material(blue) { name = "InactivePressurePad" };
                gray.color = new Color(.32f, .36f, .39f);
                if (gray.HasProperty("_BaseColor")) gray.SetColor("_BaseColor", gray.color);
                AssetDatabase.CreateAsset(gray, grayPath);
            }
            var island = Find("Island_02");
            var existing = island.Find("RelayAccess_SoloCoop");
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            var access = new GameObject("RelayAccess_SoloCoop").transform;
            Undo.RegisterCreatedObjectUndo(access.gameObject, "Rebuild relay access");
            access.SetParent(island, false);
            foreach (var t in island.Cast<Transform>().Where(t => t.name == "RelayPlinth" || t.name == "RelayStair"))
            { Undo.RecordObject(t.gameObject, "Preserve old access"); t.gameObject.SetActive(false); }
            GameObject Box(string name, Vector3 position, Vector3 scale, Material material)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(go, "Relay access geometry");
                go.name = name; go.transform.SetParent(access, false);
                go.transform.localPosition = position; go.transform.localScale = scale;
                go.GetComponent<Renderer>().sharedMaterial = material;
                return go;
            }
            for (int side = -1; side <= 1; side += 2)
            {
                string bank = side < 0 ? "Left" : "Right";
                for (int step = 0; step < 6; step++)
                {
                    float top = .5f + step * .5f;
                    Box(bank + "_JumpLanding_" + (step + 1),
                        new Vector3(side * 8f + (step % 2 == 0 ? -.85f : .85f), top - .16f, -10f + step * 2.15f),
                        new Vector3(2.6f, .32f, 1.5f), wood);
                    if (step == 2 || step == 5)
                        Box(bank + "_RestLanding_" + step, new Vector3(side * 11f, top - .16f, -10f + step * 2.15f), new Vector3(3.2f, .32f, 2.8f), wood);
                }
                Box(bank + "_PadLanding", new Vector3(side * 8, 3.34f, 3.35f), new Vector3(4, .32f, 3.6f), wood);
                var visible = Find("BluePad_" + bank + "_Visible");
                Undo.RecordObject(visible, "Raise relay pad"); visible.localPosition = new Vector3(side * 8, 3.53f, 3.35f);
                var pad = Find("BluePad_" + bank + "_Occupancy");
                Undo.RecordObject(pad, "Align relay occupancy"); pad.localPosition = new Vector3(side * 8, 3.7f, 3.35f);
                var collider = pad.GetComponent<BoxCollider>(); Undo.RecordObject(collider, "Fit feet detection");
                collider.center = Vector3.zero; collider.size = new Vector3(3, .6f, 3); collider.isTrigger = true;
            }
            var bay = Box("CargoParkingBelowPads", new Vector3(0, .05f, -10), new Vector3(6.5f, .04f, 4), blue);
            Undo.DestroyObjectImmediate(bay.GetComponent<Collider>());
            var relay = Find("RelayBridge_TwoPlayerUnlock").GetComponent<CoopRelayBridge>();
            var relaySettings = new SerializedObject(relay);
            relaySettings.FindProperty("_chargeSeconds").floatValue = 5;
            relaySettings.FindProperty("_leftIndicator").objectReferenceValue = Find("BluePad_Left_Visible").GetComponent<Renderer>();
            relaySettings.FindProperty("_rightIndicator").objectReferenceValue = Find("BluePad_Right_Visible").GetComponent<Renderer>();
            relaySettings.FindProperty("_idleMaterial").objectReferenceValue = blue;
            relaySettings.FindProperty("_activeMaterial").objectReferenceValue = blue;
            relaySettings.FindProperty("_disabledMaterial").objectReferenceValue = gray;
            relaySettings.ApplyModifiedProperties();
            if (relay.GetComponent<RelayBridgeHUD>() == null) Undo.AddComponent<RelayBridgeHUD>(relay.gameObject);
            foreach (var gate in all.Select(t => t.GetComponent<CoopPressureGate>()).Where(g => g != null))
            {
                var settings = new SerializedObject(gate);
                settings.FindProperty("_holdSeconds").floatValue = 1.5f;
                settings.FindProperty("_disabledMaterial").objectReferenceValue = gray;
                settings.ApplyModifiedProperties();
            }
            foreach (var gate in all.Select(t => t.GetComponent<CoopChargeGate>()).Where(g => g != null))
            {
                var settings = new SerializedObject(gate);
                settings.FindProperty("_chargeSeconds").floatValue = 1.5f;
                settings.ApplyModifiedProperties();
            }
            var bridge = Find("03_ForestBridge");
            var deck = bridge.Find("ContinuousCargoDeck_7m");
            var vertices = deck.GetComponent<MeshFilter>().sharedMesh.vertices;
            float minZ = vertices.Min(v => v.z), maxZ = vertices.Max(v => v.z);
            var template = Find("RelaySweep");
            for (int i = 0; i < 3; i++)
            {
                string name = i == 0 ? "RelaySweep" : "ForestHammer_" + (i + 1);
                var hammer = all.FirstOrDefault(t => t.name == name);
                if (hammer == null)
                {
                    hammer = UnityEngine.Object.Instantiate(template.gameObject, template.parent).transform;
                    hammer.name = name; Undo.RegisterCreatedObjectUndo(hammer.gameObject, "Add bridge hammer");
                }
                float z = Mathf.Lerp(minZ, maxZ, (i + 1) * .25f);
                // Generated deck has a linear slope; sample its top at both ends.
                float y0 = vertices.Where(v => Mathf.Abs(v.z - minZ) < .01f).Max(v => v.y);
                float y1 = vertices.Where(v => Mathf.Abs(v.z - maxZ) < .01f).Max(v => v.y);
                Vector3 floor = deck.TransformPoint(new Vector3(0, Mathf.Lerp(y0, y1, (i + 1) * .25f), z));
                Undo.RecordObject(hammer, "Align bridge hammer");
                hammer.SetPositionAndRotation(floor, bridge.rotation);
                var settings = new SerializedObject(hammer.GetComponent<NetworkRouteHazard>());
                settings.FindProperty("_safeSeconds").floatValue = 6;
                settings.FindProperty("_warningSeconds").floatValue = 1.75f;
                settings.FindProperty("_activeSeconds").floatValue = 2.5f;
                settings.FindProperty("_phaseOffset").floatValue = i * (10.25f / 3f);
                settings.ApplyModifiedProperties();
                var zone = hammer.Find("SweepZone");
                if (zone != null) { Undo.RecordObject(zone, "Keep warning inside bridge"); zone.localScale = new Vector3(7, .03f, 2); }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Level01 save failed");
            AssetDatabase.SaveAssets();
            return "Level01 saved: two jump routes, retained-charge relay HUD, adaptive gates, three staggered bridge hammers.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
#endif
