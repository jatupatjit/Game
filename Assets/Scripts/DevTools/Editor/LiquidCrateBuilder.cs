#if UNITY_EDITOR
using CoopGame.CarrySystem;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>Builds Assets/Prefabs/Items/Variants/Cargo_Liquid (glass water tank, <see cref="LiquidCargo"/>) from Cargo_Glass.</summary>
    public static class LiquidCrateBuilder
    {
        private const string Source = "Assets/Prefabs/Items/Variants/Cargo_Glass.prefab";
        private const string Target = "Assets/Prefabs/Items/Variants/Cargo_Liquid.prefab";
        private const string MaterialFolder = "Assets/Materials/Liquid";

        [MenuItem("Tools/Cargo/Build Liquid Crate")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(MaterialFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Liquid");
            var glass = MakeMaterial("LiquidTankGlass", new Color(.78f, .92f, 1f, .2f), true, .92f);
            var water = MakeMaterial("LiquidWater", new Color(.18f, .55f, 1f, .72f), true, .85f);
            var wood = MakeMaterial("LiquidTankFrame", new Color(.55f, .36f, .2f, 1f), false, .2f);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(Target) == null && !AssetDatabase.CopyAsset(Source, Target))
            {
                Debug.LogError("[LiquidCrateBuilder] could not copy " + Source);
                return;
            }
            var root = PrefabUtility.LoadPrefabContents(Target);
            try
            {
                root.name = "Cargo_Liquid";
                foreach (Transform child in root.transform) if (child.name == "Water" || child.name == "Frame") Object.DestroyImmediate(child.gameObject);
                root.GetComponent<MeshRenderer>().sharedMaterial = glass;
                root.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                var variant = new SerializedObject(root.GetComponent<CargoVariant>());
                variant.FindProperty("_kind").enumValueIndex = (int)CargoKind.Normal;
                variant.FindProperty("_scoreMultiplier").floatValue = 1.3f;
                variant.ApplyModifiedPropertiesWithoutUndo();
                var cargo = new SerializedObject(root.GetComponent<FragileCargo>());
                cargo.FindProperty("_maxHP").intValue = 100;
                cargo.ApplyModifiedPropertiesWithoutUndo();

                var waterBox = Box(root.transform, "Water", new Vector3(0f, -.03f, 0f), new Vector3(.9f, .82f, .9f), water);
                var frame = new GameObject("Frame").transform;
                frame.SetParent(root.transform, false);
                const float t = .07f, e = .5f - t * .5f;
                for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Box(frame, "Post", new Vector3(x * e, 0f, z * e), new Vector3(t, 1f, t), wood);
                for (int y = -1; y <= 1; y += 2)
                {
                    Box(frame, "RailX", new Vector3(0f, y * e, -e), new Vector3(1f, t, t), wood);
                    Box(frame, "RailX", new Vector3(0f, y * e, e), new Vector3(1f, t, t), wood);
                    Box(frame, "RailZ", new Vector3(-e, y * e, 0f), new Vector3(t, t, 1f), wood);
                    Box(frame, "RailZ", new Vector3(e, y * e, 0f), new Vector3(t, t, 1f), wood);
                }
                Box(frame, "Bottom", new Vector3(0f, -e, 0f), new Vector3(.98f, t * .6f, .98f), wood);

                var liquid = root.GetComponent<LiquidCargo>();
                if (liquid == null) liquid = root.AddComponent<LiquidCargo>();
                var so = new SerializedObject(liquid);
                so.FindProperty("_water").objectReferenceValue = waterBox;
                so.FindProperty("_bottomY").floatValue = -.44f;
                so.FindProperty("_fullHeight").floatValue = .82f;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, Target);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            Debug.Log("[LiquidCrateBuilder] built " + Target);
        }

        private static Transform Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            if (material.name == "LiquidWater") renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        private static Material MakeMaterial(string name, Color color, bool transparent, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            if (transparent)
            {
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_Blend", 0f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
#endif
