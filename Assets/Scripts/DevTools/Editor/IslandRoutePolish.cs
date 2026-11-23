#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Approved v8 scenery, bridge-aligned waiting bays and rebuilt climb/obstacle geometry.</summary>
public static class IslandRoutePolish
{
    public const string Evidence = "E:/Unity/Verification/IslandRoutePolish-20261010";
    private static Transform route, root;
    private static Transform[] islands, stages, bridges;
    private static Material stone, wood, gold, leaves, blue, metal, earth, red;
    private static Mesh cone, rock, barrel;
    private static Collider[] reserved;
    private static Bounds[] reservedPaths;
    private static readonly List<Vector3> clusters = new();
    private static readonly string[] BridgeNames = {"01_WideTrainingBridge","02_RelayBridgeDeck","03_ForestBridge","04_FinalBridge","05_CargoBridge","06_CargoBridge","07_CargoBridge","08_CargoBridge"};
    private const string AssetFolder = "Assets/Environment/FloatingCoop/RoutePolish";

    [MenuItem("Tools/Expedition/Apply Approved Island Scenery and Obstacles")]
    private static void MenuApply() => Debug.Log(Apply());

    public static string Apply()
    {
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if (EditorApplication.isPlaying || !scene.isLoaded || scene.isDirty)
            throw new InvalidOperationException("Requires saved Level01 in Edit Mode.");
        route = scene.GetRootGameObjects().Single(g=>g.name=="FloatingCoopRoute").transform;
        if (route.Find("IslandRoutePolish_v8") != null) throw new InvalidOperationException("Already applied.");
        islands = route.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<MeshFilter>()!=null &&
            System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^0[1-9]_(StartVillage|RelayIsland|ForestSlalom|TeamGate|RelayRest|WallRelay|TrapCrossing|HighSwitch|FinalDelivery)$")).OrderBy(t=>t.name).ToArray();
        if (islands.Length != 9) throw new InvalidOperationException("Expected nine islands.");
        stages = route.Find("IslandRouteComposition_v7").Cast<Transform>().Where(t=>t.name.StartsWith("Island_")).OrderBy(t=>t.name).ToArray();
        bridges = BridgeNames.Select(Find).ToArray();
        Directory.CreateDirectory(Evidence);
        if (!File.Exists(Evidence+"/Level01.Before.unity")) File.Copy(scene.path,Evidence+"/Level01.Before.unity");
        stone=Mat("Stone");wood=Mat("BridgeWood");gold=Mat("GoalGold");leaves=Mat("Leaves");blue=Mat("RelayBlue");
        if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets/Environment/FloatingCoop","RoutePolish");
        metal=ColorMat("IronBands",new Color(.23f,.28f,.3f));
        earth=ColorMat("PathEarth",new Color(.54f,.43f,.28f));
        red=ColorMat("SwitchRed",new Color(.9f,.12f,.06f));
        cone=CreateCone();rock=CreateRock();barrel=CreateBarrel();
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Polish island scenery and cooperative obstacles");
        var active=SceneManager.GetActiveScene();SceneManager.SetActiveScene(scene);
        try
        {
            root=Group("IslandRoutePolish_v8",route);
            foreach(var t in route.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="CargoWaitingBay"||t.name.Contains("_Tree")).ToArray()) Hide(t.gameObject);
            AlignWaitingBays();
            RebuildClimb("WallSwitch_C",3,-12.5f,2.4f);
            RebuildClimb("WallSwitch_A",5,12.5f,2.7f);
            RebuildSweeper();
            RebuildCrusher();
            AddPaths();
            Physics.SyncTransforms();
            reserved=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>()).Where(c=>c.enabled &&
                !islands.Contains(c.transform) && !bridges.Any(b=>c.transform==b||c.transform.IsChildOf(b))).ToArray();
            reservedPaths=root.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("BridgeWaitingBay_")||r.name=="CargoGroundPath").Select(r=>r.bounds).ToArray();
            clusters.Clear();
            for(int i=0;i<9;i++) DecorateIsland(i);
            foreach(var text in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true))) Hide(text.gameObject);
            AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("Cannot save Level01.");
            Undo.CollapseUndoOperations(undo);
            return "Aligned16bridge waiting bays; rebuilt2climbs, sweeper and crusher; scenery clusters="+clusters.Count+"; saved Level01. Backup: "+Evidence;
        }
        catch { Undo.RevertAllDownToGroup(undo);throw; }
        finally { if(active.IsValid()&&active.isLoaded)SceneManager.SetActiveScene(active); }
    }

    private static void AlignWaitingBays()
    {
        for(int i=0;i<8;i++)for(int end=0;end<2;end++)
        {
            var b=bridges[i];var mesh=b.Find("ContinuousCargoDeck_7m").GetComponent<MeshFilter>().sharedMesh;
            float z=end==0?0:mesh.bounds.max.z;
            float y=mesh.vertices.Where(v=>Mathf.Abs(v.z-z)<.001f).Max(v=>v.y);
            var p=b.TransformPoint(new Vector3(0,y,z))+b.forward*(end==0?-2.1f:2.1f);
            p.y=islands[i+end].position.y+.025f;
            var pad=Box("BridgeWaitingBay_"+(i+1).ToString("00")+(end==0?"_Start":"_End"),root,Vector3.zero,new Vector3(6,.025f,4),gold,false);
            pad.transform.SetPositionAndRotation(p,b.rotation);
        }
    }

    private static void RebuildClimb(string name,int index,float x,float step)
    {
        var old=Find(name+"_FourStageClimb");Hide(old.gameObject);
        var gate=Find(name).GetComponent<ClimbSwitchGate>();
        var g=Group(name+"_TerracedClimb",root);
        g.SetPositionAndRotation(stages[index].TransformPoint(new Vector3(x,0,-5)),stages[index].rotation);
        for(int j=0;j<3;j++)
        {
            float cx=(j-1)*3.6f,h=step*(j+1);
            var wall=Box("ClimbWall_"+j,g,new Vector3(cx,h*.5f,0),new Vector3(3.4f,h,1.5f),stone,true);wall.layer=3;
            // Keep each rest with its own wall; spanning under the next wall
            // creates an overhead shelf directly in the third-person aim path.
            float ledgeX=cx;
            float width=3.4f;
            var ledge=Box(j==2?"SwitchLanding":"TransferRest_"+j,g,new Vector3(ledgeX,h-.14f,-1.85f),new Vector3(width,.28f,2.2f),wood,true);ledge.layer=3;
            // Visual grips have no separate contact geometry; the climbing face remains continuous.
            for(int k=0;k<j+2;k++)for(int side=-1;side<=1;side+=2)
                Box("StoneGrip",g,new Vector3(cx+side*.75f,.8f+k*step*.65f,-.82f),new Vector3(.4f,.15f,.14f),stone,false);
            Box("LandingTrim",g,new Vector3(ledgeX,h-.03f,-2.98f),new Vector3(width,.18f,.14f),metal,false);
            float outer=j==2?cx+1.63f:cx-1.63f;
            Box("LandingSideGuard",g,new Vector3(outer,h+.42f,-1.85f),new Vector3(.14f,.84f,2.15f),wood,true);
        }
        float top=step*3;
        Box("ButtonBackplate",g,new Vector3(3.6f,top+.75f,-.56f),new Vector3(2,1.5f,.28f),stone,true);
        var button=Primitive("HighButton",PrimitiveType.Cylinder,g,new Vector3(3.6f,top+.65f,-.84f),new Vector3(.85f,.1f,.85f),red,false);
        button.transform.localRotation=Quaternion.Euler(90,0,0);
        var so=new SerializedObject(gate);so.FindProperty("_button").objectReferenceValue=button.transform;
        so.FindProperty("_indicator").objectReferenceValue=button.GetComponent<Renderer>();
        so.FindProperty("_interactionDistance").floatValue=2;
        so.FindProperty("_requiredHeightOffset").floatValue=.1f;so.ApplyModifiedProperties();
        var barrier=(GameObject)so.FindProperty("_barrier").objectReferenceValue;
        var start=g.TransformPoint(new Vector3(3.6f,top+.65f,-.65f));
        var corner=g.TransformPoint(new Vector3(5.5f,top+.65f,-.65f));
        var down=g.TransformPoint(new Vector3(5.5f,.3f,-.65f));
        Beam("SwitchCable",root,start,corner,.07f,.07f,blue,false);
        Beam("SwitchCable",root,corner,down,.07f,.07f,blue,false);
        CompleteCableRoute(index,down);
        // Frame stands outside the opening. The barrier can deactivate without removing the frame.
        for(int side=-1;side<=1;side+=2)
            Box("ClimbGatePost",stages[index],new Vector3(side*3.85f,2.3f,0),new Vector3(.6f,4.6f,1.2f),stone,true);
        Box("ClimbGateLintel",stages[index],new Vector3(0,4.75f,0),new Vector3(8.3f,.45f,1.25f),wood,true);
    }

    private static void RebuildSweeper()
    {
        var t=Find("RotatingLog_Bridge");var b=bridges[4];
        var deck=b.Find("ContinuousCargoDeck_7m");var v=deck.GetComponent<MeshFilter>().sharedMesh.vertices;
        var floor=deck.TransformPoint((v[32]+v[33]+v[34]+v[35])*.25f);
        Undo.RecordObject(t,"Align sweeper");t.SetPositionAndRotation(floor,b.rotation);t.localScale=Vector3.one;
        var old=t.Find("RollingLogVisual");Hide(old.gameObject);
        foreach(var c in b.Cast<Transform>().Where(c=>c.name=="LogBearing").ToArray())Hide(c.gameObject);
        Box("SweeperFoundation",t,new Vector3(0,.12f,0),new Vector3(.65f,.24f,.65f),stone,true);
        Primitive("VerticalAxle",PrimitiveType.Cylinder,t,new Vector3(0,.55f,0),new Vector3(.32f,.43f,.32f),metal,false);
        var beam=Box("RotatingWoodenBeam",t,new Vector3(0,1.05f,0),new Vector3(6.35f,.42f,.42f),wood,true);
        beam.GetComponent<Renderer>().sharedMaterial=ColorMat("HazardWood",new Color(.68f,.38f,.16f));
        var body=Undo.AddComponent<Rigidbody>(beam);body.isKinematic=true;body.useGravity=false;
        body.interpolation=RigidbodyInterpolation.Interpolate;body.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;
        // Work at unit Rigidbody scale: band attachments and damage volume have predictable dimensions.
        var beamMesh=beam.GetComponent<MeshFilter>();var cube=beamMesh.sharedMesh;
        beam.transform.localScale=Vector3.one;
        beamMesh.sharedMesh=ScaledMesh("SweeperBeamMesh",cube,new Vector3(6.35f,.42f,.42f));
        var solid=beam.GetComponent<BoxCollider>();solid.size=new Vector3(6.35f,.42f,.42f);
        foreach(float pos in new[]{-2.65f,0,2.65f})Box("IronBeamBand",beam.transform,new Vector3(pos,0,0),new Vector3(.18f,.45f,.45f),metal,false);
        var zone=Undo.AddComponent<BoxCollider>(beam);zone.isTrigger=true;zone.size=new Vector3(6.4f,.48f,.48f);
        beam.transform.localRotation=Quaternion.Euler(0,90,0);
        var lamp=t.Find("LogWarningLamp");Undo.RecordObject(lamp,"Mount warning lamp");lamp.localPosition=new Vector3(-4.1f,1.7f,-4);lamp.localRotation=Quaternion.identity;lamp.localScale=Vector3.one*.35f;
        Box("SweeperLampPost",t,new Vector3(-4.1f,.75f,-4),new Vector3(.3f,1.5f,.3f),wood,false);
        Box("LampRailBracket",t,new Vector3(-3.9f,.75f,-4),new Vector3(.5f,.16f,.22f),metal,false);
        var so=new SerializedObject(t.GetComponent<RotatingLogHazard>());
        so.FindProperty("_logRigidbody").objectReferenceValue=body;so.FindProperty("_impactZone").objectReferenceValue=zone;
        so.FindProperty("_axis").enumValueIndex=1;so.FindProperty("_safeSeconds").floatValue=5;
        so.FindProperty("_warningSeconds").floatValue=2;so.FindProperty("_sweepSeconds").floatValue=5;
        so.FindProperty("_parkAngle").floatValue=90;so.FindProperty("_cargoImpactDamage").intValue=10;
        so.ApplyModifiedProperties();
    }

    private static void RebuildCrusher()
    {
        var t=Find("CrusherEntry");var so=new SerializedObject(t.GetComponent<NetworkRouteHazard>());
        // Two portal walls leave a recess around the entire jaw stroke. No jaw is embedded in a solid wall.
        foreach(var wing in stages[4].Cast<Transform>().Where(c=>c.name.StartsWith("MandatoryRouteWing")).ToArray())Hide(wing.gameObject);
        var mesh=islands[4].GetComponent<MeshFilter>().sharedMesh;
        float lo=mesh.vertices.Min(v=>stages[4].InverseTransformPoint(islands[4].TransformPoint(v)).x)-1;
        float hi=mesh.vertices.Max(v=>stages[4].InverseTransformPoint(islands[4].TransformPoint(v)).x)+1;
        foreach(float z in new[]{-2.25f,2.25f})
        {
            Box("CrusherRecessWall_Left",stages[4],new Vector3((lo-3.5f)*.5f,2.2f,z),new Vector3(-3.5f-lo,4.4f,.6f),stone,true);
            Box("CrusherRecessWall_Right",stages[4],new Vector3((hi+3.5f)*.5f,2.2f,z),new Vector3(hi-3.5f,4.4f,.6f),stone,true);
        }
        foreach(var old in t.Cast<Transform>().Where(c=>c.name=="CrusherHousing").ToArray())Hide(old.gameObject);
        var left=(Rigidbody)so.FindProperty("_primary").objectReferenceValue;
        var right=(Rigidbody)so.FindProperty("_secondary").objectReferenceValue;
        for(int side=-1;side<=1;side+=2)
        {
            var body=side<0?left:right;Undo.RecordObject(body.transform,"Refit crusher jaw");
            body.transform.localPosition=new Vector3(side*5.1f,1.8f,0);body.transform.localRotation=Quaternion.identity;body.transform.localScale=new Vector3(3.2f,3.6f,1.7f);
            // Jaws open at +/-3.5m and stop with a .6m gap, without passing through each other.
            for(int band=0;band<3;band++)
                Box("JawIronFace",body.transform,new Vector3(0,-.3f+band*.3f,-.52f),new Vector3(.96f,.035f,.035f),metal,false);
            Box("CrusherRearHousing",t,new Vector3(side*7.3f,2.15f,0),new Vector3(.9f,4.3f,2.6f),stone,true);
            Box("CrusherArchPost",t,new Vector3(side*5.1f,2.35f,-1.35f),new Vector3(.7f,4.7f,.6f),stone,true);
            Box("CrusherArchPost",t,new Vector3(side*5.1f,2.35f,1.35f),new Vector3(.7f,4.7f,.6f),stone,true);
            // Tracks sit below the existing floor and never create a lip in the cargo route.
            Box("JawTrack",t,new Vector3(side*4.2f,-.11f,0),new Vector3(6.8f,.18f,2),metal,false);
            Beam("CrusherBrace",t,t.TransformPoint(new Vector3(side*8.1f,.1f,-1.7f)),t.TransformPoint(new Vector3(side*6.9f,3.8f,-1.7f)),.25f,.25f,wood,false);
        }
        Box("CrusherLintel",t,new Vector3(0,4.85f,0),new Vector3(15.5f,.55f,3.3f),stone,true);
        Box("CrusherFrontBeam",t,new Vector3(0,4.5f,-1.62f),new Vector3(15,.25f,.2f),wood,false);
        var lamp=t.Find("WarningLamp");Undo.RecordObject(lamp,"Mount crusher signal");lamp.localPosition=new Vector3(-5.1f,5.35f,0);lamp.localScale=Vector3.one*.45f;
        var warning=Box("CrusherWarningStrip",t,new Vector3(0,.035f,-2),new Vector3(7,.025f,.3f),gold,false);
        so.FindProperty("_surface").objectReferenceValue=warning.GetComponent<Renderer>();
        so.FindProperty("_primaryRest").vector3Value=left.transform.localPosition;
        so.FindProperty("_secondaryRest").vector3Value=right.transform.localPosition;
        so.FindProperty("_travel").floatValue=3.2f;
        so.FindProperty("_safeSeconds").floatValue=6;so.FindProperty("_warningSeconds").floatValue=2;
        so.FindProperty("_activeSeconds").floatValue=4;so.ApplyModifiedProperties();
    }

    private static void AddPaths()
    {
        for(int i=0;i<9;i++)
        {
            if(i==6)continue; // Preserve the real opening under the collapsing floor.
            var a=i==0?islands[i].position:root.Find("BridgeWaitingBay_"+i.ToString("00")+"_End").position;
            var b=i==8?islands[i].position:root.Find("BridgeWaitingBay_"+(i+1).ToString("00")+"_Start").position;
            var points=new List<Vector3>{a};
            if(i==2){points.Add(stages[i].TransformPoint(new Vector3(-3,0,-5)));points.Add(stages[i].TransformPoint(new Vector3(3,0,5)));}
            else points.Add(stages[i].position);
            points.Add(b);
            for(int j=0;j<points.Count-1;j++)
            {
                var p=points[j];var q=points[j+1];p.y=q.y=islands[i].position.y+.012f;
                if((q-p).sqrMagnitude>.1f)Beam("CargoGroundPath",root,p,q,4.2f,.018f,earth,false);
            }
        }
    }

    private static void DecorateIsland(int index)
    {
        var bounds=islands[index].GetComponent<Renderer>().bounds;
        float radius=Mathf.Min(bounds.extents.x,bounds.extents.z)-5.3f;
        float footprint=index==1?2.4f:4f;
        int wanted=index==0||index==8?6:4,placed=0;
        for(int pass=0;pass<2&&placed<wanted;pass++)for(int n=0;n<20&&placed<wanted;n++)
        {
            float angle=(n*18+index*13)*Mathf.Deg2Rad;
            float r=radius-pass*2;
            var p=stages[index].TransformPoint(new Vector3(Mathf.Cos(angle)*r,0,Mathf.Sin(angle)*r));
            var local=stages[index].InverseTransformPoint(p);
            if(Mathf.Abs(local.x)<(index==1?6.4f:9.3f)|| (index>0&&index<8&&Mathf.Abs(local.z)<5.3f))continue;
            if(clusters.Any(c=>new Vector2(c.x-p.x,c.z-p.z).sqrMagnitude<64))continue;
            bool clear=true;
            foreach(var c in reserved)
            {
                var b=c.bounds; if(p.x+footprint>b.min.x&&p.x-footprint<b.max.x&&p.z+footprint>b.min.z&&p.z-footprint<b.max.z){clear=false;break;}
            }
            if(!clear||!GroundFits(index,p,footprint))continue;
            if(reservedPaths.Any(b=>p.x+footprint+.25f>b.min.x&&p.x-footprint-.25f<b.max.x&&p.z+footprint+.25f>b.min.z&&p.z-footprint-.25f<b.max.z))continue;
            p.y=islands[index].position.y;
            var g=Group("Scenery_"+(index+1).ToString("00")+"_"+placed,root);g.SetPositionAndRotation(p,stages[index].rotation);
            if(index==1)g.localScale=Vector3.one*.6f;
            Tree(g);
            MeshObject("LowPolyRock",g,rock,new Vector3(2.6f,.75f,.8f),new Vector3(1.8f,1.5f,1.6f),stone);
            MeshObject("SmallRock",g,rock,new Vector3(2.6f,.3f,2.35f),new Vector3(.75f,.6f,.7f),stone);
            MeshObject("Shrub",g,rock,new Vector3(2.5f,.45f,-1.45f),new Vector3(1.15f,.9f,1.1f),leaves);
            if((index+placed)%3==1)
            {
                var sapling=Group("Sapling",g);sapling.localPosition=new Vector3(-2.55f,0,.3f);sapling.localScale=Vector3.one*.65f;Tree(sapling);
            }
            else { Barrel(g,new Vector3(-2.55f,0,1.1f));if((index+placed)%3==0)Barrel(g,new Vector3(-2.55f,0,-.3f)); }
            if((index+placed)%2==0)
            {
                for(int side=-1;side<=1;side+=2)Box("FencePost",g,new Vector3(side*1.7f,.65f,3.5f),new Vector3(.2f,1.3f,.2f),wood,false);
                for(int rail=0;rail<2;rail++)Box("FenceRail",g,new Vector3(0,.45f+rail*.5f,3.5f),new Vector3(3.6f,.16f,.14f),wood,false);
            }
            clusters.Add(p);placed++;
        }
    }

    private static void CompleteCableRoute(int index,Vector3 down)
    {
        var local=stages[index].InverseTransformPoint(down);float post=index==3?-3.85f:3.85f;
        var p=stages[index].TransformPoint(new Vector3(local.x,.08f,local.z));
        var q=stages[index].TransformPoint(new Vector3(local.x,.08f,-.8f));
        var r=stages[index].TransformPoint(new Vector3(post,.08f,-.8f));
        var top=stages[index].TransformPoint(new Vector3(post,4.98f,-.8f));
        var end=stages[index].TransformPoint(new Vector3(0,4.98f,-.8f));
        foreach(var pair in new[]{new[]{down,p},new[]{p,q},new[]{q,r},new[]{r,top},new[]{top,end}})
            if((pair[1]-pair[0]).sqrMagnitude>.001f)Beam("SwitchCable",root,pair[0],pair[1],.07f,.07f,blue,false);
    }

    public static string Refine()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if(EditorApplication.isPlaying||!scene.isLoaded||scene.isDirty)throw new InvalidOperationException("Requires saved Level01 in Edit Mode.");
        route=scene.GetRootGameObjects().Single(g=>g.name=="FloatingCoopRoute").transform;root=route.Find("IslandRoutePolish_v8");
        var children=root.Cast<Transform>().ToArray();var positions=children.Select(t=>t.position).ToArray();var rotations=children.Select(t=>t.rotation).ToArray();
        Undo.RecordObject(root,"Normalize composition scale");var parentScale=route.lossyScale;root.localScale=new Vector3(1/parentScale.x,1/parentScale.y,1/parentScale.z);
        for(int i=0;i<children.Length;i++){Undo.RecordObject(children[i],"Preserve world placement");children[i].SetPositionAndRotation(positions[i],rotations[i]);}
        islands=route.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<MeshFilter>()!=null&&System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^0[1-9]_(StartVillage|RelayIsland|ForestSlalom|TeamGate|RelayRest|WallRelay|TrapCrossing|HighSwitch|FinalDelivery)$")).OrderBy(t=>t.name).ToArray();
        stages=route.Find("IslandRouteComposition_v7").Cast<Transform>().Where(t=>t.name.StartsWith("Island_")).OrderBy(t=>t.name).ToArray();bridges=BridgeNames.Select(Find).ToArray();
        stone=Mat("Stone");wood=Mat("BridgeWood");leaves=Mat("Leaves");blue=Mat("RelayBlue");metal=AssetDatabase.LoadAssetAtPath<Material>(AssetFolder+"/IronBands.mat");
        cone=CreateCone();rock=CreateRock();barrel=CreateBarrel();
        foreach(var t in root.Cast<Transform>().Where(t=>t.name.StartsWith("Scenery_")||t.name=="SwitchCable").ToArray())Undo.DestroyObjectImmediate(t.gameObject);
        foreach(int index in new[]{3,5})
        {
            var g=root.Find((index==3?"WallSwitch_C":"WallSwitch_A")+"_TerracedClimb");float step=index==3?2.4f:2.7f;
            var start=g.TransformPoint(new Vector3(3.6f,step*3+.65f,-.65f));var corner=g.TransformPoint(new Vector3(5.5f,step*3+.65f,-.65f));var down=g.TransformPoint(new Vector3(5.5f,.3f,-.65f));
            Beam("SwitchCable",root,start,corner,.07f,.07f,blue,false);Beam("SwitchCable",root,corner,down,.07f,.07f,blue,false);CompleteCableRoute(index,down);
        }
        Find("RotatingWoodenBeam").GetComponent<Renderer>().sharedMaterial=ColorMat("HazardWood",new Color(.68f,.38f,.16f));
        var sweep=Find("RotatingLog_Bridge");if(sweep.Find("LampRailBracket")==null)Box("LampRailBracket",sweep,new Vector3(-3.9f,.75f,-4),new Vector3(.5f,.16f,.22f),metal,false);
        Physics.SyncTransforms();reserved=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Collider>()).Where(c=>c.enabled&&!islands.Contains(c.transform)&&!bridges.Any(b=>c.transform==b||c.transform.IsChildOf(b))).ToArray();
        reservedPaths=root.GetComponentsInChildren<Renderer>().Where(r=>r.name.StartsWith("BridgeWaitingBay_")||r.name=="CargoGroundPath").Select(r=>r.bounds).ToArray();
        clusters.Clear();for(int i=0;i<9;i++)DecorateIsland(i);
        EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssets();EditorSceneManager.SaveScene(scene);return "Refined cable routing; paths/pads excluded from scenery. Clusters="+clusters.Count;
    }

    private static bool GroundFits(int index,Vector3 p,float radius)
    {
        var c=islands[index].GetComponent<MeshCollider>();
        for(int n=0;n<9;n++)
        {
            var q=p+(n==8?Vector3.zero:new Vector3(Mathf.Cos(n*Mathf.PI/4),0,Mathf.Sin(n*Mathf.PI/4))*radius);
            if(!c.Raycast(new Ray(q+Vector3.up*50,Vector3.down),out var hit,60)||Mathf.Abs(hit.point.y-islands[index].position.y)>.15f)return false;
        }
        return true;
    }
    private static void Tree(Transform p)
    {
        Primitive("PineTrunk",PrimitiveType.Cylinder,p,new Vector3(0,1.35f,0),new Vector3(.5f,1.35f,.5f),wood,false);
        MeshObject("PineCrown",p,cone,new Vector3(0,2,0),new Vector3(2.8f,3.2f,2.8f),leaves);
        MeshObject("PineCrownTop",p,cone,new Vector3(0,3.35f,0),new Vector3(2,2.6f,2),leaves);
    }
    private static void Barrel(Transform p,Vector3 at)
    {
        MeshObject("WoodenBarrel",p,barrel,at,new Vector3(.95f,1.25f,.95f),wood);
        for(int j=0;j<2;j++)MeshObject("BarrelBand",p,barrel,at+Vector3.up*(j==0?.23f:.91f),new Vector3(.98f,.08f,.98f),metal);
    }
    private static Mesh CreateCone()
    {
        var v=new List<Vector3>();var t=new List<int>();
        for(int i=0;i<8;i++){float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;Tri(v,t,new Vector3(Mathf.Cos(a)*.5f,0,Mathf.Sin(a)*.5f),Vector3.up,new Vector3(Mathf.Cos(b)*.5f,0,Mathf.Sin(b)*.5f));}
        return SaveMesh("PineCone",v,t);
    }
    private static Mesh CreateRock()
    {
        var v=new List<Vector3>();var t=new List<int>();
        for(int i=0;i<6;i++){float a=i*Mathf.PI/3,b=(i+1)*Mathf.PI/3;var p=new Vector3(Mathf.Cos(a)*.5f,0,Mathf.Sin(a)*.5f);var q=new Vector3(Mathf.Cos(b)*.5f,0,Mathf.Sin(b)*.5f);Tri(v,t,p,Vector3.up*.5f,q);Tri(v,t,q,Vector3.down*.5f,p);}
        return SaveMesh("FacetedRock",v,t);
    }
    private static Mesh CreateBarrel()
    {
        var v=new List<Vector3>();var t=new List<int>();
        for(int i=0;i<8;i++)
        {
            float a=i*Mathf.PI/4,b=(i+1)*Mathf.PI/4;
            var p=new Vector3(Mathf.Cos(a)*.48f,0,Mathf.Sin(a)*.48f);var q=new Vector3(Mathf.Cos(b)*.48f,0,Mathf.Sin(b)*.48f);
            Tri(v,t,p,p+Vector3.up,q+Vector3.up);Tri(v,t,p,q+Vector3.up,q);
            Tri(v,t,Vector3.up,q+Vector3.up,p+Vector3.up);Tri(v,t,Vector3.zero,p,q);
        }
        return SaveMesh("OctagonalBarrel",v,t);
    }
    private static void Tri(List<Vector3> v,List<int> t,Vector3 a,Vector3 b,Vector3 c){int n=v.Count;v.AddRange(new[]{a,b,c});t.AddRange(new[]{n,n+1,n+2});}
    private static Mesh SaveMesh(string name,List<Vector3> v,List<int> t)
    {
        string path=AssetFolder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing!=null)return existing;
        var m=new Mesh{name=name};m.SetVertices(v);m.SetTriangles(t,0);m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,path);return m;
    }
    private static Mesh ScaledMesh(string name,Mesh source,Vector3 scale)
    {
        string path=AssetFolder+"/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing!=null)return existing;
        var m=UnityEngine.Object.Instantiate(source);m.name=name;m.vertices=source.vertices.Select(v=>Vector3.Scale(v,scale)).ToArray();m.RecalculateBounds();AssetDatabase.CreateAsset(m,path);return m;
    }
    private static GameObject MeshObject(string name,Transform p,Mesh m,Vector3 at,Vector3 scale,Material mat)
    {
        var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Create scenery");go.transform.SetParent(p,false);go.transform.localPosition=at;go.transform.localScale=scale;
        go.AddComponent<MeshFilter>().sharedMesh=m;go.AddComponent<MeshRenderer>().sharedMaterial=mat;
        GameObjectUtility.SetStaticEditorFlags(go,StaticEditorFlags.BatchingStatic);return go;
    }
    private static Material ColorMat(string name,Color color)
    {
        string path=AssetFolder+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);if(mat!=null)return mat;
        mat=new Material(wood){name=name};mat.SetColor("_BaseColor",color);AssetDatabase.CreateAsset(mat,path);return mat;
    }
    private static Transform Find(string name)=>route.GetComponentsInChildren<Transform>(true).Single(t=>t.name==name);
    private static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/"+name+".mat")??throw new InvalidOperationException("Missing material "+name);
    private static Transform Group(string name,Transform p){var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Create composition");go.transform.SetParent(p,false);var s=p.lossyScale;go.transform.localScale=new Vector3(1/s.x,1/s.y,1/s.z);return go.transform;}
    private static GameObject Box(string name,Transform p,Vector3 at,Vector3 size,Material mat,bool collider)=>Primitive(name,PrimitiveType.Cube,p,at,size,mat,collider);
    private static GameObject Primitive(string name,PrimitiveType type,Transform p,Vector3 at,Vector3 size,Material mat,bool collider)
    {
        var go=GameObject.CreatePrimitive(type);Undo.RegisterCreatedObjectUndo(go,"Create obstacle geometry");go.name=name;go.transform.SetParent(p,false);go.transform.localPosition=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;
        if(!collider)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    private static void Beam(string name,Transform p,Vector3 a,Vector3 b,float width,float depth,Material mat,bool collider)
    {
        var go=Box(name,p,Vector3.zero,Vector3.one,mat,collider);go.transform.SetPositionAndRotation((a+b)*.5f,Quaternion.LookRotation(b-a));go.transform.localScale=new Vector3(width,depth,Vector3.Distance(a,b));
    }
    private static void Hide(GameObject go){if(go.activeSelf){Undo.RecordObject(go,"Preserve replaced content");go.SetActive(false);}}
}
#endif

