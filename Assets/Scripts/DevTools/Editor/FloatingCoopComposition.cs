#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FloatingCoopComposition
{
    [MenuItem("Tools/Expedition/Expand Islands And Apply Thai Instructions")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if(!scene.isLoaded) scene=EditorSceneManager.OpenScene("Assets/Scenes/Level01.unity",OpenSceneMode.Additive);
        var roots=scene.GetRootGameObjects();
        var route=roots.Single(g=>g.name=="FloatingCoopRoute").transform;
        if(route.Find("ExpandedLayout_v1")!=null) throw new InvalidOperationException("Expanded composition already applied.");
        var font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/GameThai.ttf");
        if(font==null) throw new InvalidOperationException("GameThai font is required.");
        Directory.CreateDirectory("E:/Unity/Verification/IslandComposition-20261010");
        if(!EditorSceneManager.SaveScene(scene,"E:/Unity/Verification/IslandComposition-20261010/Level01.Before-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".unity",true)) throw new IOException("Backup failed.");
        Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Expand co-op island layout");
        try
        {
            // Scale the whole horizontal layout, preserving local references, deck profiles and island gaps.
            Undo.RecordObject(route,"Expand route"); route.localScale=Vector3.Scale(route.localScale,new Vector3(1.35f,1,1.35f));
            foreach(var root in roots.Where(g=>g.name=="DeliveryZone" || g.name=="DeliveryExitPortal_Level02" || g.name=="PlayerStart" || g.name=="WoodenCrateItem"))
            {
                Undo.RecordObject(root.transform,"Align scene root with expanded route");
                root.transform.position=Vector3.Scale(root.transform.position,new Vector3(1.35f,1,1.35f));
                if(root.name.StartsWith("Delivery")) root.transform.localScale=Vector3.Scale(root.transform.localScale,new Vector3(1.35f,1,1.35f));
            }
            // Smaller rocks relative to the expanded floor leave turning room for players on both sides.
            foreach(var rock in route.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("Slalom")))
            { Undo.RecordObject(rock,"Widen cargo slalom"); rock.localScale=Vector3.Scale(rock.localScale,new Vector3(.85f,1,.85f)); }
            ArrangeSlalom(route);
            var texts=new Dictionary<string,string>
            {
                ["StartInstructions"]="เกาะลอยฟ้า\nหาลังแล้วช่วยกันขนไปส่ง",
                ["VillageTip"]="เล่นร่วมกัน 2–4 คน\nจับลังคนละด้าน\nวางพักได้ที่แผ่นสีทอง",
                ["RelayInstructions"]="ยืนบนแผ่นสีฟ้าพร้อมกันทั้งสองจุด\nเพื่อเปิดสะพานให้ทีม",
                ["ForestInstructions"]="ช่วยกันเลี้ยวลังผ่านก้อนหิน\nค่อย ๆ เดินและเผื่อพื้นที่ให้เพื่อน",
                ["GateInstructions"]="ให้คนหนึ่งกด E ค้างที่คันโยกสีฟ้า\nเพื่อนช่วยกันขนลังผ่านประตู",
                ["RestBeforeWalls"]="จุดพักลัง\nข้างหน้าให้เพื่อนปีนไปเปิดประตู",
                ["TrapInstructions"]="สีเขียว: ข้ามได้ | สีส้ม: รอก่อน\nเปลวไฟทำให้ลังเสียพลังชีวิต\nวางพักแล้วรอจังหวะพร้อมกัน",
                ["Delivery_Label"]="จุดส่งลัง\nวางลังไว้ในวงที่กำหนด",
                ["PortalStatus"]="ส่งลังให้สำเร็จ\nเพื่อเปิดประตูวาป"
            };
            foreach(var label in roots.Where(g=>g.name!="PreviousObstacleTest_Preserved").SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true)))
            {
                Undo.RecordObject(label,"Thai instructions");
                if(texts.TryGetValue(label.name,out var text)) label.text=text;
                else if(label.name.EndsWith("_GroundInstructions")) label.text="ให้คนหนึ่งปีนตามแถบสีฟ้า\nกด E ที่ปุ่มด้านบนเพื่อเปิดประตู\nเพื่อนรอพร้อมลังที่จุดพัก";
                else if(label.name.EndsWith("_Prompt")) label.text="ปีนขึ้นไปบนกำแพง\nกด E ที่ปุ่ม";
                else if(label.name.StartsWith("FlameTrap_")) label.text="ไฟเขียว: ช่วยกันขนลังข้ามไป";
                label.font=font; label.fontSize=56; label.characterSize=.08f;
                var renderer=label.GetComponent<MeshRenderer>(); if(renderer!=null) { Undo.RecordObject(renderer,"Thai font material"); renderer.sharedMaterial=font.material; }
                // Keep glyph proportions natural despite the horizontal route scale.
                Undo.RecordObject(label.transform,"Keep Thai glyph proportions");
                var world=label.transform.lossyScale;
                label.transform.localScale=Vector3.Scale(label.transform.localScale,new Vector3(1/Mathf.Max(.001f,world.x),1/Mathf.Max(.001f,world.y),1/Mathf.Max(.001f,world.z)));
            }
            var marker=new GameObject("ExpandedLayout_v1"); Undo.RegisterCreatedObjectUndo(marker,"Mark expanded composition"); marker.transform.SetParent(route,false);
            EditorSceneManager.MarkSceneDirty(scene); if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Save failed.");
            SceneManager.SetActiveScene(scene); Selection.activeGameObject=route.gameObject;
            Undo.CollapseUndoOperations(group);
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    public static void ArrangeSlalom(Transform route)
    {
        var all=route.GetComponentsInChildren<Transform>(true);
        var island=all.Single(t=>t.name=="03_ForestSlalom");
        string[] names={"SlalomA","SlalomB","SlalomC"};
        Vector2[] offsets={new Vector2(-5.8f,-7),new Vector2(5.8f,0),new Vector2(-5.8f,7)};
        for(int i=0;i<names.Length;i++)
        { var rock=all.Single(t=>t.name==names[i]); Undo.RecordObject(rock,"Arrange readable slalom"); var p=rock.position; rock.position=new Vector3(island.position.x+offsets[i].x,p.y,island.position.z+offsets[i].y); }
    }

    public static void AddSignBackings()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        const string path="Assets/Environment/FloatingCoop/ThaiSignBacking.mat";
        var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.SetColor("_BaseColor",new Color(.12f,.08f,.045f)); AssetDatabase.CreateAsset(material,path); }
        foreach(var label in scene.GetRootGameObjects().Where(g=>g.name!="PreviousObstacleTest_Preserved").SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true)))
        {
            if(label.transform.Find("ThaiSignBacking")!=null) continue;
            var bounds=label.GetComponent<Renderer>().localBounds;
            var board=GameObject.CreatePrimitive(PrimitiveType.Cube); board.name="ThaiSignBacking"; Undo.RegisterCreatedObjectUndo(board,"Add readable sign panel");
            board.transform.SetParent(label.transform,false); board.transform.localPosition=bounds.center+Vector3.forward*.08f;
            // Extra room accommodates longer dynamic gate and trap states.
            board.transform.localScale=new Vector3(Mathf.Max(8,bounds.size.x+.5f),Mathf.Max(1.5f,bounds.size.y+.35f),.08f);
            board.GetComponent<Renderer>().sharedMaterial=material; UnityEngine.Object.DestroyImmediate(board.GetComponent<Collider>());
        }
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); if(!EditorSceneManager.SaveScene(scene))throw new IOException("Save failed.");
    }
}
#endif
