#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using CoopGame.CarrySystem;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Approved nine-island obstacle TEST layout; preserves the original route and player mechanics.</summary>
public static class IslandCoopGauntlet
{
    private const string RootName = "IslandCoopGauntlet_v5";
    private const string Evidence = "E:/Unity/Verification/NineIslandGauntlet-20261010";
    private static Material wood, stone, blue, gold, warning;
    private static FragileCargo cargo;
    private static LevelMission mission;
    private static Transform root;

    [MenuItem("Tools/Expedition/Apply Approved Nine Island Gauntlet")]
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if (!scene.isLoaded) throw new InvalidOperationException("Load Level01 first.");
        var route = scene.GetRootGameObjects().Single(g => g.name == "FloatingCoopRoute").transform;
        if (route.Find(RootName) != null) throw new InvalidOperationException("Gauntlet already exists; inspect it instead of duplicating it.");
        var all = route.GetComponentsInChildren<Transform>(true);
        string[] names = {"01_StartVillage","02_RelayIsland","03_ForestSlalom","04_TeamGate","05_RelayRest","06_WallRelay","07_TrapCrossing","08_HighSwitch","09_FinalDelivery"};
        var islands = names.Select(n => all.Single(t => t.name == n)).ToArray();
        wood = Mat("BridgeWood"); stone = Mat("Stone"); blue = Mat("RelayBlue"); gold = Mat("GoalGold"); warning = Mat("TrapWarning");
        cargo = scene.GetRootGameObjects().Select(g => g.GetComponent<FragileCargo>()).First(c => c != null);
        mission = scene.GetRootGameObjects().Select(g => g.GetComponent<LevelMission>()).First(c => c != null);
        Directory.CreateDirectory(Evidence);
        if (!EditorSceneManager.SaveScene(scene, Evidence + "/Level01.Before.unity", true)) throw new IOException("Backup failed.");
        var active = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Nine island co-op gauntlet");
        try
        {
            root = Group(RootName, route, Vector3.zero);
            root.localScale = new Vector3(1f / route.lossyScale.x, 1f / route.lossyScale.y, 1f / route.lossyScale.z);
            Undo.RecordObject(cargo.transform,"Keep initial cargo clear of the first turn");
            cargo.transform.position = islands[0].position + new Vector3(0,1.25f,-5f);
            // Old collision props are kept in the scene but replaced by measured cargo corridors.
            foreach (var t in all.Where(t => new[]{"SlalomA","SlalomB","SlalomC","SlalomExtra_A","SlalomExtra_B"}.Contains(t.name))) Hide(t.gameObject);
            for (int i = 0; i < islands.Length; i++)
            {
                var stage = Group((i+1).ToString("00") + "_" + names[i].Substring(3) + "_Challenge", root, islands[i].position);
                // World metres and level floors; keep diagonal bridge landings clear outside this compact corridor.
                switch (i)
                {
                    case 0:
                        Chicane(stage, -1.5f, 4f, 6.4f); Crusher(stage, "TimedEntryGate", 10.5f, 6.5f, 0f);
                        Rest(stage, new Vector3(0,0,-3)); break;
                    case 1:
                        Hammer(stage, "RelaySweep", -6f, 7f, 1.8f);
                        RaiseRelayPads(route); Rest(stage, new Vector3(0,0,-10.5f)); break;
                    case 2:
                        Chicane(stage, -6.5f, 6.5f, 6f); Hammer(stage,"ForestSweep",7f,5.5f,3f);
                        Rest(stage,new Vector3(0,0,-11)); break;
                    case 3:
                        NewClimbGate(stage,8.4f); Rest(stage,new Vector3(0,0,-7)); break;
                    case 4:
                        Crusher(stage,"CrusherEntry",-7f,6f,0f); Crusher(stage,"CrusherExit",7f,6f,5.8f);
                        Rails(stage,8.5f,22f); Rest(stage,Vector3.zero); break;
                    case 5:
                        RebuildClimb(route.GetComponentsInChildren<ClimbSwitchGate>(true).Single(g=>g.name=="WallSwitch_A"),10.4f);
                        Chicane(stage,-5f,8f,6.2f); Rest(stage,new Vector3(0,0,-11)); break;
                    case 6:
                        Rails(stage,9f,24f);
                        for(int j=0;j<3;j++){Flame(stage,"FirePulse_"+j,-7f+j*7f,j*3.8f); Rest(stage,new Vector3(0,0,-10.5f+j*7f),2.6f);}
                        break;
                    case 7:
                        RebuildClimb(route.GetComponentsInChildren<ClimbSwitchGate>(true).Single(g=>g.name=="WallSwitch_B"),12f);
                        Crusher(stage,"SummitCrusher",1.5f,6.5f,3.5f); Rest(stage,new Vector3(0,0,-7)); break;
                    case 8:
                        Chicane(stage,-13f,6f,6f); ChargeGate(stage,-3.5f);
                        Rest(stage,new Vector3(0,0,-17)); break;
                }
            }
            RefineCorridors(route, islands);
            // Maintain the user's request: no world instruction text or boards.
            foreach(var text in scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true))) Hide(text.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
            Undo.CollapseUndoOperations(group);
            return "Created nine island challenge groups, 4 crushers, 2 sweeps, 3 flame pulses, 3 four-stage climbs and a co-op finish gate. Backup: " + Evidence;
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
        finally { if(active.IsValid()&&active.isLoaded) SceneManager.SetActiveScene(active); }
    }

    private static void Chicane(Transform parent,float start,float spacing,float gap)
    {
        float width=12f;
        Rails(parent,width,spacing+10f,start+spacing*.5f);
        for(int i=0;i<2;i++)
        {
            float wallWidth=width-gap;
            float x=(i==0?-1:1)*(width-wallWidth)*.5f;
            Box("CargoTurnWall_"+i,parent,new Vector3(x,1.35f,start+i*spacing),new Vector3(wallWidth,2.7f,.65f),stone);
            Box("TurnMarker_"+i,parent,new Vector3(-x,.025f,start+i*spacing),new Vector3(gap-.3f,.025f,.4f),blue,false);
        }
    }

    private static void Rails(Transform parent,float width,float length,float z=0f)
    {
        for(int side=-1;side<=1;side+=2)
        {
            Box("CargoLaneRail",parent,new Vector3(side*(width*.5f+.18f),.55f,z),new Vector3(.25f,1.1f,length),wood);
            for(int j=-1;j<=1;j++) Box("RailPost",parent,new Vector3(side*(width*.5f+.18f),.75f,z+j*length*.45f),new Vector3(.4f,1.5f,.4f),stone);
        }
    }

    private static void Rest(Transform p,Vector3 at,float depth=4f)
    {
        Box("CargoRest_Gold",p,at+Vector3.up*.09f,new Vector3(5.4f,.03f,depth),gold,false);
        for(int side=-1;side<=1;side+=2)Box("RestCorner",p,at+new Vector3(side*2.6f,.035f,-depth*.5f),new Vector3(.4f,.04f,.4f),blue,false);
    }

    private static Transform Mechanism(Transform parent,string name,float z)
    {
        var t=Group(name,parent,parent.TransformPoint(new Vector3(0,0,z)));
        Undo.AddComponent<NetworkRouteHazard>(t.gameObject);
        return t;
    }

    private static void Crusher(Transform parent,string name,float z,float safe,float offset)
    {
        var t=Mechanism(parent,name,z);
        var a=Box("LeftCrusher",t,new Vector3(-5.7f,1.3f,0),new Vector3(2.4f,2.6f,2.4f),stone);
        var b=Box("RightCrusher",t,new Vector3(5.7f,1.3f,0),new Vector3(2.4f,2.6f,2.4f),stone);
        Body(a); Body(b);
        var so=new SerializedObject(t.GetComponent<NetworkRouteHazard>());
        Bind(so,NetworkRouteHazard.HazardKind.Crusher,a,b,t,safe,offset);
        so.FindProperty("_travel").floatValue=4.3f;
        so.FindProperty("_primaryRest").vector3Value=a.transform.localPosition;
        so.FindProperty("_secondaryRest").vector3Value=b.transform.localPosition;
        so.ApplyModifiedProperties();
        Box("CrushZone",t,new Vector3(0,.028f,0),new Vector3(8.6f,.03f,2.5f),warning,false);
        for(int side=-1;side<=1;side+=2)Box("CrusherHousing",t,new Vector3(side*7.1f,1.5f,0),new Vector3(.5f,3f,3f),wood);
    }

    private static void Hammer(Transform parent,string name,float z,float safe,float offset)
    {
        var t=Mechanism(parent,name,z);
        for(int side=-1;side<=1;side+=2)Box("SweepPost",t,new Vector3(side*5f,2.9f,0),new Vector3(.45f,5.8f,.5f),wood);
        Box("SweepBeam",t,new Vector3(0,5.9f,0),new Vector3(10.5f,.4f,.6f),wood);
        var pivot=Group("SwingPivot",t,t.TransformPoint(Vector3.up*5.6f));
        var rb=Undo.AddComponent<Rigidbody>(pivot.gameObject); SetupBody(rb);
        Box("SwingArm",pivot,new Vector3(0,-2.1f,0),new Vector3(.25f,4.2f,.25f),wood,false);
        var head=Box("SwingHead",pivot,new Vector3(0,-4.2f,0),new Vector3(1.6f,1.8f,1.5f),stone);
        pivot.localRotation=Quaternion.Euler(0,0,70f);
        var so=new SerializedObject(t.GetComponent<NetworkRouteHazard>());
        Bind(so,NetworkRouteHazard.HazardKind.SwingHammer,pivot.gameObject,null,t,safe,offset);
        Ref(so,"_hitA",head.GetComponent<BoxCollider>()); so.FindProperty("_swingAngle").floatValue=70f; so.ApplyModifiedProperties();
        Box("SweepZone",t,new Vector3(0,.025f,0),new Vector3(9f,.03f,2f),warning,false);
    }

    private static void Bind(SerializedObject so,NetworkRouteHazard.HazardKind kind,GameObject a,GameObject b,Transform t,float safe,float offset)
    {
        so.FindProperty("_kind").enumValueIndex=(int)kind;
        Ref(so,"_primary",a.GetComponent<Rigidbody>());Ref(so,"_secondary",b==null?null:b.GetComponent<Rigidbody>());
        Ref(so,"_hitA",a.GetComponent<BoxCollider>());Ref(so,"_hitB",b==null?null:b.GetComponent<BoxCollider>());Ref(so,"_cargo",cargo);
        so.FindProperty("_safeSeconds").floatValue=safe;so.FindProperty("_warningSeconds").floatValue=1.8f;
        so.FindProperty("_activeSeconds").floatValue=4f;so.FindProperty("_phaseOffset").floatValue=offset;so.FindProperty("_damage").intValue=10;
        var lamp=Box("WarningLamp",t,new Vector3(-4.4f,2.2f,-2f),new Vector3(.5f,.7f,.5f),warning,false);
        Ref(so,"_indicator",lamp.GetComponent<Renderer>());
    }

    private static void Flame(Transform parent,string name,float z,float offset)
    {
        var t=Group(name,parent,parent.TransformPoint(new Vector3(0,0,z)));
        var trap=Undo.AddComponent<TimedCargoTrap>(t.gameObject);
        var marker=Box("FireGrate",t,new Vector3(0,.025f,0),new Vector3(8.8f,.03f,2.2f),warning,false);
        var zone=Group("DamageZone",t,t.position).gameObject;
        var box=Undo.AddComponent<BoxCollider>(zone);box.isTrigger=true;box.center=Vector3.up*1.3f;box.size=new Vector3(8.8f,2.6f,2.2f);
        var flames=Group("Flames",t,t.position);
        for(int j=0;j<7;j++)
        {
            var flame=Box("FlameJet",flames,new Vector3(-3.6f+j*1.2f,1.2f,0),new Vector3(.28f,2.4f,.28f),warning,false);
            Box("Nozzle",t,new Vector3(-3.6f+j*1.2f,.05f,0),new Vector3(.5f,.1f,.5f),stone,false);
        }
        flames.gameObject.SetActive(false);
        var so=new SerializedObject(trap);Ref(so,"_zone",box);Ref(so,"_flames",flames.gameObject);Ref(so,"_marker",marker.GetComponent<Renderer>());Ref(so,"_cargo",cargo);
        so.FindProperty("_safeSeconds").floatValue=6.5f;so.FindProperty("_warningSeconds").floatValue=1.8f;so.FindProperty("_activeSeconds").floatValue=3f;so.FindProperty("_phaseOffset").floatValue=offset;so.ApplyModifiedProperties();
    }

    private static void RaiseRelayPads(Transform route)
    {
        foreach(string side in new[]{"Left","Right"})
        {
            var visual=route.Find("BluePad_"+side+"_Visible");var occupancy=route.Find("BluePad_"+side+"_Occupancy");
            if(visual==null||occupancy==null)throw new InvalidOperationException("Missing relay pads.");
            Vector3 ground=visual.position-Vector3.up*.03f;
            Box("RelayPlinth_"+side,root,ground+Vector3.up*.6f,new Vector3(3.8f,1.2f,3.8f),stone);
            for(int j=0;j<3;j++)Box("RelayStep_"+side+j,root,ground+new Vector3(0,(j+1)*.15f,-3.6f+j*.7f),new Vector3(3f,(j+1)*.3f,1f),wood);
            Undo.RecordObject(visual,"Raise relay pad");visual.position+=Vector3.up*1.2f;
            Undo.RecordObject(occupancy,"Raise relay occupancy");occupancy.position+=Vector3.up*1.2f;
        }
    }

    private static void NewClimbGate(Transform stage,float height)
    {
        var t=Group("WallSwitch_C",stage,stage.position);
        var gate=Undo.AddComponent<ClimbSwitchGate>(t.gameObject);
        var barrier=Box("LockedCargoPassage",stage,new Vector3(0,2f,6f),new Vector3(8f,4f,.5f),wood);
        for(int side=-1;side<=1;side+=2)Box("GateWing",stage,new Vector3(side*6f,2f,6f),new Vector3(4f,4f,.65f),stone);
        var so=new SerializedObject(gate);Ref(so,"_barrier",barrier);Ref(so,"_mission",mission);so.ApplyModifiedProperties();
        BuildClimb(gate,stage.position+new Vector3(-8,0,-2),height);
    }

    private static void RebuildClimb(ClimbSwitchGate gate,float height)
    {
        var wall=gate.transform.Find("ClimbWall");if(wall==null)throw new InvalidOperationException("Missing old climb wall.");
        Vector3 origin=new Vector3(wall.position.x+2.8f,wall.position.y-wall.lossyScale.y*.5f,wall.position.z);
        var route=gate.transform.root;
        foreach(var t in route.GetComponentsInChildren<Transform>(true))
            if(t.name.StartsWith(gate.name+"_WallStage")||t.name.StartsWith(gate.name+"_RouteStripe")||t.name.StartsWith(gate.name+"_RestLedge")||t.name.StartsWith(gate.name+"_RestMark")||t.name.StartsWith(gate.name+"_TopRail"))Hide(t.gameObject);
        Hide(wall.gameObject);Hide(gate.transform.Find("SwitchLedge").gameObject);Hide(gate.transform.Find("HighButton").gameObject);
        BuildClimb(gate,origin,height);
    }

    private static void BuildClimb(ClimbSwitchGate gate,Vector3 origin,float height)
    {
        var climb=Group(gate.name+"_FourStageClimb",root,origin);
        float step=height/4f;
        for(int j=0;j<4;j++)
        {
            float x=j%2==0?-1.3f:1.3f;
            var wall=Box("ClimbStage_"+j,climb,new Vector3(x,step*(j+.5f),0),new Vector3(4.8f,step,1.2f),stone);wall.layer=3;
            Box("ClimbStripe",climb,new Vector3(x,step*(j+.5f),-.615f),new Vector3(.25f,step-.2f,.025f),blue,false);
            var ledge=Box(j==3?"TopSwitchLanding":"RestLedge_"+j,climb,new Vector3(x,step*(j+1)-.15f,-1.4f),new Vector3(4.8f,.3f,2.8f),wood);ledge.layer=3;
            Box("RestStripe",climb,new Vector3(x,step*(j+1)+.015f,-1.8f),new Vector3(3.3f,.025f,1.2f),gold,false);
        }
        var button=Box("HighButton",climb,new Vector3(1.3f,height+.35f,-.7f),new Vector3(1f,.65f,.25f),blue,false);
        var so=new SerializedObject(gate);Ref(so,"_button",button.transform);Ref(so,"_indicator",button.GetComponent<Renderer>());
        so.FindProperty("_interactionDistance").floatValue=1.8f;so.FindProperty("_requiredHeightOffset").floatValue=.2f;so.ApplyModifiedProperties();
        // Safe descending route for the climber; cargo continues through the existing level passage.
        for(int j=0;j<4;j++)Box("SideRestGuard",climb,new Vector3((j%2==0?-1.3f:1.3f)-2.3f,step*(j+1)+.35f,-1.4f),new Vector3(.2f,.7f,2.8f),wood);
    }

    public static string RefineExisting()
    {
        var scene=SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if(EditorApplication.isPlaying||!scene.isLoaded)throw new InvalidOperationException("Requires loaded Level01 in Edit Mode.");
        var route=scene.GetRootGameObjects().Single(g=>g.name=="FloatingCoopRoute").transform;
        root=route.Find(RootName);stone=Mat("Stone");
        string[] names={"01_StartVillage","02_RelayIsland","03_ForestSlalom","04_TeamGate","05_RelayRest","06_WallRelay","07_TrapCrossing","08_HighSwitch","09_FinalDelivery"};
        var all=route.GetComponentsInChildren<Transform>(true);
        RefineCorridors(route,names.Select(n=>all.Single(t=>t.name==n)).ToArray());
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return "Refined cargo gaps, trap approach barriers, rest markings and climb guards.";
    }

    private static void RefineCorridors(Transform route,Transform[] islands)
    {
        foreach(var t in route.GetComponentsInChildren<Transform>(true))
            if(!t.IsChildOf(root) && System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^(0[1-4]_CargoRest|05_RelayRest_Pad|0[6-9]_(WallRelay|TrapCrossing|HighSwitch|FinalDelivery)_Rest)"))Hide(t.gameObject);
        var stages=root.Cast<Transform>().Where(t=>System.Text.RegularExpressions.Regex.IsMatch(t.name,@"^\d{2}_.*_Challenge$")).ToArray();
        for(int i=0;i<stages.Length;i++)
        {
            var stage=stages[i];
            if(stage.Find("CorridorCaps")!=null)continue;
            var caps=Group("CorridorCaps",stage,stage.position);
            // Cross-island baffles prevent simply carrying around the marked challenge lane.
            foreach(var wall in stage.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("CargoTurnWall_")).ToArray())
            {
                var bounds=wall.GetComponent<Renderer>().bounds;
                float z=wall.position.z-stage.position.z;
                float half=HalfWidth(islands[i],z);
                float lo=bounds.min.x-stage.position.x,hi=bounds.max.x-stage.position.x;
                float gap=12f-bounds.size.x;
                if(wall.localPosition.x<0){AddCap(caps,-half,lo,z);AddCap(caps,hi+gap,half,z);}
                else {AddCap(caps,hi,half,z);AddCap(caps,-half,lo-gap,z);}
            }
            foreach(var hazard in stage.GetComponentsInChildren<NetworkRouteHazard>())
            {
                float z=hazard.transform.position.z-stage.position.z;
                float half=HalfWidth(islands[i],z);
                float lane=hazard.Kind==NetworkRouteHazard.HazardKind.Crusher?4.5f:4.6f;
                AddCap(caps,-half,-lane,z);AddCap(caps,lane,half,z);
            }
            foreach(var trap in stage.GetComponentsInChildren<TimedCargoTrap>())
            {
                float z=trap.transform.position.z-stage.position.z;
                float half=HalfWidth(islands[i],z);AddCap(caps,-half,-4.5f,z);AddCap(caps,4.5f,half,z);
            }
            if(i==3){float half=HalfWidth(islands[i],6);AddCap(caps,-half,-8f,6);AddCap(caps,8f,half,6);}
            if(i==8){float half=HalfWidth(islands[i],-3.5f);AddCap(caps,-half,-9.9f,-3.5f);AddCap(caps,9.9f,half,-3.5f);}
        }
        foreach(var climb in root.Cast<Transform>().Where(t=>t.name.EndsWith("_FourStageClimb")))
        {
            var guards=climb.Cast<Transform>().Where(t=>t.name=="SideRestGuard").ToArray();
            for(int j=0;j<guards.Length;j++){Undo.RecordObject(guards[j],"Align resting ledge guards");var p=guards[j].localPosition;p.x=(j%2==0?-1.3f:1.3f)-2.3f;guards[j].localPosition=p;}
        }
    }

    private static float HalfWidth(Transform island,float z)
    {
        var b=island.GetComponent<Renderer>().bounds;
        // Keep walls just inside the usable edge; island meshes vary slightly from a perfect ellipse.
        float radiusZ=b.extents.z;
        return Mathf.Max(5f,b.extents.x*Mathf.Sqrt(Mathf.Max(0,1f-z*z/(radiusZ*radiusZ)))-.6f);
    }

    private static void AddCap(Transform p,float lo,float hi,float z)
    {
        if(hi-lo<.15f)return;
        Box("CrossIslandBaffle",p,new Vector3((lo+hi)*.5f,1.35f,z),new Vector3(hi-lo,2.7f,.65f),stone);
    }

    private static void ChargeGate(Transform stage,float z)
    {
        var t=Group("FinalCoopChargeGate",stage,stage.TransformPoint(new Vector3(0,0,z)));
        var gate=Undo.AddComponent<CoopChargeGate>(t.gameObject);
        var barrier=Box("FinalBarrier",t,new Vector3(0,2f,0),new Vector3(9f,4f,.45f),wood);
        for(int side=-1;side<=1;side+=2)Box("FinalGateWing",t,new Vector3(side*7.2f,2f,0),new Vector3(5.4f,4f,.5f),stone);
        var so=new SerializedObject(gate);Ref(so,"_barrier",barrier);
        for(int side=-1;side<=1;side+=2)
        {
            string prefix=side<0?"_left":"_right";
            var pad=Box("ChargePad",t,new Vector3(side*3f,.04f,-2f),new Vector3(2.5f,.06f,2.5f),blue,false);
            var zone=Group("PadOccupancy",t,pad.transform.position).gameObject;
            var box=Undo.AddComponent<BoxCollider>(zone);box.isTrigger=true;box.center=new Vector3(0,.3f,0);box.size=new Vector3(2.5f,1.3f,2.5f);
            Ref(so,prefix+"Pad",box);Ref(so,prefix+"Lamp",pad.GetComponent<Renderer>());
            Box("PadGuide",t,new Vector3(side*1.2f,.025f,-2f),new Vector3(.8f,.025f,.3f),blue,false);
        }
        so.ApplyModifiedProperties();
    }

    private static Transform Group(string name,Transform parent,Vector3 world)
    {
        var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Create gauntlet group");
        go.transform.SetParent(parent,false);go.transform.position=world;return go.transform;
    }
    private static GameObject Box(string name,Transform parent,Vector3 at,Vector3 size,Material mat,bool solid=true)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;Undo.RegisterCreatedObjectUndo(go,"Create gauntlet geometry");
        go.transform.SetParent(parent,false);go.transform.localPosition=at;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=mat;
        if(!solid)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
    private static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>("Assets/Environment/FloatingCoop/"+name+".mat")??throw new InvalidOperationException("Missing material "+name);
    private static void Hide(GameObject go){if(!go.activeSelf)return;Undo.RecordObject(go,"Preserve replaced geometry");go.SetActive(false);}
    private static void Body(GameObject go)=>SetupBody(Undo.AddComponent<Rigidbody>(go));
    private static void SetupBody(Rigidbody rb){rb.isKinematic=true;rb.useGravity=false;rb.interpolation=RigidbodyInterpolation.Interpolate;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;}
    private static void Ref(SerializedObject so,string name,UnityEngine.Object value)=>so.FindProperty(name).objectReferenceValue=value;
}
#endif
