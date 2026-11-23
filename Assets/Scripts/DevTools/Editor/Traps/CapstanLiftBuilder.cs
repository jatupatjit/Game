#if UNITY_EDITOR
using System.Collections.Generic;
using CoopGame.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools.Traps
{
    /// <summary>
    /// Builds Assets/Prefabs/Traps/Coop/Coop_CapstanLift: a capstan (pole + 4 push bars + walk ring) and a wooden
    /// platform on a pulley frame. Root sits at the pole base; the platform starts 4.5 m ahead (+Z) and rises 5 m.
    /// Move the Platform and LiftFrame children together in a level to fit the cliff. Safe to run again.
    /// </summary>
    public static class CapstanLiftBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/Traps/Coop/Coop_CapstanLift.prefab";
        private const string MaterialFolder = "Assets/Prefabs/Traps/Materials";
        private const string Medieval = "Assets/Prefabs/Traps/Materials/Medieval/";
        private const float Rise = 5f;
        private const float WalkRadius = 2.2f;
        private static readonly Vector3 PlatformRest = new(0f, .15f, 4.5f);

        [MenuItem("Tools/Traps/Build Coop Capstan Lift")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[CapstanLiftBuilder] Stop Play mode first.";
            var wood = Load(Medieval + "Medieval_Wood.mat");
            var woodLight = Load(Medieval + "Medieval_WoodLight.mat");
            var stone = Load(Medieval + "Medieval_Stone.mat");
            var iron = Load(Medieval + "Medieval_Iron.mat");
            var active = Load(MaterialFolder + "/Trap_SwitchButton.mat");
            var rope = Make("Capstan_Rope", new Color(.85f, .74f, .5f));
            var idle = Make("Capstan_RingIdle", new Color(.93f, .86f, .62f));

            var previous = EditorSceneManager.GetActiveScene();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(temp);
            try
            {
                var root = new GameObject("Coop_CapstanLift");
                root.AddComponent<NetworkObject>();
                var lift = root.AddComponent<CoopCapstanLift>();

                // Capstan: solid pole in the middle, a dashed walk ring, and a hub whose bars turn with the platform.
                var pole = Cylinder("Pole", root.transform, new Vector3(0, 1f, 0), new Vector3(.55f, 1f, .55f), wood, true);
                Cylinder("PoleBase", root.transform, new Vector3(0, .08f, 0), new Vector3(1.3f, .08f, 1.3f), stone, false);
                var ring = new List<Object>();
                const int dashes = 18;
                for (int i = 0; i < dashes; i++)
                {
                    float a = i * 360f / dashes;
                    var dash = Box("RingDash", root.transform, Quaternion.Euler(0, a, 0) * new Vector3(0, .03f, WalkRadius),
                        new Vector3(.55f, .04f, .22f), idle, new Vector3(0, a + 90f, 0), false);
                    ring.Add(dash.GetComponent<Renderer>());
                }
                // Arrows on the ring show the winding direction (clockwise from above).
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f + 45f;
                    Vector3 p = Quaternion.Euler(0, a, 0) * new Vector3(0, .05f, WalkRadius);
                    var arrow = Box("RingArrow", root.transform, p, new Vector3(.12f, .05f, .5f), active, new Vector3(0, a + 90f + 35f, 0), false);
                    arrow.transform.localPosition += arrow.transform.forward * .12f;
                    Box("RingArrow", root.transform, p, new Vector3(.12f, .05f, .5f), active, new Vector3(0, a + 90f - 35f, 0), false)
                        .transform.localPosition += Quaternion.Euler(0, a + 90f - 35f, 0) * Vector3.forward * .12f;
                    ring.Add(arrow.GetComponent<Renderer>());
                }
                var hub = new GameObject("Hub").transform;
                hub.SetParent(root.transform, false);
                hub.localPosition = new Vector3(0, 1.15f, 0);
                Cylinder("HubDrum", hub, Vector3.zero, new Vector3(.85f, .28f, .85f), woodLight, false);
                Cylinder("HubCap", hub, new Vector3(0, .95f, 0), new Vector3(.75f, .06f, .75f), iron, false);
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 90f;
                    Box("PushBar", hub, Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 1.45f), new Vector3(.16f, .16f, 2.3f), wood, new Vector3(0, a, 0), false);
                    Cylinder("BarGrip", hub, Quaternion.Euler(0, a, 0) * new Vector3(0, 0, 2.55f), new Vector3(.22f, .12f, .22f), iron, false);
                }

                // Platform on a pulley frame. The frame stands behind the platform (+Z, against the cliff).
                var platform = Box("Platform", root.transform, PlatformRest, new Vector3(3.4f, .3f, 3f), woodLight, default, true);
                var body = platform.AddComponent<Rigidbody>();
                body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.Interpolate;
                for (int i = -1; i <= 1; i++)
                    Box("Plank", platform.transform, new Vector3(i * .32f, .55f, 0), new Vector3(.27f, .15f, 1f), wood, default, false);
                foreach (float x in new[] { -.47f, .47f })
                    Box("SideRail", platform.transform, new Vector3(x, 1.2f, 0), new Vector3(.06f, 1.4f, 1f), wood, default, true);
                var frame = new GameObject("LiftFrame").transform;
                frame.SetParent(root.transform, false);
                frame.localPosition = new Vector3(PlatformRest.x, 0f, PlatformRest.z);
                float postHeight = Rise + 1.6f;
                foreach (float x in new[] { -1.95f, 1.95f })
                    Box("FramePost", frame, new Vector3(x, postHeight * .5f, 1.35f), new Vector3(.28f, postHeight, .28f), wood, default, true);
                Box("PulleyBeam", frame, new Vector3(0, postHeight, 1.35f), new Vector3(4.4f, .3f, .32f), wood, default, false);
                var ropeTop = new GameObject("RopeTop").transform;
                ropeTop.SetParent(frame, false);
                ropeTop.localPosition = new Vector3(0, postHeight - .2f, .9f);
                Cylinder("Pulley", frame, new Vector3(0, postHeight - .2f, 1.1f), new Vector3(.45f, .08f, .45f), iron, false, new Vector3(90, 0, 0));
                var platformRope = Cylinder("PlatformRope", root.transform, Vector3.zero, new Vector3(.07f, 1f, .07f), rope, false);
                Cylinder("WindRope", root.transform, Vector3.zero, new Vector3(.07f, 1f, .07f), rope, false);
                FitWindRope(root);

                var status = new GameObject("StatusText");
                status.transform.SetParent(root.transform, false);
                status.transform.localPosition = new Vector3(-3.4f, 2.3f, -.5f);
                status.transform.localRotation = Quaternion.Euler(0, 30f, 0);
                var text = status.AddComponent<TextMesh>();
                text.text = "เดินดันคานรอบเสาตามเข็มนาฬิกา\nเพื่อยกแท่น";
                text.fontSize = 112; text.characterSize = .022f;
                text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = Color.white;
                Box("StatusPlate", status.transform, new Vector3(0, 0, .06f), new Vector3(4.6f, 1.25f, .08f), wood, default, false);
                Box("StatusPost", status.transform, new Vector3(0, -1.35f, .14f), new Vector3(.16f, 1.6f, .16f), wood, default, false);

                var so = new SerializedObject(lift);
                so.FindProperty("_pole").objectReferenceValue = pole.transform;
                so.FindProperty("_hub").objectReferenceValue = hub;
                so.FindProperty("_platform").objectReferenceValue = body;
                so.FindProperty("_rise").vector3Value = new Vector3(0, Rise, 0);
                so.FindProperty("_innerRadius").floatValue = .9f;
                so.FindProperty("_outerRadius").floatValue = 3f;
                so.FindProperty("_platformRope").objectReferenceValue = platformRope.transform;
                so.FindProperty("_ropeTop").objectReferenceValue = ropeTop;
                var dashesProp = so.FindProperty("_ringIndicators");
                dashesProp.arraySize = ring.Count;
                for (int i = 0; i < ring.Count; i++) dashesProp.GetArrayElementAtIndex(i).objectReferenceValue = ring[i];
                so.FindProperty("_idleMaterial").objectReferenceValue = idle;
                so.FindProperty("_activeMaterial").objectReferenceValue = active;
                so.FindProperty("_statusText").objectReferenceValue = text;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                Object.DestroyImmediate(root);
                return ok ? "[CapstanLiftBuilder] Saved " + PrefabPath : "[CapstanLiftBuilder] Could not save " + PrefabPath;
            }
            finally
            {
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(temp, true);
            }
        }

        /// <summary>Stretches the fixed rope from the capstan drum to the pulley, after the frame has been moved.</summary>
        public static void FitWindRope(GameObject root)
        {
            var rope = root.transform.Find("WindRope");
            var top = root.transform.Find("LiftFrame/RopeTop");
            if (rope == null || top == null) return;
            Vector3 drum = new(0, 1.2f, 0);
            Vector3 end = root.transform.InverseTransformPoint(top.position);
            rope.localPosition = (drum + end) * .5f;
            rope.localRotation = Quaternion.FromToRotation(Vector3.up, end - drum);
            rope.localScale = new Vector3(.07f, (end - drum).magnitude * .5f, .07f);

            // Platform rope at rest (the script stretches it at runtime): pulley down to the platform top.
            var platformRope = root.transform.Find("PlatformRope");
            var platform = root.transform.Find("Platform");
            if (platformRope == null || platform == null) return;
            Vector3 bottom = root.transform.InverseTransformPoint(platform.position + Vector3.up * platform.lossyScale.y * .5f);
            platformRope.localPosition = (bottom + end) * .5f;
            platformRope.localRotation = Quaternion.FromToRotation(Vector3.up, end - bottom);
            platformRope.localScale = new Vector3(.07f, Mathf.Max(.01f, (end - bottom).magnitude * .5f), .07f);
        }

        private static Material Load(string path) => AssetDatabase.LoadAssetAtPath<Material>(path);

        private static Material Make(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material mat,
            Vector3 euler = default, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, Material mat,
            bool collider, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (collider) go.AddComponent<MeshCollider>().convex = true;
            return go;
        }
    }
}
#endif
