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

/// <summary>Approved gently staggered route; each challenge faces its incoming bridge.</summary>
public static class IslandRouteComposition
{
    public const string Evidence="E:/Unity/Verification/IslandRouteComposition-20261010";
    private const string RootName="IslandRouteComposition_v7";
    private static Transform route,root;
    private static Transform[] islands,bridges,stages;
    private static Vector3[] old;
    private static readonly Vector3[] Centers={new Vector3(0,0,0),new Vector3(8,3,78),new Vector3(-8,6,158),new Vector3(10,4,238),new Vector3(-6,8,322),new Vector3(12,11,408),new Vector3(-10,7,492),new Vector3(8,12,578),new Vector3(0,14,670)};
    private static Material stone,wood,gold,blue;

    [MenuItem("Tools/Expedition/Apply Approved Staggered Route Composition")]
    public static string Apply()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if(EditorApplication.isPlaying||!scene.isLoaded||scene.isDirty)throw new InvalidOperationException("Requires saved Level01 in Edit Mode.");
        route=scene.GetRootGameObjects().Single(g=>g.name=="FloatingCoopRoute").transform;
        if(route.Find(RootName)!=null)throw new InvalidOperationException("Route already applied.");
        islands=route.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<MeshFilter>()!=null&&System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^0[1-9]_(StartVillage|RelayIsland|ForestSlalom|TeamGate|RelayRest|WallRelay|TrapCrossing|HighSwitch|FinalDelivery)$")).OrderBy(t=>t.name).ToArray();
        if(islands.Length!=9)throw new InvalidOperationException("Expected nine islands.");
        string[] names={"01_WideTrainingBridge","02_RelayBridgeDeck","03_ForestBridge","04_FinalBridge","05_CargoBridge","06_CargoBridge","07_CargoBridge","08_CargoBridge"};bridges=names.Select(Find).ToArray();
        old=islands.Select(t=>t.position).ToArray();stone=Mat("Stone");wood=Mat("BridgeWood");gold=Mat("GoalGold");blue=Mat("RelayBlue");
        Directory.CreateDirectory(Evidence);if(!File.Exists(Evidence+"/Level01.Before.unity"))File.Copy(scene.path,Evidence+"/Level01.Before.unity",false);
        foreach(var p in Directory.GetFiles("Assets/Environment/FloatingCoop/ExpandedBridges","*.asset"))File.Copy(p,Evidence+"/"+Path.GetFileName(p),true);
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Arrange cooperative island route");
        var active=SceneManager.GetActiveScene();SceneManager.SetActiveScene(scene);
        try
        {
            foreach(var child in route.Cast<Transform>().ToArray()) TranslateUnits(child);
            foreach(var go in scene.GetRootGameObjects().Where(g=>new[]{"DeliveryZone","DeliveryExitPortal_Level02"}.Contains(g.name)))Shift(go.transform,Centers[8]-old[8]);
            for(int i=0;i<9;i++){Undo.RecordObject(islands[i],"Position staggered island");islands[i].position=Centers[i];}
            root=Group(RootName,route,Vector3.zero,Quaternion.identity);
            stages=Enumerable.Range(0,9).Select(i=>Group("Island_"+(i+1).ToString("00"),root,Centers[i],Incoming(i))).ToArray();
            var hammer=Find("RelaySweep");var log=Find("RotatingLog_Bridge");var fire=Find("FlameTrap_A");var crusher=Find("CrusherEntry");var collapse=Find("CollapsingDeck_Passage");
            var gateC=Find("WallSwitch_C");var gateA=Find("WallSwitch_A");var charge=Find("FinalCoopChargeGate");
            var climbC=Find("WallSwitch_C_FourStageClimb");var climbA=Find("WallSwitch_A_FourStageClimb");
            var relay=Find("RelayBridge_TwoPlayerUnlock");
            var retained=new[]{hammer,log,fire,crusher,collapse,gateC,gateA,charge,climbC,climbA,relay};
            foreach(var t in retained)Undo.SetTransformParent(t,root,"Retain complete bound mechanism");
            foreach(var b in route.GetComponentsInChildren<NetworkBehaviour>(true).Where(b=>!retained.Any(t=>b.transform==t||b.transform.IsChildOf(t))).ToArray())Hide(b.gameObject);
            foreach(var t in route.GetComponentsInChildren<Transform>(true).Where(t=>t!=route && !islands.Contains(t) && !bridges.Any(b=>t==b||t.IsChildOf(b)) && !retained.Any(r=>t==r||t.IsChildOf(r))).ToArray())
            {
                bool decoration=t.name.Contains("_Tree")||t.name.Contains("_Flag_")||t.name.StartsWith("Castle")||t.name=="Battlement"||t.name.StartsWith("Delivery");
                bool pad=t.name.StartsWith("BluePad_");
                if(!decoration&&!pad&&(t.GetComponent<Renderer>()!=null||t.GetComponent<Collider>()!=null))Hide(t.gameObject);
            }
            ConfigureRelay(relay);
            Crosswall(1,6,0,7);
            Crosswall(2,-5,-3,7);Crosswall(2,5,3,7);
            ConfigureClimb(gateC,climbC,3,-12);ConfigureClimb(gateA,climbA,5,12);
            Place(crusher,4,Vector3.zero);Crosswall(4,0,0,9);
            ConfigureCollapse(collapse);
            ConfigureCharge(charge);
            ExpandedIslandComposition.RebuildCurrentBridges();
            ConfigureBridgeHammer(hammer,2);ConfigureBridgeLog(log,4);ConfigureBridgeFire(fire,6);
            for(int i=1;i<=7;i++){Rest(stages[i],new Vector3(0,0,-13));Rest(stages[i],new Vector3(0,0,13));}
            ClearDecoration();
            foreach(var text in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true)))Hide(text.gameObject);
            Physics.SyncTransforms();EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Failed to save Level01.");
            Undo.CollapseUndoOperations(undo);return "Nine gently staggered islands, aligned mandatory challenges; hammer on bridge03, rolling log05, fire07. Backup: "+Evidence;
        }
        catch{Undo.RevertAllDownToGroup(undo);throw;}
        finally{if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active);}
    }
    private static Quaternion Incoming(int i){var d=Centers[Mathf.Min(i+1,8)]-Centers[i==0?0:i-1];if(i>0)d=Centers[i]-Centers[i-1];d.y=0;return Quaternion.LookRotation(d);}
    private static void TranslateUnits(Transform t)
    {
        if(islands.Contains(t)||bridges.Contains(t))return;
        bool unit=t.GetComponent<NetworkBehaviour>()!=null||t.GetComponent<Renderer>()!=null||t.GetComponent<Collider>()!=null||t.name.EndsWith("Challenge")||t.name.EndsWith("_FourStageClimb")||t.name=="CollapsingSteppingStones_Gauntlet";
        if(unit){int i=t.name=="WallSwitch_A"?5:t.name=="WallSwitch_B"?7:Enumerable.Range(0,9).OrderBy(j=>new Vector2(t.position.x-old[j].x,t.position.z-old[j].z).sqrMagnitude).First();Shift(t,Centers[i]-old[i]);return;}
        foreach(var c in t.Cast<Transform>().ToArray())TranslateUnits(c);
    }
    private static void Shift(Transform t,Vector3 d){Undo.RecordObject(t,"Move route content");t.position+=d;}
    private static void Place(Transform t,int island,Vector3 local)
    {
        Undo.SetTransformParent(t,stages[island],"Arrange challenge");Undo.RecordObject(t,"Align challenge to arrival");t.SetPositionAndRotation(stages[island].TransformPoint(local),stages[island].rotation);SetScale(t,Vector3.one);
    }
    private static void ConfigureRelay(Transform relay)
    {
        Place(relay,1,Vector3.zero);
        for(int side=-1;side<=1;side+=2)
        {
            string n=side<0?"Left":"Right";var visual=Find("BluePad_"+n+"_Visible");var zone=Find("BluePad_"+n+"_Occupancy");
            Undo.SetTransformParent(visual,stages[1],"Arrange relay pads");Undo.RecordObject(visual,"Align pad");visual.localPosition=new Vector3(side*8,1.23f,-6);visual.localRotation=Quaternion.identity;SetScale(visual,new Vector3(3,.06f,3));
            Undo.SetTransformParent(zone,stages[1],"Arrange relay occupancy");Undo.RecordObject(zone,"Align occupancy");zone.localPosition=new Vector3(side*8,1.2f,-6);zone.localRotation=Quaternion.identity;SetScale(zone,Vector3.one);
            var bc=zone.GetComponent<BoxCollider>();Undo.RecordObject(bc,"Size occupancy");bc.center=new Vector3(0,.35f,0);bc.size=new Vector3(3,1.4f,3);
            Box("RelayPlinth",stages[1],new Vector3(side*8,.6f,-6),new Vector3(3.5f,1.2f,3.5f),stone);
            for(int j=0;j<3;j++)Box("RelayStair",stages[1],new Vector3(side*8,(j+1)*.15f,-9+j*.7f),new Vector3(3,(j+1)*.3f,1),wood);
        }
    }
    private static void ConfigureClimb(Transform controller,Transform climb,int index,float x)
    {
        Place(controller,index,Vector3.zero);foreach(var c in controller.Cast<Transform>().ToArray())Hide(c.gameObject);
        Place(climb,index,new Vector3(x,0,-7));
        var barrier=Box("BoundClimbGate",stages[index],new Vector3(0,2.2f,0),new Vector3(7,4.4f,.75f),wood);
        var so=new SerializedObject(controller.GetComponent<ClimbSwitchGate>());so.FindProperty("_barrier").objectReferenceValue=barrier;so.ApplyModifiedProperties();
        Crosswall(index,0,0,7);
    }
    private static void ConfigureCharge(Transform t)
    {
        Place(t,7,Vector3.zero);
        foreach(var c in t.Cast<Transform>().Where(c=>c.name=="FinalGateWing"))Hide(c.gameObject);
        var barrier=t.Find("FinalBarrier");Undo.RecordObject(barrier,"Size final cooperative gate");barrier.localPosition=new Vector3(0,2.2f,0);barrier.localScale=new Vector3(7,4.4f,.75f);
        foreach(var p in t.Cast<Transform>().Where(c=>c.name=="ChargePad"||c.name=="PadOccupancy"||c.name=="PadGuide")){Undo.RecordObject(p,"Space cooperative approach");var v=p.localPosition;v.z=-5;p.localPosition=v;}
        Crosswall(7,0,0,7);
    }
    private static void Crosswall(int index,float z,float gapCenter,float width)
    {
        var stage=stages[index];var mesh=islands[index].GetComponent<MeshFilter>().sharedMesh;
        float lo=mesh.vertices.Min(v=>stage.InverseTransformPoint(islands[index].TransformPoint(v)).x)-1;
        float hi=mesh.vertices.Max(v=>stage.InverseTransformPoint(islands[index].TransformPoint(v)).x)+1;
        float a=gapCenter-width*.5f,b=gapCenter+width*.5f;
        Box("MandatoryRouteWing_Left",stage,new Vector3((lo+a)*.5f,2.2f,z),new Vector3(a-lo,4.4f,1.1f),stone);
        Box("MandatoryRouteWing_Right",stage,new Vector3((hi+b)*.5f,2.2f,z),new Vector3(hi-b,4.4f,1.1f),stone);
        var marker=Box("CargoRouteMark",stage,new Vector3(gapCenter,.035f,z-3),new Vector3(width-.8f,.025f,.35f),blue);UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
    }
    private static void ConfigureCollapse(Transform t)
    {
        Place(t,6,Vector3.zero);
        foreach(var c in t.Cast<Transform>().ToArray())if(!new[]{"DropDeck","RestoreSafetyVolume","WarningLamp"}.Contains(c.name))Hide(c.gameObject);
        var d=t.Find("DropDeck");Undo.RecordObject(d,"Fit collapse deck to trench");d.localPosition=new Vector3(0,-.17f,0);d.localRotation=Quaternion.identity;d.localScale=new Vector3(7,.44f,3.8f);
        var so=new SerializedObject(t.GetComponent<NetworkRouteHazard>());so.FindProperty("_primaryRest").vector3Value=d.localPosition;so.FindProperty("_safeSeconds").floatValue=7;so.FindProperty("_warningSeconds").floatValue=2;so.FindProperty("_activeSeconds").floatValue=2.5f;so.ApplyModifiedProperties();
        var volume=t.Find("RestoreSafetyVolume").GetComponent<BoxCollider>();Undo.RecordObject(volume,"Fit restore volume");volume.transform.localPosition=Vector3.zero;volume.center=new Vector3(0,-.4f,0);volume.size=new Vector3(8,3,5);
        CutTrench(islands[6],stages[6]);Crosswall(6,0,0,7);
        for(int side=-1;side<=1;side+=2)Box("TrenchLaneRail",stages[6],new Vector3(side*3.7f,.75f,0),new Vector3(.3f,1.5f,6),wood);
    }
    private static void CutTrench(Transform island,Transform stage)
    {
        var source=island.GetComponent<MeshFilter>().sharedMesh;var src=source.vertices;var verts=new List<Vector3>();var tris=new[]{new List<int>(),new List<int>()};
        float Signed(Vector3 v)=>stage.InverseTransformPoint(island.TransformPoint(v)).z;
        List<Vector3> Clip(List<Vector3> polygon,float sign)
        {
            var result=new List<Vector3>();for(int j=0;j<polygon.Count;j++){var a=polygon[j];var b=polygon[(j+1)%polygon.Count];float da=sign*Signed(a)-1.7f,db=sign*Signed(b)-1.7f;
                if(da>=0)result.Add(a);if((da>=0)!=(db>=0))result.Add(Vector3.Lerp(a,b,da/(da-db)));}return result;
        }
        for(int sub=0;sub<source.subMeshCount;sub++)
        {
            var indices=source.GetTriangles(sub);for(int k=0;k<indices.Length;k+=3)foreach(float sign in new[]{-1f,1f})
            {
                var p=Clip(new List<Vector3>{src[indices[k]],src[indices[k+1]],src[indices[k+2]]},sign);int start=verts.Count;verts.AddRange(p);for(int j=1;j<p.Count-1;j++)tris[sub].AddRange(new[]{start,start+j,start+j+1});
            }
        }
        float x0=src.Min(v=>stage.InverseTransformPoint(island.TransformPoint(v)).x),x1=src.Max(v=>stage.InverseTransformPoint(island.TransformPoint(v)).x);
        for(int side=-1;side<=1;side+=2)
        {
            int n=verts.Count;foreach(var v in new[]{new Vector3(x0,0,side*1.7f),new Vector3(x1,0,side*1.7f),new Vector3(x1,-7,side*1.7f),new Vector3(x0,-7,side*1.7f)})verts.Add(island.InverseTransformPoint(stage.TransformPoint(v)));
            tris[1].AddRange(side>0?new[]{n,n+1,n+2,n,n+2,n+3}:new[]{n+2,n+1,n,n+3,n+2,n});
        }
        var mesh=new Mesh{name="Island07Trench"};mesh.SetVertices(verts);mesh.subMeshCount=2;mesh.SetTriangles(tris[0],0);mesh.SetTriangles(tris[1],1);mesh.RecalculateNormals();mesh.RecalculateBounds();
        const string folder="Assets/Environment/FloatingCoop/RouteComposition";if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/Environment/FloatingCoop","RouteComposition");AssetDatabase.CreateAsset(mesh,folder+"/Island07Trench.asset");
        var filter=island.GetComponent<MeshFilter>();var collider=island.GetComponent<MeshCollider>();Undo.RecordObject(filter,"Cut island trench");Undo.RecordObject(collider,"Cut collision trench");filter.sharedMesh=mesh;collider.sharedMesh=mesh;
    }
    private static Vector3 BridgeFloor(int index)
    {
        var t=bridges[index].Find("ContinuousCargoDeck_7m");var v=t.GetComponent<MeshFilter>().sharedMesh.vertices;
        return t.TransformPoint((v[32]+v[33]+v[34]+v[35])*.25f);
    }
    private static void BridgePlace(Transform t,int index,float height)
    {
        Undo.SetTransformParent(t,root,"Place independent network hazard");Undo.RecordObject(t,"Align bridge hazard");t.SetPositionAndRotation(BridgeFloor(index)+Vector3.up*height,bridges[index].rotation);SetScale(t,Vector3.one);
        // Keep the NetworkObject active independently of relay-controlled bridge activation.
    }
    private static void ConfigureBridgeHammer(Transform t,int index)
    {
        BridgePlace(t,index,0);
        foreach(var p in t.Cast<Transform>().Where(x=>x.name=="SweepPost")){Undo.RecordObject(p,"Fit arch to bridge");var v=p.localPosition;v.x=Mathf.Sign(v.x)*3.85f;p.localPosition=v;}
        var beam=t.Find("SweepBeam");Undo.RecordObject(beam,"Fit arch beam");beam.localScale=new Vector3(8.2f,.4f,.6f);
        var so=new SerializedObject(t.GetComponent<NetworkRouteHazard>());so.FindProperty("_safeSeconds").floatValue=7;so.FindProperty("_activeSeconds").floatValue=3.5f;so.ApplyModifiedProperties();
    }
    private static void ConfigureBridgeLog(Transform t,int index)
    {
        BridgePlace(t,index,.85f);t.rotation=bridges[index].rotation*Quaternion.Euler(0,0,90);
        var so=new SerializedObject(t.GetComponent<RotatingLogHazard>());so.FindProperty("_axis").enumValueIndex=1;
        var body=(Rigidbody)so.FindProperty("_logRigidbody").objectReferenceValue;Undo.RecordObject(body.transform,"Align rolling log axle");body.transform.localPosition=Vector3.zero;body.transform.localRotation=Quaternion.identity;body.transform.localScale=new Vector3(1.4f,3.3f,1.4f);
        var zone=(BoxCollider)so.FindProperty("_impactZone").objectReferenceValue;Undo.RecordObject(zone,"Fit log impact query");zone.center=Vector3.zero;zone.size=new Vector3(1,2,1);so.ApplyModifiedProperties();
        var lamp=t.Find("LogWarningLamp");Undo.RecordObject(lamp,"Place log warning lamp");lamp.position=BridgeFloor(index)+bridges[index].right*3.9f+Vector3.up*1.8f;
        for(int side=-1;side<=1;side+=2)Box("LogBearing",bridges[index],bridges[index].InverseTransformPoint(BridgeFloor(index))+new Vector3(side*3.8f,.6f,0),new Vector3(.5f,1.2f,1),stone);
    }
    private static void ConfigureBridgeFire(Transform t,int index)
    {
        BridgePlace(t,index,0);var so=new SerializedObject(t.GetComponent<TimedCargoTrap>());
        var zone=(BoxCollider)so.FindProperty("_zone").objectReferenceValue;Undo.RecordObject(zone,"Fit bridge flame zone");zone.center=new Vector3(0,1.3f,0);zone.size=new Vector3(7,2.6f,3);
        var marker=((Renderer)so.FindProperty("_marker").objectReferenceValue).transform;Undo.RecordObject(marker,"Fit fire grate");marker.localPosition=new Vector3(0,.035f,0);marker.localRotation=Quaternion.identity;marker.localScale=new Vector3(7,.035f,3);
        so.FindProperty("_safeSeconds").floatValue=7;so.FindProperty("_warningSeconds").floatValue=2;so.FindProperty("_activeSeconds").floatValue=2.5f;so.FindProperty("_phaseOffset").floatValue=0;so.ApplyModifiedProperties();
    }
    private static void ClearDecoration()
    {
        foreach(var t in route.GetComponentsInChildren<Transform>(true).Where(t=>t.name.Contains("_Tree")).ToArray())
        {
            int i=Enumerable.Range(0,9).OrderBy(j=>(t.position-Centers[j]).sqrMagnitude).First();var p=stages[i].InverseTransformPoint(t.position);
            if(i>0&&i<8&&(Mathf.Abs(p.x)<11||Mathf.Abs(p.z)<3))Hide(t.gameObject);
        }
    }
    private static void Rest(Transform parent,Vector3 at){var go=Box("CargoWaitingBay",parent,at+Vector3.up*.09f,new Vector3(6,.025f,4),gold);UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());}
    private static Transform Find(string n)=>route.GetComponentsInChildren<Transform>(true).Single(t=>t.name==n);
    private static Material Mat(string n)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/"+n+".mat")??throw new InvalidOperationException("Missing material "+n);
    private static Transform Group(string n,Transform p,Vector3 position,Quaternion rotation){var go=new GameObject(n);Undo.RegisterCreatedObjectUndo(go,"Create route group");go.transform.SetParent(p,false);go.transform.SetPositionAndRotation(position,rotation);SetScale(go.transform,Vector3.one);return go.transform;}
    private static GameObject Box(string n,Transform p,Vector3 at,Vector3 size,Material material){var go=GameObject.CreatePrimitive(PrimitiveType.Cube);Undo.RegisterCreatedObjectUndo(go,"Create route geometry");go.name=n;go.transform.SetParent(p,false);go.transform.localPosition=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;return go;}
    private static void SetScale(Transform t,Vector3 v){var p=t.parent==null?Vector3.one:t.parent.lossyScale;t.localScale=new Vector3(v.x/p.x,v.y/p.y,v.z/p.z);}
    private static void Hide(GameObject go){if(go.activeSelf){Undo.RecordObject(go,"Preserve replaced layout");go.SetActive(false);}}
}
#endif
