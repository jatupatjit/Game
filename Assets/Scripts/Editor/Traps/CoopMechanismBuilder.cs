#if UNITY_EDITOR
using CoopGame.CarrySystem;
using CoopGame.Network;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools.Traps
{
    /// <summary>
    /// Builds the co-op mechanisms of as prefabs in Assets/Prefabs/Traps/Coop:
    /// Coop_HoldButtonDoor (portcullis + a floor button on each side, <see cref="HoldPlateDoor"/>) and
    /// Coop_Drawbridge (hinged deck over a gap + two crank wheels, <see cref="CoopDrawbridge"/>). Safe to run again.
    /// </summary>
    public static class CoopMechanismBuilder
    {
        public const string DoorPath = "Assets/Prefabs/Traps/Coop/Coop_HoldButtonDoor.prefab";
        public const string BridgePath = "Assets/Prefabs/Traps/Coop/Coop_Drawbridge.prefab";
        public const string CartPath = "Assets/Prefabs/Traps/Coop/Coop_CargoCart.prefab";
        private const string MaterialFolder = "Assets/Prefabs/Traps/Materials";
        private const string Medieval = "Assets/Prefabs/Traps/Materials/Medieval/";

        [MenuItem("Tools/Traps/Build Hold Button Door")]
        public static void BuildDoorMenu() => Debug.Log(BuildDoor());

        [MenuItem("Tools/Traps/Build Coop Drawbridge")]
        public static void BuildBridgeMenu() => Debug.Log(BuildBridge());

        // Hold-button door
        public static string BuildDoor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[CoopMechanismBuilder] Stop Play mode first.";
            var stone = Load(Medieval + "Medieval_Stone.mat");
            var iron = Load(Medieval + "Medieval_Iron.mat");
            var wood = Load(Medieval + "Medieval_Wood.mat");
            var idle = Make("HoldDoor_ButtonIdle", new Color(.85f, .3f, .25f));
            var active = Make("HoldDoor_ButtonActive", new Color(.35f, .95f, .45f));
            return InTempScene(() =>
            {
                var root = new GameObject("Coop_HoldButtonDoor");
                root.AddComponent<NetworkObject>();
                var door = root.AddComponent<HoldPlateDoor>();

                // Frame: two pillars and a lintel; side walls so the door cannot be walked round (turn off in a corridor).
                Box("PillarL", root.transform, new Vector3(-2.2f, 2.1f, 0f), new Vector3(1f, 4.2f, 1.2f), stone);
                Box("PillarR", root.transform, new Vector3(2.2f, 2.1f, 0f), new Vector3(1f, 4.2f, 1.2f), stone);
                Box("Lintel", root.transform, new Vector3(0f, 4.55f, 0f), new Vector3(5.4f, .7f, 1.3f), stone);
                var walls = new GameObject("SideWalls").transform;
                walls.SetParent(root.transform, false);
                Box("WallL", walls, new Vector3(-6.7f, 1.5f, 0f), new Vector3(8f, 3f, .8f), stone);
                Box("WallR", walls, new Vector3(6.7f, 1.5f, 0f), new Vector3(8f, 3f, .8f), stone);

                // Portcullis: iron bars in a kinematic body that slides up.
                var gate = new GameObject("Portcullis");
                gate.transform.SetParent(root.transform, false);
                gate.transform.localPosition = new Vector3(0f, 1.75f, 0f);
                var body = gate.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                var col = gate.AddComponent<BoxCollider>();
                col.size = new Vector3(3.4f, 3.5f, .3f);
                for (int i = 0; i < 7; i++)
                    Box("Bar", gate.transform, new Vector3(-1.5f + i * .5f, 0f, 0f), new Vector3(.12f, 3.5f, .12f), iron, false);
                for (int i = 0; i < 3; i++)
                    Box("Cross", gate.transform, new Vector3(0f, -1.2f + i * 1.2f, 0f), new Vector3(3.4f, .12f, .14f), iron, false);
                for (int i = 0; i < 7; i++)
                    Box("Spike", gate.transform, new Vector3(-1.5f + i * .5f, -1.82f, 0f), new Vector3(.07f, .18f, .07f), iron, false, new Vector3(0f, 0f, 45f));

                // Buttons: one in front (-Z) and one behind (+Z), off to the side of the doorway.
                var plates = new BoxCollider[2];
                var tops = new Transform[2];
                var lights = new Renderer[2];
                for (int i = 0; i < 2; i++)
                {
                    float z = i == 0 ? -3.2f : 3.2f;
                    var plate = new GameObject(i == 0 ? "ButtonFront" : "ButtonBack");
                    plate.transform.SetParent(root.transform, false);
                    plate.transform.localPosition = new Vector3(-3.6f, 0f, z);
                    Box("Base", plate.transform, new Vector3(0f, .05f, 0f), new Vector3(1.7f, .1f, 1.7f), stone);
                    var top = Box("Top", plate.transform, new Vector3(0f, .14f, 0f), new Vector3(1.3f, .1f, 1.3f), idle, false);
                    var area = plate.AddComponent<BoxCollider>();
                    area.isTrigger = true;
                    area.center = new Vector3(0f, .6f, 0f);
                    area.size = new Vector3(1.5f, 1.2f, 1.5f);
                    plates[i] = area; tops[i] = top.transform; lights[i] = top.GetComponent<Renderer>();
                    // Arrow on the floor pointing from the button to the door.
                    Box("Arrow", plate.transform, new Vector3(1.4f, .02f, -z * .25f), new Vector3(.9f, .03f, .18f), active, false, new Vector3(0f, i == 0 ? -20f : 20f, 0f));
                }

                var passage = new GameObject("Doorway");
                passage.transform.SetParent(root.transform, false);
                var passageBox = passage.AddComponent<BoxCollider>();
                passageBox.isTrigger = true;
                passageBox.center = new Vector3(0f, 1.6f, 0f);
                passageBox.size = new Vector3(3.4f, 3.2f, 2.4f);

                var sign = new GameObject("Sign");
                sign.transform.SetParent(root.transform, false);
                sign.transform.localPosition = new Vector3(0f, 5.5f, -.7f);
                var text = sign.AddComponent<TextMesh>();
                text.text = "ยืนบนปุ่มค้างไว้ ประตูจะเปิด\nปล่อยแล้วประตูปิด";
                text.fontSize = 112; text.characterSize = .022f;
                text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = Color.white;
                Box("SignBoard", sign.transform, new Vector3(0f, 0f, .06f), new Vector3(4.6f, 1.1f, .08f), wood, false);

                var so = new SerializedObject(door);
                Set(so, "_plates", plates);
                Set(so, "_plateTops", tops);
                Set(so, "_plateLights", lights);
                so.FindProperty("_idleMaterial").objectReferenceValue = idle;
                so.FindProperty("_activeMaterial").objectReferenceValue = active;
                so.FindProperty("_gate").objectReferenceValue = body;
                so.FindProperty("_passage").objectReferenceValue = passageBox;
                so.FindProperty("_label").objectReferenceValue = text;
                so.ApplyModifiedPropertiesWithoutUndo();
                return Save(root, DoorPath);
            });
        }

        // Drawbridge
        public static string BuildBridge()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[CoopMechanismBuilder] Stop Play mode first.";
            var stone = Load(Medieval + "Medieval_Stone.mat");
            var iron = Load(Medieval + "Medieval_Iron.mat");
            var wood = Load(Medieval + "Medieval_Wood.mat");
            var woodLight = Load(Medieval + "Medieval_WoodLight.mat");
            var rope = Make("Capstan_Rope", new Color(.85f, .74f, .5f));
            var idle = Make("HoldDoor_ButtonIdle", new Color(.85f, .3f, .25f));
            var active = Make("HoldDoor_ButtonActive", new Color(.35f, .95f, .45f));
            const float length = 7f, width = 3.4f;
            return InTempScene(() =>
            {
                var root = new GameObject("Coop_Drawbridge");
                root.AddComponent<NetworkObject>();
                var bridge = root.AddComponent<CoopDrawbridge>();

                // Hinge at the near edge (root), deck along +Z.
                var hinge = new GameObject("Hinge").transform;
                hinge.SetParent(root.transform, false);
                hinge.localPosition = new Vector3(0f, -.1f, 0f);
                var deck = new GameObject("Deck");
                deck.transform.SetParent(hinge, false);
                deck.transform.localPosition = new Vector3(0f, 0f, length * .5f);
                var deckBody = deck.AddComponent<Rigidbody>();
                deckBody.isKinematic = true;
                deckBody.interpolation = RigidbodyInterpolation.Interpolate;
                var deckCol = deck.AddComponent<BoxCollider>();
                deckCol.size = new Vector3(width, .25f, length);
                for (int i = 0; i < 10; i++)
                    Box("Plank", deck.transform, new Vector3(0f, 0f, -length * .5f + .35f + i * .7f), new Vector3(width, .22f, .62f), i % 2 == 0 ? wood : woodLight, false);
                Box("RailL", deck.transform, new Vector3(-width * .5f + .08f, .2f, 0f), new Vector3(.14f, .2f, length), wood, false);
                Box("RailR", deck.transform, new Vector3(width * .5f - .08f, .2f, 0f), new Vector3(.14f, .2f, length), wood, false);
                var tipL = new GameObject("RopeEndL").transform; tipL.SetParent(deck.transform, false); tipL.localPosition = new Vector3(-width * .5f + .1f, .2f, length * .5f - .2f);
                var tipR = new GameObject("RopeEndR").transform; tipR.SetParent(deck.transform, false); tipR.localPosition = new Vector3(width * .5f - .1f, .2f, length * .5f - .2f);

                // Gate posts at the near edge (ropes run from the deck tip to their tops), and the near landing.
                Box("PostL", root.transform, new Vector3(-2.3f, 2.5f, -.4f), new Vector3(.6f, 5f, .6f), stone);
                Box("PostR", root.transform, new Vector3(2.3f, 2.5f, -.4f), new Vector3(.6f, 5f, .6f), stone);
                var topL = new GameObject("RopeTopL").transform; topL.SetParent(root.transform, false); topL.localPosition = new Vector3(-2.05f, 4.7f, -.2f);
                var topR = new GameObject("RopeTopR").transform; topR.SetParent(root.transform, false); topR.localPosition = new Vector3(2.05f, 4.7f, -.2f);
                var ropeL = Cylinder("RopeL", root.transform, Vector3.zero, new Vector3(.06f, 1f, .06f), rope, false);
                var ropeR = Cylinder("RopeR", root.transform, Vector3.zero, new Vector3(.06f, 1f, .06f), rope, false);
                Box("HingeBeam", root.transform, new Vector3(0f, -.15f, -.15f), new Vector3(width + .6f, .3f, .3f), iron, false);

                // Two crank wheels beside the posts, facing the approach (-Z).
                var cranks = new Transform[2];
                var lights = new Renderer[2];
                for (int i = 0; i < 2; i++)
                {
                    float x = i == 0 ? -3.6f : 3.6f;
                    var stand = new GameObject(i == 0 ? "CrankLeft" : "CrankRight").transform;
                    stand.SetParent(root.transform, false);
                    stand.localPosition = new Vector3(x, 0f, -1.6f);
                    Box("Stand", stand, new Vector3(0f, .55f, .25f), new Vector3(.35f, 1.1f, .35f), wood);
                    var wheel = new GameObject("Wheel").transform;
                    wheel.SetParent(stand, false);
                    wheel.localPosition = new Vector3(0f, 1.15f, 0f);
                    Cylinder("Rim", wheel, Vector3.zero, new Vector3(1.1f, .06f, 1.1f), wood, false, new Vector3(90f, 0f, 0f));
                    for (int s = 0; s < 4; s++)
                        Box("Spoke", wheel, Vector3.zero, new Vector3(.08f, 1.05f, .08f), woodLight, false, new Vector3(0f, 0f, s * 45f));
                    Cylinder("Handle", wheel, new Vector3(.45f, 0f, -.22f), new Vector3(.08f, .2f, .08f), iron, false, new Vector3(90f, 0f, 0f));
                    var ring = Cylinder("Ring", stand, new Vector3(0f, .02f, -.6f), new Vector3(1.6f, .02f, 1.6f), idle, false);
                    cranks[i] = wheel; lights[i] = ring.GetComponent<Renderer>();
                }

                var sign = new GameObject("Sign");
                sign.transform.SetParent(root.transform, false);
                sign.transform.localPosition = new Vector3(0f, 2.6f, -3.2f);
                var text = sign.AddComponent<TextMesh>();
                text.text = "กด E ค้างที่ล้อหมุน 2 คนพร้อมกัน\nเพื่อลดสะพาน";
                text.fontSize = 112; text.characterSize = .022f;
                text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.color = Color.white;
                Box("SignBoard", sign.transform, new Vector3(0f, 0f, .06f), new Vector3(4.8f, 1.1f, .08f), wood, false);
                Box("SignPost", sign.transform, new Vector3(0f, -1.3f, .12f), new Vector3(.16f, 1.6f, .16f), wood, false);

                var so = new SerializedObject(bridge);
                so.FindProperty("_hinge").objectReferenceValue = hinge;
                so.FindProperty("_deck").objectReferenceValue = deckBody;
                Set(so, "_cranks", cranks);
                Set(so, "_crankLights", lights);
                so.FindProperty("_idleMaterial").objectReferenceValue = idle;
                so.FindProperty("_activeMaterial").objectReferenceValue = active;
                Set(so, "_ropes", new Object[] { ropeL.transform, ropeR.transform });
                Set(so, "_ropeTops", new Object[] { topL, topR });
                Set(so, "_ropeEnds", new Object[] { tipL, tipR });
                so.FindProperty("_label").objectReferenceValue = text;
                so.ApplyModifiedPropertiesWithoutUndo();
                // The hinge stays level in the prefab (its rest); the script raises it at runtime from the progress.
                return Save(root, BridgePath);
            });
        }

        // Cargo cart / sled
        [MenuItem("Tools/Traps/Build Cargo Cart")]
        public static void BuildCartMenu() => Debug.Log(BuildCart());

        /// <summary>Wooden sled with a flat bed and low rails (long axis = local Z, grip ends at +Z / -Z).</summary>
        public static string BuildCart()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[CoopMechanismBuilder] Stop Play mode first.";
            var wood = Load(Medieval + "Medieval_Wood.mat");
            var woodLight = Load(Medieval + "Medieval_WoodLight.mat");
            var iron = Load(Medieval + "Medieval_Iron.mat");
            var slide = MakePhysics("CargoCart_Slide", .03f, PhysicsMaterialCombine.Minimum);
            var grip = MakePhysics("CargoCart_Bed", 1f, PhysicsMaterialCombine.Maximum);
            var heavy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Items/Variants/Cargo_Heavy.prefab");
            const float width = 1.8f, length = 3.4f, thick = .1f, railHeight = .14f;
            return InTempScene(() =>
            {
                var root = new GameObject("Coop_CargoCart");
                var body = root.AddComponent<Rigidbody>();
                body.mass = 18f;
                body.linearDamping = .15f;
                body.angularDamping = 2.5f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                root.AddComponent<NetworkObject>();
                // Same replication settings as the crates.
                var crateTransform = heavy != null ? heavy.GetComponent<NetworkTransform>() : null;
                if (crateTransform == null || !UnityEditorInternal.ComponentUtility.CopyComponent(crateTransform) ||
                    !UnityEditorInternal.ComponentUtility.PasteComponentAsNew(root))
                    root.AddComponent<NetworkTransform>();
                var cart = root.AddComponent<CargoCart>();
                root.AddComponent<CarryGripEnds>();
                root.AddComponent<CarryableHome>();

                // Bed (high friction so crates stay put) with low rails on all four edges.
                var bed = Box("Bed", root.transform, Vector3.zero, new Vector3(width, thick, length), woodLight);
                bed.GetComponent<Collider>().sharedMaterial = grip;
                float railY = thick * .5f + railHeight * .5f;
                var rails = new Collider[4];
                for (int i = 0; i < 2; i++)
                {
                    float x = i == 0 ? -1f : 1f;
                    var side = Box(i == 0 ? "RailL" : "RailR", root.transform, new Vector3(x * (width * .5f - .04f), railY, 0f), new Vector3(.08f, railHeight, length), wood);
                    side.GetComponent<Collider>().sharedMaterial = grip;
                    rails[i] = side.GetComponent<Collider>();
                    var end = Box(i == 0 ? "RailBack" : "RailFront", root.transform, new Vector3(0f, railY, x * (length * .5f - .04f)), new Vector3(width - .16f, railHeight, .08f), wood);
                    end.GetComponent<Collider>().sharedMaterial = grip;
                    rails[2 + i] = end.GetComponent<Collider>();
                }

                // Runners: low friction against the ground, so it glides downhill.
                for (int i = 0; i < 2; i++)
                {
                    float x = i == 0 ? -.65f : .65f;
                    var runner = Box(i == 0 ? "RunnerL" : "RunnerR", root.transform, new Vector3(x, -.12f, 0f), new Vector3(.16f, .28f, length), wood);
                    runner.GetComponent<Collider>().sharedMaterial = slide;
                    Box("RunnerTip", root.transform, new Vector3(x, -.14f, length * .5f + .12f), new Vector3(.16f, .12f, .4f), wood, false, new Vector3(-20f, 0f, 0f));
                    Box("Brace", root.transform, new Vector3(0f, -.08f, (i == 0 ? -1f : 1f) * length * .3f), new Vector3(width - .3f, .08f, .14f), wood, false);
                }

                // Push handles at both ends (visual only; players grip the end faces).
                for (int i = 0; i < 2; i++)
                {
                    float z = (i == 0 ? -1f : 1f) * (length * .5f + .03f);
                    Box("HandlePostL", root.transform, new Vector3(-.7f, .32f, z), new Vector3(.07f, .5f, .07f), iron, false);
                    Box("HandlePostR", root.transform, new Vector3(.7f, .32f, z), new Vector3(.07f, .5f, .07f), iron, false);
                    Box("HandleBar", root.transform, new Vector3(0f, .58f, z), new Vector3(1.47f, .07f, .07f), iron, false);
                }

                // Trigger over the bed: crates inside it ride along.
                var zone = new GameObject("BedZone");
                zone.transform.SetParent(root.transform, false);
                var zoneBox = zone.AddComponent<BoxCollider>();
                zoneBox.isTrigger = true;
                zoneBox.center = new Vector3(0f, thick * .5f + .3f, 0f);
                zoneBox.size = new Vector3(width - .2f, .6f, length - .2f);

                // Exactly two carry sockets: front (+Z) and back (-Z).
                var front = new GameObject("Socket_Front").transform;
                front.SetParent(root.transform, false);
                front.localPosition = new Vector3(0f, 0f, length * .5f);
                var back = new GameObject("Socket_Back").transform;
                back.SetParent(root.transform, false);
                back.localPosition = new Vector3(0f, 0f, -length * .5f);
                back.localRotation = Quaternion.Euler(0f, 180f, 0f);

                var carrySo = new SerializedObject(cart);
                carrySo.FindProperty("_requiresTwoCarriers").boolValue = true; // one player drags it, two carry it
                Set(carrySo, "_sockets", new Object[] { front, back });
                carrySo.FindProperty("_bed").objectReferenceValue = zoneBox;
                Set(carrySo, "_rails", rails);
                carrySo.ApplyModifiedPropertiesWithoutUndo();
                return Save(root, CartPath);
            });
        }

        private static PhysicsMaterial MakePhysics(string name, float friction, PhysicsMaterialCombine combine)
        {
            string path = MaterialFolder + "/" + name + ".asset";
            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (material == null)
            {
                material = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(material, path);
            }
            material.dynamicFriction = friction;
            material.staticFriction = friction;
            material.bounciness = 0f;
            material.frictionCombine = combine;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
        }

        // Helpers
        private static string InTempScene(System.Func<string> build)
        {
            var previous = EditorSceneManager.GetActiveScene();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(temp);
            try { return build(); }
            finally
            {
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(temp, true);
            }
        }

        private static string Save(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            return ok ? "[CoopMechanismBuilder] built " + path : "[CoopMechanismBuilder] FAILED " + path;
        }

        private static void Set(SerializedObject so, string name, Object[] values)
        {
            var p = so.FindProperty(name);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
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
            bool collider = true, Vector3 euler = default)
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
