#if UNITY_EDITOR
using CoopGame.CarrySystem;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Builds Assets/Prefabs/Items/Variants/Cargo_Long: a long wooden crate (about 0.85 x 0.75 x 2.4 m, long axis = local Z)
    /// that needs two carriers (front and back end). Based on Cargo_Heavy (FragileCargo + CargoVariant + network components).
    /// One player alone drags the far end (CarryableObject.RequiresTwoCarriers); <see cref="CarryGripEnds"/> makes players
    /// grip the ends. Safe to run again.
    /// </summary>
    public static class LongCrateBuilder
    {
        private const string Source = "Assets/Prefabs/Items/Variants/Cargo_Heavy.prefab";
        private const string Target = "Assets/Prefabs/Items/Variants/Cargo_Long.prefab";
        private const string Medieval = "Assets/Prefabs/Traps/Materials/Medieval/";
        private const float Length = 2.4f, Width = .85f, Height = .75f;

        [MenuItem("Tools/Cargo/Build Long Crate")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[LongCrateBuilder] Stop Play mode first."); return; }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(Target) == null && !AssetDatabase.CopyAsset(Source, Target))
            {
                Debug.LogError("[LongCrateBuilder] could not copy " + Source);
                return;
            }
            var wood = AssetDatabase.LoadAssetAtPath<Material>(Medieval + "Medieval_Wood.mat");
            var woodLight = AssetDatabase.LoadAssetAtPath<Material>(Medieval + "Medieval_WoodLight.mat");
            var iron = AssetDatabase.LoadAssetAtPath<Material>(Medieval + "Medieval_Iron.mat");

            var root = PrefabUtility.LoadPrefabContents(Target);
            try
            {
                root.name = "Cargo_Long";
                for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

                // The root cube is the crate body: scaled to the long shape, with a unit box collider.
                root.transform.localScale = new Vector3(Width, Height, Length);
                var renderer = root.GetComponent<MeshRenderer>();
                if (renderer != null && wood != null) renderer.sharedMaterial = wood;
                var box = root.GetComponent<BoxCollider>();
                box.center = Vector3.zero;
                box.size = Vector3.one;
                var body = root.GetComponent<Rigidbody>();
                body.mass = 16f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                // Iron bands round the body and a handle bar at each end (visual only), sized in world metres.
                foreach (float z in new[] { -.85f, 0f, .85f })
                    Decor(root.transform, "Band", new Vector3(0f, 0f, z), new Vector3(Width + .03f, Height + .03f, .1f), iron);
                foreach (float z in new[] { -1f, 1f })
                {
                    float end = z * (Length * .5f + .05f);
                    Decor(root.transform, "Handle", new Vector3(0f, .08f, end), new Vector3(.55f, .07f, .07f), woodLight);
                    Decor(root.transform, "HandlePostL", new Vector3(-.25f, .08f, end * .985f), new Vector3(.07f, .07f, .1f), woodLight);
                    Decor(root.transform, "HandlePostR", new Vector3(.25f, .08f, end * .985f), new Vector3(.07f, .07f, .1f), woodLight);
                }

                // Exactly two sockets: front (+Z) and back (-Z), so at most two carriers, one at each end.
                var front = Socket(root.transform, "Socket_Front", Length * .5f, 0f);
                var back = Socket(root.transform, "Socket_Back", -Length * .5f, 180f);

                var carry = root.GetComponent<CarryableObject>();
                var carrySo = new SerializedObject(carry);
                carrySo.FindProperty("_requiresTwoCarriers").boolValue = true;
                var sockets = carrySo.FindProperty("_sockets");
                sockets.arraySize = 2;
                sockets.GetArrayElementAtIndex(0).objectReferenceValue = front;
                sockets.GetArrayElementAtIndex(1).objectReferenceValue = back;
                carrySo.ApplyModifiedPropertiesWithoutUndo();

                if (root.GetComponent<CarryGripEnds>() == null) root.AddComponent<CarryGripEnds>();

                var variant = root.GetComponent<CargoVariant>();
                if (variant != null)
                {
                    var variantSo = new SerializedObject(variant);
                    variantSo.FindProperty("_kind").enumValueIndex = (int)CargoKind.Normal;
                    variantSo.FindProperty("_scoreMultiplier").floatValue = 1.25f;
                    variantSo.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(root, Target);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[LongCrateBuilder] built " + Target);
        }

        // Child of the scaled root, placed and sized in world metres (the root scale is divided out).
        private static void Decor(Transform root, string name, Vector3 position, Vector3 size, Material material)
        {
            Vector3 scale = root.localScale;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(position.x / scale.x, position.y / scale.y, position.z / scale.z);
            go.transform.localScale = new Vector3(size.x / scale.x, size.y / scale.y, size.z / scale.z);
            if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Transform Socket(Transform root, string name, float z, float yaw)
        {
            var socket = new GameObject(name).transform;
            socket.SetParent(root, false);
            socket.localPosition = new Vector3(0f, 0f, z / root.localScale.z);
            socket.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return socket;
        }
    }
}
#endif
