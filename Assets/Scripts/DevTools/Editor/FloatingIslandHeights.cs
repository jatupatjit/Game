#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FloatingIslandHeights
{
    static readonly string[] Names = { "01_StartVillage", "02_RelayIsland", "03_ForestSlalom", "04_TeamGate", "05_RelayRest", "06_WallRelay", "07_TrapCrossing", "08_HighSwitch", "09_FinalDelivery" };
    static readonly float[] Heights = { 0, 2, 4, 1, 3, 6, 4.5f, 8, 10 };
    const string Folder = "Assets/Environment/FloatingCoop";

    [MenuItem("Tools/Expedition/Apply Island Height Variation")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if (!scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/Scenes/Level01.unity", OpenSceneMode.Additive);
        var route = scene.GetRootGameObjects().Single(g => g.name == "FloatingCoopRoute").transform;
        if (route.Find("ChallengeUpgrade_v2") != null) throw new InvalidOperationException("Challenge layout contains a cut bridge deck. Refit its opening together with the traps before changing heights.");
        var all = route.GetComponentsInChildren<Transform>(true);
        var islands = Names.Select(n => all.Single(t => t.name == n)).ToArray();
        var old = islands.Select(t => t.position).ToArray();
        var bridges = all.Where(t => t.Find("SolidCargoDeck") != null).OrderBy(t => t.name).ToArray();
        if (bridges.Length != 8) throw new InvalidOperationException("Expected eight existing bridges.");
        Directory.CreateDirectory("E:/Unity/Verification/IslandHeights-20261010");
        string backup = "E:/Unity/Verification/IslandHeights-20261010/Level01.Before-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
        if (!EditorSceneManager.SaveScene(scene, backup, true)) throw new IOException("Backup failed.");
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Island height variation");
        try
        {
            var props = all.Where(t => t.parent == route || t.parent != null && t.parent.name == "ExtendedCoopRoute").ToArray();
            foreach (var t in props)
            {
                if (t.name == "ExtendedCoopRoute" || bridges.Contains(t) || t.name.StartsWith("FlameTrap_") || t.name == "BridgeChicanePost") continue;
                int index = t.name == "WallSwitch_A" ? 5 : t.name == "WallSwitch_B" ? 7 : Nearest(t.position, old);
                Move(t, Vector3.up * (Heights[index] - old[index].y));
            }
            foreach (var g in scene.GetRootGameObjects())
                if (g.name == "DeliveryZone" || g.name == "DeliveryExitPortal_Level02") Move(g.transform, Vector3.up * (Heights[8] - old[8].y));
            for (int i = 0; i < bridges.Length; i++) ShapeBridge(bridges[i], Heights[i], Heights[i + 1]);
            foreach (var trap in props.Where(t => t.name.StartsWith("FlameTrap_")))
            {
                var bridge = bridges[trap.name.EndsWith("A") ? 5 : 7];
                Undo.RecordObject(trap, "Align bridge trap");
                trap.position = bridge.position;
                var deck = bridge.Find("SolidCargoDeck").GetComponent<MeshFilter>().sharedMesh;
                float length = deck.bounds.size.z;
                int i = Array.IndexOf(bridges, bridge);
                var forward = bridge.forward * length + Vector3.up * (Heights[i + 1] - Heights[i]);
                trap.rotation = Quaternion.LookRotation(forward);
            }
            foreach (var post in props.Where(t => t.name == "BridgeChicanePost"))
            {
                var b = bridges[4]; var p = b.InverseTransformPoint(post.position);
                float length = b.Find("SolidCargoDeck").GetComponent<MeshFilter>().sharedMesh.bounds.size.z;
                Undo.RecordObject(post, "Align bridge obstacle");
                post.position = b.TransformPoint(new Vector3(p.x, Surface(p.z, length, Heights[4], Heights[5]) + .75f, p.z));
            }
            EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
            SceneManager.SetActiveScene(scene); Selection.activeGameObject = route.gameObject;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.LookAt(new Vector3(0, 4, 125), Quaternion.Euler(35, -45, 0), 170);
            Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    static int Nearest(Vector3 p, Vector3[] centers) => Enumerable.Range(0, centers.Length).OrderBy(i => new Vector2(p.x - centers[i].x, p.z - centers[i].z).sqrMagnitude).First();
    static void Move(Transform t, Vector3 delta) { Undo.RecordObject(t, "Raise island contents"); t.position += delta; }
    // Flat approach landings prevent raised lips where a sloping bridge overlaps an island.
    static float Landing(float length) => Mathf.Min(3.5f, length * .3f);
    static float Surface(float z, float length, float a, float b) => Mathf.Lerp(a, b, Mathf.InverseLerp(-length * .5f + Landing(length), length * .5f - Landing(length), z)) - (a + b) * .5f;

    public static void RefitApproaches()
    {
        if(EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        var route=scene.GetRootGameObjects().Single(g=>g.name=="FloatingCoopRoute");
        if (route.transform.Find("ChallengeUpgrade_v2") != null) throw new InvalidOperationException("Challenge layout contains a cut bridge deck. Preserve the trap opening when refitting approaches.");
        Directory.CreateDirectory("E:/Unity/Verification/IslandSeams-20261010");
        if(!EditorSceneManager.SaveScene(scene,"E:/Unity/Verification/IslandSeams-20261010/Level01.Before-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity",true))throw new IOException("Backup failed.");
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup();
        try
        {
            var all=route.GetComponentsInChildren<Transform>(true);
            var bridges=all.Where(t=>t.Find("SolidCargoDeck")!=null).OrderBy(t=>t.name).ToArray();
            if(bridges.Length!=8)throw new InvalidOperationException("Expected eight bridges.");
            for(int i=0;i<8;i++)ShapeBridge(bridges[i],Heights[i],Heights[i+1]);
            foreach(var trap in all.Where(t=>t.name=="FlameTrap_A" || t.name=="FlameTrap_B"))
            {
                int i=trap.name.EndsWith("A")?5:7; var b=bridges[i]; float length=b.Find("SolidCargoDeck").GetComponent<MeshFilter>().sharedMesh.bounds.size.z;
                Undo.RecordObject(trap,"Align trap with central slope");
                trap.rotation=Quaternion.LookRotation(b.forward*((length-2*Landing(length))*b.lossyScale.z)+Vector3.up*(Heights[i+1]-Heights[i]));
            }
            EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); if(!EditorSceneManager.SaveScene(scene))throw new IOException("Save failed."); Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    static void ShapeBridge(Transform bridge, float a, float b)
    {
        var deck = bridge.Find("SolidCargoDeck");
        float length = deck.GetComponent<MeshFilter>().sharedMesh.name.StartsWith("HeightDeck_") ? deck.GetComponent<MeshFilter>().sharedMesh.bounds.size.z : deck.localScale.z;
        float width = deck.localScale.x == 1 ? deck.GetComponent<MeshFilter>().sharedMesh.bounds.size.x : deck.localScale.x;
        Undo.RecordObject(bridge, "Raise bridge"); var pos = bridge.position; pos.y = (a + b) * .5f; bridge.position = pos;
        float[] zs = { -length * .5f, -length * .5f + Landing(length), length * .5f - Landing(length), length * .5f };
        var vertices = new System.Collections.Generic.List<Vector3>(); var triangles = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 3; i++)
        {
            float z0 = zs[i], z1 = zs[i + 1], y0 = Surface(z0, length, a, b) + .04f, y1 = Surface(z1, length, a, b) + .04f;
            int n = vertices.Count;
            vertices.AddRange(new[] { new Vector3(-width/2,y0,z0), new Vector3(width/2,y0,z0), new Vector3(width/2,y1,z1), new Vector3(-width/2,y1,z1), new Vector3(-width/2,y0-.44f,z0), new Vector3(width/2,y0-.44f,z0), new Vector3(width/2,y1-.44f,z1), new Vector3(-width/2,y1-.44f,z1) });
            int[] faces = {0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
            triangles.AddRange(faces.Select(x => x+n));
        }
        string path = Folder + "/HeightDeck_" + bridge.name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = new Mesh { name = "HeightDeck_" + bridge.name }; AssetDatabase.CreateAsset(mesh, path); }
        else { Undo.RecordObject(mesh, "Reshape bridge mesh"); mesh.Clear(); }
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0); mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        Undo.RecordObject(deck, "Replace deck geometry"); deck.localPosition = Vector3.zero; deck.localRotation = Quaternion.identity; deck.localScale = Vector3.one;
        Undo.RecordObject(deck.GetComponent<MeshFilter>(), "Set deck mesh"); deck.GetComponent<MeshFilter>().sharedMesh = mesh;
        var box = deck.GetComponent<BoxCollider>(); if (box != null) { Undo.RecordObject(box,"Disable old deck collider"); box.enabled = false; }
        var collider = deck.GetComponent<MeshCollider>(); if (collider == null) collider = Undo.AddComponent<MeshCollider>(deck.gameObject);
        Undo.RecordObject(collider,"Set deck collider"); collider.sharedMesh = mesh;
        foreach (var child in bridge.Cast<Transform>().ToArray())
        {
            if (child == deck) continue;
            Undo.RecordObject(child, "Align bridge trim"); var p = child.localPosition;
            if (child.name == "SafetyEdge")
            {
                for(int segment=0;segment<3;segment++)
                {
                    var rail = segment == 1 ? child : bridge.Find(child.localPosition.x < 0 ? "HeightRailL_"+segment : "HeightRailR_"+segment);
                    if(rail == null) { var go = GameObject.CreatePrimitive(PrimitiveType.Cube); Undo.RegisterCreatedObjectUndo(go,"Add landing rail"); go.name = (p.x < 0 ? "HeightRailL_" : "HeightRailR_")+segment; go.transform.SetParent(bridge,false); go.GetComponent<Renderer>().sharedMaterial=child.GetComponent<Renderer>().sharedMaterial; rail=go.transform; }
                    Undo.RecordObject(rail,"Align rail segment");
                    float y0=Surface(zs[segment],length,a,b), y1=Surface(zs[segment+1],length,a,b), run=zs[segment+1]-zs[segment];
                    rail.localPosition=new Vector3(p.x,(y0+y1)*.5f+.22f,(zs[segment]+zs[segment+1])*.5f);
                    rail.localRotation=Quaternion.Euler(-Mathf.Atan2(y1-y0,run)*Mathf.Rad2Deg,0,0);
                    rail.localScale=new Vector3(.18f,.36f,Mathf.Sqrt(run*run+(y1-y0)*(y1-y0)));
                }
                continue;
            }
            else if(child.name.StartsWith("HeightRail")) continue;
            else p.y = Surface(p.z,length,a,b) + (child.name == "EdgePost" ? .54f : .065f);
            child.localPosition = p;
        }
    }
}
#endif
