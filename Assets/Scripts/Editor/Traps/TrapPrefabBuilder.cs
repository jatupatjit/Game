#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using CoopGame.Traps;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools.Traps
{
    /// <summary>
    /// Builds the always-on trap prefabs (Assets/Prefabs/Traps) from simple shapes, the remakes of the old timed hazards
    /// (no warning colours, never resting), and the TrapGym showcase scene. Safe to run again: it overwrites only its own
    /// prefabs and scene. It never edits the old prefabs in Assets/Prefabs/Hazards or any level scene.
    /// </summary>
    public static class TrapPrefabBuilder
    {
        private const string Root = "Assets/Prefabs/Traps";
        private const string NewFolder = Root + "/New";
        private const string ObstacleFolder = Root + "/Obstacles";
        private const string CoopFolder = Root + "/Coop";
        private const string RemakeFolder = Root + "/Remake";
        private const string MaterialFolder = Root + "/Materials";
        private const string GymScene = "Assets/Scenes/Test/TrapGym.unity";
        private const string MedievalMaterials = "Assets/Prefabs/Hazards/Medieval/Materials/";
        private const int WallLayer = 3;

        private static Material _wood, _woodLight, _iron, _stone, _ember, _rock, _ice, _bumper, _dark, _ground, _button;
        private static PhysicsMaterial _slopePhysics, _icePhysics, _screePhysics;

        [MenuItem("Tools/Traps/Build All Trap Prefabs")]
        public static void BuildAllMenu() => Debug.Log(BuildAll());

        [MenuItem("Tools/Traps/Build TrapGym Scene")]
        public static void BuildGymMenu() => Debug.Log(BuildGym());

        public static string BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[TrapPrefabBuilder] Stop Play mode first.";
            EnsureFolders();
            LoadMaterials();
            var made = new List<string>();
            // Build in a throw-away scene so the open level is never touched or marked dirty.
            var previous = EditorSceneManager.GetActiveScene();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(temp);
            try
            {
                made.AddRange(BuildNewTraps());
                made.AddRange(BuildObstacles());
                made.Add(BuildCoopLift());
                made.AddRange(BuildRemakes());
            }
            finally
            {
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(temp, true);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return "[TrapPrefabBuilder] Built " + made.Count + " prefabs:\n" + string.Join("\n", made);
        }

        // ───────────────────────── Setup ─────────────────────────

        private static void EnsureFolders()
        {
            foreach (var path in new[] { Root, NewFolder, ObstacleFolder, CoopFolder, RemakeFolder, MaterialFolder, "Assets/Scenes/Test" })
            {
                if (AssetDatabase.IsValidFolder(path)) continue;
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            }
        }

        private static void LoadMaterials()
        {
            _wood = Load("Medieval_Wood", new Color(.45f, .3f, .17f));
            _woodLight = Load("Medieval_WoodLight", new Color(.66f, .48f, .3f));
            _iron = Load("Medieval_Iron", new Color(.35f, .37f, .4f));
            _stone = Load("Medieval_Stone", new Color(.55f, .55f, .53f));
            _ember = Load("Medieval_Ember", new Color(1f, .45f, .1f));
            _rock = MakeMaterial("Trap_Rock", new Color(.42f, .4f, .38f), .15f);
            _ice = MakeMaterial("Trap_Ice", new Color(.72f, .9f, 1f), .9f);
            _bumper = MakeMaterial("Trap_Bumper", new Color(.92f, .88f, .8f), .5f);
            _dark = MakeMaterial("Trap_Belt", new Color(.16f, .17f, .2f), .3f);
            _ground = MakeMaterial("Trap_GymGround", new Color(.47f, .6f, .38f), .1f);
            _button = MakeMaterial("Trap_SwitchButton", new Color(1f, .6f, .1f), .4f);
            _slopePhysics = MakePhysics("Trap_Slope", .35f, .45f, PhysicsMaterialCombine.Average);
            _screePhysics = MakePhysics("Trap_Scree", .18f, .22f, PhysicsMaterialCombine.Minimum);
            _icePhysics = MakePhysics("Trap_Ice", .02f, .02f, PhysicsMaterialCombine.Minimum);
        }

        private static Material Load(string name, Color fallback)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MedievalMaterials + name + ".mat");
            return mat != null ? mat : MakeMaterial("Trap_" + name, fallback, .2f);
        }

        private static Material MakeMaterial(string name, Color color, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        private static PhysicsMaterial MakePhysics(string name, float dynamicFriction, float staticFriction, PhysicsMaterialCombine combine)
        {
            string path = MaterialFolder + "/" + name + ".physicMaterial";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (mat != null) return mat;
            mat = new PhysicsMaterial(name)
            {
                dynamicFriction = dynamicFriction, staticFriction = staticFriction,
                frictionCombine = combine, bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        // ───────────────────────── Shape helpers ─────────────────────────

        private static GameObject Node(string name, Transform parent, Vector3 position, Vector3 euler = default)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            return go;
        }

        private static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 position, Vector3 scale,
            Material mat, Vector3 euler = default, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (!collider) Object.DestroyImmediate(col);
            else if (type == PrimitiveType.Cylinder)
            {
                // The primitive cylinder has a capsule collider (round ends); flat discs and logs need the real shape.
                Object.DestroyImmediate(col);
                var mesh = go.AddComponent<MeshCollider>();
                mesh.convex = true;
            }
            return go;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, Material mat, Vector3 euler = default, bool collider = true)
            => Shape(PrimitiveType.Cube, name, parent, position, scale, mat, euler, collider);

        private static Rigidbody Kinematic(GameObject go)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return rb;
        }

        private static void Set(Object target, params (string name, object value)[] values)
        {
            var so = new SerializedObject(target);
            foreach (var (name, value) in values)
            {
                var p = so.FindProperty(name);
                if (p == null) throw new Exception(target.GetType().Name + " has no field " + name);
                switch (value)
                {
                    case null: p.objectReferenceValue = null; break;
                    case float f: p.floatValue = f; break;
                    case bool b: p.boolValue = b; break;
                    case int i:
                        if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = i; else p.intValue = i;
                        break;
                    case Enum e: p.enumValueIndex = Convert.ToInt32(e); break;
                    case string s: p.stringValue = s; break;
                    case Vector3 v3: p.vector3Value = v3; break;
                    case Vector2 v2: p.vector2Value = v2; break;
                    case Object[] arr:
                        p.arraySize = arr.Length;
                        for (int k = 0; k < arr.Length; k++) p.GetArrayElementAtIndex(k).objectReferenceValue = arr[k];
                        break;
                    case Object o: p.objectReferenceValue = o; break;
                    default: throw new Exception("Unsupported value for " + name);
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TrapMotor Motor(GameObject go, TrapMotor.MotionMode mode, params (string, object)[] values)
        {
            if (mode != TrapMotor.MotionMode.Blink && go.GetComponentInChildren<Collider>() != null) Kinematic(go);
            var motor = go.AddComponent<TrapMotor>();
            var all = new List<(string, object)> { ("_mode", mode) };
            all.AddRange(values);
            Set(motor, all.ToArray());
            return motor;
        }

        private static TrapHitbox Hit(GameObject go, TrapHitbox.PushDirection direction, float launch, float upward, int damage,
            string reason, params (string, object)[] extra)
        {
            var hit = go.AddComponent<TrapHitbox>();
            var all = new List<(string, object)>
            {
                ("_direction", direction), ("_launchSpeed", launch), ("_upwardSpeed", upward),
                ("_cargoDamage", damage), ("_damageReason", reason),
            };
            all.AddRange(extra);
            Set(hit, all.ToArray());
            return hit;
        }

        private static string Save(GameObject root, string folder)
        {
            string path = folder + "/" + root.name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool ok);
            Object.DestroyImmediate(root);
            if (!ok) throw new Exception("Could not save " + path);
            return path;
        }

        // ───────────────────────── A: always-on traps ─────────────────────────

        private static IEnumerable<string> BuildNewTraps()
        {
            // A1 Sweeper: knee-high bar spinning round a post.
            {
                var root = new GameObject("Trap_Sweeper");
                Shape(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0, .2f, 0), new Vector3(1.4f, .2f, 1.4f), _stone);
                Shape(PrimitiveType.Cylinder, "Post", root.transform, new Vector3(0, .9f, 0), new Vector3(.5f, .9f, .5f), _iron);
                var pivot = Node("Pivot", root.transform, new Vector3(0, .55f, 0));
                var bar = Box("Bar", pivot.transform, Vector3.zero, new Vector3(9f, .35f, .35f), _wood);
                Hit(bar, TrapHitbox.PushDirection.Motion, 7f, 3.5f, 10, "Sweeper bar");
                Motor(pivot, TrapMotor.MotionMode.Spin, ("_axis", Vector3.up), ("_degreesPerSecond", 170f));
                yield return Save(root, NewFolder);
            }
            // A2 Twin sweeper: knee bar and chest bar turning opposite ways at speeds that never line up.
            {
                var root = new GameObject("Trap_TwinSweeper");
                Shape(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0, .2f, 0), new Vector3(1.4f, .2f, 1.4f), _stone);
                Shape(PrimitiveType.Cylinder, "Post", root.transform, new Vector3(0, 1.4f, 0), new Vector3(.5f, 1.4f, .5f), _iron);
                var low = Node("PivotLow", root.transform, new Vector3(0, .5f, 0));
                Hit(Box("LowBar", low.transform, Vector3.zero, new Vector3(8f, .32f, .32f), _wood), TrapHitbox.PushDirection.Motion, 6f, 3.5f, 10, "Twin sweeper");
                Motor(low, TrapMotor.MotionMode.Spin, ("_axis", Vector3.up), ("_degreesPerSecond", 150f));
                var high = Node("PivotHigh", root.transform, new Vector3(0, 1.55f, 0));
                Hit(Box("HighBar", high.transform, Vector3.zero, new Vector3(6f, .32f, .32f), _woodLight), TrapHitbox.PushDirection.Motion, 8f, 2.5f, 10, "Twin sweeper");
                Motor(high, TrapMotor.MotionMode.Spin, ("_axis", Vector3.up), ("_degreesPerSecond", -97f));
                yield return Save(root, NewFolder);
            }
            // A3 Pendulum axe swinging across the path (path runs along Z).
            {
                var root = new GameObject("Trap_Pendulum");
                Box("PostL", root.transform, new Vector3(-2.7f, 3f, 0), new Vector3(.4f, 6f, .4f), _wood);
                Box("PostR", root.transform, new Vector3(2.7f, 3f, 0), new Vector3(.4f, 6f, .4f), _wood);
                Box("Beam", root.transform, new Vector3(0, 6.1f, 0), new Vector3(5.8f, .4f, .5f), _wood);
                var pivot = Node("Pivot", root.transform, new Vector3(0, 5.85f, 0));
                Box("Arm", pivot.transform, new Vector3(0, -2.1f, 0), new Vector3(.15f, 4.2f, .15f), _iron, default, false);
                Hit(Box("Blade", pivot.transform, new Vector3(0, -4.5f, 0), new Vector3(1.7f, 1.1f, .5f), _iron), TrapHitbox.PushDirection.Motion, 9f, 3f, 15, "Pendulum axe");
                Motor(pivot, TrapMotor.MotionMode.Swing, ("_axis", Vector3.forward), ("_swingAngle", 70f), ("_period", 1.5f));
                yield return Save(root, NewFolder);
            }
            // A4 Piston wall: logs punch out of a wall into the path (+X).
            {
                var root = new GameObject("Trap_PistonWall");
                Box("Wall", root.transform, new Vector3(-.5f, 1.5f, 0), new Vector3(1f, 3f, 8.5f), _stone);
                float[] z = { -2.8f, 0f, 2.8f };
                float[] periods = { 1.3f, 1.1f, 1.6f };
                for (int i = 0; i < 3; i++)
                {
                    var piston = Node("Piston_" + (i + 1), root.transform, new Vector3(-1.15f, i == 1 ? 1.5f : .9f, z[i]));
                    Hit(Box("Head", piston.transform, Vector3.zero, new Vector3(2.2f, .9f, 1.1f), _woodLight), TrapHitbox.PushDirection.Motion, 9f, 3f, 12, "Piston wall");
                    Motor(piston, TrapMotor.MotionMode.Piston, ("_travel", new Vector3(2.3f, 0, 0)), ("_period", periods[i]),
                        ("_outPortion", .1f), ("_holdPortion", .12f), ("_phaseOffset", i * .43f));
                }
                yield return Save(root, NewFolder);
            }
            // A5 Spinning post with four arms at chest height.
            {
                var root = new GameObject("Trap_SpinPost");
                Shape(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0, .2f, 0), new Vector3(1.4f, .2f, 1.4f), _stone);
                Shape(PrimitiveType.Cylinder, "Post", root.transform, new Vector3(0, 1.2f, 0), new Vector3(.5f, 1.2f, .5f), _iron);
                var pivot = Node("Pivot", root.transform, new Vector3(0, 1.35f, 0));
                for (int i = 0; i < 4; i++)
                {
                    var arm = Node("Arm_" + (i + 1), pivot.transform, Vector3.zero, new Vector3(0, i * 90f, 0));
                    Box("Rod", arm.transform, new Vector3(1.5f, 0, 0), new Vector3(3f, .25f, .25f), _wood, default, false);
                    Hit(Box("Head", arm.transform, new Vector3(3f, 0, 0), new Vector3(.75f, .75f, .75f), _iron), TrapHitbox.PushDirection.Motion, 9f, 3f, 12, "Spin post");
                }
                Motor(pivot, TrapMotor.MotionMode.Spin, ("_axis", Vector3.up), ("_degreesPerSecond", 220f));
                yield return Save(root, NewFolder);
            }
            // A6 Turntable with bumps that sweep people off.
            {
                var root = new GameObject("Trap_Turntable");
                Shape(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0, .1f, 0), new Vector3(8.4f, .1f, 8.4f), _stone);
                var pivot = Node("Pivot", root.transform, new Vector3(0, .35f, 0));
                Shape(PrimitiveType.Cylinder, "Disc", pivot.transform, Vector3.zero, new Vector3(8f, .15f, 8f), _woodLight);
                Hit(Shape(PrimitiveType.Cylinder, "CentrePost", pivot.transform, new Vector3(0, .7f, 0), new Vector3(.6f, .6f, .6f), _iron), TrapHitbox.PushDirection.AwayFromCenter, 4f, 2f, 5, "Turntable");
                Hit(Box("BumpA", pivot.transform, new Vector3(2.4f, .45f, 0), new Vector3(.4f, .6f, 2.2f), _wood), TrapHitbox.PushDirection.Motion, 5f, 2.5f, 8, "Turntable", ("_minPartSpeed", .4f));
                Hit(Box("BumpB", pivot.transform, new Vector3(-1.2f, .45f, -2.1f), new Vector3(2.2f, .6f, .4f), _wood, new Vector3(0, 30f, 0)), TrapHitbox.PushDirection.Motion, 5f, 2.5f, 8, "Turntable", ("_minPartSpeed", .4f));
                Motor(pivot, TrapMotor.MotionMode.Spin, ("_axis", Vector3.up), ("_degreesPerSecond", 65f));
                yield return Save(root, NewFolder);
            }
            // A7 Spike roller spinning in place across the path.
            {
                var root = new GameObject("Trap_SpikeRoller");
                Box("PostL", root.transform, new Vector3(-2.8f, .7f, 0), new Vector3(.4f, 1.4f, .6f), _wood);
                Box("PostR", root.transform, new Vector3(2.8f, .7f, 0), new Vector3(.4f, 1.4f, .6f), _wood);
                var pivot = Node("Pivot", root.transform, new Vector3(0, .95f, 0));
                var log = Shape(PrimitiveType.Cylinder, "Log", pivot.transform, Vector3.zero, new Vector3(1.1f, 2.6f, 1.1f), _wood, new Vector3(0, 0, 90f));
                Hit(log, TrapHitbox.PushDirection.Motion, 7f, 4f, 18, "Spike roller", ("_margin", .4f), ("_minPartSpeed", .3f));
                for (int ring = 0; ring < 5; ring++)
                    for (int k = 0; k < 6; k++)
                    {
                        float angle = k * 60f + ring * 30f;
                        Vector3 dir = Quaternion.Euler(angle, 0, 0) * Vector3.up;
                        Box("Spike", pivot.transform, new Vector3(-2f + ring, 0, 0) + dir * .65f, new Vector3(.18f, .45f, .18f), _iron,
                            new Vector3(angle, 0, 0), false);
                    }
                Motor(pivot, TrapMotor.MotionMode.Spin, ("_axis", Vector3.right), ("_degreesPerSecond", -200f));
                yield return Save(root, NewFolder);
            }
            // A8 Wall saw running up and down a climbable wall.
            {
                var root = new GameObject("Trap_WallSaw");
                var wall = Box("ClimbWall", root.transform, new Vector3(0, 3.5f, 0), new Vector3(6f, 7f, .6f), _stone);
                wall.layer = WallLayer;
                Box("Groove", root.transform, new Vector3(0, 3.5f, -.29f), new Vector3(.5f, 6.4f, .05f), _dark, default, false);
                var saw = Node("Saw", root.transform, new Vector3(0, .9f, -.4f));
                Hit(Box("SawHit", saw.transform, Vector3.zero, new Vector3(1.6f, 1.6f, .35f), null), TrapHitbox.PushDirection.Motion, 6f, 1f, 15, "Wall saw", ("_minPartSpeed", .2f));
                Object.DestroyImmediate(saw.transform.Find("SawHit").GetComponent<MeshRenderer>());
                var blade = Shape(PrimitiveType.Cylinder, "Blade", saw.transform, Vector3.zero, new Vector3(1.6f, .04f, 1.6f), _iron, new Vector3(90f, 0, 0), false);
                Motor(blade, TrapMotor.MotionMode.Spin, ("_axis", Vector3.forward), ("_degreesPerSecond", 720f), ("_randomPhaseFromPosition", false));
                Motor(saw, TrapMotor.MotionMode.PingPong, ("_travel", new Vector3(0, 5.2f, 0)), ("_period", 2.4f));
                yield return Save(root, NewFolder);
            }
            // A9 Wind fan blowing along +Z without pause.
            {
                var root = new GameObject("Trap_WindFan");
                Box("Housing", root.transform, new Vector3(0, 1.5f, -.5f), new Vector3(3.2f, 3.2f, 1f), _iron);
                var blades = Node("Blades", root.transform, new Vector3(0, 1.5f, .05f));
                Box("BladeA", blades.transform, Vector3.zero, new Vector3(2.8f, .35f, .08f), _woodLight, default, false);
                Box("BladeB", blades.transform, Vector3.zero, new Vector3(.35f, 2.8f, .08f), _woodLight, default, false);
                Motor(blades, TrapMotor.MotionMode.Spin, ("_axis", Vector3.forward), ("_degreesPerSecond", 900f));
                var zone = Node("WindZone", root.transform, new Vector3(0, 1.5f, 5.2f));
                zone.AddComponent<BoxCollider>().size = new Vector3(3.2f, 3.2f, 10f);
                zone.AddComponent<TrapPushZone>();
                yield return Save(root, NewFolder);
            }
            // A10 Conveyor belt pulling backwards (-Z).
            {
                var root = new GameObject("Trap_Conveyor");
                Box("Belt", root.transform, new Vector3(0, .15f, 0), new Vector3(3f, .3f, 10f), _dark);
                var ridges = Node("Ridges", root.transform, new Vector3(0, .31f, 0));
                for (int i = 0; i < 10; i++)
                {
                    var ridge = Box("Ridge_" + i, ridges.transform, new Vector3(0, 0, 4.5f - i), new Vector3(2.9f, .04f, .12f), _iron, default, false);
                    Motor(ridge, TrapMotor.MotionMode.Loop, ("_travel", new Vector3(0, 0, -1f)), ("_period", .4f), ("_randomPhaseFromPosition", false));
                }
                var zone = Node("PullZone", root.transform, new Vector3(0, 1.05f, 0));
                zone.AddComponent<BoxCollider>().size = new Vector3(3f, 1.5f, 10f);
                var push = zone.AddComponent<TrapPushZone>();
                Set(push, ("_direction", Vector3.back), ("_playerAcceleration", 14f), ("_controllerDrift", 2.5f), ("_cargoAcceleration", 10f));
                yield return Save(root, NewFolder);
            }
            // A11 Bumper post: touch it and bounce off.
            {
                var root = new GameObject("Trap_Bumper");
                Shape(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0, .05f, 0), new Vector3(1.9f, .05f, 1.9f), _iron);
                var body = Node("Body", root.transform, new Vector3(0, .85f, 0));
                Hit(Shape(PrimitiveType.Cylinder, "Post", body.transform, Vector3.zero, new Vector3(1.4f, .8f, 1.4f), _bumper), TrapHitbox.PushDirection.AwayFromCenter, 8f, 3f, 8, "Bumper", ("_margin", .18f));
                Motor(body, TrapMotor.MotionMode.PingPong, ("_travel", new Vector3(0, .12f, 0)), ("_period", .35f));
                yield return Save(root, NewFolder);
            }
            // A12 Slam doors: a double door slamming open and shut.
            {
                var root = new GameObject("Trap_SlamDoor");
                Box("PostL", root.transform, new Vector3(-3.2f, 1.6f, 0), new Vector3(.4f, 3.2f, .4f), _wood);
                Box("PostR", root.transform, new Vector3(3.2f, 1.6f, 0), new Vector3(.4f, 3.2f, .4f), _wood);
                Box("Lintel", root.transform, new Vector3(0, 3.35f, 0), new Vector3(6.8f, .3f, .4f), _wood);
                var left = Node("HingeL", root.transform, new Vector3(-3f, 1.45f, 0));
                Hit(Box("Door", left.transform, new Vector3(1.48f, 0, 0), new Vector3(2.9f, 2.7f, .2f), _woodLight), TrapHitbox.PushDirection.Motion, 8f, 2.5f, 12, "Slam door");
                Motor(left, TrapMotor.MotionMode.Swing, ("_axis", Vector3.up), ("_swingAngle", 45f), ("_swingCenter", -45f), ("_period", 1.2f), ("_randomPhaseFromPosition", false));
                var right = Node("HingeR", root.transform, new Vector3(3f, 1.45f, 0));
                Hit(Box("Door", right.transform, new Vector3(-1.48f, 0, 0), new Vector3(2.9f, 2.7f, .2f), _woodLight), TrapHitbox.PushDirection.Motion, 8f, 2.5f, 12, "Slam door");
                Motor(right, TrapMotor.MotionMode.Swing, ("_axis", Vector3.up), ("_swingAngle", 45f), ("_swingCenter", 45f), ("_period", 1.2f), ("_randomPhaseFromPosition", false));
                yield return Save(root, NewFolder);
            }
        }

        // ───────────────────────── B: ragdoll obstacles ─────────────────────────

        private static IEnumerable<string> BuildObstacles()
        {
            // B1 Boulder chute: rocks bounce down a 20° ramp, one after another.
            {
                var root = new GameObject("Obstacle_BoulderChute");
                var ramp = Node("Ramp", root.transform, Vector3.zero, new Vector3(-20f, 0, 0));
                Box("Surface", ramp.transform, new Vector3(0, -.25f, 9f), new Vector3(4f, .5f, 18f), _stone).GetComponent<Collider>().sharedMaterial = _slopePhysics;
                Box("RailL", ramp.transform, new Vector3(-2.15f, .3f, 9f), new Vector3(.3f, .6f, 18f), _wood);
                Box("RailR", ramp.transform, new Vector3(2.15f, .3f, 9f), new Vector3(.3f, .6f, 18f), _wood);
                var start = Node("RockStart", ramp.transform, new Vector3(0, 0, 17.3f));
                var end = Node("RockEnd", ramp.transform, new Vector3(0, 0, -1.5f));
                var rocks = Node("Rocks", root.transform, Vector3.zero);
                var bodies = new List<Object>();
                for (int i = 0; i < 5; i++)
                {
                    var rock = Shape(PrimitiveType.Sphere, "Rock_" + i, rocks.transform, new Vector3(0, -50f, 0), Vector3.one, _rock);
                    bodies.Add(Kinematic(rock));
                    Hit(rock, TrapHitbox.PushDirection.Motion, 7f, 3f, 12, "Rolling rock");
                }
                Set(root.AddComponent<TrapRockStream>(), ("_mode", TrapRockStream.StreamMode.Roll), ("_rocks", bodies.ToArray()),
                    ("_start", start.transform), ("_end", end.transform), ("_interval", 1.6f), ("_jitter", .6f), ("_rollSeconds", 3.2f));
                yield return Save(root, ObstacleFolder);
            }
            // B2 Steep slopes at 25°, 35° and 45°.
            foreach (int angle in new[] { 25, 35, 45 })
            {
                var root = new GameObject("Obstacle_SteepSlope_" + angle);
                float length = 10f;
                var ramp = Node("Ramp", root.transform, Vector3.zero, new Vector3(-angle, 0, 0));
                Box("Surface", ramp.transform, new Vector3(0, -.25f, length * .5f), new Vector3(4f, .5f, length), _stone).GetComponent<Collider>().sharedMaterial = _slopePhysics;
                float topY = Mathf.Sin(angle * Mathf.Deg2Rad) * length;
                float topZ = Mathf.Cos(angle * Mathf.Deg2Rad) * length;
                Box("TopLanding", root.transform, new Vector3(0, topY - .25f, topZ + 2f), new Vector3(4f, .5f, 4f), _stone);
                yield return Save(root, ObstacleFolder);
            }
            // B3 Loose scree slope: slippery 30° ramp covered in small rocks.
            {
                var root = new GameObject("Obstacle_ScreeSlope");
                var ramp = Node("Ramp", root.transform, Vector3.zero, new Vector3(-30f, 0, 0));
                Box("Surface", ramp.transform, new Vector3(0, -.25f, 5f), new Vector3(4f, .5f, 10f), _rock).GetComponent<Collider>().sharedMaterial = _screePhysics;
                for (int i = 0; i < 28; i++)
                {
                    float x = (TrapClock.Hash01(i, 1, 0, 5) - .5f) * 3.6f;
                    float z = .5f + TrapClock.Hash01(i, 2, 0, 5) * 9f;
                    float s = .25f + TrapClock.Hash01(i, 3, 0, 5) * .25f;
                    Shape(PrimitiveType.Sphere, "Pebble", ramp.transform, new Vector3(x, 0, z), new Vector3(s, s * .6f, s), _stone).GetComponent<Collider>().sharedMaterial = _screePhysics;
                }
                yield return Save(root, ObstacleFolder);
            }
            // B4 Cliff ledge: 1 m wide path along a cliff face over a deep drop.
            {
                var root = new GameObject("Obstacle_CliffLedge");
                var wall = Box("CliffWall", root.transform, new Vector3(-1.25f, 4f, 0), new Vector3(1.5f, 8f, 12f), _rock);
                wall.layer = WallLayer;
                Box("Ledge", root.transform, new Vector3(0, -.2f, 0), new Vector3(1f, .4f, 12f), _stone);
                Box("CliffBelow", root.transform, new Vector3(-.75f, -10.4f, 0), new Vector3(2.5f, 20f, 12f), _rock);
                yield return Save(root, ObstacleFolder);
            }
            // B5 Chasm gaps of 2, 3 and 4 metres between two pillars.
            foreach (int gap in new[] { 2, 3, 4 })
            {
                var root = new GameObject("Obstacle_ChasmGap_" + gap + "m");
                Box("Near", root.transform, new Vector3(0, -10f, -2.5f - gap * .5f), new Vector3(5f, 20f, 5f), _rock);
                Box("Far", root.transform, new Vector3(0, -10f, 2.5f + gap * .5f), new Vector3(5f, 20f, 5f), _rock);
                yield return Save(root, ObstacleFolder);
            }
            // B6 Rolling log bridge over a gap.
            {
                var root = new GameObject("Obstacle_RollingLogBridge");
                Box("Near", root.transform, new Vector3(0, -.5f, -5.5f), new Vector3(4f, 1f, 3f), _stone);
                Box("Far", root.transform, new Vector3(0, -.5f, 5.5f), new Vector3(4f, 1f, 3f), _stone);
                var pivot = Node("Pivot", root.transform, new Vector3(0, -.45f, 0));
                Shape(PrimitiveType.Cylinder, "Log", pivot.transform, Vector3.zero, new Vector3(1.1f, 4f, 1.1f), _wood, new Vector3(90f, 0, 0));
                Motor(pivot, TrapMotor.MotionMode.Spin, ("_axis", Vector3.forward), ("_degreesPerSecond", 45f));
                yield return Save(root, ObstacleFolder);
            }
            // B7 Wobbly rope bridge: planks keep rocking side to side in a wave.
            {
                var root = new GameObject("Obstacle_WobblyBridge");
                Box("Near", root.transform, new Vector3(0, -.5f, -1.5f), new Vector3(3f, 1f, 3f), _stone);
                Box("Far", root.transform, new Vector3(0, -.5f, 15.5f), new Vector3(3f, 1f, 3f), _stone);
                for (int i = 0; i < 12; i++)
                {
                    float z = .65f + i * 1.2f;
                    float sag = -Mathf.Sin((i + .5f) / 12f * Mathf.PI) * .6f;
                    var plank = Node("Plank_" + (i + 1), root.transform, new Vector3(0, sag - .08f, z));
                    Box("Board", plank.transform, Vector3.zero, new Vector3(2f, .15f, 1.05f), _woodLight);
                    Motor(plank, TrapMotor.MotionMode.Swing, ("_axis", Vector3.forward), ("_swingAngle", 9f), ("_period", 1.6f),
                        ("_phaseOffset", i * .13f), ("_randomPhaseFromPosition", false));
                }
                Box("RopeL", root.transform, new Vector3(-1.1f, .7f, 7f), new Vector3(.06f, .06f, 15f), _wood, default, false);
                Box("RopeR", root.transform, new Vector3(1.1f, .7f, 7f), new Vector3(.06f, .06f, 15f), _wood, default, false);
                yield return Save(root, ObstacleFolder);
            }
            // B8 Falling rocks: rocks drop from above onto a 4 x 10 m path at random spots.
            {
                var root = new GameObject("Obstacle_FallingRocks");
                Box("Path", root.transform, new Vector3(0, -.25f, 0), new Vector3(4f, .5f, 10f), _stone);
                Box("Overhang", root.transform, new Vector3(-2.5f, 11f, 0), new Vector3(3f, 1.5f, 10f), _rock);
                var area = Node("DropArea", root.transform, new Vector3(0, 5f, 0));
                var bodies = new List<Object>();
                for (int i = 0; i < 6; i++)
                {
                    var rock = Shape(PrimitiveType.Sphere, "Rock_" + i, area.transform, new Vector3(0, -50f, 0), Vector3.one, _rock);
                    bodies.Add(Kinematic(rock));
                    Hit(rock, TrapHitbox.PushDirection.Motion, 4f, 1f, 10, "Falling rock", ("_minPartSpeed", 2f));
                }
                Set(area.AddComponent<TrapRockStream>(), ("_mode", TrapRockStream.StreamMode.Drop), ("_rocks", bodies.ToArray()),
                    ("_interval", .7f), ("_jitter", .4f), ("_sizeRange", new Vector2(.6f, 1f)), ("_dropArea", new Vector3(3.6f, 10f, 9f)));
                yield return Save(root, ObstacleFolder);
            }
            // B9 Tilt plate (see-saw that never stops rocking).
            {
                var root = new GameObject("Obstacle_TiltPlate");
                Box("Fulcrum", root.transform, new Vector3(0, .4f, 0), new Vector3(3f, .8f, .8f), _iron, new Vector3(45f, 0, 0));
                var pivot = Node("Pivot", root.transform, new Vector3(0, 1f, 0));
                Box("Board", pivot.transform, Vector3.zero, new Vector3(3f, .3f, 9f), _woodLight);
                Motor(pivot, TrapMotor.MotionMode.Swing, ("_axis", Vector3.right), ("_swingAngle", 12f), ("_period", 3.2f));
                yield return Save(root, ObstacleFolder);
            }
            // B10 Slippery ice patch.
            {
                var root = new GameObject("Obstacle_SlipperyPatch");
                Box("Ice", root.transform, new Vector3(0, .05f, 0), new Vector3(4f, .1f, 6f), _ice).GetComponent<Collider>().sharedMaterial = _icePhysics;
                yield return Save(root, ObstacleFolder);
            }
        }

        // ───────────────────────── Co-op lift ─────────────────────────

        private static string BuildCoopLift()
        {
            // One player climbs the wall (right) and presses E on the button; the lift with the team and crate rises
            // 6.8 m to the upper deck and stays there.
            var root = new GameObject("Coop_ClimbSwitchLift");
            root.AddComponent<NetworkObject>();
            var gate = root.AddComponent<CoopGame.Network.ClimbSwitchGate>();
            var wall = Box("ClimbWall", root.transform, new Vector3(5f, 3.5f, 0), new Vector3(3f, 7f, 1f), _stone);
            wall.layer = WallLayer;
            Box("SwitchLedge", root.transform, new Vector3(5f, 7.15f, -.5f), new Vector3(3f, .3f, 2f), _stone);
            var button = Box("SwitchButton", root.transform, new Vector3(5f, 7.05f, -1.6f), new Vector3(.6f, .35f, .25f), _button);
            Box("LiftShaftBack", root.transform, new Vector3(0, 3.5f, 2.3f), new Vector3(4.4f, 7f, .4f), _wood);
            var platform = Box("LiftPlatform", root.transform, new Vector3(0, .2f, 0), new Vector3(4f, .4f, 4f), _woodLight);
            Kinematic(platform);
            Box("UpperDeck", root.transform, new Vector3(0, 7f, -6.2f), new Vector3(4f, .4f, 8f), _stone);
            Set(gate, ("_button", button.transform), ("_indicator", button.GetComponent<Renderer>()));
            Set(root.AddComponent<CoopSwitchLift>(), ("_switch", gate), ("_platform", platform.GetComponent<Rigidbody>()),
                ("_rise", new Vector3(0, 6.8f, 0)), ("_speed", 1.2f));
            return Save(root, CoopFolder);
        }

        // ───────────────────────── Remakes of the old hazards ─────────────────────────

        private static readonly string[] TelegraphNames =
            { "Warning", "_Status", "ThaiSignBacking", "WaitHereTip", "SafeWaitingBay", "Tip", "Sign", "Lamp", "CrushZone", "SweepZone", "PulseMarker", "RestoreSafetyVolume" };

        private static IEnumerable<string> BuildRemakes()
        {
            var made = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Hazards" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.name = "Remake_" + source.name;
                instance.SetActive(true); // some old hazard prefabs were saved switched off
                // Several old hazards were switched off part by part; the remake shows every part again
                // (except an unused spare log visual).
                foreach (var part in instance.GetComponentsInChildren<Transform>(true))
                    if (part.name != "RollingLogVisual") part.gameObject.SetActive(true);
                try
                {
                    if (!Remake(instance)) { Object.DestroyImmediate(instance); continue; }
                    made.Add(Save(instance, RemakeFolder));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[TrapPrefabBuilder] Skipped " + source.name + ": " + e.Message);
                    if (instance != null) Object.DestroyImmediate(instance);
                }
            }
            return made;
        }

        private static bool Remake(GameObject root)
        {
            var route = root.GetComponent<CoopGame.Network.NetworkRouteHazard>();
            var log = root.GetComponent<CoopGame.Network.RotatingLogHazard>();
            var timed = root.GetComponent<CoopGame.Network.TimedCargoTrap>();
            var mech = root.GetComponent<CoopGame.Network.MedievalMechanismHazard>();
            var spikes = root.GetComponent<CoopGame.Network.NetworkCargoDamageZone>();
            var stone = root.GetComponent<CoopGame.Network.CollapsingSteppingStone>();

            if (stone != null)
            {
                // Weight-triggered stone: keep its network script, drop the shake warning.
                Set(stone, ("_warningSeconds", .12f), ("_shakeMagnitude", 0f));
                return true;
            }

            if (route != null)
            {
                var so = new SerializedObject(route);
                int kind = so.FindProperty("_kind").enumValueIndex;
                string kindName = so.FindProperty("_kind").enumNames[kind];
                var primary = so.FindProperty("_primary").objectReferenceValue as Rigidbody;
                var secondary = so.FindProperty("_secondary").objectReferenceValue as Rigidbody;
                var hitA = so.FindProperty("_hitA").objectReferenceValue as Collider;
                var hitB = so.FindProperty("_hitB").objectReferenceValue as Collider;
                float swing = so.FindProperty("_swingAngle").floatValue;
                float travel = so.FindProperty("_travel").floatValue;
                float drop = so.FindProperty("_dropDistance").floatValue;
                float scale2 = so.FindProperty("_secondarySwingScale").floatValue;
                bool authored = so.FindProperty("_useAuthoredRestPose").boolValue;
                StripOld(root, route);
                switch (kindName)
                {
                    case "SwingHammer":
                        SwingRemake(root, primary, swing, authored, -1f, 0f, 1.6f);
                        HitOn(hitA, 9f, 3f, 15, "Swing hammer");
                        break;
                    case "DoublePendulum":
                        SwingRemake(root, primary, swing, authored, -1f, 0f, 1.5f);
                        if (secondary != null) SwingRemake(root, secondary, swing * (authored ? 1f : scale2), authored, 1f, .75f, 1.5f);
                        HitOn(hitA, 9f, 3f, 15, "Pendulum");
                        if (hitB != null && hitB != hitA) HitOn(hitB, 9f, 3f, 15, "Pendulum");
                        break;
                    case "Crusher":
                        PistonRemake(root, primary, root.transform.TransformVector(Vector3.right * travel), 1.4f, 0f);
                        if (secondary != null) PistonRemake(root, secondary, root.transform.TransformVector(Vector3.left * travel), 1.4f, 0f);
                        HitOn(hitA, 7f, 4f, 18, "Crusher");
                        if (hitB != null) HitOn(hitB, 7f, 4f, 18, "Crusher");
                        break;
                    default: // CollapsingDeck: the floor drops away fast and comes back, over and over.
                        PistonRemake(root, primary, root.transform.TransformVector(Vector3.down * Mathf.Max(4f, drop)), 2.6f, 0f, .08f, .35f);
                        break;
                }
                return true;
            }

            if (log != null)
            {
                var so = new SerializedObject(log);
                var body = so.FindProperty("_logRigidbody").objectReferenceValue as Rigidbody;
                int axisIndex = so.FindProperty("_axis").enumValueIndex;
                string axisName = so.FindProperty("_axis").enumNames[axisIndex];
                float speed = Mathf.Max(150f, so.FindProperty("_rotationSpeedDegrees").floatValue);
                if (so.FindProperty("_reverseDirection").boolValue) speed = -speed;
                bool authored = so.FindProperty("_useAuthoredRestPose").boolValue;
                var zone = so.FindProperty("_impactZone").objectReferenceValue as Collider;
                StripOld(root, log);
                Vector3 localAxis = axisName == "X" ? Vector3.right : axisName == "Y" ? Vector3.up : Vector3.forward;
                if (body == null) return false;
                Transform t = body.transform;
                if (!authored) t.rotation = root.transform.rotation;
                Vector3 axis = t.parent.InverseTransformDirection(t.rotation * localAxis);
                Motor(t.gameObject, TrapMotor.MotionMode.Spin, ("_axis", axis), ("_degreesPerSecond", speed));
                HitOn(zone, 8f, 3f, 12, "Rotating beam");
                return true;
            }

            if (timed != null)
            {
                var so = new SerializedObject(timed);
                var zone = so.FindProperty("_zone").objectReferenceValue as Collider;
                var flames = so.FindProperty("_flames").objectReferenceValue as GameObject;
                bool isSpikes = root.name.Contains("Spike");
                StripOld(root, timed);
                if (flames == null || zone == null) return false;
                flames.SetActive(true);
                var blinker = Node("Blinker", root.transform, Vector3.zero);
                Motor(blinker, TrapMotor.MotionMode.Blink, ("_period", isSpikes ? .9f : 1f), ("_blinkOnPortion", isSpikes ? .4f : .5f),
                    ("_blinkObjects", new Object[] { flames }));
                var hit = Hit(zone.gameObject, TrapHitbox.PushDirection.Up, isSpikes ? 2f : 3f, isSpikes ? 7f : 6f, 12,
                    isSpikes ? "Spikes" : "Flames", ("_onlyWhileActive", flames));
                return hit != null;
            }

            if (mech != null)
            {
                var so = new SerializedObject(mech);
                string kindName = so.FindProperty("_kind").enumNames[so.FindProperty("_kind").enumValueIndex];
                var bodiesProp = so.FindProperty("_movingBodies");
                var bodies = new List<Rigidbody>();
                for (int i = 0; i < bodiesProp.arraySize; i++) if (bodiesProp.GetArrayElementAtIndex(i).objectReferenceValue is Rigidbody rb) bodies.Add(rb);
                Vector3 travelVector = so.FindProperty("_travelVector").vector3Value;
                float roll = so.FindProperty("_rollDegrees").floatValue;
                var hitZone = so.FindProperty("_hitZone").objectReferenceValue as Collider;
                StripOld(root, mech);
                if (hitZone != null) Object.DestroyImmediate(hitZone.gameObject);
                Vector3 worldTravel = root.transform.TransformVector(travelVector);
                for (int i = 0; i < bodies.Count; i++)
                {
                    Transform t = bodies[i].transform;
                    Vector3 travel = t.parent.InverseTransformVector(worldTravel);
                    if (kindName == "BarrelRamp")
                    {
                        Vector3 axis = t.parent.InverseTransformDirection(root.transform.right);
                        Motor(t.gameObject, TrapMotor.MotionMode.Loop, ("_travel", travel), ("_period", 2f), ("_loopRollDegrees", roll), ("_axis", axis));
                        Hit(t.gameObject, TrapHitbox.PushDirection.Motion, 7f, 3f, 12, "Rolling barrel");
                    }
                    else
                    {
                        Motor(t.gameObject, TrapMotor.MotionMode.Piston, ("_travel", travel), ("_period", 1.2f), ("_outPortion", .1f),
                            ("_holdPortion", .2f), ("_phaseOffset", i * .15f), ("_randomPhaseFromPosition", false));
                        Hit(t.gameObject, TrapHitbox.PushDirection.Up, 3f, 7f, 10, "Rising posts");
                    }
                }
                return true;
            }

            if (spikes != null)
            {
                var zone = new SerializedObject(spikes).FindProperty("_zone").objectReferenceValue as Collider;
                StripOld(root, spikes);
                if (zone == null) return false;
                Hit(zone.gameObject, TrapHitbox.PushDirection.Up, 3f, 6f, 10, "Wooden spikes");
                return true;
            }
            return false;
        }

        private static void StripOld(GameObject root, Component hazard)
        {
            Object.DestroyImmediate(hazard);
            var net = root.GetComponent<NetworkObject>();
            if (net != null) Object.DestroyImmediate(net);
            var doomed = new List<GameObject>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root.transform) continue;
                foreach (string word in TelegraphNames)
                    if (t.name.Contains(word)) { doomed.Add(t.gameObject); break; }
            }
            foreach (var go in doomed) if (go != null) Object.DestroyImmediate(go);
            foreach (var text in root.GetComponentsInChildren<TextMesh>(true)) if (text != null) Object.DestroyImmediate(text.gameObject);
        }

        private static void SwingRemake(GameObject root, Rigidbody body, float swing, bool authored, float authoredSign, float phase, float period)
        {
            if (body == null) return;
            Transform t = body.transform;
            float center = 0f;
            if (authored) center = authoredSign * swing; // old authored pose was one end of the sweep
            else t.rotation = root.transform.rotation;
            Vector3 axis = t.parent.InverseTransformDirection(t.rotation * Vector3.forward);
            if (!authored) axis = t.parent.InverseTransformDirection(root.transform.forward);
            Motor(t.gameObject, TrapMotor.MotionMode.Swing, ("_axis", axis), ("_swingAngle", Mathf.Clamp(swing, 20f, 85f)),
                ("_swingCenter", center), ("_period", period), ("_phaseOffset", phase * period));
        }

        private static void PistonRemake(GameObject root, Rigidbody body, Vector3 worldTravel, float period, float phase,
            float outPortion = .12f, float hold = .1f)
        {
            if (body == null) return;
            Transform t = body.transform;
            Motor(t.gameObject, TrapMotor.MotionMode.Piston, ("_travel", t.parent.InverseTransformVector(worldTravel)), ("_period", period),
                ("_outPortion", outPortion), ("_holdPortion", hold), ("_phaseOffset", phase), ("_randomPhaseFromPosition", false));
        }

        private static void HitOn(Collider collider, float launch, float upward, int damage, string reason)
        {
            if (collider == null || collider.GetComponent<TrapHitbox>() != null) return;
            Hit(collider.gameObject, TrapHitbox.PushDirection.Motion, launch, upward, damage, reason);
        }

        // ───────────────────────── TrapGym showcase scene ─────────────────────────

        public static string BuildGym()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[TrapPrefabBuilder] Stop Play mode first.";
            EnsureFolders();
            LoadMaterials();
            // Built as an extra scene next to whatever is open, saved, then closed: the open level is not touched.
            var previous = EditorSceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);
            int placed = 0;
            try
            {
                Box("Ground", null, new Vector3(45f, -.5f, 45f), new Vector3(150f, 1f, 130f), _ground);
                // Obstacles stand over empty space (z < -15) so cliffs and chasms really drop away.
                var rows = new (string folder, float z, float spacing)[]
                    { (ObstacleFolder, -60f, 14f), (NewFolder, 0f, 14f), (RemakeFolder, 40f, 16f), (CoopFolder, 85f, 16f) };
                foreach (var (folder, z, spacing) in rows)
                {
                    var row = new GameObject("Row_" + Path.GetFileName(folder));
                    var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                    Array.Sort(guids, (a, b) => string.CompareOrdinal(AssetDatabase.GUIDToAssetPath(a), AssetDatabase.GUIDToAssetPath(b)));
                    int column = 0;
                    foreach (string guid in guids)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        instance.transform.SetParent(row.transform, false);
                        instance.transform.position = new Vector3((column % 10) * spacing, 0f, z + (column / 10) * 20f);
                        Label(prefab.name, instance.transform.position + new Vector3(0, 9f, -3f));
                        column++;
                        placed++;
                    }
                }
                foreach (var go in scene.GetRootGameObjects())
                {
                    var cam = go.GetComponent<Camera>();
                    if (cam != null) { cam.transform.position = new Vector3(60f, 45f, -95f); cam.transform.rotation = Quaternion.Euler(30f, -10f, 0f); }
                }
                EditorSceneManager.SaveScene(scene, GymScene);
            }
            finally
            {
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
            return "[TrapPrefabBuilder] TrapGym saved with " + placed + " prefabs at " + GymScene;
        }

        private static void Label(string text, Vector3 position)
        {
            var go = new GameObject("Label_" + text);
            go.transform.position = position;
            var mesh = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) { mesh.font = font; go.GetComponent<MeshRenderer>().sharedMaterial = font.material; }
            mesh.text = text;
            mesh.characterSize = .25f;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.color = Color.white;
        }
    }
}
#endif
