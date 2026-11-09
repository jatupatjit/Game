#if UNITY_EDITOR
using CoopGame.CarrySystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools.Traps
{
    /// <summary>
    /// Builds Assets/Prefabs/Traps/Obstacles/Prop_HayBalePad: two hay bales with a wide cushion bale on top that
    /// squashes under the crate (<see cref="HayCushion"/>); crates take no damage on it. Safe to run again.
    /// </summary>
    public static class HayBaleBuilder
    {
        public const string PrefabPath = "Assets/Prefabs/Traps/Obstacles/Prop_HayBalePad.prefab";
        private const string MaterialFolder = "Assets/Prefabs/Traps/Materials";

        [MenuItem("Tools/Traps/Build Hay Bale Pad")]
        public static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[HayBaleBuilder] Stop Play mode first.";
            var hay = Make("Hay_Straw", new Color(.86f, .7f, .33f));
            var hayDark = Make("Hay_StrawDark", new Color(.7f, .54f, .22f));
            var twine = Make("Hay_Twine", new Color(.42f, .3f, .17f));
            var previous = EditorSceneManager.GetActiveScene();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(temp);
            try
            {
                var root = new GameObject("Prop_HayBalePad");
                var cushion = root.AddComponent<HayCushion>();
                foreach (float x in new[] { -.72f, .72f })
                {
                    var bale = Bale("BaseBale", root.transform, new Vector3(x, .25f, 0), new Vector3(1.4f, .5f, 2.5f), hayDark, twine);
                    bale.transform.localEulerAngles = new Vector3(0, x > 0 ? 2f : -3f, 0);
                }
                var top = Bale("CushionBale", root.transform, new Vector3(0, .78f, 0), new Vector3(2.7f, .56f, 2.3f), hay, twine);
                var body = top.AddComponent<Rigidbody>();
                body.isKinematic = true; body.useGravity = false;
                // Loose straw tufts on top (no collider).
                for (int i = 0; i < 6; i++)
                {
                    var tuft = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tuft.name = "Straw";
                    Object.DestroyImmediate(tuft.GetComponent<Collider>());
                    tuft.transform.SetParent(top.transform, false);
                    tuft.transform.localPosition = new Vector3(-.38f + i * .15f, .52f, (i % 3 - 1) * .3f);
                    tuft.transform.localEulerAngles = new Vector3(0, i * 47f, 0);
                    tuft.transform.localScale = new Vector3(.09f, .06f, .02f);
                    tuft.GetComponent<Renderer>().sharedMaterial = hayDark;
                }
                var so = new SerializedObject(cushion);
                so.FindProperty("_topBale").objectReferenceValue = body;
                so.FindProperty("_sensorSize").vector3Value = new Vector3(2.6f, 1.6f, 2.2f);
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
                Object.DestroyImmediate(root);
                return ok ? "[HayBaleBuilder] Saved " + PrefabPath : "[HayBaleBuilder] Could not save " + PrefabPath;
            }
            finally
            {
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(temp, true);
            }
        }

        private static GameObject Bale(string name, Transform parent, Vector3 position, Vector3 size, Material hay, Material twine)
        {
            var bale = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bale.name = name;
            bale.transform.SetParent(parent, false);
            bale.transform.localPosition = position;
            bale.transform.localScale = size;
            bale.GetComponent<Renderer>().sharedMaterial = hay;
            // Two twine bands round the short way (children scale with the bale).
            foreach (float x in new[] { -.25f, .25f })
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
                band.name = "Twine";
                Object.DestroyImmediate(band.GetComponent<Collider>());
                band.transform.SetParent(bale.transform, false);
                band.transform.localPosition = new Vector3(x, 0, 0);
                band.transform.localScale = new Vector3(.03f, 1.02f, 1.02f);
                band.GetComponent<Renderer>().sharedMaterial = twine;
            }
            return bale;
        }

        private static Material Make(string name, Color color)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .05f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
#endif
