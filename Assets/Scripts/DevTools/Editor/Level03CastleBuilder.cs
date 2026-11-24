#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Level03 Castle (copy of Level02 George): a bigger end island with the friend's castle model made hollow
    /// (open courtyard, gate in the front wall), a co-op gate (a team-mate stands on the plate to hold it open),
    /// a front yard of troll paths (bumpy cobbles, two narrow lanes at different heights over trip stones) and a
    /// raised delivery dais reached by a narrow tilted ramp or a throw. Menu: Tools/Expedition/Build Level03 Castle.
    /// Run with Level03 Castle open; safe to run again (it removes what it built first).
    /// </summary>
    public static class Level03CastleBuilder
    {
        private const string ScenePath = "Assets/Scenes/Level01.unity";
        private const string CastleModel = "Assets/Models/Environment/Castle/castle.fbx";
        private const string GatePrefab = "Assets/Prefabs/Traps/Coop/Coop_HoldButtonDoor.prefab";
        private const string TripStones = "Assets/Prefabs/Traps/Obstacles/Obstacle_TripStones_Cluster.prefab";
        private const string Root = "=== L3 Castle ===";

        // Island: front edge kept where the drawbridge lands, grown toward +Z and sideways.
        private const float IslandFrontZ = 324.2f, IslandSizeX = 70f, IslandSizeZ = 64f;
        private static readonly Vector3 CastleCentre = new Vector3(-86.4f, 39.8f, 364.5f);
        private const float CastleScale = 3f;
        private static readonly Vector3 ModelCentre = new Vector3(8.74f, 0f, 36.3f); // model space centre of the footprint
        private const float Top = 39.8f;

        private static Material _stone, _wood, _dark, _cobble;

        [MenuItem("Tools/Expedition/Build Level03 Castle")]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath) { Debug.LogError("[Level03] open " + ScenePath + " first"); return; }
            var old = GameObject.Find(Root);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(Root).transform;

            GrowIsland();
            foreach (var n in new[] { "L2_Castle", "L2_DeliveryLanding", "L2_DeliveryLanding_Edge", "L2_CastleFlagL_Pole", "L2_CastleFlagR_Pole", "L2_CastleFlagL_Cloth", "L2_CastleFlagR_Cloth", "L2_TripStones" })
            {
                GameObject g;
                while ((g = GameObject.Find(n)) != null) Object.DestroyImmediate(g);
            }
            ClearIslandDecor();

            var castle = BuildCastle(root);
            BuildGate(root);
            BuildFrontYard(root);
            BuildDais(root);
            MoveDeliveryAndPortal();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Level03] castle island built");
        }

        private static Material Mat(string name, Color color)
        {
            string path = "Assets/Prefabs/Traps/Materials/L3_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Vector3 euler = default, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.eulerAngles = euler;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static void GrowIsland()
        {
            var island = GameObject.Find("L2_11_Castle");
            var col = island.GetComponent<Collider>();
            // Reset to the original size first so running the builder twice does not grow it twice.
            island.transform.localScale = Vector3.one;
            island.transform.position = new Vector3(-86.43f, 39.75f, 339.28f);
            Physics.SyncTransforms();
            Bounds b = col.bounds;
            float sx = IslandSizeX / b.size.x, sz = IslandSizeZ / b.size.z;
            island.transform.localScale = new Vector3(sx, 1f, sz);
            Physics.SyncTransforms();
            b = col.bounds;
            island.transform.position += new Vector3(0f, 0f, IslandFrontZ - b.min.z);
            Physics.SyncTransforms();
        }

        /// <summary>Removes the small decor that sat on the old castle island (it would float or clip now).</summary>
        private static void ClearIslandDecor()
        {
            var kill = new List<GameObject>();
            foreach (var t in Object.FindObjectsByType<Transform>())
            {
                if (t.parent != null) continue;
                if (!(t.name.StartsWith("Decor_") || t.name.StartsWith("Obstacle_TripStone"))) continue;
                Vector3 p = t.position;
                if (p.x > -122f && p.x < -50f && p.z > 324f && p.z < 390f && p.y > 36f) kill.Add(t.gameObject);
            }
            foreach (var g in kill) Object.DestroyImmediate(g);
        }

        private static Transform BuildCastle(Transform root)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(CastleModel);
            var castle = (GameObject)PrefabUtility.InstantiatePrefab(asset, root);
            castle.name = "Castle";
            castle.transform.localScale = Vector3.one * CastleScale;
            castle.transform.position = CastleCentre - ModelCentre * CastleScale;
            Material wallMat = null;
            // Hollow it: drop the solid wall block, its base and everything inside the walls (the keep), and the
            // window/battlement pieces where the gate goes.
            var remove = new List<GameObject>();
            foreach (var r in castle.GetComponentsInChildren<Renderer>())
            {
                Vector3 c = (r.bounds.center - castle.transform.position) / CastleScale - ModelCentre;
                Vector3 s = r.bounds.size / CastleScale;
                bool solidBlock = s.x > 13f && s.z > 11f;
                bool inner = Mathf.Abs(c.x) < 4.6f && Mathf.Abs(c.z) < 4.6f;
                bool gate = c.z < -5.5f && Mathf.Abs(c.x) < 1.2f;
                if (solidBlock && wallMat == null && s.y > 4f) wallMat = r.sharedMaterial;
                if (solidBlock || inner || gate) remove.Add(r.gameObject);
            }
            foreach (var g in remove) Object.DestroyImmediate(g);
            _stone = wallMat != null ? wallMat : Mat("Stone", new Color(.62f, .6f, .56f));

            // New walls (model units -> world), 1.4 units thick, from the ground to the wall top (6.7 units).
            const float halfX = 7f, halfZ = 6.2f, thick = 1.4f, height = 6.7f, gateHalf = 1f;
            Transform walls = new GameObject("Walls").transform;
            walls.SetParent(castle.transform.parent, true);
            void Wall(string n, float x0, float x1, float z0, float z1, float y0 = 0f, float y1 = height)
            {
                Vector3 a = CastleCentre + new Vector3(x0, y0, z0) * CastleScale, b = CastleCentre + new Vector3(x1, y1, z1) * CastleScale;
                Box(n, walls, (a + b) * .5f, new Vector3(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z)), _stone);
            }
            Wall("Back", -halfX, halfX, halfZ - thick, halfZ);
            Wall("Left", -halfX, -halfX + thick, -halfZ, halfZ);
            Wall("Right", halfX - thick, halfX, -halfZ, halfZ);
            Wall("FrontL", -halfX, -gateHalf, -halfZ, -halfZ + thick);
            Wall("FrontR", gateHalf, halfX, -halfZ, -halfZ + thick);
            Wall("OverGate", -gateHalf, gateHalf, -halfZ, -halfZ + thick, 2.2f, height);
            // Corner towers keep their own shapes; give them colliders.
            foreach (var r in castle.GetComponentsInChildren<MeshRenderer>())
                if (r.bounds.size.y > 6f && r.GetComponent<Collider>() == null && r.TryGetComponent(out MeshFilter f))
                    r.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            return castle.transform;
        }

        private static void BuildGate(Transform root)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(GatePrefab);
            var gate = (GameObject)PrefabUtility.InstantiatePrefab(asset, root);
            gate.name = "CastleGate_Coop";
            gate.transform.localScale = Vector3.one * 1.35f;
            float frontZ = CastleCentre.z - 6.2f * CastleScale + .7f * CastleScale;
            gate.transform.position = new Vector3(CastleCentre.x, Top, frontZ);
            var side = gate.transform.Find("SideWalls");
            if (side != null) side.gameObject.SetActive(false);
            var sign = gate.GetComponentInChildren<TextMesh>(true);
            if (sign != null) sign.text = "ให้เพื่อนยืนบนปุ่ม ประตูจะเปิด";
        }

        /// <summary>Front yard: bumpy cobbles, then two narrow lanes at different heights over trip stones.</summary>
        private static void BuildFrontYard(Transform root)
        {
            _cobble = Mat("Cobble", new Color(.55f, .52f, .48f));
            _wood = Mat("Plank", new Color(.55f, .38f, .22f));
            var yard = new GameObject("FrontYard").transform;
            yard.SetParent(root, true);
            // Bumpy cobbles from the drawbridge landing to the lanes.
            var rng = new System.Random(3);
            for (float z = 327f; z < 333.5f; z += .9f)
                for (float x = -96f; x < -77f; x += .9f)
                {
                    float h = .08f + (float)rng.NextDouble() * .22f;
                    Box("Cobble", yard, new Vector3(x + (float)rng.NextDouble() * .3f, Top + h * .5f, z + (float)rng.NextDouble() * .3f),
                        new Vector3(.75f, h, .75f), _cobble, new Vector3(0f, (float)rng.NextDouble() * 40f, 0f));
                }
            // Two lanes, 1.1 m wide, 3.2 m apart: the left one climbs higher than the right one.
            Lane(yard, -88f, 1.8f);
            Lane(yard, -84.8f, .9f);
            // Trip stones fill the ground between and beside the lanes.
            var stones = AssetDatabase.LoadAssetAtPath<GameObject>(TripStones);
            if (stones != null)
                foreach (var x in new[] { -91.5f, -86.4f, -81.3f })
                {
                    var s = (GameObject)PrefabUtility.InstantiatePrefab(stones, yard);
                    s.transform.position = new Vector3(x, Top, 336.8f);
                }
            var sign = new GameObject("Sign");
            sign.transform.SetParent(yard, true);
            sign.transform.position = new Vector3(-95.5f, Top + 2.6f, 332f);
            var text = sign.AddComponent<TextMesh>();
            text.text = "ทางสูงกับทางต่ำ\nแบกลังคนละทางระวังลังตก!";
            text.fontSize = 112; text.characterSize = .03f; text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center;
        }

        private static void Lane(Transform parent, float x, float rise)
        {
            const float z0 = 333.5f, z1 = 340.5f, up = 2.5f, flat = 2f;
            Vector3 a = new Vector3(x, Top, z0), b = new Vector3(x, Top + rise, z0 + up), c = new Vector3(x, Top + rise, z0 + up + flat), d = new Vector3(x, Top, z1);
            Plank(parent, a, b, 1.1f);
            Plank(parent, b, c, 1.1f);
            Plank(parent, c, d, 1.1f);
        }

        private static void Plank(Transform parent, Vector3 a, Vector3 b, float width, float roll = 0f)
        {
            Vector3 d = b - a;
            var go = Box("Plank", parent, (a + b) * .5f + Vector3.down * .1f, new Vector3(width, .2f, d.magnitude + .2f), _wood);
            go.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, roll);
        }

        /// <summary>Raised delivery dais at the back-left of the courtyard: a narrow tilted ramp up, or throw it up.</summary>
        private static void BuildDais(Transform root)
        {
            var dais = new GameObject("DeliveryDais").transform;
            dais.SetParent(root, true);
            Vector3 centre = new Vector3(-99f, Top, 378f);
            Box("Dais", dais, centre + new Vector3(0f, .9f, 0f), new Vector3(9f, 1.8f, 7f), _cobble);
            // Narrow ramp from the courtyard, rolled 10 degrees so a crate slides off the low side.
            Plank(dais, new Vector3(-93f, Top, 368f), new Vector3(-96.5f, Top + 1.8f, 374.4f), 1.2f, 10f);
        }

        private static void MoveDeliveryAndPortal()
        {
            // The zone's trigger sits on its root; its markers (ring, beacons, arrows, label) hang off a child group.
            var zone = GameObject.Find("DeliveryZone");
            if (zone != null)
            {
                zone.transform.position = new Vector3(-99f, Top + 1.8f, 378f);
                var markers = zone.transform.Find("DeliveryMarkers");
                if (markers != null)
                {
                    markers.position = zone.transform.position;
                    var rs = markers.GetComponentsInChildren<Renderer>();
                    if (rs.Length > 0)
                    {
                        Bounds mb = rs[0].bounds; foreach (var r in rs) mb.Encapsulate(r.bounds);
                        Vector3 off = zone.transform.position - mb.center; off.y = 0f;
                        markers.position += off;
                    }
                }
            }
            Move("L2_DeliveryExitPortal_Level01", new Vector3(-74f, Top, 380f), "NewPortalModel");
        }

        /// <summary>Moves a root so that its drawn centre (or the named child) lands on <paramref name="target"/>.</summary>
        private static void Move(string name, Vector3 target, string anchorChild = null)
        {
            var go = GameObject.Find(name);
            if (go == null) { Debug.LogWarning("[Level03] missing " + name); return; }
            Vector3 anchor;
            var child = anchorChild != null ? go.transform.Find(anchorChild) : null;
            if (child != null) anchor = child.position;
            else
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) { anchor = go.transform.position; }
                else { Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); anchor = new Vector3(b.center.x, b.min.y, b.center.z); }
            }
            go.transform.position += target - anchor;
        }
    }
}
#endif
