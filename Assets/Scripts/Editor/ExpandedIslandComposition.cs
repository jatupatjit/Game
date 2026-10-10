#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CoopGame.Network;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One-shot, undoable migration of the approved larger island test layout.</summary>
public static class ExpandedIslandComposition
{
    public const string Evidence = "E:/Unity/Verification/ExpandedIslandComposition-20261010";
    private const string Marker = "ExpandedIslandComposition_v6";
    private static Vector3[] before, after;
    private static Transform[] islands;
    private static Transform route;
    private static Material wood, stone;
    private static readonly string[] BridgeNames = {"01_WideTrainingBridge","02_RelayBridgeDeck","03_ForestBridge","04_FinalBridge","05_CargoBridge","06_CargoBridge","07_CargoBridge","08_CargoBridge"};

    [MenuItem("Tools/Expedition/Apply Approved Larger Island Composition")]
    public static string Apply()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if (EditorApplication.isPlaying || !scene.isLoaded || scene.isDirty)
            throw new InvalidOperationException("Requires saved Level01 in Edit Mode.");
        route = scene.GetRootGameObjects().Single(g=>g.name=="FloatingCoopRoute").transform;
        if (route.Find(Marker)!=null) throw new InvalidOperationException("Composition already applied.");
        islands = route.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<MeshFilter>()!=null &&
            System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^0[1-9]_(StartVillage|RelayIsland|ForestSlalom|TeamGate|RelayRest|WallRelay|TrapCrossing|HighSwitch|FinalDelivery)$")).OrderBy(t=>t.name).ToArray();
        if (islands.Length!=9) throw new InvalidOperationException("Expected nine island meshes.");
        before = islands.Select(t=>t.position).ToArray();
        after = before.Select(p=>new Vector3(p.x*1.9f,p.y,p.z*1.9f)).ToArray();
        wood=Mat("BridgeWood"); stone=Mat("Stone");
        Directory.CreateDirectory(Evidence);
        File.Copy(scene.path,Evidence+"/Level01.Before.unity",true);
        Undo.IncrementCurrentGroup(); int undo=Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Expand islands and clear cargo bridges");
        var active=SceneManager.GetActiveScene(); SceneManager.SetActiveScene(scene);
        try
        {
            foreach(Transform t in route.Cast<Transform>().ToArray()) MoveUnits(t);
            foreach(var go in scene.GetRootGameObjects().Where(g=>new[]{"DeliveryZone","DeliveryExitPortal_Level02","WoodenCrateItem","PlayerStart"}.Contains(g.name))) Move(go.transform,after[Nearest(go.transform.position)]-before[Nearest(go.transform.position)]);
            for(int i=0;i<9;i++)
            {
                Undo.RecordObject(islands[i],"Enlarge island footprint");
                islands[i].position=after[i];
                var scale=islands[i].localScale; scale.x*=1.5f; scale.z*=1.5f; islands[i].localScale=scale;
            }
            Group(Marker,route,Vector3.zero);
            foreach(var t in route.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="CorridorCaps" || t.name=="BridgeChicanePost")) Hide(t.gameObject);
            // Keep the introductory and delivery islands free of gameplay challenges.
            var gauntlet=route.Find("IslandCoopGauntlet_v5");
            var entry=gauntlet.Find("01_StartVillage_Challenge");
            Place(entry,2,new Vector3(14,0,0)); entry.name="03_RelocatedTrainingChallenge";
            var finish=gauntlet.Find("09_FinalDelivery_Challenge");
            Place(finish,7,new Vector3(14,0,0)); finish.name="08_RelocatedCoopChallenge";
            // Move complete mechanisms so all bound colliders and visual children travel together.
            Place(Find("SwingHammer_Forest"),2,new Vector3(0,0,-15));
            Place(Find("StoneCrusher_Relay"),4,new Vector3(0,0,16));
            Place(Find("DoublePendulum_Relay"),5,new Vector3(0,0,-15));
            Place(Find("RotatingLog_Bridge"),4,new Vector3(0,.7f,-16));
            Place(Find("FlameTrap_A"),5,new Vector3(0,0,13));
            Place(Find("FlameTrap_B"),7,new Vector3(0,0,15));
            Place(Find("TeamPassageGate"),3,new Vector3(0,0,13),false);
            RefitCollapse(); RefitCoopGate(); RefitStones();
            RefineApproaches();
            for(int i=0;i<8;i++) RebuildBridge(Find(BridgeNames[i]),i);
            SpreadDecoration();
            RefineApproaches();
            foreach(var text in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true))) Hide(text.gameObject);
            Physics.SyncTransforms();
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Failed to save Level01.");
            AssetDatabase.SaveAssets(); Undo.CollapseUndoOperations(undo);
            return "Expanded nine island footprints 50%; spacing 90%; rebuilt eight clear 7m cargo bridges; moved start/finish and bridge challenges onto islands 2-8. Backup: "+Evidence;
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
        finally { if(active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
    }

    private static void MoveUnits(Transform t)
    {
        if(islands.Contains(t) || BridgeNames.Contains(t.name)) return;
        bool unit=t.GetComponent<NetworkBehaviour>()!=null || t.GetComponent<Renderer>()!=null || t.GetComponent<Collider>()!=null ||
            t.name.EndsWith("_Challenge") || t.name.EndsWith("_FourStageClimb") || t.name=="CollapsingSteppingStones_Gauntlet";
        if(unit)
        {
            int index=t.name=="WallSwitch_A"?5:t.name=="WallSwitch_B"?7:Nearest(t.position);
            Move(t,after[index]-before[index]); return;
        }
        foreach(Transform child in t.Cast<Transform>().ToArray()) MoveUnits(child);
    }
    private static int Nearest(Vector3 p)=>Enumerable.Range(0,9).OrderBy(i=>new Vector2(p.x-before[i].x,p.z-before[i].z).sqrMagnitude).First();
    private static void Move(Transform t,Vector3 delta)
    {
        Undo.RecordObject(t,"Move island content"); t.position+=delta;
        foreach(var gate in t.GetComponentsInChildren<TwoPlayerTimedSwitchGate>(true))
        {
            var so=new SerializedObject(gate); so.FindProperty("_closedPosition").vector3Value+=delta; so.FindProperty("_openPosition").vector3Value+=delta; so.ApplyModifiedProperties();
        }
    }
    private static void Place(Transform t,int island,Vector3 offset,bool flatten=true)
    {
        Move(t,after[island]+offset-t.position);
        if(flatten){Undo.RecordObject(t,"Align island mechanism"); t.rotation=Quaternion.identity;}
    }
    private static Transform Find(string n)=>route.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n);

    private static void RefitCollapse()
    {
        var t=Find("CollapsingDeck_Passage"); Place(t,3,new Vector3(13,2.5f,0));
        foreach(Transform c in t.Cast<Transform>().ToArray()) if(!new[]{"DropDeck","RestoreSafetyVolume","WarningLamp"}.Contains(c.name)) Hide(c.gameObject);
        var deck=t.Find("DropDeck"); Undo.RecordObject(deck,"Align raised drop deck"); deck.localRotation=Quaternion.identity; deck.localPosition=new Vector3(0,-.22f,0);
        var so=new SerializedObject(t.GetComponent<NetworkRouteHazard>()); so.FindProperty("_primaryRest").vector3Value=deck.localPosition; so.ApplyModifiedProperties();
        var safety=t.Find("RestoreSafetyVolume").GetComponent<BoxCollider>(); Undo.RecordObject(safety,"Align restore safety"); safety.center=new Vector3(0,-.6f,0); safety.size=new Vector3(8,4,6);
        for(int side=-1;side<=1;side+=2)
        {
            Box("RaisedLanding",t,new Vector3(0,-.2f,side*4.1f),new Vector3(6.08f,.4f,4.76f),wood);
            Beam("ApproachRamp",t,t.TransformPoint(new Vector3(0,-2.46f,side*13)),t.TransformPoint(new Vector3(0,0,side*6.48f)),6.08f,.3f,wood);
        }
    }
    private static void RefitCoopGate()
    {
        var t=Find("TwoPlayerTimedSwitchGate_Overpass"); Place(t,4,new Vector3(-15,0,6));
        var body=t.Find("CoopDrawbridge"); Undo.RecordObject(body,"Repurpose island pressure gate");
        body.localPosition=new Vector3(0,2,0); body.localRotation=Quaternion.identity; SetWorldScale(body,new Vector3(7,4,.45f));
        var so=new SerializedObject(t.GetComponent<TwoPlayerTimedSwitchGate>());
        so.FindProperty("_closedPosition").vector3Value=body.position;so.FindProperty("_openPosition").vector3Value=body.position+Vector3.up*5;so.ApplyModifiedProperties();
        for(int side=-1;side<=1;side+=2)
        {
            var p=t.Find(side<0?"PressurePlate_A":"PressurePlate_B"); Undo.RecordObject(p,"Place cooperative pressure pads");p.position=t.position+new Vector3(side*5,.1f,-3);p.rotation=Quaternion.identity;
            Box("PressureGateWing",t,new Vector3(side*5.2f,2,0),new Vector3(3.4f,4,.5f),stone);
        }
    }
    private static void RefitStones()
    {
        var t=Find("CollapsingSteppingStones_Gauntlet"); Place(t,6,new Vector3(14,0,0));
        var stones=t.GetComponentsInChildren<CollapsingSteppingStone>(true).OrderBy(x=>x.name).ToArray();
        for(int i=0;i<stones.Length;i++){Undo.RecordObject(stones[i].transform,"Arrange raised side challenge");stones[i].transform.position=t.position+new Vector3(i%2==0?-1.2f:1.2f,1.2f,-8+i*4);}
        for(int side=-1;side<=1;side+=2)
            Beam("StoneApproachRamp",t,t.position+new Vector3(0,.04f,side*14),t.position+new Vector3(0,1.4f,side*10),5,.3f,wood);
    }
    private static void SpreadDecoration()
    {
        foreach(var t in route.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("_Tree") || t.name.Contains("_Flag_")).ToArray())
        {
            int i=Enumerable.Range(0,9).OrderBy(k=>new Vector2(t.position.x-after[k].x,t.position.z-after[k].z).sqrMagnitude).First();
            Undo.RecordObject(t,"Place decoration on outer island edge");var p=t.position-after[i];p.x*=1.5f;p.z*=1.5f;t.position=after[i]+p;
            if(i==0||i==8){var c=t.GetComponent<Collider>();if(c!=null){Undo.RecordObject(c,"Keep safe islands clear");c.enabled=false;}}
        }
    }
    private static void RebuildBridge(Transform bridge,int i)
    {
        foreach(Transform child in bridge.Cast<Transform>().ToArray())
            if(new[]{"ContinuousCargoDeck_7m","CargoGuardRail","GuardPost"}.Contains(child.name))Undo.DestroyObjectImmediate(child.gameObject);
            else Hide(child.gameObject);
        Vector3 direction=after[i+1]-after[i];direction.y=0;direction.Normalize();
        float Radius(int j){var b=islands[j].GetComponent<Renderer>().bounds;return 1f/Mathf.Sqrt(direction.x*direction.x/(b.extents.x*b.extents.x)+direction.z*direction.z/(b.extents.z*b.extents.z));}
        Vector3 a=after[i]+direction*Radius(i)*.72f+Vector3.up*.025f;
        Vector3 b=after[i+1]-direction*Radius(i+1)*.72f+Vector3.up*.025f;
        Undo.RecordObject(bridge,"Align cargo bridge");bridge.position=a;bridge.rotation=Quaternion.LookRotation(direction);SetWorldScale(bridge,Vector3.one);
        float length=Vector3.Dot(b-a,direction);float rise=b.y-a.y;
        if(length<10)throw new InvalidOperationException("Insufficient bridge spacing: "+bridge.name);
        float EdgeProjection(int j,Vector3 toward)=>islands[j].GetComponent<MeshFilter>().sharedMesh.vertices.Max(v=>Vector3.Dot(islands[j].TransformPoint(v)-after[j],toward));
        float landingA=EdgeProjection(i,direction)-Radius(i)*.72f+2f;
        float landingB=EdgeProjection(i+1,-direction)-Radius(i+1)*.72f+2f;
        if(landingA+landingB>=length-2)throw new InvalidOperationException("Bridge ramp needs more space.");
        // Bevel onto the deck: a 2.5cm lip, then a gentle 1.3m transition to a raised landing.
        // Raising the flat landing prevents coplanar grass/wood flicker without a tall cargo-blocking step.
        const float lift=.175f;
        Vector3[] points={Vector3.zero,new Vector3(0,lift,1.3f),new Vector3(0,lift,landingA),
            new Vector3(0,rise+lift,length-landingB),new Vector3(0,rise+lift,length-1.3f),new Vector3(0,rise,length)};
        var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
        // One watertight mesh avoids internal collider lips between landing and slope.
        Action<Vector3,Vector3,Vector3,Vector3> quad=(v0,v1,v2,v3)=>{int n=vertices.Count;vertices.AddRange(new[]{v0,v1,v2,v3});uv.AddRange(new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up});triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});};
        Vector3 x=Vector3.right*3.5f,down=Vector3.down*.35f;
        for(int j=0;j<points.Length-1;j++)
        {
            var p=points[j];var q=points[j+1];
            quad(p-x,q-x,q+x,p+x);quad(p+x+down,q+x+down,q-x+down,p-x+down);
            quad(p-x+down,q-x+down,q-x,p-x);quad(p+x,q+x,q+x+down,p+x+down);
            for(int side=-1;side<=1;side+=2)
            {
                var offset=Vector3.right*(side*3.68f)+Vector3.up*.7f;
                Beam("CargoGuardRail",bridge,bridge.TransformPoint(p+offset),bridge.TransformPoint(q+offset),.22f,1.4f,wood);
            }
        }
        quad(points[0]+x,points[0]+x+down,points[0]-x+down,points[0]-x);
        var end=points[points.Length-1];quad(end-x,end-x+down,end+x+down,end+x);
        var mesh=new Mesh{name=bridge.name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.SetUVs(0,uv);mesh.RecalculateNormals();mesh.RecalculateBounds();
        const string folder="Assets/Environment/FloatingCoop/ExpandedBridges";if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Environment/FloatingCoop","ExpandedBridges");
        string path=folder+"/"+bridge.name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if(existing==null)AssetDatabase.CreateAsset(mesh,path);
        else
        {
            Undo.RecordObject(existing,"Refine continuous bridge mesh");existing.Clear();existing.SetVertices(vertices);existing.SetTriangles(triangles,0);existing.SetUVs(0,uv);
            existing.name=bridge.name;existing.RecalculateNormals();existing.RecalculateBounds();UnityEngine.Object.DestroyImmediate(mesh);mesh=existing;EditorUtility.SetDirty(existing);
        }
        var deck=Group("ContinuousCargoDeck_7m",bridge,a).gameObject;deck.AddComponent<MeshFilter>().sharedMesh=mesh;deck.AddComponent<MeshRenderer>().sharedMaterial=wood;deck.AddComponent<MeshCollider>().sharedMesh=mesh;
        for(int j=0;j<points.Length;j++) for(int side=-1;side<=1;side+=2)Box("GuardPost",bridge,points[j]+new Vector3(side*3.68f,.8f,0),new Vector3(.4f,1.6f,.4f),stone);
    }
    public static string RefineExisting()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if(EditorApplication.isPlaying||!scene.isLoaded||scene.isDirty)throw new InvalidOperationException("Requires saved Level01 in Edit Mode.");
        route=scene.GetRootGameObjects().Single(g=>g.name=="FloatingCoopRoute").transform;
        if(route.Find(Marker)==null)throw new InvalidOperationException("Apply the approved composition first.");
        islands=route.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<MeshFilter>()!=null && System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^0[1-9]_(StartVillage|RelayIsland|ForestSlalom|TeamGate|RelayRest|WallRelay|TrapCrossing|HighSwitch|FinalDelivery)$")).OrderBy(t=>t.name).ToArray();
        after=islands.Select(t=>t.position).ToArray();wood=Mat("BridgeWood");stone=Mat("Stone");
        Place(Find("03_RelocatedTrainingChallenge"),2,new Vector3(14,0,0));
        Place(Find("CollapsingDeck_Passage"),3,new Vector3(13,2.5f,0));
        Place(Find("CollapsingSteppingStones_Gauntlet"),6,new Vector3(14,0,0));
        RefineApproaches();for(int i=0;i<8;i++)RebuildBridge(Find(BridgeNames[i]),i);
        Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);
        return "Refined bridge landings, side challenge placement and unobstructed approaches.";
    }
    public static void RebuildCurrentBridges()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if(EditorApplication.isPlaying||!scene.isLoaded)throw new InvalidOperationException("Requires loaded Level01 in Edit Mode.");
        route=scene.GetRootGameObjects().Single(g=>g.name=="FloatingCoopRoute").transform;
        islands=route.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<MeshFilter>()!=null && System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^0[1-9]_(StartVillage|RelayIsland|ForestSlalom|TeamGate|RelayRest|WallRelay|TrapCrossing|HighSwitch|FinalDelivery)$")).OrderBy(t=>t.name).ToArray();
        if(islands.Length!=9)throw new InvalidOperationException("Expected nine islands.");
        after=islands.Select(t=>t.position).ToArray();wood=Mat("BridgeWood");stone=Mat("Stone");
        for(int i=0;i<8;i++)RebuildBridge(Find(BridgeNames[i]),i);
    }
    private static void RefineApproaches()
    {
        var finish=Find("08_RelocatedCoopChallenge");
        var charge=finish.GetComponentInChildren<CoopChargeGate>(true).transform;Place(charge,7,new Vector3(0,0,-9));
        var walls=finish.GetComponentsInChildren<Transform>(true).Where(t=>t.name.StartsWith("CargoTurnWall_")).OrderBy(t=>t.name).ToArray();
        for(int i=0;i<walls.Length;i++)Move(walls[i],after[7]+new Vector3(i==0?-3:3,1.35f,7+i*7)-walls[i].position);
        foreach(var t in finish.GetComponentsInChildren<Transform>(true).Where(t=>new[]{"CargoLaneRail","RailPost","TurnMarker_0","TurnMarker_1","CargoRest_Gold","RestCorner"}.Contains(t.name)))Hide(t.gameObject);
        Place(Find("FlameTrap_B"),1,new Vector3(0,0,13));
        var log=Find("RotatingLog_Bridge").GetComponent<RotatingLogHazard>();
        var logSettings=new SerializedObject(log);
        if(logSettings.FindProperty("_impactZone").objectReferenceValue==null)
        {
            var body=(Rigidbody)logSettings.FindProperty("_logRigidbody").objectReferenceValue;
            var zone=Undo.AddComponent<BoxCollider>(body.gameObject);zone.isTrigger=true;zone.size=new Vector3(1,2,1);
            logSettings.FindProperty("_impactZone").objectReferenceValue=zone;logSettings.ApplyModifiedProperties();
        }
        Physics.SyncTransforms();
        // Remove trees intersecting a complete mechanism or the swept width of a bridge landing.
        var hazards=route.GetComponentsInChildren<NetworkBehaviour>(true).Where(n=>n.gameObject.activeInHierarchy).ToArray();
        foreach(var tree in route.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("_Tree")&&t.name.EndsWith("Trunk")).ToArray())
        {
            var p=tree.position;
            bool blocked=hazards.Any(h=>new Vector2(p.x-h.transform.position.x,p.z-h.transform.position.z).sqrMagnitude<121f ||
                h.GetComponentsInChildren<Collider>(true).Where(c=>c.gameObject.activeInHierarchy).Any(c=>p.x+2.5f>c.bounds.min.x && p.x-2.5f<c.bounds.max.x && p.z+2.5f>c.bounds.min.z && p.z-2.5f<c.bounds.max.z));
            if(!blocked)continue;Hide(tree.gameObject);var crown=route.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name==tree.name.Replace("Trunk","Crown"));if(crown!=null)Hide(crown.gameObject);
        }
    }
    private static void Beam(string name,Transform parent,Vector3 a,Vector3 b,float width,float thickness,Material mat)
    {
        var go=Box(name,parent,Vector3.zero,Vector3.one,mat);go.transform.position=(a+b)*.5f;go.transform.rotation=Quaternion.LookRotation(b-a);SetWorldScale(go.transform,new Vector3(width,thickness,Vector3.Distance(a,b)));
    }
    private static Transform Group(string name,Transform parent,Vector3 world)
    {
        var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Create expanded composition");go.transform.SetParent(parent,false);go.transform.position=world;SetWorldScale(go.transform,Vector3.one);return go.transform;
    }
    private static GameObject Box(string name,Transform parent,Vector3 p,Vector3 size,Material mat)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);Undo.RegisterCreatedObjectUndo(go,"Create expanded geometry");go.name=name;go.transform.SetParent(parent,false);go.transform.localPosition=p;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;return go;
    }
    private static void SetWorldScale(Transform t,Vector3 size){var p=t.parent==null?Vector3.one:t.parent.lossyScale;t.localScale=new Vector3(size.x/p.x,size.y/p.y,size.z/p.z);}
    private static void Hide(GameObject go){if(go.activeSelf){Undo.RecordObject(go,"Preserve replaced geometry");go.SetActive(false);}}
    private static Material Mat(string n)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/"+n+".mat")??throw new InvalidOperationException("Missing material "+n);
}
#endif
