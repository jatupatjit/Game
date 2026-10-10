#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopGame.CarrySystem;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Applies the approved climbing/trap concept to the existing test route without replacing its player systems.</summary>
public static class FloatingCoopChallenges
{
    private const string Folder = "Assets/Environment/FloatingCoop";
    private const string Evidence = "E:/Unity/Verification/CoopChallenges-20261010";
    private static Transform _root;
    private static Material _wood, _stone, _blue, _gold, _warning, _board;
    private static Font _font;
    private static FragileCargo _cargo;

    [MenuItem("Tools/Expedition/Add Approved Climbing And Trap Challenges")]
    public static string Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = SceneManager.GetSceneByPath("Assets/Scenes/Level01.unity");
        if (!scene.isLoaded) throw new InvalidOperationException("Open Level01 first.");
        var roots = scene.GetRootGameObjects();
        var route = roots.Single(g => g.name == "FloatingCoopRoute").transform;
        if (route.Find("ChallengeUpgrade_v2") != null) throw new InvalidOperationException("Challenge upgrade already applied.");
        _wood = Mat("BridgeWood"); _stone = Mat("Stone"); _blue = Mat("RelayBlue");
        _gold = Mat("GoalGold"); _warning = Mat("TrapWarning"); _board = Mat("ThaiSignBacking");
        _font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/GameThai.ttf");
        _cargo = roots.Select(r => r.GetComponent<FragileCargo>()).FirstOrDefault(c => c != null);
        if (_font == null || _cargo == null) throw new InvalidOperationException("Thai font and cargo are required.");
        var all = route.GetComponentsInChildren<Transform>(true);
        var gates = route.GetComponentsInChildren<ClimbSwitchGate>(true);
        if (gates.Length != 2) throw new InvalidOperationException("Expected two climb switches.");
        Directory.CreateDirectory(Evidence);
        if (!EditorSceneManager.SaveScene(scene, Evidence + "/Level01.Before-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true))
            throw new IOException("Could not back up scene.");
        SceneManager.SetActiveScene(scene);
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Add approved co-op challenges");
        try
        {
            _root = new GameObject("ChallengeUpgrade_v2").transform;
            Undo.RegisterCreatedObjectUndo(_root.gameObject, "Create challenge group");
            _root.SetParent(route, false);
            // Mechanism dimensions are world meters even though the route was expanded horizontally.
            var scale = route.lossyScale;
            _root.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            foreach (var gate in gates) UpgradeWall(gate, gate.name.EndsWith("A") ? 7.5f : 9f);
            CreateHammer(all.Single(t => t.name == "03_ForestBridge"));
            CreateCrusher(all.Single(t => t.name == "07_CargoBridge"));
            CreateCollapse(all.Single(t => t.name == "04_FinalBridge"));
            RewriteLabels(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save challenge layout.");
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = _root.gameObject;
            return "Applied two staggered climbs, hammer, crusher, collapsing deck/recovery ramp, and conversational Thai signs.";
        }
        catch { Undo.RevertAllDownToGroup(group); throw; }
    }

    private static void UpgradeWall(ClimbSwitchGate gate, float height)
    {
        var wall = gate.transform.Find("ClimbWall");
        var ledge = gate.transform.Find("SwitchLedge");
        var button = gate.transform.Find("HighButton");
        if (wall == null || ledge == null || button == null) throw new InvalidOperationException("Missing wall/switch references.");
        float baseY = wall.position.y - wall.lossyScale.y * .5f;
        Vector3 origin = new Vector3(wall.position.x, baseY, wall.position.z);
        float step = height / 3f;
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 2.8f;
            Vector3 at = origin + new Vector3(x, step * (i + .5f), 0);
            var segment = i == 0 ? wall.gameObject : Box(gate.name + "_WallStage" + i, at, new Vector3(5.4f, step, 1.35f), _stone, _root);
            if (i == 0) { Undo.RecordObject(wall, "Reshape first climb stage"); wall.position = at; WorldScale(wall, new Vector3(5.4f, step, 1.35f)); }
            segment.layer = 3;
            var stripe = Box(gate.name + "_RouteStripe" + i, origin + new Vector3(x, step * (i + .5f), -.69f),
                new Vector3(.3f, step - .25f, .025f), _blue, _root, false);
            if (i == 2) continue;
            var rest = Box(gate.name + "_RestLedge" + (i + 1), origin + new Vector3(x + 1.35f, step * (i + 1) - .12f, -.95f),
                new Vector3(5.1f, .24f, 3.1f), _wood, _root);
            rest.layer = 3;
            Box(gate.name + "_RestMark" + i, rest.transform.position + new Vector3(0,.13f,-.7f), new Vector3(2.8f,.025f,1.2f), _gold, _root, false);
            Sign(gate.name + "_RestTip" + i, origin + new Vector3(x + 1.25f, step * (i + 1) + .7f, -1.12f), "ขึ้นชานแล้วปล่อยมือพักก่อน", _root, 0, .045f);
        }
        var oldStripe = gate.transform.Find("ClimbStripe");
        if (oldStripe != null) { Undo.RecordObject(oldStripe.gameObject, "Replace old climb route stripe"); oldStripe.gameObject.SetActive(false); }
        Undo.RecordObject(ledge, "Move top switch ledge");
        ledge.position = origin + new Vector3(2.8f, height - .12f, -.5f); WorldScale(ledge, new Vector3(5.5f,.3f,3.1f)); ledge.gameObject.layer = 3;
        Undo.RecordObject(button, "Move high switch"); button.position = origin + new Vector3(2.8f,height + .3f,-1.15f);
        var prompt = gate.transform.Find(gate.name + "_Prompt");
        if (prompt != null) { Undo.RecordObject(prompt, "Move high switch prompt"); prompt.position = button.position + Vector3.up * 1.35f; }
        var data = new SerializedObject(gate);
        data.FindProperty("_interactionDistance").floatValue = 1.8f;
        data.ApplyModifiedProperties();
        // Side rails leave the climbing face open and protect the top waiting area.
        for (int side = -1; side <= 1; side += 2)
            Box(gate.name + "_TopRail", ledge.position + new Vector3(side * 2.7f,.45f,0), new Vector3(.12f,.9f,3.1f), _wood, _root);
    }

    private static Transform HazardRoot(string name, Transform bridge)
    {
        var root = new GameObject(name).transform;
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Create network obstacle"); root.SetParent(_root, false);
        var mesh = bridge.Find("SolidCargoDeck").GetComponent<MeshFilter>().sharedMesh;
        var vertices = mesh.vertices;
        float min = mesh.bounds.min.z, max = mesh.bounds.max.z;
        float a = vertices.Where(v => Mathf.Abs(v.z - min) < .01f).Max(v => v.y);
        float b = vertices.Where(v => Mathf.Abs(v.z - max) < .01f).Max(v => v.y);
        float landing = Mathf.Min(3.5f, (max - min) * .3f);
        Vector3 direction = bridge.forward * ((max - min - 2 * landing) * bridge.lossyScale.z) + Vector3.up * (b - a);
        root.SetPositionAndRotation(bridge.TransformPoint(new Vector3(0,(a+b)*.5f,0)), Quaternion.LookRotation(direction));
        root.gameObject.AddComponent<NetworkRouteHazard>();
        return root;
    }

    private static void CreateHammer(Transform bridge)
    {
        var root = HazardRoot("SwingHammer_Forest", bridge);
        float width = bridge.Find("SolidCargoDeck").GetComponent<MeshFilter>().sharedMesh.bounds.size.x * bridge.lossyScale.x;
        const float pivotHeight = 4.6f;
        for (int side = -1; side <= 1; side += 2)
            LocalBox("HammerPost", new Vector3(side * (width*.5f+.6f),pivotHeight*.5f,0), new Vector3(.5f,pivotHeight,.5f),_wood,root);
        LocalBox("HammerBeam", new Vector3(0,pivotHeight+.2f,0),new Vector3(width+1.8f,.4f,.6f),_wood,root);
        var pivot = new GameObject("HammerPivot").transform;
        Undo.RegisterCreatedObjectUndo(pivot.gameObject,"Create hammer pivot"); pivot.SetParent(root,false); pivot.localPosition=Vector3.up*pivotHeight;
        var body = Body(pivot.gameObject);
        LocalBox("HammerArm",new Vector3(0,-1.8f,0),new Vector3(.25f,3.6f,.25f),_wood,pivot,false);
        var head = LocalBox("HammerHead",new Vector3(0,-3.6f,0),new Vector3(1.7f,1.6f,1.5f),_stone,pivot);
        pivot.localRotation=Quaternion.Euler(0,0,65);
        var hazard=root.GetComponent<NetworkRouteHazard>(); var data=new SerializedObject(hazard);
        Bind(data,NetworkRouteHazard.HazardKind.SwingHammer,body,null,head.GetComponent<BoxCollider>(),null,root);
        data.FindProperty("_primaryRest").vector3Value=pivot.localPosition;
        data.FindProperty("_activeSeconds").floatValue=3.2f;
        data.ApplyModifiedPropertiesWithoutUndo();
        WaitingPads(root,width,5.5f);
        Sign("HammerTip",root.TransformPoint(new Vector3(-width*.5f-3f,2.3f,-5)),"รอค้อนผ่าน แล้วค่อยไป\nวางลังที่จุดพักได้",root,0,.065f);
    }

    private static void CreateCrusher(Transform bridge)
    {
        var root=HazardRoot("StoneCrusher_Relay",bridge);
        float width=bridge.Find("SolidCargoDeck").GetComponent<MeshFilter>().sharedMesh.bounds.size.x*bridge.lossyScale.x;
        float openX=width*.5f+1.2f;
        var left=LocalBox("LeftCrusher",new Vector3(-openX,1.2f,0),new Vector3(2.1f,2.4f,2.2f),_stone,root);
        var right=LocalBox("RightCrusher",new Vector3(openX,1.2f,0),new Vector3(2.1f,2.4f,2.2f),_stone,root);
        var data=new SerializedObject(root.GetComponent<NetworkRouteHazard>());
        Bind(data,NetworkRouteHazard.HazardKind.Crusher,Body(left),Body(right),left.GetComponent<BoxCollider>(),right.GetComponent<BoxCollider>(),root);
        data.FindProperty("_primaryRest").vector3Value=left.transform.localPosition;
        data.FindProperty("_secondaryRest").vector3Value=right.transform.localPosition;
        data.FindProperty("_travel").floatValue=openX-1.3f;
        data.FindProperty("_activeSeconds").floatValue=3.2f;
        data.FindProperty("_safeSeconds").floatValue=7f;
        data.ApplyModifiedPropertiesWithoutUndo();
        for(int side=-1;side<=1;side+=2)
            LocalBox("CrusherWarningStripe",new Vector3(side*(width*.5f-.25f),.07f,0),new Vector3(.25f,.025f,2.6f),_warning,root,false);
        WaitingPads(root,width,5.5f);
        Sign("CrusherTip",root.TransformPoint(new Vector3(-width*.5f-3f,2.3f,-5)),"ไฟส้มแล้ว ถอยมารอก่อน\nรอหินแยก แล้วค่อยขนผ่าน",root,0,.065f);
    }

    private static void CreateCollapse(Transform bridge)
    {
        var root=HazardRoot("CollapsingDeck_Passage",bridge);
        var deck=bridge.Find("SolidCargoDeck"); var source=deck.GetComponent<MeshFilter>().sharedMesh;
        float width=source.bounds.size.x*bridge.lossyScale.x;
        const float gap=3.4f;
        CutBridge(deck, gap/bridge.lossyScale.z);
        foreach(var child in bridge.Cast<Transform>())
            if(child.name=="PlankSeam"&&Mathf.Abs(child.localPosition.z)<gap*.5f/bridge.lossyScale.z)
            { Undo.RecordObject(child.gameObject,"Hide seams across trap opening"); child.gameObject.SetActive(false); }
        var platform=LocalBox("DropDeck",new Vector3(0,-.22f,0),new Vector3(width,.44f,gap+.04f),_warning,root);
        var body=Body(platform);
        var volume=new GameObject("RestoreSafetyVolume"); Undo.RegisterCreatedObjectUndo(volume,"Create restoration safety volume"); volume.transform.SetParent(root,false);
        var box=volume.AddComponent<BoxCollider>(); box.isTrigger=true; box.center=new Vector3(0,-.1f,0); box.size=new Vector3(width+1f,3.4f,gap+1f);
        var data=new SerializedObject(root.GetComponent<NetworkRouteHazard>());
        Bind(data,NetworkRouteHazard.HazardKind.CollapsingDeck,body,null,platform.GetComponent<BoxCollider>(),null,root);
        Ref(data,"_surface",platform.GetComponent<Renderer>());
        Ref(data,"_restoreVolume",box); data.FindProperty("_primaryRest").vector3Value=platform.transform.localPosition;
        data.FindProperty("_safeSeconds").floatValue=7f; data.FindProperty("_warningSeconds").floatValue=2f;
        data.FindProperty("_activeSeconds").floatValue=3f; data.ApplyModifiedPropertiesWithoutUndo();
        // A catch platform and broad side ramp let teams recover cargo instead of losing it to a blind hole.
        LocalBox("RecoveryShelf",new Vector3(2f,-1.65f,0),new Vector3(width+5f,.35f,5f),_stone,root);
        float rampX=width*.5f+2.4f;
        var ramp=LocalBox("RecoveryRamp",new Vector3(rampX,-.78f,-3.7f),new Vector3(4.5f,.3f,7.3f),_wood,root);
        ramp.transform.localRotation=Quaternion.Euler(-Mathf.Atan2(-1.45f,7.1f)*Mathf.Rad2Deg,0,0);
        LocalBox("RecoveryJoin",new Vector3(width*.5f+.6f,-.125f,-7.1f),new Vector3(3.5f,.25f,2.2f),_wood,root);
        LocalBox("RecoveryOuterRail",new Vector3(width*.5f+4.6f,-.4f,-1),new Vector3(.18f,.6f,4.8f),_wood,root);
        WaitingPads(root,width,5.5f);
        Sign("CollapseTip",root.TransformPoint(new Vector3(-width*.5f-3f,2.3f,-5)),"พื้นจะยุบ อย่าหยุดกลางสะพาน\nถ้าพลาด ใช้ทางลาดข้าง ๆ",root,0,.065f);
    }

    private static void CutBridge(Transform deck,float gap)
    {
        var source=deck.GetComponent<MeshFilter>().sharedMesh; var sv=source.vertices;
        float lo=source.bounds.min.z,hi=source.bounds.max.z,w=source.bounds.size.x;
        float a=sv.Where(v=>Mathf.Abs(v.z-lo)<.01f).Max(v=>v.y),b=sv.Where(v=>Mathf.Abs(v.z-hi)<.01f).Max(v=>v.y);
        float landing=Mathf.Min(3.5f,(hi-lo)*.3f);
        float Y(float z)=>Mathf.Lerp(a,b,Mathf.InverseLerp(lo+landing,hi-landing,z));
        float[] breaks={lo,lo+landing,-gap*.5f,gap*.5f,hi-landing,hi};
        Array.Sort(breaks); var vertices=new List<Vector3>(); var triangles=new List<int>();
        for(int i=0;i<breaks.Length-1;i++)
        {
            float z0=breaks[i],z1=breaks[i+1]; if(z0>=-gap*.5f-.001f && z1<=gap*.5f+.001f)continue;
            int n=vertices.Count; float y0=Y(z0),y1=Y(z1);
            vertices.AddRange(new[]{new Vector3(-w/2,y0,z0),new Vector3(w/2,y0,z0),new Vector3(w/2,y1,z1),new Vector3(-w/2,y1,z1),new Vector3(-w/2,y0-.44f,z0),new Vector3(w/2,y0-.44f,z0),new Vector3(w/2,y1-.44f,z1),new Vector3(-w/2,y1-.44f,z1)});
            int[] faces={0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5};
            triangles.AddRange(faces.Select(x=>x+n));
        }
        var mesh=new Mesh{name="ChallengeDeck_"+deck.parent.name};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();
        string path=Folder+"/"+mesh.name+".asset";if(AssetDatabase.LoadAssetAtPath<Mesh>(path)!=null)throw new InvalidOperationException("Challenge mesh already exists.");
        AssetDatabase.CreateAsset(mesh,path);
        Undo.RecordObject(deck.GetComponent<MeshFilter>(),"Cut real deck opening");deck.GetComponent<MeshFilter>().sharedMesh=mesh;
        var collider=deck.GetComponent<MeshCollider>();Undo.RecordObject(collider,"Cut real collision opening");collider.sharedMesh=mesh;
    }

    private static void Bind(SerializedObject data,NetworkRouteHazard.HazardKind kind,Rigidbody primary,Rigidbody secondary,BoxCollider hitA,BoxCollider hitB,Transform root)
    {
        data.FindProperty("_kind").enumValueIndex=(int)kind;Ref(data,"_primary",primary);Ref(data,"_secondary",secondary);
        Ref(data,"_hitA",hitA);Ref(data,"_hitB",hitB);Ref(data,"_cargo",_cargo);
        var indicator=LocalBox("WarningLamp",new Vector3(-3.6f,2.2f,-2.2f),new Vector3(.45f,.6f,.45f),_warning,root,false);
        Ref(data,"_indicator",indicator.GetComponent<Renderer>());
        var label=Sign(root.name+"_Status",root.TransformPoint(new Vector3(0,3.4f,1.6f)),
            kind==NetworkRouteHazard.HazardKind.SwingHammer?"ค้อนหยุดแล้ว ขนลังผ่านได้":kind==NetworkRouteHazard.HazardKind.Crusher?"ทางเปิดแล้ว ขนลังผ่านได้":"ข้ามได้เลย อย่าหยุดกลางสะพาน",root,0,.06f);
        Ref(data,"_label",label);
    }

    private static void WaitingPads(Transform root,float width,float distance)
    {
        for(int side=-1;side<=1;side+=2)
        {
            LocalBox("SafeWaitingBay",new Vector3(0,.035f,side*distance),new Vector3(width-.2f,.025f,2.4f),_gold,root,false);
            Sign("WaitHereTip",root.TransformPoint(new Vector3(width*.5f+2f,1.5f,side*distance)),"วางลังไว้ตรงนี้ก่อน",root,0,.045f);
        }
    }

    private static void RewriteLabels(Scene scene)
    {
        var texts=new Dictionary<string,string>
        {
            ["StartInstructions"]="หาลัง แล้วช่วยกันขนไปส่งที่ปราสาท",
            ["VillageTip"]="จับลังคนละด้าน จะขนง่ายขึ้น\nเหนื่อยแล้ววางพักที่แผ่นสีทองได้",
            ["RelayInstructions"]="แบ่งกันยืนบนแผ่นสีฟ้า\nรอสักครู่ สะพานจะเปิด",
            ["ForestInstructions"]="ค่อย ๆ เลี้ยวผ่านก้อนหิน\nเดินให้พร้อมกัน ระวังลังชน",
            ["GateInstructions"]="คนหนึ่งกด E ค้างที่คันโยก\nที่เหลือช่วยกันขนลังผ่านประตู",
            ["RestBeforeWalls"]="วางลังพักตรงนี้ก่อน\nให้เพื่อนปีนไปเปิดประตูข้างหน้า",
            ["TrapInstructions"]="ไฟส้มแล้ว รอก่อน\nรอไฟหยุด แล้วค่อยขนลังข้าม",
            ["Delivery_Label"]="ถึงแล้ว วางลังในวงนี้เลย",
            ["PortalStatus"]="ส่งลังตรงจุดหมายก่อน\nแล้วประตูจะเปิด"
        };
        foreach(var label in scene.GetRootGameObjects().Where(g=>g.name!="PreviousObstacleTest_Preserved").SelectMany(g=>g.GetComponentsInChildren<TextMesh>(true)))
        {
            Undo.RecordObject(label,"Use conversational Thai");
            if(texts.TryGetValue(label.name,out var text))label.text=text;
            else if(label.name.EndsWith("_GroundInstructions"))label.text="คนหนึ่งปีนไปกดปุ่มด้านบน\nขึ้นชานแล้วปล่อยมือพักได้\nที่เหลือรอกับลัง แล้วค่อยไปพร้อมกัน";
            else if(label.name.EndsWith("_Prompt"))label.text="กด E เปิดประตูให้เพื่อน";
            else if(label.name.StartsWith("FlameTrap_"))label.text="ไฟหยุดแล้ว ขนลังข้ามได้";
            ResizeBoard(label);
        }
        var mission=scene.GetRootGameObjects().Select(g=>g.GetComponent<LevelMission>()).FirstOrDefault(m=>m!=null);
        if(mission!=null)
        {var data=new SerializedObject(mission);data.FindProperty("_objective").stringValue="ช่วยกันขนลังไปส่งที่ปราสาท";data.ApplyModifiedProperties();}
    }

    private static TextMesh Sign(string name,Vector3 world,string text,Transform parent,float yaw,float size)
    {
        var go=new GameObject(name);Undo.RegisterCreatedObjectUndo(go,"Create Thai instruction sign");go.transform.SetParent(parent,false);
        go.transform.SetPositionAndRotation(world,Quaternion.Euler(0,yaw,0));WorldScale(go.transform,Vector3.one);
        var label=go.AddComponent<TextMesh>();label.text=text;label.font=_font;label.fontSize=56;label.characterSize=size;
        label.anchor=TextAnchor.MiddleCenter;label.alignment=TextAlignment.Center;label.color=Color.white;
        go.GetComponent<MeshRenderer>().sharedMaterial=_font.material;
        _font.RequestCharactersInTexture(text,56,FontStyle.Normal);
        ResizeBoard(label);return label;
    }

    private static void ResizeBoard(TextMesh label)
    {
        if(label.font!=null)label.font.RequestCharactersInTexture(label.text,label.fontSize,label.fontStyle);
        var bounds=label.GetComponent<Renderer>().localBounds;
        var board=label.transform.Find("ThaiSignBacking");
        if(board==null)board=LocalBox("ThaiSignBacking",Vector3.zero,Vector3.one,_board,label.transform,false).transform;
        Undo.RecordObject(board,"Fit Thai sign panel");board.localPosition=bounds.center+Vector3.forward*.08f;
        float minimum=label.name.EndsWith("_Status")||label.name.EndsWith("_Prompt")?10f:6f;
        board.localScale=new Vector3(Mathf.Max(minimum,bounds.size.x+.5f),Mathf.Max(1.6f,bounds.size.y+.3f),.08f);
    }

    private static Rigidbody Body(GameObject go)
    {
        var rb=Undo.AddComponent<Rigidbody>(go);rb.isKinematic=true;rb.useGravity=false;rb.interpolation=RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode=CollisionDetectionMode.ContinuousSpeculative;return rb;
    }
    private static Material Mat(string name)=>AssetDatabase.LoadAssetAtPath<Material>(Folder+"/"+name+".mat") ?? throw new InvalidOperationException("Missing material "+name);
    private static void Ref(SerializedObject data,string name,UnityEngine.Object value)=>data.FindProperty(name).objectReferenceValue=value;
    private static void WorldScale(Transform t,Vector3 size)
    {var p=t.parent==null?Vector3.one:t.parent.lossyScale;t.localScale=new Vector3(size.x/p.x,size.y/p.y,size.z/p.z);}
    private static GameObject LocalBox(string name,Vector3 at,Vector3 size,Material material,Transform parent,bool solid=true)
    {var go=Box(name,parent.TransformPoint(at),size,material,parent,solid);go.transform.rotation=parent.rotation;return go;}
    private static GameObject Box(string name,Vector3 at,Vector3 size,Material material,Transform parent,bool solid=true)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;Undo.RegisterCreatedObjectUndo(go,"Add challenge geometry");
        go.transform.SetParent(parent,false);go.transform.position=at;WorldScale(go.transform,size);go.GetComponent<Renderer>().sharedMaterial=material;
        if(!solid)UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());return go;
    }
}
#endif
