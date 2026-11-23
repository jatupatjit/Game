using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ThornColliderFixer : EditorWindow
{
    [MenuItem("Tools/Fix Thorn Colliders")]
    public static void ShowWindow()
    {
        GetWindow<ThornColliderFixer>("Thorn Collider Fixer");
    }

    [MenuItem("Tools/Fix Thorn Colliders in Level 02 George")]
    public static void FixInLevel02George()
    {
        FixThornsInScene("Level02 George.unity");
    }

    [MenuItem("Tools/Fix All Thorn Colliders (All Scenes)")]
    public static void FixAllScenesMenu()
    {
        FixAllScenes();
    }

    [InitializeOnLoadMethod]
    private static void OnProjectLoaded()
    {
        EditorApplication.delayCall += () =>
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name.IndexOf("Level02 George", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                int count = FixThornsInOpenedScene(activeScene);
                if (count > 0)
                {
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    EditorSceneManager.SaveScene(activeScene);
                    Debug.Log("[ThornColliderFixer] Automatically fixed " + count + " thorn hitboxes in " + activeScene.name + ".");
                }
            }
        };
    }

    private void OnGUI()
    {
        GUILayout.Label("Thorn Collider Fixer (หนามไม้)", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (GUILayout.Button("Fix All Thorns in Level 02 George", GUILayout.Height(32)))
        {
            FixInLevel02George();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Fix All Thorns in Active Scene", GUILayout.Height(30)))
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                int count = FixThornsInOpenedScene(activeScene);
                if (count > 0)
                {
                    EditorSceneManager.MarkSceneDirty(activeScene);
                    EditorSceneManager.SaveScene(activeScene);
                    Debug.Log("[ThornColliderFixer] Fixed " + count + " thorn hitboxes in active scene: " + activeScene.name);
                }
                else
                {
                    Debug.Log("[ThornColliderFixer] All thorns in " + activeScene.name + " already have accurate hitboxes.");
                }
            }
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("Fix All Thorns Across All Scenes", GUILayout.Height(30)))
        {
            FixAllScenes();
        }
    }

    public static void FixAllScenes()
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        int totalFixed = 0;
        foreach (string guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int fixedCount = FixThornsInOpenedScene(scene);
            if (fixedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[ThornColliderFixer] Fixed " + fixedCount + " thorn hitboxes in scene: " + path);
                totalFixed += fixedCount;
            }
        }
        Debug.Log("[ThornColliderFixer] Finished. Fixed " + totalFixed + " thorn hitboxes across all scenes.");
    }

    public static void FixThornsInScene(string sceneName)
    {
        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        foreach (string guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(sceneName, StringComparison.OrdinalIgnoreCase)) continue;

            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int fixedCount = FixThornsInOpenedScene(scene);
            if (fixedCount > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[ThornColliderFixer] Fixed " + fixedCount + " thorn hitboxes in scene: " + path);
            }
            else
            {
                Debug.Log("[ThornColliderFixer] All thorns in " + sceneName + " already have accurate hitboxes.");
            }
            return;
        }
        Debug.LogWarning("[ThornColliderFixer] Scene " + sceneName + " not found.");
    }

    public static int FixThornsInOpenedScene(Scene scene)
    {
        int fixedParts = 0;
        var rootObjects = scene.GetRootGameObjects();

        foreach (var root in rootObjects)
        {
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (!IsThornMesh(mf)) continue;

                var col = mf.GetComponent<MeshCollider>();
                if (col == null)
                {
                    col = Undo.AddComponent<MeshCollider>(mf.gameObject);
                    col.sharedMesh = mf.sharedMesh;
                    col.convex = true;
                    col.isTrigger = false;
                    fixedParts++;
                }
                else
                {
                    bool modified = false;
                    if (col.sharedMesh != mf.sharedMesh)
                    {
                        Undo.RecordObject(col, "Fix Thorn Collider Mesh");
                        col.sharedMesh = mf.sharedMesh;
                        modified = true;
                    }
                    if (!col.convex)
                    {
                        Undo.RecordObject(col, "Set Thorn Convex");
                        col.convex = true;
                        modified = true;
                    }
                    if (col.isTrigger)
                    {
                        Undo.RecordObject(col, "Set Thorn Solid");
                        col.isTrigger = false;
                        modified = true;
                    }
                    if (modified) fixedParts++;
                }
            }

            // Remove any misplaced BoxCollider directly on empty thorn parent roots
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if ((t.name.Contains("หนามไม้") || t.name.IndexOf("WoodenSpikes", StringComparison.OrdinalIgnoreCase) >= 0)
                    && t.GetComponent<MeshFilter>() == null)
                {
                    var misplacedColliders = t.GetComponents<Collider>();
                    foreach (var c in misplacedColliders)
                    {
                        Undo.DestroyObjectImmediate(c);
                        fixedParts++;
                    }
                }
            }
        }

        return fixedParts;
    }

    private static bool IsThornMesh(MeshFilter mf)
    {
        if (mf == null || mf.sharedMesh == null) return false;

        string meshPath = AssetDatabase.GetAssetPath(mf.sharedMesh);
        if (!string.IsNullOrEmpty(meshPath) && meshPath.Contains("หนามไม้"))
            return true;

        Transform curr = mf.transform;
        while (curr != null)
        {
            if (curr.name.Contains("หนามไม้") || curr.name.IndexOf("WoodenSpikes", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            curr = curr.parent;
        }

        return false;
    }
}
