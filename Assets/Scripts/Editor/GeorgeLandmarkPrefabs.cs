using CoopGame.SceneDecor;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// George 2026-11-21: his windmill, windmill blades and lighthouse FBX as moving decor prefabs.
    /// Windmill: blades on the hub at the front of the cap, spinning (DecorMotion, local only, same speed on every screen).
    /// Lighthouse: a glowing lamp with two light beams turning at the top. Pivots sit at the centre of the base on the ground.
    /// Menu: Tools/Expedition/Build George Landmark Prefabs.
    /// </summary>
    public static class GeorgeLandmarkPrefabs
    {
        private const string ModelDir = "Assets/Models/Environment/LandmarksGeorge/";
        private const string PrefabDir = "Assets/Prefabs/Decor/Landmarks/";
        private const string MatDir = "Assets/Materials/Lobby/";

        [MenuItem("Tools/Expedition/Build George Landmark Prefabs")]
        public static void Build()
        {
            BuildWindmill();
            BuildLighthouse();
            AssetDatabase.SaveAssets();
            Debug.Log("[GeorgeLandmarks] built George_Windmill and George_Lighthouse prefabs.");
        }

        private static GameObject Model(string name, Transform parent, Vector3 localPosition)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + name + ".fbx");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.localPosition = localPosition;
            return go;
        }

        private static void Collide(GameObject model, params string[] parts)
        {
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                if (System.Array.IndexOf(parts, renderer.name) >= 0 && renderer.TryGetComponent(out MeshFilter filter))
                    renderer.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }

        private static void Spin(GameObject go, Vector3 axis, float degreesPerSecond)
        {
            var motion = go.AddComponent<DecorMotion>();
            motion.Configure(axis, degreesPerSecond, 0f, 6f, Vector3.right, 0f, 0f);
        }

        private static void Save(GameObject root, string name)
        {
            PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + name + ".prefab");
            Object.DestroyImmediate(root);
        }

        // Model space measured from the FBX: tower base centre (0.77, 0.03, 26.71); the blades were exported 16.78 m
        // to the side (hub back face x -20.34) and go flush on the cap's front face (x -3.56), same height and depth.
        private static readonly Vector3 WindmillBase = new Vector3(.77f, .03f, 26.71f);
        private static readonly Vector3 BladeHub = new Vector3(-20.55f, 13.19f, 26.73f);
        private const float BladeShiftX = 16.78f;

        private static void BuildWindmill()
        {
            var root = new GameObject("George_Windmill");
            var body = Model("กังหันลม", root.transform, -WindmillBase);
            Collide(body, "ทรงกระบอก", "ทรงกระบอก.001");
            Vector3 hub = BladeHub + Vector3.right * BladeShiftX - WindmillBase;
            var pivot = new GameObject("BladesPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = hub;
            Model("ใบพัดกังหันลม", pivot.transform, -BladeHub);
            Spin(pivot, Vector3.right, 40f);
            Save(root, "George_Windmill");
        }

        // Lighthouse base centre (0.21, 0.05, 52.04); lantern room (glass) centre (0.29, 13.9, 51.99).
        private static readonly Vector3 LighthouseBase = new Vector3(.21f, .05f, 52.04f);
        private static readonly Vector3 Lantern = new Vector3(.29f, 13.9f, 51.99f);

        private static void BuildLighthouse()
        {
            var root = new GameObject("George_Lighthouse");
            var body = Model("หอประภาคาร", root.transform, -LighthouseBase);
            Collide(body, "ทรงกระบอก.009", "ทรงกระบอก.010", "ทรงกระบอก.011");
            var lamp = new GameObject("LampSpin");
            lamp.transform.SetParent(root.transform, false);
            lamp.transform.localPosition = Lantern - LighthouseBase;

            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "Bulb";
            Object.DestroyImmediate(bulb.GetComponent<Collider>());
            bulb.transform.SetParent(lamp.transform, false);
            bulb.transform.localScale = Vector3.one * .7f;
            bulb.GetComponent<Renderer>().sharedMaterial = BulbMaterial();
            bulb.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var beamMat = BeamMaterial();
            foreach (float yaw in new[] { 0f, 180f })
            {
                var beam = new GameObject("Beam", typeof(MeshFilter), typeof(MeshRenderer));
                beam.transform.SetParent(lamp.transform, false);
                beam.transform.localEulerAngles = new Vector3(0f, yaw, 0f);
                beam.GetComponent<MeshFilter>().sharedMesh = BeamMesh();
                var renderer = beam.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = beamMat;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var lightGo = new GameObject("LampLight", typeof(Light));
            lightGo.transform.SetParent(lamp.transform, false);
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, .86f, .55f);
            light.range = 9f;
            light.intensity = 2.5f;
            light.shadows = LightShadows.None;
            Spin(lamp, Vector3.up, 45f);
            Save(root, "George_Lighthouse");
        }

        private static Material BulbMaterial()
        {
            string path = MatDir + "Landmark_LampBulb.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", new Color(1f, .93f, .7f));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1f, .8f, .4f) * 4f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>URP Unlit, transparent, additive: a soft warm beam.</summary>
        private static Material BeamMaterial()
        {
            string path = MatDir + "Landmark_LightBeam.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetColor("_BaseColor", new Color(1f, .85f, .5f, .12f));
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f); // additive
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>An open cone along +Z: narrow at the lamp, wide 11 m out.</summary>
        private static Mesh BeamMesh()
        {
            string path = PrefabDir + "Landmark_BeamCone.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            const int n = 16;
            const float length = 11f, near = .25f, far = 1.6f;
            var verts = new Vector3[n * 2];
            var tris = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                verts[i] = new Vector3(Mathf.Cos(a) * near, Mathf.Sin(a) * near, .3f);
                verts[i + n] = new Vector3(Mathf.Cos(a) * far, Mathf.Sin(a) * far, length);
                int j = (i + 1) % n, t = i * 6;
                tris[t] = i; tris[t + 1] = i + n; tris[t + 2] = j;
                tris[t + 3] = j; tris[t + 4] = i + n; tris[t + 5] = j + n;
            }
            mesh = new Mesh { name = "Landmark_BeamCone", vertices = verts, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
