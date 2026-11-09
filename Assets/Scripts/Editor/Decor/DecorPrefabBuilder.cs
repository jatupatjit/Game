#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CoopGame.SceneDecor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CoopGame.EditorTools.Decor
{
    /// <summary>
    /// Builds the scene decoration prefabs in Assets/Prefabs/Decor (grass, flowers, big rocks, bushes, mushrooms,
    /// fences, windmill, lighthouse, hot air balloon, clouds, fog ring, cloud sea). Everything is plain mesh with no
    /// NetworkObject; small ground decor has no collider so it never changes gameplay. Safe to run again: meshes and
    /// materials keep their asset ids, prefabs are overwritten.
    /// </summary>
    public static class DecorPrefabBuilder
    {
        public const string PrefabRoot = "Assets/Prefabs/Decor";
        private const string ArtRoot = "Assets/Art/Decor";
        private const string MeshFolder = ArtRoot + "/Meshes";
        private const string MatFolder = ArtRoot + "/Materials";
        private const string L2Mat = "Assets/Environment/Level02/";

        private static readonly Dictionary<string, Material> Mats = new();

        [MenuItem("Tools/Decor/Build Decor Prefabs")]
        public static void BuildMenu() => Debug.Log(BuildAll());

        public static string BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "[DecorPrefabBuilder] Stop Play mode first.";
            foreach (var f in new[] { ArtRoot, MeshFolder, MatFolder, PrefabRoot, PrefabRoot + "/Nature", PrefabRoot + "/Props", PrefabRoot + "/Landmarks", PrefabRoot + "/Sky" })
                EnsureFolder(f);
            Mats.Clear();
            var saved = new List<string>();
            var previous = EditorSceneManager.GetActiveScene();
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(temp);
            try
            {
                for (int i = 0; i < 3; i++) saved.Add(GrassClump("ABC"[i], 100 + i));
                saved.Add(FlowerPatch('A', 200, "FlowerRed", "FlowerYellow"));
                saved.Add(FlowerPatch('B', 201, "FlowerWhite", "FlowerPink"));
                saved.Add(FlowerPatch('C', 202, "FlowerPurple", "FlowerBlue"));
                saved.Add(FlowerPatch('D', 203, "FlowerRed", "FlowerWhite", "FlowerYellow", "FlowerPurple", "FlowerOrange"));
                for (int i = 0; i < 3; i++) saved.Add(BigRock("ABC"[i], 300 + i));
                saved.Add(SmallRocks(310));
                saved.Add(Bush('A', 400)); saved.Add(Bush('B', 401));
                saved.Add(Mushrooms(450));
                saved.Add(Fence(500));
                saved.Add(Windmill());
                saved.Add(Lighthouse());
                saved.Add(Balloon());
                for (int i = 0; i < 3; i++) saved.Add(Cloud("ABC"[i], 700 + i));
                saved.Add(FogRing());
                saved.Add(CloudSea());
            }
            finally
            {
                if (previous.IsValid()) EditorSceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(temp, true);
                AssetDatabase.SaveAssets();
            }
            return "[DecorPrefabBuilder] Saved " + saved.Count + " prefabs:\n" + string.Join("\n", saved);
        }

        // ---------- Nature ----------

        private static string GrassClump(char v, int seed)
        {
            var b = new DecorMesh(seed);
            Material[] blades = { M("GrassBlade"), M("GrassBladeDark"), M("GrassBladeLight") };
            int tufts = b.Int(14, 20);
            for (int i = 0; i < tufts; i++)
            {
                float a = b.Range(0, Mathf.PI * 2), r = Mathf.Sqrt(b.Range(0, 1)) * 2.4f;
                b.Tuft(blades[b.Int(0, 3)], new Vector3(Mathf.Cos(a) * r, -.03f, Mathf.Sin(a) * r), b.Int(5, 9), b.Range(.3f, .6f), .07f);
            }
            return Save("Nature/Decor_GrassClump_" + v, b, null);
        }

        private static string FlowerPatch(char v, int seed, params string[] colours)
        {
            var b = new DecorMesh(seed);
            int flowers = b.Int(7, 11);
            for (int i = 0; i < flowers; i++)
            {
                float a = b.Range(0, Mathf.PI * 2), r = Mathf.Sqrt(b.Range(0, 1)) * 1.3f;
                b.Flower(M("FlowerStem"), M(colours[b.Int(0, colours.Length)]), M("FlowerMiddle"), new Vector3(Mathf.Cos(a) * r, -.02f, Mathf.Sin(a) * r),
                    b.Range(.25f, .5f), b.Range(.14f, .22f), b.Int(5, 7));
            }
            for (int i = 0; i < 5; i++)
            {
                float a = b.Range(0, Mathf.PI * 2), r = b.Range(.2f, 1.5f);
                b.Tuft(M("GrassBlade"), new Vector3(Mathf.Cos(a) * r, -.03f, Mathf.Sin(a) * r), 6, .3f, .06f);
            }
            return Save("Nature/Decor_FlowerPatch_" + v, b, null);
        }

        private static string BigRock(char v, int seed)
        {
            var b = new DecorMesh(seed);
            float s = 1.2f + (v - 'A') * .45f;
            var main = new Vector3(s * b.Range(1f, 1.3f), s * b.Range(.75f, 1f), s * b.Range(.85f, 1.1f));
            b.Blob(M("Rock"), new Vector3(0, main.y * .55f, 0), main, Quaternion.Euler(0, b.Range(0, 360), b.Range(-6, 6)), 1, .18f, -.45f);
            // Collider shape = main boulder only (convex), the side stones and moss are visual.
            var colliderMesh = new DecorMesh(seed);
            colliderMesh.Blob(M("Rock"), new Vector3(0, main.y * .55f, 0), main * .97f, Quaternion.identity, 1, 0f, -.45f);
            b.Blob(M("RockDark"), new Vector3(main.x * .9f, main.y * .2f, main.z * .3f), main * .4f, Quaternion.Euler(0, b.Range(0, 360), 10), 1, .2f, -.3f);
            b.Blob(M("RockDark"), new Vector3(-main.x * .7f, main.y * .15f, -main.z * .7f), main * .3f, Quaternion.Euler(0, b.Range(0, 360), -8), 0, .2f, -.3f);
            b.Blob(M("Moss"), new Vector3(0, main.y * 1.32f, 0), new Vector3(main.x * .62f, main.y * .12f, main.z * .58f), Quaternion.Euler(0, b.Range(0, 360), 0), 1, .15f);
            for (int i = 0; i < 4; i++)
            {
                float a = b.Range(0, Mathf.PI * 2);
                b.Tuft(M("GrassBladeDark"), new Vector3(Mathf.Cos(a) * main.x * 1.05f, -.03f, Mathf.Sin(a) * main.z * 1.05f), 6, .4f, .07f);
            }
            return Save("Nature/Decor_Rock_Big_" + v, b, go =>
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = SaveMesh("Decor_Rock_Big_" + v + "_Collider", colliderMesh.Build("Decor_Rock_Big_" + v + "_Collider"));
                mc.convex = true;
            });
        }

        private static string SmallRocks(int seed)
        {
            var b = new DecorMesh(seed);
            for (int i = 0; i < 5; i++)
            {
                float a = b.Range(0, Mathf.PI * 2), r = b.Range(.2f, 1.1f), s = b.Range(.12f, .3f);
                b.Blob(M(i % 2 == 0 ? "Rock" : "RockDark"), new Vector3(Mathf.Cos(a) * r, s * .3f, Mathf.Sin(a) * r), new Vector3(s * 1.3f, s, s * 1.1f),
                    Quaternion.Euler(0, b.Range(0, 360), 0), 0, .2f, -.4f);
            }
            return Save("Nature/Decor_Rocks_Small", b, null);
        }

        private static string Bush(char v, int seed)
        {
            var b = new DecorMesh(seed);
            int lumps = v == 'A' ? 3 : 5;
            for (int i = 0; i < lumps; i++)
            {
                float a = i / (float)lumps * Mathf.PI * 2 + b.Range(-.3f, .3f), r = i == 0 ? 0 : b.Range(.35f, .6f), s = b.Range(.45f, .7f);
                b.Blob(M(i % 2 == 0 ? "Bush" : "BushLight"), new Vector3(Mathf.Cos(a) * r, s * .7f, Mathf.Sin(a) * r), new Vector3(s, s * .85f, s),
                    Quaternion.Euler(0, b.Range(0, 360), 0), 1, .15f, -.5f);
            }
            if (v == 'B')
                for (int i = 0; i < 4; i++)
                {
                    float a = b.Range(0, Mathf.PI * 2);
                    b.Blob(M("FlowerPink"), new Vector3(Mathf.Cos(a) * .6f, b.Range(.6f, 1f), Mathf.Sin(a) * .6f), Vector3.one * .08f, Quaternion.identity, 0, .1f);
                }
            return Save("Nature/Decor_Bush_" + v, b, null);
        }

        private static string Mushrooms(int seed)
        {
            var b = new DecorMesh(seed);
            for (int i = 0; i < 4; i++)
            {
                float a = b.Range(0, Mathf.PI * 2), r = i == 0 ? 0 : b.Range(.25f, .55f), h = b.Range(.15f, .35f), cap = h * b.Range(.8f, 1.1f);
                var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                b.Frustum(M("MushroomStem"), p, cap * .25f, cap * .2f, h, 6, Quaternion.identity);
                b.Blob(M("MushroomCap"), p + Vector3.up * h, new Vector3(cap * .7f, cap * .45f, cap * .7f), Quaternion.Euler(0, b.Range(0, 360), 0), 1, .05f, -.1f);
                for (int d = 0; d < 3; d++)
                {
                    float da = b.Range(0, Mathf.PI * 2);
                    b.Blob(M("MushroomSpot"), p + Vector3.up * (h + cap * .38f) + new Vector3(Mathf.Cos(da), 0, Mathf.Sin(da)) * cap * .3f, new Vector3(cap * .1f, cap * .04f, cap * .1f), Quaternion.identity, 0, 0f);
                }
            }
            return Save("Nature/Decor_Mushrooms", b, null);
        }

        // ---------- Props ----------

        private static string Fence(int seed)
        {
            var b = new DecorMesh(seed);
            for (int i = 0; i < 3; i++)
                b.Box(M("Wood"), new Vector3(-1.5f + i * 1.5f, .5f, 0), new Vector3(.14f, 1f, .14f), Quaternion.Euler(b.Range(-3, 3), b.Range(0, 20), b.Range(-3, 3)));
            b.Box(M("WoodDark"), new Vector3(0, .72f, 0), new Vector3(3.2f, .1f, .06f), Quaternion.Euler(0, 0, b.Range(-2, 2)));
            b.Box(M("WoodDark"), new Vector3(0, .38f, 0), new Vector3(3.2f, .1f, .06f), Quaternion.Euler(0, 0, b.Range(-2, 2)));
            return Save("Props/Decor_Fence", b, go =>
            {
                var c = go.AddComponent<BoxCollider>();
                c.center = new Vector3(0, .5f, 0); c.size = new Vector3(3.2f, 1f, .2f);
            });
        }

        // ---------- Landmarks ----------

        private static string Windmill()
        {
            var b = new DecorMesh(600);
            b.Frustum(M("Stone"), Vector3.zero, 3.3f, 3.1f, .6f, 8, Quaternion.identity);
            b.Frustum(Plaster(), new Vector3(0, .6f, 0), 2.8f, 1.9f, 7.5f, 8, Quaternion.identity);
            b.Frustum(Roof(), new Vector3(0, 8.1f, 0), 2.35f, 0f, 2.6f, 8, Quaternion.identity);
            b.Box(M("WoodDark"), new Vector3(0, 1.6f, -2.62f), new Vector3(1.2f, 2.1f, .25f), Quaternion.Euler(-4, 0, 0));
            b.Box(M("WindowGlow"), new Vector3(0, 4.6f, -2.3f), new Vector3(.7f, .9f, .2f), Quaternion.Euler(-6, 0, 0));
            b.Box(M("WindowGlow"), new Vector3(2.2f, 3.4f, 0), new Vector3(.2f, .8f, .6f), Quaternion.Euler(0, 0, 6));
            b.Box(M("Wood"), new Vector3(0, 2.8f, -2.5f), new Vector3(1.6f, .12f, .4f), Quaternion.identity);
            // Small balcony under the sails.
            b.Box(M("Wood"), new Vector3(0, 6.6f, -2.1f), new Vector3(2.6f, .15f, 1.2f), Quaternion.identity);
            var root = new GameObject("Decor_Windmill");
            Attach(root, b, "Decor_Windmill");

            var rotor = new GameObject("Sails").transform;
            rotor.SetParent(root.transform, false);
            rotor.localPosition = new Vector3(0, 7.4f, -2.75f);
            var r = new DecorMesh(601);
            r.Frustum(M("WoodDark"), new Vector3(0, 0, .6f), .35f, .3f, .9f, 8, Quaternion.Euler(-90, 0, 0));
            for (int i = 0; i < 4; i++)
            {
                var q = Quaternion.Euler(0, 0, i * 90f);
                r.Box(M("WoodDark"), q * new Vector3(0, 3.1f, -.35f), new Vector3(.18f, 6.2f, .15f), q);
                r.Box(M("Sail"), q * new Vector3(.62f, 3.6f, -.38f), new Vector3(1.05f, 4.6f, .05f), q);
                for (int k = 0; k < 4; k++) r.Box(M("Wood"), q * new Vector3(.62f, 1.7f + k * 1.3f, -.42f), new Vector3(1.15f, .07f, .05f), q);
            }
            Attach(rotor.gameObject, r, "Decor_Windmill_Sails");
            rotor.gameObject.AddComponent<DecorMotion>().Configure(Vector3.forward, 22f, 0, 1, Vector3.right, 0, 0);

            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 4.5f, 0); col.radius = 2.7f; col.height = 9f;
            return SaveGo(root, "Landmarks/Decor_Windmill");
        }

        private static string Lighthouse()
        {
            var b = new DecorMesh(610);
            b.Blob(M("Rock"), new Vector3(0, .4f, 0), new Vector3(4.2f, 1.4f, 4f), Quaternion.identity, 1, .15f, -.3f);
            float y = 1.2f, r = 2.4f;
            for (int i = 0; i < 6; i++)
            {
                float h = 2.2f, rt = r - .22f;
                b.Frustum(M(i % 2 == 0 ? "LighthouseWhite" : "LighthouseRed"), new Vector3(0, y, 0), r, rt, h, 12, Quaternion.identity, false);
                y += h; r = rt;
            }
            b.Frustum(M("LighthouseRed"), new Vector3(0, y, 0), 2.3f, 2.3f, .3f, 12, Quaternion.identity, true, true);
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2;
                b.Box(M("Iron"), new Vector3(Mathf.Cos(a) * 2.15f, y + .8f, Mathf.Sin(a) * 2.15f), new Vector3(.08f, 1f, .08f), Quaternion.identity);
            }
            b.Frustum(M("Iron"), new Vector3(0, y + 1.25f, 0), 2.2f, 2.2f, .08f, 16, Quaternion.identity, false);
            b.Frustum(M("WindowGlow"), new Vector3(0, y + .3f, 0), 1.3f, 1.3f, 2f, 10, Quaternion.identity);
            b.Frustum(M("LighthouseRed"), new Vector3(0, y + 2.3f, 0), 1.7f, 0f, 1.6f, 10, Quaternion.identity);
            b.Blob(M("Iron"), new Vector3(0, y + 4f, 0), Vector3.one * .25f, Quaternion.identity, 0, 0);
            b.Box(M("WoodDark"), new Vector3(0, 2.2f, -2.32f), new Vector3(1f, 1.9f, .3f), Quaternion.identity);
            for (int i = 0; i < 3; i++) b.Box(M("WindowGlow"), new Vector3(0, 5f + i * 2.6f, -(2.2f - i * .45f)), new Vector3(.5f, .7f, .25f), Quaternion.identity);
            var root = new GameObject("Decor_Lighthouse");
            Attach(root, b, "Decor_Lighthouse");

            var lamp = new GameObject("LampBeams").transform;
            lamp.SetParent(root.transform, false);
            lamp.localPosition = new Vector3(0, y + 1.3f, 0);
            var beam = new DecorMesh(611);
            // Two cones pointing opposite ways along local Z; uv.y = 0 at the lamp.
            beam.Frustum(M("Beam", true), Vector3.zero, .6f, 6f, 38f, 12, Quaternion.Euler(90, 0, 0), false);
            beam.Frustum(M("Beam", true), Vector3.zero, .6f, 6f, 38f, 12, Quaternion.Euler(-90, 0, 0), false);
            Attach(lamp.gameObject, beam, "Decor_Lighthouse_Beams", false);
            lamp.gameObject.AddComponent<DecorMotion>().Configure(Vector3.up, 30f, 0, 1, Vector3.right, 0, 0);

            var col = root.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 8f, 0); col.radius = 2.4f; col.height = 16f;
            return SaveGo(root, "Landmarks/Decor_Lighthouse");
        }

        private static string Balloon()
        {
            var root = new GameObject("Decor_HotAirBalloon");
            var b = new DecorMesh(620);
            int stripes = 10;
            // Envelope: stacked rings, alternating colour per vertical stripe.
            float[] rings = { 0.55f, 1.6f, 2.5f, 3f, 3.1f, 2.9f, 2.3f, 1.2f, 0f };
            float[] heights = { 0f, 1f, 2f, 3f, 4f, 5f, 6f, 6.8f, 7.2f };
            for (int k = 0; k < rings.Length - 1; k++)
                for (int s = 0; s < stripes; s++)
                {
                    float a0 = s / (float)stripes * Mathf.PI * 2, a1 = (s + 1) / (float)stripes * Mathf.PI * 2;
                    Vector3 P(float r, float h, float a) => new(Mathf.Cos(a) * r, 2.6f + h, Mathf.Sin(a) * r);
                    var m = M(s % 2 == 0 ? "BalloonA" : "BalloonB");
                    if (rings[k + 1] > 0.001f) b.Quad(m, P(rings[k], heights[k], a0), P(rings[k + 1], heights[k + 1], a0), P(rings[k + 1], heights[k + 1], a1), P(rings[k], heights[k], a1));
                    else b.Tri(m, P(rings[k], heights[k], a0), P(0, heights[k + 1], a0), P(rings[k], heights[k], a1));
                }
            b.Frustum(M("Wood"), Vector3.zero, .7f, .8f, .9f, 8, Quaternion.identity, true, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2 + Mathf.PI / 4;
                var from = new Vector3(Mathf.Cos(a) * .7f, .9f, Mathf.Sin(a) * .7f);
                var to = new Vector3(Mathf.Cos(a) * .55f, 2.6f, Mathf.Sin(a) * .55f);
                b.Box(M("WoodDark"), (from + to) * .5f, new Vector3(.04f, (to - from).magnitude, .04f), Quaternion.FromToRotation(Vector3.up, to - from));
            }
            Attach(root, b, "Decor_HotAirBalloon");
            root.AddComponent<DecorMotion>().Configure(Vector3.up, 2f, 1.2f, 9f, Vector3.right, 0, 0);
            return SaveGo(root, "Landmarks/Decor_HotAirBalloon");
        }

        // ---------- Sky ----------

        private static string Cloud(char v, int seed)
        {
            var b = new DecorMesh(seed);
            int puffs = 5 + (v - 'A') * 2;
            float len = 6f + (v - 'A') * 3f;
            for (int i = 0; i < puffs; i++)
            {
                float t = puffs == 1 ? .5f : i / (float)(puffs - 1);
                float x = Mathf.Lerp(-len * .5f, len * .5f, t) + b.Range(-.6f, .6f);
                float bulge = Mathf.Sin(t * Mathf.PI);
                float s = b.Range(1.6f, 2.4f) * (.6f + bulge * .7f);
                b.Blob(M("Cloud"), new Vector3(x, s * .35f + bulge * .4f, b.Range(-1f, 1f)), new Vector3(s * 1.2f, s, s), Quaternion.Euler(0, b.Range(0, 360), 0), 1, .08f, -.35f);
            }
            var root = new GameObject("Decor_Cloud_" + v);
            Attach(root, b, "Decor_Cloud_" + v, false);
            root.AddComponent<DecorMotion>().Configure(Vector3.up, 0, .6f, 14f, Vector3.right, .5f, 60f);
            return SaveGo(root, "Sky/Decor_Cloud_" + v);
        }

        private static string FogRing()
        {
            var b = new DecorMesh(800);
            // Unit size: radius 1, height 1. Scale X/Z to the island radius and Y to the fog height.
            b.Ring(Fog("FogRing", 0, new Color(.93f, .96f, 1f, .55f)), 1f, 1f, 32);
            b.Ring(Fog("FogRing", 0, new Color(.93f, .96f, 1f, .55f)), .82f, .8f, 32);
            var root = new GameObject("Decor_FogRing");
            Attach(root, b, "Decor_FogRing", false);
            return SaveGo(root, "Sky/Decor_FogRing");
        }

        private static string CloudSea()
        {
            var root = new GameObject("Decor_CloudSea");
            for (int i = 0; i < 3; i++)
            {
                var layer = new GameObject("Layer" + i);
                layer.transform.SetParent(root.transform, false);
                layer.transform.localPosition = new Vector3(0, i * 3f, 0);
                layer.transform.localEulerAngles = new Vector3(0, i * 40f, 0);
                var b = new DecorMesh(810 + i);
                // Unit radius 1: scale the prefab to the size of the level.
                b.Disc(Fog("CloudSea", 1, new Color(1f, 1f, 1f, .45f - i * .1f)), 1f, 48, 6);
                Attach(layer, b, "Decor_CloudSea_Layer" + i, false);
            }
            return SaveGo(root, "Sky/Decor_CloudSea");
        }

        // ---------- helpers ----------

        private static string Save(string relPath, DecorMesh b, Action<GameObject> extra)
        {
            string name = relPath.Substring(relPath.LastIndexOf('/') + 1);
            var go = new GameObject(name);
            Attach(go, b, name);
            extra?.Invoke(go);
            return SaveGo(go, relPath);
        }

        private static void Attach(GameObject go, DecorMesh b, string meshName, bool shadows = true)
        {
            go.AddComponent<MeshFilter>().sharedMesh = SaveMesh(meshName, b.Build(meshName));
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = b.Materials;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static string SaveGo(GameObject go, string relPath)
        {
            string path = PrefabRoot + "/" + relPath + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(go, path, out bool ok);
            Object.DestroyImmediate(go);
            if (!ok) throw new Exception("Could not save " + path);
            return path;
        }

        private static Mesh SaveMesh(string name, Mesh mesh)
        {
            string path = MeshFolder + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
            EditorUtility.CopySerialized(mesh, existing);
            existing.name = name;
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Material Plaster() => AssetDatabase.LoadAssetAtPath<Material>(L2Mat + "Plaster.mat") ?? M("LighthouseWhite");
        private static Material Roof() => AssetDatabase.LoadAssetAtPath<Material>(L2Mat + "Roof.mat") ?? M("LighthouseRed");

        private static readonly Dictionary<string, Color> Colours = new()
        {
            { "GrassBlade", new Color(.42f, .74f, .34f) }, { "GrassBladeDark", new Color(.29f, .57f, .28f) }, { "GrassBladeLight", new Color(.6f, .82f, .36f) },
            { "FlowerStem", new Color(.27f, .56f, .25f) }, { "FlowerMiddle", new Color(1f, .78f, .2f) },
            { "FlowerRed", new Color(.95f, .27f, .3f) }, { "FlowerYellow", new Color(1f, .88f, .25f) }, { "FlowerWhite", new Color(.98f, .97f, .94f) },
            { "FlowerPink", new Color(1f, .58f, .78f) }, { "FlowerPurple", new Color(.64f, .45f, .95f) }, { "FlowerBlue", new Color(.38f, .62f, 1f) },
            { "FlowerOrange", new Color(1f, .6f, .22f) },
            { "Rock", new Color(.6f, .6f, .63f) }, { "RockDark", new Color(.45f, .46f, .5f) }, { "Moss", new Color(.38f, .64f, .33f) },
            { "Bush", new Color(.26f, .56f, .3f) }, { "BushLight", new Color(.36f, .68f, .33f) },
            { "MushroomStem", new Color(.97f, .93f, .84f) }, { "MushroomCap", new Color(.92f, .22f, .2f) }, { "MushroomSpot", new Color(1f, 1f, .97f) },
            { "Wood", new Color(.6f, .42f, .24f) }, { "WoodDark", new Color(.4f, .27f, .16f) }, { "Stone", new Color(.62f, .6f, .58f) },
            { "Sail", new Color(.97f, .94f, .86f) }, { "LighthouseWhite", new Color(.97f, .97f, .95f) }, { "LighthouseRed", new Color(.88f, .22f, .22f) },
            { "Iron", new Color(.25f, .26f, .3f) }, { "BalloonA", new Color(1f, .45f, .38f) }, { "BalloonB", new Color(1f, .86f, .38f) },
            { "Cloud", new Color(1f, 1f, 1f) },
        };

        private static Material M(string key, bool beam = false)
        {
            if (Mats.TryGetValue(key, out var cached)) return cached;
            string path = MatFolder + "/Decor_" + key + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (beam)
            {
                if (isNew) mat = new Material(Shader.Find("DontDropIt/LightBeam"));
                mat.SetColor("_Color", new Color(1f, .9f, .55f, .35f));
            }
            else
            {
                if (isNew) mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                if (key == "WindowGlow")
                {
                    mat.SetColor("_BaseColor", new Color(1f, .85f, .45f));
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(1f, .78f, .35f) * 1.6f);
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                else mat.SetColor("_BaseColor", Colours.TryGetValue(key, out var c) ? c : Color.magenta);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", .08f);
                if (key == "Cloud")
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(.42f, .45f, .5f));
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                // Thin blades and leaves are single planes: draw both sides.
                if (key.StartsWith("GrassBlade") || key == "FlowerStem") { mat.SetFloat("_Cull", 0f); mat.doubleSidedGI = true; }
            }
            mat.enableInstancing = true;
            if (isNew) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
            Mats[key] = mat;
            return mat;
        }

        private static Material Fog(string key, int mode, Color colour)
        {
            if (Mats.TryGetValue(key, out var cached)) return cached;
            string path = MatFolder + "/Decor_" + key + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(Shader.Find("DontDropIt/HeightFog"));
            mat.SetColor("_Color", colour);
            mat.SetFloat("_Mode", mode);
            mat.SetFloat("_NoiseScale", mode == 0 ? .08f : .012f);
            mat.SetFloat("_NoiseSpeed", mode == 0 ? .5f : .25f);
            mat.SetFloat("_NoiseStrength", .55f);
            mat.SetFloat("_SoftDistance", mode == 0 ? 3f : 8f);
            mat.SetFloat("_CameraFade", 8f);
            if (isNew) AssetDatabase.CreateAsset(mat, path); else EditorUtility.SetDirty(mat);
            Mats[key] = mat;
            return mat;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int cut = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, cut));
            AssetDatabase.CreateFolder(path.Substring(0, cut), path.Substring(cut + 1));
        }
    }
}
#endif
