using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Map Hitbox Setup Utility
    ///
    /// Automatically detects map models inside the "Island" parent GameObject (and any selected parent),
    /// generates accurate non-convex MeshColliders for every mesh, and configures "เกาะปีน" (climbing island)
    /// to be fully climbable with the Wallclimb mechanics.
    ///
    /// Key Features:
    /// 1. Automatic on-compile execution: Hooks [InitializeOnLoadMethod] and sceneOpened so Island is automatically configured.
    /// 2. Accurate Hitboxes: Non-convex MeshColliders matching exact FBX model geometry (supports ledges, caves, crevices).
    /// 3. Climbable Setup: Places เกาะปีน on Layer 3 ("Wall!"), which is explicitly targeted by Wallclimb._wallLayers.
    /// 4. Auto-Detection: Searches for "เกาะปีน" by GameObject name, mesh name, and asset path.
    /// </summary>
    [InitializeOnLoad]
    public static class MapHitboxSetup
    {
        // TagManager Layer 3 is "Wall!" in this project, which Wallclimb explicitly scans via _wallLayers (1 << 3).
        public const string CLIMB_LAYER_NAME = "Wall!";
        public const int DEFAULT_CLIMB_LAYER = 3;

        static MapHitboxSetup()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        [InitializeOnLoadMethod]
        private static void InitOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                RunAutoSetup(interactive: false);
            };
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            EditorApplication.delayCall += () =>
            {
                RunAutoSetup(interactive: false);
            };
        }

        /// <summary>
        /// Retrieves the layer index for climbable surfaces ("Wall!" layer 3, fallback to 0).
        /// </summary>
        public static int GetClimbableLayer()
        {
            int layer = LayerMask.NameToLayer(CLIMB_LAYER_NAME);
            if (layer != -1) return layer;
            layer = LayerMask.NameToLayer("Climbable");
            if (layer != -1) return layer;
            return DEFAULT_CLIMB_LAYER;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // MENU ACTIONS
        // ─────────────────────────────────────────────────────────────────────────

        [MenuItem("CoopGame/Map/Setup Hitboxes for 'Island' (Auto-Find & Make เกาะปีน Climbable)", priority = 1)]
        public static void MenuSetupIslandHitboxes()
        {
            RunAutoSetup(interactive: true);
        }

        [MenuItem("CoopGame/Map/Add Mesh Colliders to Selected Parent", priority = 2)]
        public static void MenuAddMeshCollidersToSelected()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog(
                    "No Selection",
                    "Please select a parent GameObject in the Hierarchy that contains your map FBX models.",
                    "OK");
                return;
            }

            ProcessParent(selected, interactive: true);
        }

        [MenuItem("CoopGame/Map/Add Mesh Colliders to Selected Parent", validate = true)]
        private static bool MenuAddMeshCollidersToSelected_Validate() => Selection.activeGameObject != null;

        [MenuItem("CoopGame/Map/Make Selected as Climbable Island (Wall! Layer)", priority = 3)]
        public static void MenuMarkSelectedClimbable()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return;

            int climbLayer = GetClimbableLayer();
            Undo.SetCurrentGroupName("Mark Climbable Island");
            int undoGroup = Undo.GetCurrentGroup();

            SetLayerRecursive(selected.transform, climbLayer, undoGroup);

            // Ensure it has a MeshCollider if it has a MeshFilter
            MeshFilter mf = selected.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && selected.GetComponent<Collider>() == null)
            {
                MeshCollider mc = Undo.AddComponent<MeshCollider>(selected);
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
            }

            Undo.CollapseUndoOperations(undoGroup);
            MarkSceneDirtyAndSave(selected.scene);

            EditorUtility.DisplayDialog(
                "Climbable Island Set",
                $"\"{selected.name}\" and all children are now on Layer {climbLayer} (\"{LayerMask.LayerToName(climbLayer)}\") and ready for climbing!",
                "OK");
        }

        [MenuItem("CoopGame/Map/Make Selected as Climbable Island (Wall! Layer)", validate = true)]
        private static bool MenuMarkSelectedClimbable_Validate() => Selection.activeGameObject != null;

        [MenuItem("CoopGame/Map/Remove All Mesh Colliders from Selected Parent", priority = 20)]
        public static void MenuRemoveMeshColliders()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null) return;

            MeshCollider[] cols = selected.GetComponentsInChildren<MeshCollider>(true);
            if (cols.Length == 0)
            {
                EditorUtility.DisplayDialog("Nothing to Remove",
                    $"No MeshColliders found under \"{selected.name}\".", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Remove Map Mesh Colliders");
            int undoGroup = Undo.GetCurrentGroup();
            foreach (var mc in cols)
            {
                Undo.DestroyObjectImmediate(mc);
            }
            Undo.CollapseUndoOperations(undoGroup);

            MarkSceneDirtyAndSave(selected.scene);
            Debug.Log($"[MapHitboxSetup] Removed {cols.Length} MeshColliders from \"{selected.name}\".");
        }

        [MenuItem("CoopGame/Map/Remove All Mesh Colliders from Selected Parent", validate = true)]
        private static bool MenuRemoveMeshColliders_Validate() => Selection.activeGameObject != null;

        // ─────────────────────────────────────────────────────────────────────────
        // CORE PROCESSING
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Finds the "Island" parent GameObject in open scenes or Level01, sets hitboxes on all children,
        /// and ensures all instances of เกาะปีน are climbable.
        /// </summary>
        public static void RunAutoSetup(bool interactive)
        {
            GameObject islandObj = FindIslandObject();

            if (islandObj == null)
            {
                // Try checking if Level01.unity exists and is not loaded
                string level01Path = "Assets/Scenes/Level01.unity";
                if (!interactive && File.Exists(level01Path))
                {
                    // Check if open scenes contain it
                    Scene active = SceneManager.GetActiveScene();
                    if (!active.path.Equals(level01Path, StringComparison.OrdinalIgnoreCase))
                    {
                        // Don't disturb active editing during background check
                        return;
                    }
                }

                if (interactive)
                {
                    // If interactive and not found in active scene, offer to open Level01
                    if (File.Exists(level01Path))
                    {
                        bool open = EditorUtility.DisplayDialog(
                            "Island Not in Current Scene",
                            "GameObject \"Island\" was not found in the currently active scene.\n\nWould you like to open \"Assets/Scenes/Level01.unity\" and set it up?",
                            "Open & Setup", "Cancel");
                        if (open)
                        {
                            Scene scene = EditorSceneManager.OpenScene(level01Path, OpenSceneMode.Single);
                            islandObj = FindIslandObject();
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }

            if (islandObj == null)
            {
                if (interactive)
                {
                    EditorUtility.DisplayDialog("Not Found", "Could not find a GameObject named \"Island\". Please select the parent manually and use 'Add Mesh Colliders to Selected Parent'.", "OK");
                }
                return;
            }

            // Process the Island parent
            ProcessParent(islandObj, interactive);

            // Also search scene for any standalone เกาะปีน instances outside of Island
            ProcessAllClimbingIslandsInScene(islandObj.scene, interactive);
        }

        /// <summary>
        /// Finds GameObject named "Island" (case-insensitive) across all loaded scenes, searching roots and hierarchy.
        /// </summary>
        public static GameObject FindIslandObject()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.name.Equals("Island", StringComparison.OrdinalIgnoreCase))
                        return root;

                    // Also search direct children
                    Transform found = root.transform.Find("Island");
                    if (found != null)
                        return found.gameObject;
                }
            }
            return null;
        }

        /// <summary>
        /// Processes a parent GameObject: adds non-convex MeshColliders to all MeshFilters,
        /// and marks any "เกาะปีน" as climbable (Layer 3: Wall!).
        /// </summary>
        public static void ProcessParent(GameObject parent, bool interactive)
        {
            if (parent == null) return;

            MeshFilter[] filters = parent.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0)
            {
                if (interactive)
                {
                    EditorUtility.DisplayDialog("Nothing to Process", $"No MeshFilter components found under \"{parent.name}\".", "OK");
                }
                return;
            }

            int added = 0;
            int skipped = 0;
            int climbableCount = 0;
            int climbLayer = GetClimbableLayer();

            Undo.SetCurrentGroupName("Setup Map Hitboxes & Climbable Island");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (MeshFilter mf in filters)
            {
                if (mf.sharedMesh == null)
                {
                    skipped++;
                    continue;
                }

                GameObject go = mf.gameObject;
                bool isClimb = IsClimbIsland(go, mf.sharedMesh);

                // Add MeshCollider if missing
                MeshCollider mc = go.GetComponent<MeshCollider>();
                if (mc == null)
                {
                    // Check if it has another collider
                    Collider existingCol = go.GetComponent<Collider>();
                    if (existingCol == null)
                    {
                        Undo.RecordObject(go, "Add MeshCollider");
                        mc = Undo.AddComponent<MeshCollider>(go);
                        mc.sharedMesh = mf.sharedMesh;
                        mc.convex = false; // Non-convex = accurate 1:1 geometry with walls and ledges
                        mc.cookingOptions =
                            MeshColliderCookingOptions.EnableMeshCleaning |
                            MeshColliderCookingOptions.WeldColocatedVertices |
                            MeshColliderCookingOptions.UseFastMidphase;
                        added++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                else
                {
                    // Ensure sharedMesh is assigned and convex is false
                    if (mc.sharedMesh == null)
                    {
                        Undo.RecordObject(mc, "Fix MeshCollider sharedMesh");
                        mc.sharedMesh = mf.sharedMesh;
                    }
                    if (mc.convex)
                    {
                        Undo.RecordObject(mc, "Set Non-Convex MeshCollider");
                        mc.convex = false;
                    }
                }

                // If this is เกาะปีน, set to climbable layer
                if (isClimb)
                {
                    SetLayerRecursive(go.transform, climbLayer, undoGroup);
                    climbableCount++;
                }

                EditorUtility.SetDirty(go);
            }

            // Also check all direct children of parent for เกาะปีน
            foreach (Transform child in parent.transform)
            {
                if (IsClimbIsland(child.gameObject, null))
                {
                    SetLayerRecursive(child, climbLayer, undoGroup);
                }
            }

            Undo.CollapseUndoOperations(undoGroup);
            MarkSceneDirtyAndSave(parent.scene);

            string logMsg = $"[MapHitboxSetup] Finished \"{parent.name}\": Added {added} MeshColliders, {skipped} existing/skipped, {climbableCount} เกาะปีน set to Layer {climbLayer} (\"{LayerMask.LayerToName(climbLayer)}\").";
            Debug.Log(logMsg);

            if (interactive)
            {
                EditorUtility.DisplayDialog(
                    "Setup Complete!",
                    $"Successfully updated \"{parent.name}\":\n\n" +
                    $"• Added MeshColliders: {added}\n" +
                    $"• Already had colliders / skipped: {skipped}\n" +
                    $"• เกาะปีน climbing meshes: {climbableCount} configured on Layer {climbLayer} (\"{LayerMask.LayerToName(climbLayer)}\").\n\n" +
                    "All hitboxes are accurate non-convex colliders and เกาะปีน is ready to climb!",
                    "OK");
            }
        }

        /// <summary>
        /// Finds and configures any other เกาะปีน instances in the scene outside of the Island parent.
        /// </summary>
        private static void ProcessAllClimbingIslandsInScene(Scene scene, bool interactive)
        {
            if (!scene.isLoaded) return;

            int climbLayer = GetClimbableLayer();
            int standaloneClimbs = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
                foreach (MeshFilter mf in filters)
                {
                    if (mf == null || mf.sharedMesh == null) continue;

                    if (IsClimbIsland(mf.gameObject, mf.sharedMesh))
                    {
                        // Ensure MeshCollider exists
                        MeshCollider mc = mf.gameObject.GetComponent<MeshCollider>();
                        if (mc == null && mf.gameObject.GetComponent<Collider>() == null)
                        {
                            mc = mf.gameObject.AddComponent<MeshCollider>();
                            mc.sharedMesh = mf.sharedMesh;
                            mc.convex = false;
                        }

                        // Set layer to climbable
                        SetLayerRecursive(mf.transform, climbLayer, -1);
                        EditorUtility.SetDirty(mf.gameObject);
                        standaloneClimbs++;
                    }
                }
            }

            if (standaloneClimbs > 0)
            {
                MarkSceneDirtyAndSave(scene);
                Debug.Log($"[MapHitboxSetup] Ensured {standaloneClimbs} standalone เกาะปีน instances in scene \"{scene.name}\" are on climbable Layer {climbLayer}.");
            }
        }

        /// <summary>
        /// Determines if a GameObject or mesh represents "เกาะปีน" (climbing island).
        /// </summary>
        public static bool IsClimbIsland(GameObject go, Mesh mesh)
        {
            if (go != null)
            {
                string name = go.name;
                if (name.Contains("\u0e40\u0e01\u0e32\u0e30\u0e1b\u0e35\u0e19") || name.Contains("เกาะปีน"))
                    return true;

                Transform t = go.transform.parent;
                while (t != null)
                {
                    if (t.name.Contains("\u0e40\u0e01\u0e32\u0e30\u0e1b\u0e35\u0e19") || t.name.Contains("เกาะปีน"))
                        return true;
                    t = t.parent;
                }
            }

            if (mesh != null)
            {
                string meshName = mesh.name;
                if (meshName.Contains("\u0e40\u0e01\u0e32\u0e30\u0e1b\u0e35\u0e19") || meshName.Contains("เกาะปีน"))
                    return true;

                string path = AssetDatabase.GetAssetPath(mesh);
                if (!string.IsNullOrEmpty(path) && (path.Contains("\u0e40\u0e01\u0e32\u0e30\u0e1b\u0e35\u0e19") || path.Contains("เกาะปีน")))
                    return true;
            }

            return false;
        }

        public static void SetLayerRecursive(Transform root, int layer, int undoGroup)
        {
            if (root == null) return;
            if (undoGroup != -1)
                Undo.RecordObject(root.gameObject, "Set Climbable Layer");
            root.gameObject.layer = layer;

            foreach (Transform child in root)
            {
                SetLayerRecursive(child, layer, undoGroup);
            }
        }

        private static void MarkSceneDirtyAndSave(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!Application.isPlaying)
            {
                EditorSceneManager.SaveScene(scene);
            }
        }
    }
}
