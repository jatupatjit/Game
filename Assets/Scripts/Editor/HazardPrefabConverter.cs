#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CoopGame.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Idempotent: converts hazard objects in Level01/Level02 into Hazard_* prefab instances without recreating them
    /// (PrefabUtility.ConvertToPrefabInstance keeps the original objects, so scene references stay valid and every
    /// differing value is recorded as an instance override).
    /// </summary>
    public static class HazardPrefabConverter
    {
        private const string Folder = "Assets/Prefabs/Hazards";
        private static readonly string[] Scenes = { "Assets/Scenes/Level01.unity", "Assets/Scenes/Level02.unity" };
        private static readonly string SnapshotDir = "Temp/HazardSnapshots";

        private static IEnumerable<Component> HazardsIn(Scene s)
        {
            foreach (var root in s.GetRootGameObjects())
            {
                foreach (var c in root.GetComponentsInChildren<NetworkBehaviour>(true))
                    if (c is NetworkRouteHazard || c is TimedCargoTrap || c is RotatingLogHazard || c is CollapsingSteppingStone) yield return c;
            }
        }

        private static string Base(Component c)
        {
            if (c is NetworkRouteHazard)
            {
                var kind = new SerializedObject(c).FindProperty("_kind").enumNames[new SerializedObject(c).FindProperty("_kind").enumValueIndex];
                return kind;
            }
            if (c is TimedCargoTrap) return "TimedCargoTrap";
            if (c is RotatingLogHazard) return "RotatingLog";
            return "CollapsingStone";
        }

        private static string Signature(Component c)
        {
            var root = c.gameObject.transform;
            var parts = new List<string> { c.GetType().Name + ":" + Base(c) };
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string rel = AnimationUtility.CalculateTransformPath(t, root);
                var comps = t.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().Name);
                parts.Add(rel + "[" + string.Join(",", comps) + "]");
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }

        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        private static string CleanName(string n) => new string(n.Where(char.IsLetter).ToArray());

        [MenuItem("Tools/Expedition/Convert Hazards To Prefabs")]
        public static void Convert()
        {
            Directory.CreateDirectory(SnapshotDir);
            AssetDatabase.Refresh();
            var log = new StringBuilder("[HazardPrefabConverter]\n");
            var sigToPrefab = new Dictionary<string, string>();
            foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                if (path.Contains("/Medieval/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var c = go.GetComponents<NetworkBehaviour>().FirstOrDefault(x => x is NetworkRouteHazard || x is TimedCargoTrap || x is RotatingLogHazard || x is CollapsingSteppingStone);
                if (c != null) sigToPrefab[Signature(c)] = path;
            }

            foreach (var scenePath in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var snapFile = System.IO.Path.Combine(SnapshotDir, System.IO.Path.GetFileNameWithoutExtension(scenePath) + ".txt");
                var hazards = HazardsIn(scene).ToList();
                if (!File.Exists(snapFile)) File.WriteAllText(snapFile, Snapshot(hazards));
                log.AppendLine("== " + scenePath + " hazards=" + hazards.Count);
                var settings = new ConvertToPrefabInstanceSettings
                {
                    changeRootNameToAssetName = false,
                    componentsNotMatchedBecomesOverride = true,
                    gameObjectsNotMatchedBecomesOverride = true,
                    objectMatchMode = ObjectMatchMode.ByHierarchy,
                    recordPropertyOverridesOfMatches = true,
                    logInfo = false
                };
                foreach (var c in hazards)
                {
                    var go = c.gameObject;
                    if (PrefabUtility.IsPartOfPrefabInstance(go)) { log.AppendLine("  skip (already prefab) " + Path(go.transform)); continue; }
                    string sig = Signature(c);
                    if (!sigToPrefab.TryGetValue(sig, out var prefabPath))
                    {
                        string b = "Hazard_" + Base(c);
                        string name = File.Exists(Folder + "/" + b + ".prefab") ? b + "_" + CleanName(go.name) : b;
                        prefabPath = Folder + "/" + name + ".prefab";
                        var temp = UnityEngine.Object.Instantiate(go);
                        temp.name = name;
                        temp.transform.SetParent(null);
                        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        PrefabUtility.SaveAsPrefabAsset(temp, prefabPath);
                        UnityEngine.Object.DestroyImmediate(temp);
                        sigToPrefab[sig] = prefabPath;
                        log.AppendLine("  created " + prefabPath);
                    }
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    string before = Path(go.transform);
                    try
                    {
                        PrefabUtility.ConvertToPrefabInstance(go, asset, settings, InteractionMode.AutomatedAction);
                        log.AppendLine("  " + before + " -> " + prefabPath);
                    }
                    catch (Exception e) { log.AppendLine("  UNCONVERTED " + before + ": " + e.Message); }
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(SnapshotDir + "/convert.log", log.ToString()); Debug.Log(log.ToString());
        }

        /// <summary>Compares post-conversion values with the pre-conversion snapshot and reports counts and integrity.</summary>
        [MenuItem("Tools/Expedition/Verify Hazard Prefabs")]
        public static void Verify()
        {
            var sb = new StringBuilder("[HazardPrefabVerify]\n");
            foreach (var scenePath in Scenes)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var hazards = HazardsIn(scene).ToList();
                string snapFile = System.IO.Path.Combine(SnapshotDir, System.IO.Path.GetFileNameWithoutExtension(scenePath) + ".txt");
                var before = File.Exists(snapFile) ? File.ReadAllText(snapFile).Split('\n').Where(l => l.Length > 0).ToList() : new List<string>();
                var after = Snapshot(hazards).Split('\n').Where(l => l.Length > 0).ToList();
                int diff = 0;
                foreach (var l in before.Except(after)) { diff++; sb.AppendLine("  DIFF before: " + l); }
                foreach (var l in after.Except(before)) sb.AppendLine("  DIFF after : " + l);
                int noLink = hazards.Count(h => !PrefabUtility.IsPartOfPrefabInstance(h.gameObject));
                int missing = 0;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                sb.AppendLine("== " + scenePath + " hazards=" + hazards.Count + " snapshotLines before/after=" + before.Count + "/" + after.Count +
                    " differingLines=" + diff + " noPrefabLink=" + noLink + " missingScripts=" + missing);
                foreach (var g in hazards.GroupBy(h => h.GetType().Name + ":" + Base(h) + " -> " +
                    (PrefabUtility.IsPartOfPrefabInstance(h.gameObject) ? AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(h.gameObject)) : "NONE")))
                    sb.AppendLine("  " + g.Key + " x" + g.Count());
            }
            File.WriteAllText(SnapshotDir + "/verify.log", sb.ToString()); Debug.Log(sb.ToString());
        }

        private static string Snapshot(List<Component> hazards)
        {
            var sb = new StringBuilder();
            foreach (var c in hazards.OrderBy(h => Path(h.transform), StringComparer.Ordinal))
            {
                string p = Path(c.transform);
                var t = c.transform;
                sb.AppendLine(p + "#T " + t.position.ToString("F4") + t.rotation.ToString("F4") + t.lossyScale.ToString("F4") +
                    " children=" + t.GetComponentsInChildren<Transform>(true).Length + " sib=" + t.GetSiblingIndex());
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                for (bool enter = true; it.NextVisible(enter); enter = false)
                {
                    if (it.propertyPath == "m_Script") continue;
                    string v;
                    switch (it.propertyType)
                    {
                        case SerializedPropertyType.ObjectReference:
                            var o = it.objectReferenceValue;
                            v = o == null ? "null" : (o is Component oc ? Path(oc.transform) + ":" + o.GetType().Name : o is GameObject og ? Path(og.transform) : o.name);
                            break;
                        case SerializedPropertyType.Float: v = it.floatValue.ToString("R"); break;
                        case SerializedPropertyType.Integer: v = it.intValue.ToString(); break;
                        case SerializedPropertyType.Boolean: v = it.boolValue.ToString(); break;
                        case SerializedPropertyType.String: v = it.stringValue; break;
                        case SerializedPropertyType.Enum: v = it.enumValueIndex.ToString(); break;
                        case SerializedPropertyType.Vector3: v = it.vector3Value.ToString("R"); break;
                        case SerializedPropertyType.Color: v = it.colorValue.ToString(); break;
                        default: v = it.propertyType.ToString(); break;
                    }
                    sb.AppendLine(p + "#" + c.GetType().Name + "." + it.propertyPath + "=" + v);
                }
            }
            return sb.ToString();
        }
    }
}
#endif
