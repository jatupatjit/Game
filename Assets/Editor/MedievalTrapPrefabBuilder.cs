using System;
using System.Collections.Generic;
using CoopGame.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>Creates reusable medieval prototype hazards without changing any scene.</summary>
    public static class MedievalTrapPrefabBuilder
    {
        private const string Root = "Assets/Prefabs/Hazards/Medieval";
        private const string Materials = Root + "/Materials";

        private static Material _wood;
        private static Material _woodLight;
        private static Material _iron;
        private static Material _stone;
        private static Material _warning;
        private static Material _ember;

        [MenuItem("Tools/Coop Game/Rebuild Medieval Trap Prefabs")]
        public static void BuildAll()
        {
            EnsureFolder(Materials);
            CreateMaterials();

            BuildSwingingLog();
            BuildRotatingCrossbar();
            BuildCrusherGate();
            BuildFloorBraziers();
            BuildCollapsingDeck();
            BuildRisingPosts();
            BuildTwinPendulums();
            BuildBarrelRamp();
            BuildWoodenSpikes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MedievalTrapPrefabBuilder] Built 9 editable trap prefabs in " + Root);
        }

        private static void BuildSwingingLog()
        {
            GameObject root = NewRoot("Medieval_SwingingLog");
            AddBlock("TopBeam", root.transform, new Vector3(0f, 3.65f, 0f), new Vector3(5.6f, .35f, .45f), _wood);
            AddBlock("LeftPost", root.transform, new Vector3(-2.55f, 1.8f, 0f), new Vector3(.42f, 3.6f, .42f), _wood);
            AddBlock("RightPost", root.transform, new Vector3(2.55f, 1.8f, 0f), new Vector3(.42f, 3.6f, .42f), _wood);

            GameObject pivot = NewObject("SwingPivot", root.transform, new Vector3(0f, 3.4f, 0f));
            pivot.transform.localRotation = Quaternion.Euler(0f, 0f, 68f);
            Rigidbody body = AddMovingBody(pivot);
            AddCylinder("Rope", pivot.transform, new Vector3(0f, -1.25f, 0f), new Vector3(.12f, 1.25f, .12f), _iron, false);
            GameObject log = AddCylinder("Log", pivot.transform, new Vector3(0f, -2.45f, 0f), new Vector3(.62f, 1.75f, .62f), _wood, true);
            log.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            BoxCollider hit = ReplaceWithBox(log, new Vector3(3.5f, 1.05f, 1.05f));
            Renderer indicator = AddBlock("WarningBand", pivot.transform, new Vector3(0f, -2.45f, 0f), new Vector3(.18f, 1.12f, 1.12f), _warning, false).GetComponent<Renderer>();

            NetworkRouteHazard hazard = root.AddComponent<NetworkRouteHazard>();
            Set(hazard, "_kind", NetworkRouteHazard.HazardKind.SwingHammer);
            Set(hazard, "_primary", body);
            Set(hazard, "_hitA", hit);
            Set(hazard, "_indicator", indicator);
            Set(hazard, "_swingAngle", 68f);
            Set(hazard, "_useAuthoredRestPose", true);
            SetCycle(hazard, 6f, 1.75f, 2.5f, 15);
            Save(root);
        }

        private static void BuildRotatingCrossbar()
        {
            GameObject root = NewRoot("Medieval_RotatingCrossbar");
            AddCylinder("StoneBase", root.transform, new Vector3(0f, .25f, 0f), new Vector3(1.25f, .25f, 1.25f), _stone, true);
            GameObject pivot = NewObject("CrossbarPivot", root.transform, new Vector3(0f, 1.25f, 0f));
            pivot.transform.localRotation = Quaternion.Euler(0f, 45f, 0f);
            Rigidbody body = AddMovingBody(pivot);
            AddCylinder("Axle", pivot.transform, Vector3.zero, new Vector3(.32f, 1.2f, .32f), _iron, true);
            AddBlock("BeamA", pivot.transform, Vector3.zero, new Vector3(7f, .38f, .48f), _wood, true);
            AddBlock("BeamB", pivot.transform, Vector3.zero, new Vector3(.48f, .38f, 7f), _wood, true);
            BoxCollider zone = AddZone("ImpactZone", pivot.transform, Vector3.zero, new Vector3(7f, .8f, 7f));
            Renderer indicator = AddCylinder("WarningCap", pivot.transform, new Vector3(0f, .42f, 0f), new Vector3(.5f, .12f, .5f), _warning, false).GetComponent<Renderer>();

            RotatingLogHazard hazard = root.AddComponent<RotatingLogHazard>();
            Set(hazard, "_logRigidbody", body);
            Set(hazard, "_axis", RotatingLogHazard.RotationAxis.Y);
            Set(hazard, "_safeSeconds", 3.5f);
            Set(hazard, "_warningSeconds", 1.5f);
            Set(hazard, "_sweepSeconds", 4f);
            Set(hazard, "_parkAngle", 45f);
            Set(hazard, "_useAuthoredRestPose", true);
            Set(hazard, "_cargoImpactDamage", 10);
            Set(hazard, "_impactZone", zone);
            Set(hazard, "_indicatorRenderer", indicator);
            Save(root);
        }

        private static void BuildCrusherGate()
        {
            GameObject root = NewRoot("Medieval_CrusherGate");
            AddBlock("Lintel", root.transform, new Vector3(0f, 3.25f, 0f), new Vector3(7.2f, .55f, 1f), _stone);
            AddBlock("LeftFrame", root.transform, new Vector3(-3.35f, 1.6f, 0f), new Vector3(.5f, 3.2f, 1f), _stone);
            AddBlock("RightFrame", root.transform, new Vector3(3.35f, 1.6f, 0f), new Vector3(.5f, 3.2f, 1f), _stone);

            GameObject left = AddBlock("LeftCrusher", root.transform, new Vector3(-2.6f, 1.5f, 0f), new Vector3(1.15f, 2.7f, .9f), _wood, true);
            GameObject right = AddBlock("RightCrusher", root.transform, new Vector3(2.6f, 1.5f, 0f), new Vector3(1.15f, 2.7f, .9f), _wood, true);
            Rigidbody leftBody = AddMovingBody(left);
            Rigidbody rightBody = AddMovingBody(right);
            BoxCollider leftHit = left.GetComponent<BoxCollider>();
            BoxCollider rightHit = right.GetComponent<BoxCollider>();
            Renderer indicator = AddBlock("WarningPlate", root.transform, new Vector3(0f, 3.57f, -.51f), new Vector3(1.8f, .12f, .08f), _warning, false).GetComponent<Renderer>();

            NetworkRouteHazard hazard = root.AddComponent<NetworkRouteHazard>();
            Set(hazard, "_kind", NetworkRouteHazard.HazardKind.Crusher);
            Set(hazard, "_primary", leftBody);
            Set(hazard, "_secondary", rightBody);
            Set(hazard, "_hitA", leftHit);
            Set(hazard, "_hitB", rightHit);
            Set(hazard, "_indicator", indicator);
            Set(hazard, "_primaryRest", left.transform.localPosition);
            Set(hazard, "_secondaryRest", right.transform.localPosition);
            Set(hazard, "_travel", 2.05f);
            SetCycle(hazard, 5f, 1.5f, 2.25f, 15);
            Save(root);
        }

        private static void BuildFloorBraziers()
        {
            GameObject root = NewRoot("Medieval_FloorBraziers");
            AddBlock("StoneLane", root.transform, new Vector3(0f, .12f, 0f), new Vector3(5.5f, .24f, 5f), _stone);
            GameObject flames = NewObject("Flames", root.transform, Vector3.zero);
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
            {
                AddCylinder("Brazier", root.transform, new Vector3(x * 1.6f, .34f, z * 1.45f), new Vector3(.52f, .22f, .52f), _iron, true);
                AddCylinder("Flame", flames.transform, new Vector3(x * 1.6f, .85f, z * 1.45f), new Vector3(.3f, .72f, .3f), _ember, false);
            }
            BoxCollider zone = AddZone("DamageZone", root.transform, new Vector3(0f, .8f, 0f), new Vector3(5.2f, 1.6f, 4.7f));
            Renderer marker = AddBlock("WarningStrip", root.transform, new Vector3(0f, .27f, -2.3f), new Vector3(4.8f, .08f, .22f), _warning, false).GetComponent<Renderer>();

            TimedCargoTrap hazard = root.AddComponent<TimedCargoTrap>();
            Set(hazard, "_zone", zone);
            Set(hazard, "_flames", flames);
            Set(hazard, "_marker", marker);
            Set(hazard, "_safeSeconds", 4f);
            Set(hazard, "_warningSeconds", 1.5f);
            Set(hazard, "_activeSeconds", 1.75f);
            Set(hazard, "_damage", 10);
            Save(root);
        }

        private static void BuildCollapsingDeck()
        {
            GameObject root = NewRoot("Medieval_CollapsingDeck");
            AddBlock("LeftStone", root.transform, new Vector3(-3.65f, -.05f, 0f), new Vector3(1.3f, .7f, 4.2f), _stone);
            AddBlock("RightStone", root.transform, new Vector3(3.65f, -.05f, 0f), new Vector3(1.3f, .7f, 4.2f), _stone);
            GameObject deck = AddBlock("DropDeck", root.transform, new Vector3(0f, .15f, 0f), new Vector3(6.1f, .28f, 4f), _wood, true);
            Rigidbody body = AddMovingBody(deck);
            BoxCollider floor = deck.GetComponent<BoxCollider>();
            BoxCollider restore = AddZone("RestoreSafetyVolume", root.transform, new Vector3(0f, -.35f, 0f), new Vector3(6.2f, 1.5f, 4.1f));
            Renderer indicator = AddBlock("WarningStrip", root.transform, new Vector3(0f, .34f, -1.6f), new Vector3(4.6f, .12f, .2f), _warning, false).GetComponent<Renderer>();

            NetworkRouteHazard hazard = root.AddComponent<NetworkRouteHazard>();
            Set(hazard, "_kind", NetworkRouteHazard.HazardKind.CollapsingDeck);
            Set(hazard, "_primary", body);
            Set(hazard, "_hitA", floor);
            Set(hazard, "_restoreVolume", restore);
            Set(hazard, "_indicator", indicator);
            Set(hazard, "_surface", deck.GetComponent<Renderer>());
            Set(hazard, "_primaryRest", deck.transform.localPosition);
            SetCycle(hazard, 5f, 1.5f, 2.5f, 0);
            Save(root);
        }

        private static void BuildRisingPosts()
        {
            GameObject root = NewRoot("Medieval_RisingPosts");
            AddBlock("StoneLane", root.transform, new Vector3(0f, .1f, 0f), new Vector3(5.5f, .2f, 7.5f), _stone);
            List<Rigidbody> bodies = new List<Rigidbody>();
            List<Vector3> rests = new List<Vector3>();
            for (int i = 0; i < 7; i++)
            {
                float x = i % 2 == 0 ? -1.15f : 1.15f;
                float z = -2.7f + i * .9f;
                GameObject post = AddCylinder("RisingPost_" + (i + 1), root.transform, new Vector3(x, -.7f, z), new Vector3(.42f, .9f, .42f), _iron, true);
                Rigidbody body = AddMovingBody(post);
                bodies.Add(body);
                rests.Add(post.transform.localPosition);
            }
            BoxCollider zone = AddZone("DamageZone", root.transform, new Vector3(0f, .7f, 0f), new Vector3(5.1f, 1.6f, 7.1f));
            Renderer indicator = AddBlock("WarningPlate", root.transform, new Vector3(0f, .24f, -3.4f), new Vector3(3.8f, .08f, .22f), _warning, false).GetComponent<Renderer>();

            MedievalMechanismHazard hazard = root.AddComponent<MedievalMechanismHazard>();
            Set(hazard, "_kind", MedievalMechanismHazard.MechanismKind.RisingPosts);
            Set(hazard, "_movingBodies", bodies.ToArray());
            Set(hazard, "_restLocalPositions", rests.ToArray());
            Set(hazard, "_travelVector", new Vector3(0f, 1.65f, 0f));
            Set(hazard, "_safeSeconds", 4.5f);
            Set(hazard, "_warningSeconds", 1.25f);
            Set(hazard, "_activeSeconds", 3f);
            Set(hazard, "_hitZone", zone);
            Set(hazard, "_damage", 10);
            Set(hazard, "_indicator", indicator);
            Save(root);
        }

        private static void BuildTwinPendulums()
        {
            GameObject root = NewRoot("Medieval_TwinPendulums");
            AddBlock("TopBeam", root.transform, new Vector3(0f, 4.4f, 0f), new Vector3(7f, .42f, .55f), _wood);
            AddBlock("LeftPost", root.transform, new Vector3(-3.2f, 2.15f, 0f), new Vector3(.45f, 4.3f, .45f), _wood);
            AddBlock("RightPost", root.transform, new Vector3(3.2f, 2.15f, 0f), new Vector3(.45f, 4.3f, .45f), _wood);
            Rigidbody[] bodies = new Rigidbody[2];
            BoxCollider[] hits = new BoxCollider[2];
            for (int i = 0; i < 2; i++)
            {
                float x = i == 0 ? -1.5f : 1.5f;
                GameObject pivot = NewObject("PendulumPivot_" + (i + 1), root.transform, new Vector3(x, 4.1f, 0f));
                pivot.transform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 58f : -58f);
                bodies[i] = AddMovingBody(pivot);
                AddCylinder("Chain", pivot.transform, new Vector3(0f, -1.35f, 0f), new Vector3(.1f, 1.35f, .1f), _iron, false);
                GameObject weight = AddCylinder("IronWeight", pivot.transform, new Vector3(0f, -2.75f, 0f), new Vector3(.78f, .78f, .78f), _iron, true);
                hits[i] = ReplaceWithBox(weight, new Vector3(1.35f, 1.35f, 1.35f));
            }
            Renderer indicator = AddBlock("WarningPlate", root.transform, new Vector3(0f, 4.68f, -.35f), new Vector3(2.2f, .12f, .08f), _warning, false).GetComponent<Renderer>();

            NetworkRouteHazard hazard = root.AddComponent<NetworkRouteHazard>();
            Set(hazard, "_kind", NetworkRouteHazard.HazardKind.DoublePendulum);
            Set(hazard, "_primary", bodies[0]);
            Set(hazard, "_secondary", bodies[1]);
            Set(hazard, "_hitA", hits[0]);
            Set(hazard, "_hitB", hits[1]);
            Set(hazard, "_indicator", indicator);
            Set(hazard, "_swingAngle", 58f);
            Set(hazard, "_useAuthoredRestPose", true);
            SetCycle(hazard, 5.5f, 1.5f, 3f, 15);
            Save(root);
        }

        private static void BuildWoodenSpikes()
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/หนามไม้.fbx");
            if (source == null)
            {
                Debug.LogWarning("[MedievalTrapPrefabBuilder] Skipped WoodenSpikes: Assets/Prefabs/หนามไม้.fbx was not found.");
                return;
            }

            GameObject root = NewRoot("Medieval_WoodenSpikes");
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.name = "WoodenSpikes_Model";
            model.transform.SetParent(root.transform, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!hasBounds) { bounds = renderers[i].bounds; hasBounds = true; }
                else bounds.Encapsulate(renderers[i].bounds);
            }
            if (!hasBounds)
            {
                UnityEngine.Object.DestroyImmediate(root);
                Debug.LogWarning("[MedievalTrapPrefabBuilder] Skipped WoodenSpikes: model has no renderer.");
                return;
            }

            // The source FBX origin is about 20 metres away. Offset only the model so the prefab pivot stays centred at floor level.
            model.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            Vector3 footprint = new Vector3(bounds.size.x + .3f, .18f, bounds.size.z + .3f);
            AddBlock("StoneBase", root.transform, new Vector3(0f, -.09f, 0f), footprint, _stone, true);
            BoxCollider zone = AddZone("DamageZone", root.transform,
                new Vector3(0f, Mathf.Max(.25f, bounds.size.y * .45f), 0f),
                new Vector3(bounds.size.x, Mathf.Max(.5f, bounds.size.y * .9f), bounds.size.z));
            Renderer indicator = AddBlock("WarningStrip", root.transform,
                new Vector3(0f, .03f, -footprint.z * .5f - .12f),
                new Vector3(footprint.x, .06f, .18f), _warning, false).GetComponent<Renderer>();

            NetworkCargoDamageZone hazard = root.AddComponent<NetworkCargoDamageZone>();
            Set(hazard, "_zone", zone);
            Set(hazard, "_damage", 15);
            Set(hazard, "_cooldownSeconds", 1f);
            Set(hazard, "_damageReason", "Wooden spikes");
            indicator.enabled = true;
            Save(root);
        }

        private static void BuildBarrelRamp()
        {
            GameObject root = NewRoot("Medieval_BarrelRamp");
            GameObject ramp = AddBlock("Ramp", root.transform, new Vector3(0f, 1.2f, 0f), new Vector3(5.2f, .35f, 10f), _wood, true);
            ramp.transform.localRotation = Quaternion.Euler(-10f, 0f, 0f);
            AddBlock("LeftRail", root.transform, new Vector3(-2.55f, 1.65f, 0f), new Vector3(.3f, .8f, 10f), _wood);
            AddBlock("RightRail", root.transform, new Vector3(2.55f, 1.65f, 0f), new Vector3(.3f, .8f, 10f), _wood);

            GameObject barrel = AddCylinder("RollingBarrel", root.transform, new Vector3(0f, 2.4f, 4f), new Vector3(1f, 1.15f, 1f), _woodLight, true);
            barrel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            Rigidbody body = AddMovingBody(barrel);
            AddCylinder("IronBandA", barrel.transform, new Vector3(0f, -.55f, 0f), new Vector3(1.04f, .08f, 1.04f), _iron, false);
            AddCylinder("IronBandB", barrel.transform, new Vector3(0f, .55f, 0f), new Vector3(1.04f, .08f, 1.04f), _iron, false);
            BoxCollider zone = AddZone("ImpactZone", barrel.transform, Vector3.zero, new Vector3(2.1f, 2.1f, 2.1f));
            Renderer indicator = AddBlock("WarningStrip", root.transform, new Vector3(0f, .25f, -4.35f), new Vector3(4.5f, .1f, .25f), _warning, false).GetComponent<Renderer>();

            MedievalMechanismHazard hazard = root.AddComponent<MedievalMechanismHazard>();
            Set(hazard, "_kind", MedievalMechanismHazard.MechanismKind.BarrelRamp);
            Set(hazard, "_movingBodies", new[] { body });
            Set(hazard, "_restLocalPositions", new[] { barrel.transform.localPosition });
            Set(hazard, "_travelVector", new Vector3(0f, -1.5f, -8f));
            Set(hazard, "_rollDegrees", 1080f);
            Set(hazard, "_safeSeconds", 4f);
            Set(hazard, "_warningSeconds", 1.5f);
            Set(hazard, "_activeSeconds", 3f);
            Set(hazard, "_hitZone", zone);
            Set(hazard, "_damage", 15);
            Set(hazard, "_indicator", indicator);
            Save(root);
        }

        private static GameObject NewRoot(string name)
        {
            GameObject root = new GameObject(name);
            root.AddComponent<NetworkObject>();
            return root;
        }

        private static GameObject NewObject(string name, Transform parent, Vector3 localPosition)
        {
            GameObject item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            return item;
        }

        private static GameObject AddBlock(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool collider = true)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cube);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localScale = localScale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        private static GameObject AddCylinder(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material, bool collider)
        {
            GameObject item = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localScale = localScale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(item.GetComponent<Collider>());
            return item;
        }

        private static Rigidbody AddMovingBody(GameObject item)
        {
            Rigidbody body = item.GetComponent<Rigidbody>();
            if (body == null) body = item.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            return body;
        }

        private static BoxCollider ReplaceWithBox(GameObject item, Vector3 size)
        {
            Collider current = item.GetComponent<Collider>();
            if (current != null) UnityEngine.Object.DestroyImmediate(current);
            BoxCollider box = item.AddComponent<BoxCollider>();
            box.size = size;
            return box;
        }

        private static BoxCollider AddZone(string name, Transform parent, Vector3 localPosition, Vector3 size)
        {
            GameObject item = NewObject(name, parent, localPosition);
            BoxCollider zone = item.AddComponent<BoxCollider>();
            zone.size = size;
            zone.isTrigger = true;
            return zone;
        }

        private static void SetCycle(NetworkRouteHazard hazard, float safe, float warning, float active, int damage)
        {
            Set(hazard, "_safeSeconds", safe);
            Set(hazard, "_warningSeconds", warning);
            Set(hazard, "_activeSeconds", active);
            Set(hazard, "_damage", damage);
        }

        private static void Set(UnityEngine.Object target, string propertyName, object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException(target.GetType().Name + " has no serialized field " + propertyName);
            if (value is UnityEngine.Object objectValue) property.objectReferenceValue = objectValue;
            else if (value is int intValue) property.intValue = intValue;
            else if (value is float floatValue) property.floatValue = floatValue;
            else if (value is bool boolValue) property.boolValue = boolValue;
            else if (value is Vector3 vectorValue) property.vector3Value = vectorValue;
            else if (value is Color colorValue) property.colorValue = colorValue;
            else if (value is string stringValue) property.stringValue = stringValue;
            else if (value is Enum enumValue) property.enumValueIndex = Convert.ToInt32(enumValue);
            else if (value is Array array)
            {
                property.arraySize = array.Length;
                for (int i = 0; i < array.Length; i++)
                {
                    object element = array.GetValue(i);
                    SerializedProperty slot = property.GetArrayElementAtIndex(i);
                    if (element is UnityEngine.Object unityObject) slot.objectReferenceValue = unityObject;
                    else if (element is Vector3 vector) slot.vector3Value = vector;
                    else throw new NotSupportedException("Unsupported array value for " + propertyName);
                }
            }
            else throw new NotSupportedException("Unsupported serialized value for " + propertyName);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Save(GameObject root)
        {
            string path = Root + "/" + root.name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void CreateMaterials()
        {
            _wood = Material("Medieval_Wood", new Color(.29f, .12f, .045f));
            _woodLight = Material("Medieval_WoodLight", new Color(.48f, .23f, .08f));
            _iron = Material("Medieval_Iron", new Color(.09f, .11f, .13f), .75f);
            _stone = Material("Medieval_Stone", new Color(.29f, .3f, .27f), .15f);
            _warning = Material("Medieval_Warning", new Color(.95f, .55f, .05f), .2f);
            _ember = Material("Medieval_Ember", new Color(1f, .16f, .015f), .1f, true);
        }

        private static Material Material(string name, Color color, float metallic = 0f, bool emission = false)
        {
            string path = Materials + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", metallic > .5f ? .58f : .24f);
            if (emission && material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 3f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
