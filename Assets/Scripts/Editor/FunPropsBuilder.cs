#if UNITY_EDITOR
using CoopGame.CarrySystem;
using CoopGame.Network;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Throwable fun props (George 2026-11-17: balls, flowers, ...): plain carryables (F lift, Q throw, hold F to catch)
    /// that count for nothing, sync through the same NetworkTransform as crates and go home if they fall off
    /// (<see cref="CarryableHome"/>). Tools/Expedition/Build Fun Props makes the prefabs; ".. And Place In Open Scene"
    /// also drops a set in front of the open scene's spawn area under "=== Fun Props ===".
    /// </summary>
    public static class FunPropsBuilder
    {
        private const string PrefabFolder = "Assets/Prefabs/FunProps";
        private const string MaterialFolder = "Assets/Materials/FunProps";

        [MenuItem("Tools/Expedition/Build Fun Props")]
        public static void BuildPrefabs() => Build(false, Vector3.zero);

        [MenuItem("Tools/Expedition/Build Fun Props And Place In Open Scene")]
        public static void BuildAndPlace() => Build(true, new Vector3(-3f, .6f, -3f));

        public static string Build(bool place, Vector3 origin)
        {
            EnsureFolder(PrefabFolder);
            EnsureFolder(MaterialFolder);
            var bouncy = PhysicsMat("FunBall_Bouncy", .75f, .4f);
            var soft = PhysicsMat("FunPot_Soft", .1f, .6f);

            var prefabs = new[]
            {
                Ball("Prop_Ball_Red", .45f, 1.5f, new Color(.93f, .22f, .22f), bouncy),
                Ball("Prop_Ball_Blue", .7f, 2.5f, new Color(.25f, .55f, .95f), bouncy),
                Ball("Prop_Ball_Yellow", .35f, 1f, new Color(1f, .82f, .2f), bouncy),
                FlowerPot("Prop_FlowerPot_Pink", "Assets/Prefabs/Decor/Nature/Decor_FlowerPatch_A.prefab", soft),
                FlowerPot("Prop_FlowerPot_Mix", "Assets/Prefabs/Decor/Nature/Decor_FlowerPatch_C.prefab", soft),
            };
            AssetDatabase.SaveAssets();
            if (!place) return "built " + prefabs.Length;

            var scene = EditorSceneManager.GetActiveScene();
            var root = GameObject.Find("=== Fun Props ===");
            if (root != null) Object.DestroyImmediate(root);
            root = new GameObject("=== Fun Props ===");
            for (int i = 0; i < prefabs.Length; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], scene);
                instance.transform.SetParent(root.transform, true);
                instance.transform.position = origin + new Vector3((i % 3) * 1.4f, 0f, (i / 3) * -1.4f);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            return "placed " + prefabs.Length + " in " + scene.name;
        }

        private static GameObject Ball(string name, float diameter, float mass, Color color, PhysicsMaterial material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.localScale = Vector3.one * diameter;
            // Box hitbox, not the sphere: a held sphere flung the carrier off (measured 2.7-4 m in 0.5 s and a drop,
            // box-shaped pot 0.01 m, 2026-11-17); the carry grips are built for box faces.
            Object.DestroyImmediate(go.GetComponent<SphereCollider>());
            var box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one * .9f;
            box.sharedMaterial = material;
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit(name, color, .55f);
            return Finish(go, mass, .05f);
        }

        private static GameObject FlowerPot(string name, string flowerPath, PhysicsMaterial material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.localScale = new Vector3(.42f, .18f, .42f); // cylinder is 2 units tall: .36 m pot
            Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = Lit("FlowerPot_Clay", new Color(.78f, .42f, .26f), .2f);
            var flowerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(flowerPath);
            if (flowerPrefab != null)
            {
                var flower = (GameObject)PrefabUtility.InstantiatePrefab(flowerPrefab);
                PrefabUtility.UnpackPrefabInstance(flower, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                flower.name = "Flowers";
                foreach (var c in flower.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var m in flower.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(m);
                flower.transform.SetParent(go.transform, false);
                // Fit the patch on top of the pot (patch is a ground decal of several flowers).
                var bounds = new Bounds(flower.transform.position, Vector3.zero);
                foreach (var r in flower.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
                float s = bounds.size.x > .01f ? .5f / Mathf.Max(bounds.size.x, bounds.size.z) : 1f;
                flower.transform.localScale = new Vector3(s / .42f, s / .18f, s / .42f);
                flower.transform.localPosition = new Vector3(0f, 1f, 0f);
            }
            // Collider: the pot plus a little of the flowers, in the pot's own (scaled) space.
            var box = go.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, .35f, 0f);
            box.size = new Vector3(1f, 2.7f, 1f);
            box.sharedMaterial = material;
            return Finish(go, 3f, .2f);
        }

        private static GameObject Finish(GameObject go, float mass, float drag)
        {
            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.linearDamping = drag;
            body.angularDamping = .3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();
            go.AddComponent<CarryableOutline>();
            go.AddComponent<CarryableObject>();
            go.AddComponent<CarryableHome>();
            string path = PrefabFolder + "/" + go.name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        private static Material Lit(string name, Color color, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static PhysicsMaterial PhysicsMat(string name, float bounce, float friction)
        {
            string path = MaterialFolder + "/" + name + ".physicMaterial";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (mat == null)
            {
                mat = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.bounciness = bounce;
            mat.dynamicFriction = friction;
            mat.staticFriction = friction;
            mat.bounceCombine = PhysicsMaterialCombine.Maximum;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
#endif
