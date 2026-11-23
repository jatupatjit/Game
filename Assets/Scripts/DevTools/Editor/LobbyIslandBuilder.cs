using System.Collections.Generic;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// The Lobby moves onto the big island (เกาะใหญ่). Builds two shop stall prefabs
    /// (gear + outfit, press E at the counter, <see cref="ShopStall"/>) and lays the Lobby out on the island:
    /// portal + spawn + stalls + toys + decor. The old flat floor, walls and see-saw are removed.
    /// Menu: Tools/Expedition/Build Lobby Island (open Lobby first). Backup of the scene before this: E:/Unity/Backups/20261120-LobbyShops.
    /// </summary>
    public static class LobbyIslandBuilder
    {
        private const string PrefabDir = "Assets/Prefabs/Lobby";
        private const string MatDir = "Assets/Materials/Lobby";
        private const string GearPath = PrefabDir + "/Stall_Gear.prefab";
        private const string OutfitPath = PrefabDir + "/Stall_Outfit.prefab";
        private const string ConePath = PrefabDir + "/LobbyCone.asset";
        private const float Top = 10.9f; // island top surface (raycast)

        // Island layout (world): spawn south facing +Z (the camera starts facing +Z), portal north, stalls either side of the plaza.
        private static readonly Vector3 Plaza = new Vector3(8f, Top, -47f);
        private static readonly Vector3 Spawn = new Vector3(8f, Top + 1.1f, -64f); // pivot at the capsule centre (NetworkPlayer default spawn y 1.05 over y 0)
        private static readonly Vector3 Portal = new Vector3(8f, Top, -29f);
        private static readonly Vector3 GearStall = new Vector3(18.5f, Top, -47f);
        private static readonly Vector3 OutfitStall = new Vector3(-2.5f, Top, -47f);

        [MenuItem("Tools/Expedition/Build Lobby Island")]
        public static void BuildAll()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != "Lobby") { EditorUtility.DisplayDialog("Lobby Island", "Open Assets/Scenes/Lobby.unity first.", "OK"); return; }
            BuildStallPrefabs();
            LayOut();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[LobbyIsland] built stalls and laid out the Lobby on the island.");
        }

        // Materials

        private static readonly Dictionary<string, Material> _mats = new();

        private static Material Mat(string name, Color color, float smooth = .25f, float metal = 0f, Color? emission = null)
        {
            if (_mats.TryGetValue(name, out var cached) && cached != null) return cached;
            string path = MatDir + "/Lobby_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && color == Color.white) { _mats[name] = mat; return mat; } // white = "reuse as made"
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smooth);
            mat.SetFloat("_Metallic", metal);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            _mats[name] = mat;
            return mat;
        }

        private static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        // Shapes

        private static Transform Group(Transform parent, string name, Vector3 pos, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            return go.transform;
        }

        private static Transform Shape(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat,
                                       Vector3 euler = default, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        private static Mesh Cone()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ConePath);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "LobbyCone" };
            const int n = 20;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * .5f, 0f, Mathf.Sin(a0) * .5f), p1 = new Vector3(Mathf.Cos(a1) * .5f, 0f, Mathf.Sin(a1) * .5f);
                int b = v.Count; v.Add(p0); v.Add(Vector3.up); v.Add(p1); t.Add(b); t.Add(b + 1); t.Add(b + 2);
                b = v.Count; v.Add(p0); v.Add(p1); v.Add(Vector3.zero); t.Add(b); t.Add(b + 1); t.Add(b + 2);
            }
            mesh.SetVertices(v); mesh.SetTriangles(t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, ConePath);
            return mesh;
        }

        private static Transform ConeShape(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localEulerAngles = euler; go.transform.localScale = scale;
            go.GetComponent<MeshFilter>().sharedMesh = Cone();
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // Stalls

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        public static void BuildStallPrefabs()
        {
            EnsureFolder(PrefabDir); EnsureFolder(MatDir);
            Save(BuildStall(ShopStall.Kind.Gear), GearPath);
            Save(BuildStall(ShopStall.Kind.Outfit), OutfitPath);
            AssetDatabase.SaveAssets();
        }

        private static void Save(GameObject go, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
        }

        /// <summary>Front faces local +Z; the customer stands at z = +1.7.</summary>
        private static GameObject BuildStall(ShopStall.Kind kind)
        {
            bool gear = kind == ShopStall.Kind.Gear;
            var root = new GameObject(gear ? "Stall_Gear" : "Stall_Outfit").transform;
            var wood = Mat("WoodDark", Hex("#7A4E2D"));
            var woodLight = Mat("WoodLight", Hex("#C8935A"));
            var stripeA = gear ? Mat("AwningOrange", Hex("#F08A3C")) : Mat("AwningPink", Hex("#FC6788"));
            var stripeB = Mat("AwningCream", Hex("#FFF4E0"));
            var signMat = gear ? Mat("SignTeal", Hex("#2F8F83")) : Mat("SignPlum", Hex("#A0457A"));

            // Frame
            Shape(root, "Base", PrimitiveType.Cube, new Vector3(0f, .1f, 0f), new Vector3(4.4f, .2f, 2.8f), wood, collider: true);
            Shape(root, "Counter", PrimitiveType.Cube, new Vector3(0f, .65f, .75f), new Vector3(3.6f, .9f, .7f), woodLight, collider: true);
            Shape(root, "CounterTop", PrimitiveType.Cube, new Vector3(0f, 1.14f, .75f), new Vector3(3.9f, .08f, .9f), wood);
            Shape(root, "CounterStripe", PrimitiveType.Cube, new Vector3(0f, .7f, 1.105f), new Vector3(3.62f, .22f, .02f), stripeA);
            Shape(root, "BackWall", PrimitiveType.Cube, new Vector3(0f, 1.45f, -1.2f), new Vector3(3.8f, 2.7f, .14f), woodLight, collider: true);
            for (int s = 0; s < 2; s++)
                Shape(root, "Shelf" + s, PrimitiveType.Cube, new Vector3(0f, 1.55f + s * .6f, -.98f), new Vector3(3.5f, .06f, .38f), wood);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -2.02f : 2.02f, z = i < 2 ? 1.2f : -1.2f;
                Shape(root, "Post" + i, PrimitiveType.Cylinder, new Vector3(x, 1.5f, z), new Vector3(.18f, 1.5f, .18f), wood, collider: true);
            }
            // Striped awning, front edge lower
            var awning = Group(root, "Awning", new Vector3(0f, 3.05f, 0f), new Vector3(14f, 0f, 0f));
            for (int i = 0; i < 7; i++)
                Shape(awning, "Stripe" + i, PrimitiveType.Cube, new Vector3(-1.98f + i * .66f, 0f, .1f), new Vector3(.66f, .07f, 3.2f), i % 2 == 0 ? stripeA : stripeB);
            for (int i = 0; i < 7; i++) // scalloped valance
                Shape(awning, "Flap" + i, PrimitiveType.Cylinder, new Vector3(-1.98f + i * .66f, -.12f, 1.7f), new Vector3(.62f, .02f, .3f), i % 2 == 0 ? stripeA : stripeB, new Vector3(90f, 0f, 0f));
            // Sign board over the awning
            Shape(root, "SignBoard", PrimitiveType.Cube, new Vector3(0f, 3.85f, .2f), new Vector3(3.1f, .8f, .12f), signMat);
            Shape(root, "SignFrame", PrimitiveType.Cube, new Vector3(0f, 3.85f, .17f), new Vector3(3.3f, 1f, .08f), wood);
            var signAnchor = Group(root, "SignAnchor", new Vector3(0f, 3.85f, .28f), new Vector3(0f, 180f, 0f));
            var counter = Group(root, "CounterPoint", new Vector3(0f, 0f, 1.7f));
            // Lanterns on the front posts
            var glow = Mat("LanternGlow", Hex("#FFD27A"), .5f, 0f, Hex("#FFB347") * 2.2f);
            foreach (float x in new[] { -2.02f, 2.02f })
            {
                Shape(root, "LanternArm", PrimitiveType.Cube, new Vector3(x, 2.55f, 1.4f), new Vector3(.06f, .06f, .4f), wood);
                Shape(root, "Lantern", PrimitiveType.Sphere, new Vector3(x, 2.4f, 1.58f), new Vector3(.24f, .3f, .24f), glow);
            }

            var spin = new List<Transform>();
            if (gear) BuildGearDisplay(root, spin); else BuildOutfitDisplay(root, spin);

            var stall = root.gameObject.AddComponent<ShopStall>();
            var so = new SerializedObject(stall);
            so.FindProperty("_kind").enumValueIndex = (int)kind;
            so.FindProperty("_title").stringValue = gear ? "ร้านอุปกรณ์" : "ร้านแต่งตัว";
            so.FindProperty("_counter").objectReferenceValue = counter;
            so.FindProperty("_signAnchor").objectReferenceValue = signAnchor;
            var list = so.FindProperty("_spin");
            list.arraySize = spin.Count;
            for (int i = 0; i < spin.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = spin[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return root.gameObject;
        }

        private static Transform Glove(Transform parent, string name, Vector3 pos, float scale, Material mat, Material cuff)
        {
            var g = Group(parent, name, pos);
            g.localScale = Vector3.one * scale;
            Shape(g, "Palm", PrimitiveType.Cube, new Vector3(0f, .14f, 0f), new Vector3(.24f, .26f, .09f), mat);
            for (int f = 0; f < 4; f++)
                Shape(g, "Finger" + f, PrimitiveType.Capsule, new Vector3(-.09f + f * .06f, .33f + (f == 1 || f == 2 ? .02f : 0f), 0f), new Vector3(.055f, .07f, .055f), mat);
            Shape(g, "Thumb", PrimitiveType.Capsule, new Vector3(.16f, .17f, 0f), new Vector3(.06f, .07f, .06f), mat, new Vector3(0f, 0f, -40f));
            Shape(g, "Cuff", PrimitiveType.Cylinder, new Vector3(0f, -.02f, 0f), new Vector3(.27f, .06f, .14f), cuff);
            return g;
        }

        private static Transform Glasses(Transform parent, string name, Vector3 pos, Material lens, Material frame)
        {
            var g = Group(parent, name, pos);
            foreach (float x in new[] { -.1f, .1f })
            {
                Shape(g, "Rim", PrimitiveType.Cylinder, new Vector3(x, 0f, 0f), new Vector3(.17f, .012f, .17f), frame, new Vector3(90f, 0f, 0f));
                Shape(g, "Lens", PrimitiveType.Cylinder, new Vector3(x, 0f, .006f), new Vector3(.15f, .012f, .15f), lens, new Vector3(90f, 0f, 0f));
                Shape(g, "Arm", PrimitiveType.Cube, new Vector3(x * 1.85f, .02f, -.09f), new Vector3(.015f, .015f, .2f), frame);
            }
            Shape(g, "Bridge", PrimitiveType.Cube, new Vector3(0f, .02f, 0f), new Vector3(.06f, .015f, .015f), frame);
            return g;
        }

        private static void BuildGearDisplay(Transform root, List<Transform> spin)
        {
            var leather = Mat("Leather", Hex("#8B5A2B"));
            var climbGlove = Mat("GloveBlue", Hex("#3C7DD9"));
            var cuff = Mat("Cuff", Hex("#F3B600"));
            var dark = Mat("LensDark", Hex("#1E2433"), .9f);
            var aviator = Mat("LensGold", Hex("#C9A13B"), .9f, .6f);
            var metal = Mat("Metal", Hex("#B8BEC6"), .7f, .8f);
            var rope = Mat("Rope", Hex("#D8B98A"));
            var red = Mat("ToolRed", Hex("#D9483B"));
            var green = Mat("PackGreen", Hex("#5E8C4A"));
            var stand = Mat("StandWhite", Hex("#F5EFE6"));

            // Counter: 2 gloves + 2 glasses on little stands (the 4 items the gear shop sells)
            float y = 1.18f;
            var items = new[] { -1.35f, -.45f, .45f, 1.35f };
            for (int i = 0; i < 4; i++)
                Shape(root, "Stand" + i, PrimitiveType.Cylinder, new Vector3(items[i], y + .04f, .75f), new Vector3(.36f, .04f, .36f), stand);
            spin.Add(Glove(root, "Display_LeatherGlove", new Vector3(items[0], y + .1f, .75f), 1.3f, leather, cuff));
            spin.Add(Glove(root, "Display_ClimbGlove", new Vector3(items[1], y + .1f, .75f), 1.3f, climbGlove, cuff));
            spin.Add(Glasses(root, "Display_Sunglasses", new Vector3(items[2], y + .32f, .75f), dark, Mat("FrameBlack", Hex("#202020"))));
            spin.Add(Glasses(root, "Display_Aviators", new Vector3(items[3], y + .32f, .75f), aviator, metal));

            // Shelves: rope coils, tool boxes, a backpack, a lantern
            for (int i = 0; i < 2; i++)
                Shape(root, "RopeCoil" + i, PrimitiveType.Cylinder, new Vector3(-1.3f + i * .55f, 1.66f, -.98f), new Vector3(.42f, .07f, .42f), rope);
            Shape(root, "ToolBox", PrimitiveType.Cube, new Vector3(.4f, 1.72f, -.98f), new Vector3(.6f, .28f, .3f), red);
            Shape(root, "ToolBoxHandle", PrimitiveType.Cube, new Vector3(.4f, 1.9f, -.98f), new Vector3(.3f, .05f, .05f), metal);
            Shape(root, "Pack", PrimitiveType.Cube, new Vector3(1.25f, 2.36f, -.98f), new Vector3(.5f, .45f, .3f), green);
            Shape(root, "PackFlap", PrimitiveType.Sphere, new Vector3(1.25f, 2.56f, -.94f), new Vector3(.5f, .2f, .32f), green);
            Shape(root, "Pickaxe", PrimitiveType.Cube, new Vector3(-.9f, 2.4f, -1.08f), new Vector3(.06f, .7f, .06f), Mat("WoodDark", Color.white), new Vector3(0f, 0f, 20f));
            Shape(root, "PickaxeHead", PrimitiveType.Cube, new Vector3(-.78f, 2.72f, -1.08f), new Vector3(.55f, .07f, .07f), metal, new Vector3(0f, 0f, 20f));
            Shape(root, "Helmet", PrimitiveType.Sphere, new Vector3(-.15f, 2.3f, -.98f), new Vector3(.42f, .3f, .42f), Mat("HelmetYellow", Hex("#F3B600"), .6f));
            // Big glove on the roof: tells what the stall sells from far away
            var big = Glove(root, "RoofGlove", new Vector3(1.9f, 4.25f, .2f), 2.4f, leather, cuff);
            big.localEulerAngles = new Vector3(0f, 0f, -15f);
        }

        private static Transform Hat(Transform parent, int kind, Vector3 pos, float scale)
        {
            var hat = Group(parent, "Display_Hat" + kind, pos);
            hat.localScale = Vector3.one * scale;
            switch (kind)
            {
                case 1: // party hat
                    ConeShape(hat, "Cone", Vector3.zero, new Vector3(.3f, .42f, .3f), Mat("HatPink", Hex("#FC6788")));
                    Shape(hat, "Pom", PrimitiveType.Sphere, new Vector3(0f, .43f, 0f), Vector3.one * .08f, Mat("HatYellow", Hex("#F3D35B")));
                    break;
                case 2: // top hat
                    var black = Mat("HatBlack", Hex("#26232A"), .5f);
                    Shape(hat, "Brim", PrimitiveType.Cylinder, new Vector3(0f, .01f, 0f), new Vector3(.42f, .01f, .42f), black);
                    Shape(hat, "Crown", PrimitiveType.Cylinder, new Vector3(0f, .18f, 0f), new Vector3(.26f, .17f, .26f), black);
                    Shape(hat, "Band", PrimitiveType.Cylinder, new Vector3(0f, .06f, 0f), new Vector3(.265f, .03f, .265f), Mat("HatRed", Hex("#D9483B")));
                    break;
                case 3: // crown
                    var gold = Mat("Gold", Hex("#F2C14E"), .8f, .7f);
                    Shape(hat, "Ring", PrimitiveType.Cylinder, new Vector3(0f, .07f, 0f), new Vector3(.3f, .07f, .3f), gold);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * Mathf.PI / 3f;
                        ConeShape(hat, "Spike" + i, new Vector3(Mathf.Cos(a) * .13f, .13f, Mathf.Sin(a) * .13f), new Vector3(.07f, .13f, .07f), gold);
                    }
                    Shape(hat, "Gem", PrimitiveType.Sphere, new Vector3(0f, .08f, .15f), Vector3.one * .05f, Mat("GemBlue", Hex("#3C7DD9"), .9f));
                    break;
                default: // cap
                    var blue = Mat("HatBlue", Hex("#3C7DD9"));
                    Shape(hat, "Dome", PrimitiveType.Sphere, new Vector3(0f, .02f, 0f), new Vector3(.3f, .24f, .3f), blue);
                    Shape(hat, "Peak", PrimitiveType.Cube, new Vector3(0f, .01f, .17f), new Vector3(.24f, .015f, .16f), blue);
                    break;
            }
            return hat;
        }

        private static void BuildOutfitDisplay(Transform root, List<Transform> spin)
        {
            var stand = Mat("StandWhite", Hex("#F5EFE6"));
            float y = 1.18f;
            var xs = new[] { -1.35f, -.45f, .45f, 1.35f };
            int[] kinds = { 1, 2, 3, 4 };
            for (int i = 0; i < 4; i++)
            {
                Shape(root, "HatStand" + i, PrimitiveType.Cylinder, new Vector3(xs[i], y + .14f, .75f), new Vector3(.08f, .14f, .08f), stand);
                Shape(root, "HatHead" + i, PrimitiveType.Sphere, new Vector3(xs[i], y + .34f, .75f), new Vector3(.26f, .22f, .26f), stand);
                spin.Add(Hat(root, kinds[i], new Vector3(xs[i], y + .42f, .75f), 1.1f));
            }
            // Clothes rail on the back wall with pastel shirts
            var metal = Mat("Metal", Hex("#B8BEC6"), .7f, .8f);
            Shape(root, "Rail", PrimitiveType.Cylinder, new Vector3(0f, 2.45f, -.85f), new Vector3(.04f, 1.6f, .04f), metal, new Vector3(0f, 0f, 90f));
            string[] shirts = { "#FFB3C7", "#A8D8FF", "#B9F2C2", "#FFE08A", "#D7B8FF", "#FFC79E" };
            for (int i = 0; i < shirts.Length; i++)
            {
                var m = Mat("Shirt" + i, Hex(shirts[i]));
                float x = -1.3f + i * .52f;
                Shape(root, "Shirt" + i, PrimitiveType.Cube, new Vector3(x, 2.05f, -.85f), new Vector3(.42f, .6f, .06f), m);
                Shape(root, "Sleeves" + i, PrimitiveType.Cube, new Vector3(x, 2.25f, -.85f), new Vector3(.62f, .16f, .05f), m);
            }
            Shape(root, "FoldedPile", PrimitiveType.Cube, new Vector3(-1.1f, 1.66f, -.98f), new Vector3(.5f, .16f, .3f), Mat("Shirt1", Color.white));
            Shape(root, "FoldedPile2", PrimitiveType.Cube, new Vector3(-.45f, 1.66f, -.98f), new Vector3(.5f, .16f, .3f), Mat("Shirt0", Color.white));

            // Mirror at the side
            var frame = Mat("Gold", Color.white);
            var mirror = Group(root, "Mirror", new Vector3(2.75f, 0f, .4f), new Vector3(0f, -30f, 0f));
            Shape(mirror, "Frame", PrimitiveType.Cube, new Vector3(0f, 1.1f, 0f), new Vector3(.8f, 1.9f, .08f), frame, collider: true);
            Shape(mirror, "Glass", PrimitiveType.Cube, new Vector3(0f, 1.1f, .045f), new Vector3(.66f, 1.74f, .02f), Mat("Mirror", Hex("#CFE8F5"), .97f, 1f));
            Shape(mirror, "Foot", PrimitiveType.Cube, new Vector3(0f, .05f, 0f), new Vector3(.6f, .1f, .4f), Mat("WoodDark", Color.white));

            // Mannequin in an outfit, wearing a top hat
            var body = Mat("Mannequin", Hex("#F1E3D3"));
            var man = Group(root, "Mannequin", new Vector3(-2.8f, 0f, .5f), new Vector3(0f, 25f, 0f));
            Shape(man, "Pole", PrimitiveType.Cylinder, new Vector3(0f, .5f, 0f), new Vector3(.06f, .5f, .06f), metal);
            Shape(man, "FootBase", PrimitiveType.Cylinder, new Vector3(0f, .03f, 0f), new Vector3(.45f, .03f, .45f), metal, collider: true);
            Shape(man, "Torso", PrimitiveType.Capsule, new Vector3(0f, 1.35f, 0f), new Vector3(.55f, .45f, .4f), Mat("Shirt0", Color.white));
            Shape(man, "Head", PrimitiveType.Sphere, new Vector3(0f, 2.0f, 0f), Vector3.one * .38f, body);
            Hat(man, 2, new Vector3(0f, 2.15f, 0f), 1.2f).name = "MannequinHat";

            // Big top hat on the roof
            var big = Hat(root, 2, new Vector3(1.85f, 4.3f, .2f), 2.4f);
            big.name = "RoofHat";
            big.localEulerAngles = new Vector3(0f, 0f, -12f);
        }

        // Lobby layout

        private static GameObject Find(string name)
        {
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        private static void Remove(string name)
        {
            GameObject go;
            while ((go = Find(name)) != null) Object.DestroyImmediate(go);
        }

        private static GameObject Place(string assetPath, Transform parent, Vector3 pos, float yaw, float scale = 1f)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null) { Debug.LogWarning("[LobbyIsland] missing " + assetPath); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = go.transform.localScale * scale;
            return go;
        }

        private static float GroundY(float x, float z)
        {
            return Physics.Raycast(new Vector3(x, Top + 30f, z), Vector3.down, out var hit, 60f) ? hit.point.y : Top;
        }

        public static void LayOut()
        {
            // 1) Old flat lobby out.
            foreach (var n in new[] { "Plane", "wall", "wall (1)", "wall (2)", "SeeSaw", "SeeSawWeight", "=== Lobby Island ===" }) Remove(n);

            var island = new GameObject("=== Lobby Island ===").transform;

            // 2) Portal onto the island, facing the spawn. Drop the 3 stacked duplicate portal models.
            var portal = Find("LobbyPortalController");
            if (portal != null)
            {
                portal.transform.SetPositionAndRotation(Portal, Quaternion.Euler(0f, 180f, 0f));
                bool kept = false;
                var dupes = new List<GameObject>();
                foreach (Transform c in portal.transform)
                    if (c.name == "NewPortalModel") { if (kept) dupes.Add(c.gameObject); kept = true; }
                foreach (var d in dupes) Object.DestroyImmediate(d);
            }

            // 3) Spawn point (NetworkPlayer looks for "PlayerStart"), facing the portal.
            var start = new GameObject("PlayerStart").transform;
            start.SetParent(island, false);
            start.SetPositionAndRotation(Spawn, Quaternion.identity);

            // 4) Stalls either side of the plaza, fronts facing the plaza.
            var gear = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(GearPath), island);
            gear.transform.SetPositionAndRotation(GearStall, Quaternion.Euler(0f, -90f, 0f));
            var outfit = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OutfitPath), island);
            outfit.transform.SetPositionAndRotation(OutfitStall, Quaternion.Euler(0f, 90f, 0f));

            // 5) Plaza + path: pale stone disc and stepping stones spawn -> portal.
            var stone = Mat("Stone", Hex("#D9CFC1"), .15f);
            var stoneDark = Mat("StoneDark", Hex("#B5A897"), .15f);
            var plaza = Shape(island, "Plaza", PrimitiveType.Cylinder, Plaza + Vector3.up * .02f, new Vector3(12.5f, .02f, 12.5f), stone);
            Shape(island, "PlazaRim", PrimitiveType.Cylinder, Plaza + Vector3.up * .012f, new Vector3(13.5f, .012f, 13.5f), stoneDark);
            Shape(island, "PlazaStar", PrimitiveType.Cylinder, Plaza + Vector3.up * .03f, new Vector3(3.2f, .02f, 3.2f), Mat("AwningPink", Color.white));
            for (float z = Spawn.z - 1f; z < Plaza.z - 6.5f; z += 1.6f)
                Shape(island, "PathStone", PrimitiveType.Cylinder, new Vector3(Spawn.x + Mathf.Sin(z) * .25f, Top + .02f, z), new Vector3(1.4f, .02f, 1.1f), stone);
            for (float z = Plaza.z + 6.5f; z < Portal.z - 2.5f; z += 1.6f)
                Shape(island, "PathStone", PrimitiveType.Cylinder, new Vector3(Portal.x + Mathf.Sin(z) * .25f, Top + .02f, z), new Vector3(1.4f, .02f, 1.1f), stone);

            // 6) Toys near the spawn: practice crate + fun props.
            var crate = Find("CarryablePackage");
            if (crate != null) crate.transform.position = new Vector3(Spawn.x + 4.5f, Top + .55f, Spawn.z + 4f);
            var fun = Find("=== Fun Props ===");
            if (fun != null)
            {
                var offsets = new[] { new Vector3(-4f, 0, -3f), new Vector3(-5.2f, 0, -4f), new Vector3(-3.6f, 0, -5f), new Vector3(-6f, 0, -2.5f), new Vector3(-6.4f, 0, -5.2f) };
                int i = 0;
                foreach (Transform c in fun.transform)
                {
                    var o = offsets[i++ % offsets.Length];
                    c.position = new Vector3(Spawn.x + o.x, Top + .35f, Spawn.z - o.z);
                }
            }

            // 7) Signs, benches, fences, lamp posts.
            var signGo = Place("Assets/Prefabs/Decor/Level02Props/L2_WoodSign.prefab", island, new Vector3(Spawn.x + 2.2f, Top, Spawn.z + 1.5f), 90f);
            var lamp = Mat("LanternGlow", Color.white);
            var wood = Mat("WoodDark", Color.white);
            foreach (var p in new[] { new Vector3(-5f, 0, 5f), new Vector3(5f, 0, 5f), new Vector3(-5f, 0, -5f), new Vector3(5f, 0, -5f) })
            {
                var post = Group(island, "LampPost", Plaza + p);
                Shape(post, "Pole", PrimitiveType.Cylinder, new Vector3(0f, 1.5f, 0f), new Vector3(.14f, 1.5f, .14f), wood, collider: true);
                Shape(post, "Lamp", PrimitiveType.Sphere, new Vector3(0f, 3.15f, 0f), new Vector3(.45f, .55f, .45f), lamp);
                Shape(post, "Cap", PrimitiveType.Cylinder, new Vector3(0f, 3.45f, 0f), new Vector3(.5f, .04f, .5f), wood);
            }
            foreach (var p in new[] { new Vector3(-3f, 0, -7.3f), new Vector3(3f, 0, -7.3f) })
            {
                var bench = Group(island, "Bench", Plaza + p);
                Shape(bench, "Seat", PrimitiveType.Cube, new Vector3(0f, .45f, 0f), new Vector3(1.8f, .1f, .5f), Mat("WoodLight", Color.white), collider: true);
                Shape(bench, "Back", PrimitiveType.Cube, new Vector3(0f, .8f, -.22f), new Vector3(1.8f, .45f, .08f), Mat("WoodLight", Color.white));
                foreach (float x in new[] { -.75f, .75f }) Shape(bench, "Leg", PrimitiveType.Cube, new Vector3(x, .2f, 0f), new Vector3(.1f, .4f, .45f), wood);
            }
            // Crates and barrels beside the gear stall, flower pots beside the outfit stall.
            Place("Assets/Prefabs/Decor/Level02Props/L2_WoodCrate_Square.prefab", island, GearStall + new Vector3(.5f, 0f, 3.2f), 15f);
            Place("Assets/Prefabs/Decor/Level02Props/L2_WoodCrate_Square.prefab", island, GearStall + new Vector3(1.2f, 0f, 3.6f), -20f);
            Place("Assets/Prefabs/Decor/Level02Props/L2_WoodCrate_Square.prefab", island, GearStall + new Vector3(.8f, 1.08f, 3.4f), 40f);
            Place("Assets/Prefabs/Decor/Level02Props/L2_BeerCrate_Round.prefab", island, GearStall + new Vector3(.6f, 0f, -3.3f), 0f);
            Place("Assets/Prefabs/Decor/Nature/Decor_FlowerPatch_B.prefab", island, OutfitStall + new Vector3(-.5f, 0f, 3.4f), 30f);
            Place("Assets/Prefabs/Decor/Nature/Decor_FlowerPatch_D.prefab", island, OutfitStall + new Vector3(-.5f, 0f, -3.6f), 100f);

            // 8) Nature: trees, bushes, flowers, rocks, grass around the edge (seeded, kept off the plaza/path/houses).
            var rng = new System.Random(20261120);
            string[] small = { "Decor_FlowerPatch_A", "Decor_FlowerPatch_B", "Decor_FlowerPatch_C", "Decor_FlowerPatch_D", "Decor_Bush_A", "Decor_Bush_B",
                               "Decor_GrassClump_A", "Decor_GrassClump_B", "Decor_GrassClump_C", "Decor_Mushrooms", "Decor_Rocks_Small" };
            var decor = Group(island, "Nature", Vector3.zero);
            var placed = new List<Vector3>();
            int trees = 0, bits = 0;
            for (int tries = 0; tries < 900 && (trees < 30 || bits < 45); tries++)
            {
                float x = -12f + (float)rng.NextDouble() * 45f, z = -18f - (float)rng.NextDouble() * 62f;
                var p = new Vector3(x, 0f, z);
                if (!OnIsland(x, z, out float y)) continue;
                if (Near(p, Plaza, 8.5f) || Near(p, Portal, 6.5f) || Near(p, Spawn, 6f) || Near(p, GearStall, 5f) || Near(p, OutfitStall, 5f)) continue;
                if (Mathf.Abs(x - Spawn.x) < 2.2f && z > Spawn.z && z < Portal.z) continue; // path
                bool tree = trees < 30 && rng.NextDouble() < .5 && (EdgeDistance(x, z) < 12f || !Near(p, Plaza, 16f));
                bool clash = false;
                foreach (var q in placed) if (Near(p, q, tree ? 4f : 2.4f)) { clash = true; break; }
                if (clash) continue;
                placed.Add(p);
                p.y = y;
                if (tree)
                {
                    trees++;
                    float s = 1.1f + (float)rng.NextDouble() * .8f;
                    var t = Place("Assets/Prefabs/ต้นสน.fbx", decor, p, (float)rng.NextDouble() * 360f, s);
                    if (t != null)
                    {
                        var col = t.AddComponent<CapsuleCollider>();
                        col.center = new Vector3(0f, 1.2f, 0f); col.radius = .3f; col.height = 2.4f;
                    }
                }
                else if (bits < 45)
                {
                    bits++;
                    Place("Assets/Prefabs/Decor/Nature/" + small[rng.Next(small.Length)] + ".prefab", decor, p, (float)rng.NextDouble() * 360f, .8f + (float)rng.NextDouble() * .5f);
                }
            }
            Place("Assets/Prefabs/Decor/Nature/Decor_Rock_Big_A.prefab", decor, new Vector3(26f, GroundY(26f, -60f), -60f), 40f);
            Place("Assets/Prefabs/Decor/Nature/Decor_Rock_Big_B.prefab", decor, new Vector3(-6f, GroundY(-6f, -72f), -72f), 200f);

            // 9) Sky: clouds around and below, a windmill and a balloon in the distance.
            var sky = Group(island, "Sky", Vector3.zero);
            string[] clouds = { "Decor_Cloud_A", "Decor_Cloud_B", "Decor_Cloud_C" };
            for (int i = 0; i < 14; i++)
            {
                float a = i * Mathf.PI * 2f / 14f + (float)rng.NextDouble() * .3f;
                float r = 62f + (float)rng.NextDouble() * 20f;
                var p = new Vector3(Plaza.x + Mathf.Cos(a) * r, Top - 8f + (float)rng.NextDouble() * 22f, Plaza.z + Mathf.Sin(a) * r);
                Place("Assets/Prefabs/Decor/Sky/" + clouds[i % 3] + ".prefab", sky, p, (float)rng.NextDouble() * 360f, 1.2f + (float)rng.NextDouble() * 1.2f);
            }
            Place("Assets/Prefabs/Decor/Landmarks/Decor_HotAirBalloon.prefab", sky, new Vector3(55f, Top + 14f, -20f), 0f);
            Place("Assets/Prefabs/Decor/Landmarks/Decor_Windmill.prefab", sky, new Vector3(-30f, GroundY(-30f, -50f), -50f), 60f, .8f);

            Selection.activeGameObject = island.gameObject;
        }

        private static bool Near(Vector3 a, Vector3 b, float r) { a.y = b.y = 0f; return (a - b).sqrMagnitude < r * r; }

        private static bool OnIsland(float x, float z, out float y)
        {
            y = Top;
            if (!Physics.Raycast(new Vector3(x, Top + 30f, z), Vector3.down, out var hit, 60f)) return false;
            if (!hit.collider.name.StartsWith("เกาะใหญ่") || Mathf.Abs(hit.point.y - Top) > .3f) return false;
            y = hit.point.y;
            // keep 2.5 m from the edge
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                if (!Physics.Raycast(new Vector3(x + Mathf.Cos(a) * 2.5f, Top + 30f, z + Mathf.Sin(a) * 2.5f), Vector3.down, 60f)) return false;
            }
            return true;
        }

        private static float EdgeDistance(float x, float z)
        {
            for (float d = 2f; d < 20f; d += 1.5f)
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    if (!Physics.Raycast(new Vector3(x + Mathf.Cos(a) * d, Top + 30f, z + Mathf.Sin(a) * d), Vector3.down, 60f)) return d;
                }
            return 99f;
        }
    }
}
