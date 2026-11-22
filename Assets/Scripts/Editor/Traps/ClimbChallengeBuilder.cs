#if UNITY_EDITOR
using CoopGame.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools.Traps
{
    /// <summary>
    /// George 2026-11-23, "climbing should do something": builds the climb challenges as prefabs in
    /// Assets/Prefabs/Traps/Climb and a test scene Assets/Scenes/Test/ClimbChallenges.unity to try them.
    /// A: Climb_WallSwitch (climb up, E pulls a lever, latches on for good) opening Climb_SwitchGate (rises),
    ///    Climb_SwitchBridge (lowers over a gap) and Climb_SwitchPlatform (slides out).
    /// B: Climb_RopeHoist (cliff + winch + basket; needs 2+ players; letting go drops the crate).
    /// E: Climb_ThrowShortcut (George picked E-b: a 4.5 m wall; one climbs up, the other throws the crate up to be
    ///    caught with hold-F; a miss drops the crate. In the test scene a long ramp is the safe way round).
    /// Menu: Tools/Traps/Build Climb Challenges. Safe to run again (prefabs and the test scene are rebuilt).
    /// </summary>
    public static class ClimbChallengeBuilder
    {
        private const string Folder = "Assets/Prefabs/Traps/Climb";
        private const string SwitchPath = Folder + "/Climb_WallSwitch.prefab";
        private const string GatePath = Folder + "/Climb_SwitchGate.prefab";
        private const string BridgePath = Folder + "/Climb_SwitchBridge.prefab";
        private const string PlatformPath = Folder + "/Climb_SwitchPlatform.prefab";
        private const string HoistPath = Folder + "/Climb_RopeHoist.prefab";
        private const string ShortcutPath = Folder + "/Climb_ThrowShortcut.prefab";
        private const string ScenePath = "Assets/Scenes/Test/ClimbChallenges.unity";
        private const string CratePath = "Assets/Prefabs/Items/WoodenCrateItem.prefab";
        private const string Medieval = "Assets/Prefabs/Traps/Materials/Medieval/";
        private const string MaterialFolder = "Assets/Prefabs/Traps/Materials";

        private static Material _stone, _wood, _woodLight, _iron, _grip, _lamp, _rope, _ground;

        [MenuItem("Tools/Traps/Build Climb Challenges")]
        public static void BuildMenu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[ClimbChallenge] Stop Play mode first.";
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs/Traps", "Climb");
            _stone = Load("Medieval_Stone"); _wood = Load("Medieval_Wood"); _woodLight = Load("Medieval_WoodLight"); _iron = Load("Medieval_Iron");
            _grip = Make("Climb_Grip", new Color(.55f, .45f, .35f));
            _lamp = Make("Climb_Lamp", new Color(1f, .6f, .15f));
            _rope = Make("Climb_Rope", new Color(.78f, .66f, .45f));
            _ground = Make("Climb_Ground", new Color(.45f, .72f, .38f));
            string log = "";
            log += InTempScene(BuildSwitch) + "\n";
            log += InTempScene(BuildGate) + "\n";
            log += InTempScene(BuildBridge) + "\n";
            log += InTempScene(BuildPlatform) + "\n";
            log += InTempScene(BuildHoist) + "\n";
            log += InTempScene(BuildShortcut) + "\n";
            AssetDatabase.SaveAssets();
            log += BuildTestScene();
            return log;
        }

        // ───────────────────────── A: wall switch + targets ─────────────────────────

        private static string BuildSwitch()
        {
            var root = new GameObject("Climb_WallSwitch");
            root.AddComponent<NetworkObject>();
            var sw = root.AddComponent<ClimbWallSwitch>();
            // Climbable wall (front face at z = -0.5), handhold stones on it, a lever near the top.
            Box("Wall", root.transform, new Vector3(0f, 3f, 0f), new Vector3(4f, 6f, 1f), _stone);
            Grips(root.transform, 4f, 6f, -.5f);
            Box("TopLedge", root.transform, new Vector3(0f, 6.1f, .3f), new Vector3(4.4f, .2f, 1.8f), _wood);
            var mount = Box("LeverMount", root.transform, new Vector3(0f, 5.3f, -.6f), new Vector3(.7f, .7f, .2f), _iron, false);
            var pivot = new GameObject("LeverPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 5.3f, -.75f);
            pivot.localEulerAngles = new Vector3(0f, 0f, 35f);
            Box("Arm", pivot, new Vector3(0f, .35f, 0f), new Vector3(.12f, .7f, .12f), _woodLight, false);
            var knob = Sphere("Knob", pivot, new Vector3(0f, .72f, 0f), Vector3.one * .26f, _lamp);
            var label = Label(root.transform, new Vector3(0f, 6.9f, -.6f), "ปีนขึ้นมาดึงคันโยก");
            var so = new SerializedObject(sw);
            so.FindProperty("_button").objectReferenceValue = pivot;
            so.FindProperty("_lever").objectReferenceValue = pivot;
            so.FindProperty("_leverOnEuler").vector3Value = new Vector3(0f, 0f, -70f);
            so.FindProperty("_indicator").objectReferenceValue = knob.GetComponent<Renderer>();
            so.FindProperty("_label").objectReferenceValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
            return Save(root, SwitchPath);
        }

        private static string BuildGate()
        {
            var root = new GameObject("Climb_SwitchGate");
            Box("PillarL", root.transform, new Vector3(-2.2f, 2.1f, 0f), new Vector3(1f, 4.2f, 1.2f), _stone);
            Box("PillarR", root.transform, new Vector3(2.2f, 2.1f, 0f), new Vector3(1f, 4.2f, 1.2f), _stone);
            Box("Lintel", root.transform, new Vector3(0f, 4.55f, 0f), new Vector3(5.4f, .7f, 1.3f), _stone);
            var gate = new GameObject("Portcullis");
            gate.transform.SetParent(root.transform, false);
            gate.transform.localPosition = new Vector3(0f, 1.75f, 0f);
            var body = gate.AddComponent<Rigidbody>();
            body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate;
            for (int i = 0; i < 6; i++) Box("Bar" + i, gate.transform, new Vector3(-1.5f + i * .6f, 0f, 0f), new Vector3(.14f, 3.5f, .14f), _iron);
            for (int i = 0; i < 3; i++) Box("Rail" + i, gate.transform, new Vector3(0f, -1.2f + i * 1.2f, 0f), new Vector3(3.4f, .14f, .16f), _iron);
            var t = gate.AddComponent<SwitchTarget>();
            var so = new SerializedObject(t);
            so.FindProperty("_openOffset").vector3Value = new Vector3(0f, 3.3f, 0f);
            so.FindProperty("_seconds").floatValue = 2.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return Save(root, GatePath);
        }

        private static string BuildBridge()
        {
            // Hinged deck standing up at the near edge; lowering it (90 deg on X) spans a 5.5 m gap toward +Z.
            var root = new GameObject("Climb_SwitchBridge");
            Box("Abutment", root.transform, new Vector3(0f, -.5f, -.6f), new Vector3(3.6f, 1f, 1.2f), _stone);
            var hinge = new GameObject("Hinge");
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.localPosition = new Vector3(0f, 0f, 0f);
            hinge.transform.localEulerAngles = new Vector3(-90f, 0f, 0f);
            var body = hinge.AddComponent<Rigidbody>();
            body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate;
            Box("Deck", hinge.transform, new Vector3(0f, -.1f, 2.9f), new Vector3(3f, .2f, 5.8f), _wood);
            for (int i = 0; i < 6; i++) Box("Plank" + i, hinge.transform, new Vector3(0f, .01f, .5f + i * .95f), new Vector3(3.05f, .04f, .12f), _woodLight, false);
            Box("RailL", hinge.transform, new Vector3(-1.45f, .25f, 2.9f), new Vector3(.12f, .5f, 5.8f), _woodLight);
            Box("RailR", hinge.transform, new Vector3(1.45f, .25f, 2.9f), new Vector3(.12f, .5f, 5.8f), _woodLight);
            var t = hinge.AddComponent<SwitchTarget>();
            var so = new SerializedObject(t);
            so.FindProperty("_openEuler").vector3Value = new Vector3(90f, 0f, 0f);
            so.FindProperty("_seconds").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return Save(root, BridgePath);
        }

        private static string BuildPlatform()
        {
            // A ledge slab tucked in a wall slides out 2.5 m so players can step across.
            var root = new GameObject("Climb_SwitchPlatform");
            var slab = new GameObject("Slab");
            slab.transform.SetParent(root.transform, false);
            var body = slab.AddComponent<Rigidbody>();
            body.isKinematic = true; body.interpolation = RigidbodyInterpolation.Interpolate;
            Box("Top", slab.transform, new Vector3(0f, 0f, 0f), new Vector3(2.6f, .3f, 2.6f), _wood);
            var t = slab.AddComponent<SwitchTarget>();
            var so = new SerializedObject(t);
            so.FindProperty("_openOffset").vector3Value = new Vector3(0f, 0f, -2.5f);
            so.FindProperty("_seconds").floatValue = 2f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return Save(root, PlatformPath);
        }

        // ───────────────────────── B: rope hoist ─────────────────────────

        private static string BuildHoist()
        {
            const float h = 6f;
            var root = new GameObject("Climb_RopeHoist");
            root.AddComponent<NetworkObject>();
            var hoist = root.AddComponent<RopeHoist>();
            // Cliff: front face at z = 0 (climbable), top at y = h, 6 m deep.
            Box("Cliff", root.transform, new Vector3(0f, h * .5f, 3f), new Vector3(8f, h, 6f), _stone);
            Grips(root.transform, 8f, h, 0f);
            // Winch on top, a little back from the edge.
            var winch = new GameObject("Winch").transform;
            winch.SetParent(root.transform, false);
            winch.localPosition = new Vector3(1.6f, h, 1.4f);
            Box("PostL", winch, new Vector3(-.5f, .55f, 0f), new Vector3(.15f, 1.1f, .3f), _wood);
            Box("PostR", winch, new Vector3(.5f, .55f, 0f), new Vector3(.15f, 1.1f, .3f), _wood);
            var wheel = new GameObject("Wheel").transform;
            wheel.SetParent(winch, false);
            wheel.localPosition = new Vector3(0f, .95f, 0f);
            wheel.localEulerAngles = new Vector3(0f, 90f, 0f);
            Cylinder("Drum", wheel, Vector3.zero, new Vector3(.45f, .45f, .45f), _woodLight, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 4; i++)
                Box("Spoke" + i, wheel, Vector3.zero, new Vector3(1.3f, .1f, .1f), _wood, false, new Vector3(0f, 0f, i * 45f));
            var lamp = Sphere("Lamp", winch, new Vector3(0f, 1.6f, 0f), Vector3.one * .3f, _lamp);
            // Pulley arm over the edge.
            // Post beside the basket (not between it and the ledge, where it blocked the crate being lifted out),
            // with an L-shaped arm reaching over to the pulley.
            Box("ArmPost", root.transform, new Vector3(-2.65f, h + 1.3f, .3f), new Vector3(.25f, 2.6f, .25f), _wood);
            Box("ArmX", root.transform, new Vector3(-1.86f, h + 2.5f, .3f), new Vector3(1.8f, .22f, .22f), _wood);
            Box("ArmZ", root.transform, new Vector3(-1.2f, h + 2.5f, -.42f), new Vector3(.22f, .22f, 1.5f), _wood);
            var pulley = new GameObject("Pulley").transform;
            pulley.SetParent(root.transform, false);
            pulley.localPosition = new Vector3(-1.2f, h + 2.35f, -1.05f);
            Cylinder("PulleyWheel", pulley, Vector3.zero, new Vector3(.4f, .06f, .4f), _iron, new Vector3(0f, 0f, 90f));
            // Rope from the winch drum to the pulley (fixed) and from the pulley down to the basket (stretched).
            var ropeTop = Cylinder("RopeToWinch", root.transform, Vector3.zero, Vector3.one, _rope, default);
            Stretch(ropeTop.transform, winch.localPosition + new Vector3(0f, .95f, 0f), pulley.localPosition);
            var rope = Cylinder("Rope", root.transform, new Vector3(-1.2f, h * .5f, -1.05f), new Vector3(.07f, 1f, .07f), _rope, default);
            // Basket: kinematic body, floor + low rails; the hold box sees a crate inside.
            var basketGo = new GameObject("Basket");
            basketGo.transform.SetParent(root.transform, false);
            basketGo.transform.localPosition = new Vector3(-1.2f, 0f, -1.05f);
            var basket = basketGo.AddComponent<Rigidbody>();
            basket.isKinematic = true; basket.interpolation = RigidbodyInterpolation.Interpolate;
            Box("Floor", basketGo.transform, new Vector3(0f, .1f, 0f), new Vector3(1.9f, .2f, 1.9f), _woodLight);
            Box("RailFront", basketGo.transform, new Vector3(0f, .3f, -.9f), new Vector3(1.9f, .2f, .1f), _wood);
            // No rail on the cliff side: the cliff face holds the crate in on the way up, and at the top it slides
            // straight out onto the ledge (a back rail trapped it against the carrier and scraped it every frame).
            Box("RailL", basketGo.transform, new Vector3(-.9f, .3f, 0f), new Vector3(.1f, .2f, 1.9f), _wood);
            Box("RailR", basketGo.transform, new Vector3(.9f, .3f, 0f), new Vector3(.1f, .2f, 1.9f), _wood);
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -.85f : .85f, z = i < 2 ? -.85f : .85f;
                var cord = Cylinder("Cord" + i, basketGo.transform, Vector3.zero, Vector3.one, _rope, default);
                Stretch(cord.transform, new Vector3(x, .5f, z), new Vector3(0f, 1.5f, 0f));
            }
            var basketTop = new GameObject("RopeAttach").transform;
            basketTop.SetParent(basketGo.transform, false);
            basketTop.localPosition = new Vector3(0f, 1.5f, 0f);
            var holdGo = new GameObject("Hold");
            holdGo.transform.SetParent(basketGo.transform, false);
            var hold = holdGo.AddComponent<BoxCollider>();
            hold.isTrigger = true;
            hold.center = new Vector3(0f, .65f, 0f);
            hold.size = new Vector3(1.6f, .9f, 1.6f);
            var label = Label(root.transform, new Vector3(-1.2f, h + 3.2f, -1.05f), "วางลังในตะกร้า");

            var so = new SerializedObject(hoist);
            so.FindProperty("_winch").objectReferenceValue = winch;
            so.FindProperty("_wheel").objectReferenceValue = wheel;
            so.FindProperty("_basket").objectReferenceValue = basket;
            so.FindProperty("_height").floatValue = h - .2f; // floor top ends flush with the ledge, like a crate on the ground
            so.FindProperty("_rope").objectReferenceValue = rope.transform;
            so.FindProperty("_pulley").objectReferenceValue = pulley;
            so.FindProperty("_basketTop").objectReferenceValue = basketTop;
            so.FindProperty("_hold").objectReferenceValue = hold;
            so.FindProperty("_label").objectReferenceValue = label;
            so.FindProperty("_lamp").objectReferenceValue = lamp.GetComponent<Renderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
            return Save(root, HoistPath);
        }

        // ───────────────────────── E: throw-up shortcut ─────────────────────────

        private static string BuildShortcut()
        {
            const float h = 4.5f;
            var root = new GameObject("Climb_ThrowShortcut");
            // Climbable front face at z = 0, plateau 6 m deep.
            Box("Cliff", root.transform, new Vector3(0f, h * .5f, 3f), new Vector3(10f, h, 6f), _stone);
            Grips(root.transform, 10f, h, 0f);
            // Yellow strips: the catcher stands on the top one and holds F; the thrower stands on the bottom one.
            var mark = Make("Climb_CatchMark", new Color(1f, .85f, .2f));
            Box("CatchStrip", root.transform, new Vector3(0f, h + .01f, .7f), new Vector3(9.6f, .02f, 1.2f), mark, false);
            Box("ThrowStrip", root.transform, new Vector3(0f, .01f, -3.5f), new Vector3(4f, .02f, 1.5f), mark, false);
            var sign = new GameObject("Sign");
            sign.transform.SetParent(root.transform, false);
            sign.transform.localPosition = new Vector3(-3.4f, 2.2f, -1.4f);
            Box("Board", sign.transform, new Vector3(0f, 0f, .06f), new Vector3(4.8f, 1.6f, .08f), _wood, false);
            Box("Post", sign.transform, new Vector3(0f, -1.3f, .1f), new Vector3(.15f, 1.8f, .15f), _wood);
            var label = Label(sign.transform, Vector3.zero, "ทางลัด! คนหนึ่งปีนขึ้นไปยืนแถบเหลือง ค้าง F รอรับ\nอีกคนยืนแถบเหลืองล่าง ค้าง Q เล็งขึ้นแล้วโยน\nรับพลาด ลังตกเสีย HP");
            label.characterSize = .022f;
            return Save(root, ShortcutPath);
        }

        // ───────────────────────── Test scene ─────────────────────────

        private static string BuildTestScene()
        {
            var previous = EditorSceneManager.GetActiveScene().path;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects()) if (go.GetComponent<Camera>() != null) Object.DestroyImmediate(go);
            var ground = new GameObject("=== Ground ===").transform;
            // Spawn field (z -20..6), a 5.5 m gap (z 6..11.5) closed by the switch bridge, then the far field.
            Box("FieldNear", ground, new Vector3(0f, -.5f, -7f), new Vector3(60f, 1f, 26f), _ground);
            Box("FieldFar", ground, new Vector3(0f, -.5f, 24.25f), new Vector3(60f, 1f, 25.5f), _ground);
            // No floor in the gap: falling in respawns at the start (fall limit).
            var start = new GameObject("PlayerStart");
            start.transform.position = new Vector3(0f, 1.1f, -16f);
            for (int i = 0; i < 2; i++)
                Instantiate(CratePath, new Vector3(-2f + i * 4f, .6f, -12f), Quaternion.identity);

            // A1: climb the wall left of the gap -> the bridge lowers over the gap.
            var sw1 = Instantiate(SwitchPath, new Vector3(-8f, 0f, 2f), Quaternion.identity).GetComponent<ClimbWallSwitch>();
            var bridge = Instantiate(BridgePath, new Vector3(0f, 0f, 6f), Quaternion.identity);
            Link(bridge, sw1);
            Sign(new Vector3(-8f, 3.2f, -1f), "A: ปีนกำแพง ดึงคันโยก\nสะพานจะลง");

            // A2: on the far side a gate blocks the path; climb the wall beside it.
            var gate = Instantiate(GatePath, new Vector3(0f, 0f, 20f), Quaternion.identity);
            Box("GateWallL", ground, new Vector3(-16.5f, 2f, 20f), new Vector3(28f, 4f, 1f), _stone);
            Box("GateWallR", ground, new Vector3(16.5f, 2f, 20f), new Vector3(28f, 4f, 1f), _stone);
            var sw2 = Instantiate(SwitchPath, new Vector3(6f, 0f, 16f), Quaternion.identity).GetComponent<ClimbWallSwitch>();
            Link(gate, sw2);
            var platform = Instantiate(PlatformPath, new Vector3(12f, 2.6f, 20.6f), Quaternion.identity); // tucked in the wall, slides out as a step
            Link(platform, sw2);

            // B: rope hoist cliff on the right; the goal pad is on top.
            Instantiate(HoistPath, new Vector3(18f, 0f, -8f), Quaternion.identity);

            // E: past the gate, a 4.5 m wall straight ahead (throw shortcut) or the long ramp on the right.
            Instantiate(ShortcutPath, new Vector3(0f, 0f, 29f), Quaternion.identity);
            var ramp = Box("LongRamp", ground, new Vector3(20f, 2f, 28.8f), new Vector3(4f, .5f, 17f), _wood);
            ramp.transform.localEulerAngles = new Vector3(-15.4f, 0f, 0f);
            Box("RampTopWalk", ground, new Vector3(12f, 4.25f, 36.2f), new Vector3(16f, .5f, 3.2f), _wood);
            Box("FlagPole", ground, new Vector3(0f, 6f, 33f), new Vector3(.15f, 3f, .15f), _wood);
            Box("Flag", ground, new Vector3(.6f, 7f, 33f), new Vector3(1.1f, .7f, .05f), _lamp, false);
            Sign(new Vector3(14f, 3.2f, -10f), "B: วางลังในตะกร้า\nเพื่อนปีนขึ้นไปกด E ค้างที่รอก\n(ต้องมี 2 คนขึ้นไป)");

            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!scenes.Exists(s => s.path == ScenePath)) { scenes.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            return "[ClimbChallenge] test scene " + ScenePath;
        }

        private static void Link(GameObject targetRoot, ClimbWallSwitch source)
        {
            foreach (var t in targetRoot.GetComponentsInChildren<SwitchTarget>(true))
            {
                var so = new SerializedObject(t);
                so.FindProperty("_source").objectReferenceValue = source;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static GameObject Instantiate(string path, Vector3 position, Quaternion rotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        private static void Sign(Vector3 position, string text)
        {
            var go = new GameObject("Sign");
            go.transform.position = position;
            Box("Board", go.transform, new Vector3(0f, 0f, .06f), new Vector3(4.6f, 1.6f, .08f), _wood, false);
            Box("Post", go.transform, new Vector3(0f, -1.7f, .1f), new Vector3(.15f, 2f, .15f), _wood);
            var label = Label(go.transform, Vector3.zero, text);
            label.characterSize = .026f;
        }

        // ───────────────────────── Helpers ─────────────────────────

        private static void Grips(Transform parent, float width, float height, float faceZ)
        {
            var grips = new GameObject("Grips").transform;
            grips.SetParent(parent, false);
            var rng = new System.Random(23);
            for (float y = .9f; y < height - .4f; y += .75f)
                for (float x = -width * .5f + .5f; x < width * .5f - .3f; x += .9f)
                {
                    float jx = (float)rng.NextDouble() * .4f - .2f, jy = (float)rng.NextDouble() * .3f - .15f;
                    Box("Grip", grips, new Vector3(x + jx, y + jy, faceZ - .06f), new Vector3(.32f, .16f, .14f), _grip, false,
                        new Vector3(0f, 0f, (float)rng.NextDouble() * 30f - 15f));
                }
        }

        private static void Stretch(Transform t, Vector3 a, Vector3 b)
        {
            Vector3 d = b - a;
            t.localPosition = (a + b) * .5f;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d);
            t.localScale = new Vector3(.06f, d.magnitude * .5f, .06f);
        }

        private static TextMesh Label(Transform parent, Vector3 position, string text)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.fontSize = 112; mesh.characterSize = .035f;
            mesh.anchor = TextAnchor.MiddleCenter; mesh.alignment = TextAlignment.Center; mesh.color = Color.white;
            return mesh;
        }

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
            Object.DestroyImmediate(root);
            return ok ? "[ClimbChallenge] built " + path : "[ClimbChallenge] FAILED " + path;
        }

        private static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(Medieval + name + ".mat");

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

        private static GameObject Sphere(string name, Transform parent, Vector3 position, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Cylinder(string name, Transform parent, Vector3 position, Vector3 scale, Material mat, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }
    }
}
#endif
