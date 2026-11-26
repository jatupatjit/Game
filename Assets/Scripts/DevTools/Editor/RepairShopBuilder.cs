#if UNITY_EDITOR
using System.Collections.Generic;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools/Expedition/Build Repair Shop (open scene): replaces the old repair stall (ShopStall + text label) and the flat
/// RepairPad with a box-built workshop and a round pad, at the same spot under Branch_Repair. Re-running rebuilds it.
/// Wires <see cref="RepairStation"/>: pad centre (the old pad's origin sat ~100 m away from its slab, so the crate was
/// never detected), lamp renderers (4 lanterns + the pad's "+"), green/grey materials, no text label.
/// </summary>
public static class RepairShopBuilder
{
    private const string MatFolder = "Assets/Prefabs/Decor/Repair";

    [MenuItem("Tools/Expedition/Build Repair Shop")]
    public static void BuildMenu() => Debug.Log("[RepairShopBuilder] " + Build());

    public static string Build()
    {
        var station = Object.FindAnyObjectByType<RepairStation>(FindObjectsInactive.Include);
        if (station == null) return "no RepairStation in the open scene";
        var so = new SerializedObject(station);
        Transform branch = null, oldStall = null, oldPad = null, oldShop = null;
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.name == "Branch_Repair") branch = t;
            if (t.name == "RepairShop") oldShop = t;
        }
        if (branch == null) return "Branch_Repair not found";
        foreach (Transform c in branch)
        {
            if (c.name == "ShopStall") oldStall = c;
            if (c.name == "RepairPad") oldPad = c;
        }

        // Where: the old visible slab (or the rebuilt pad), facing the old counter.
        Vector3 centre, back;
        if (oldShop != null)
        {
            centre = oldShop.Find("Pad").position;
            back = oldShop.forward;
        }
        else
        {
            var slab = oldPad != null ? oldPad.Find("PadSlab") : null;
            var counter = oldStall != null ? oldStall.Find("Counter") : null;
            if (slab == null || counter == null) return "old slab/counter not found";
            centre = slab.position - Vector3.up * (slab.lossyScale.y * .5f);
            back = counter.position - slab.position;
        }
        back.y = 0f;
        back.Normalize();
        if (oldShop != null) Object.DestroyImmediate(oldShop.gameObject);
        if (oldStall != null) Object.DestroyImmediate(oldStall.gameObject);
        if (oldPad != null) Object.DestroyImmediate(oldPad.gameObject);

        Material wood = Find("BridgeWood"), cloth = Find("StallCloth"), iron = Find("Medieval_Iron") ?? Find("Iron") ?? Find("InactivePressurePad");
        Material green = Mat("RepairReady", new Color(.35f, .95f, .45f), new Color(.2f, .9f, .3f) * 1.6f);
        Material grey = Mat("RepairUsed", new Color(.42f, .42f, .45f), Color.black);
        Material lantern = Mat("RepairLantern", new Color(1f, .85f, .45f), new Color(1f, .7f, .25f) * 2f);
        Material darkWood = Mat("RepairDarkWood", new Color(.33f, .22f, .14f), Color.black);

        var root = new GameObject("RepairShop").transform;
        root.SetParent(branch, true);
        root.SetPositionAndRotation(centre, Quaternion.LookRotation(back));
        var lamps = new List<Renderer>();

        // Pad: round platform, metal rim, green "+" inlay, 4 lanterns. Its origin is the pad centre.
        var pad = new GameObject("Pad").transform;
        pad.SetParent(root, false);
        var rim = Shape(PrimitiveType.Cylinder, "Rim", pad, new Vector3(0, .07f, 0), new Vector3(4.6f, .07f, 4.6f), iron);
        Object.DestroyImmediate(rim.GetComponent<Collider>());
        var mc = rim.AddComponent<MeshCollider>();
        mc.sharedMesh = rim.GetComponent<MeshFilter>().sharedMesh;
        mc.convex = true;
        Shape(PrimitiveType.Cylinder, "Deck", pad, new Vector3(0, .15f, 0), new Vector3(4.2f, .02f, 4.2f), wood, false);
        var plus = new GameObject("Plus").transform;
        plus.SetParent(pad, false);
        plus.localPosition = new Vector3(0, .18f, 0);
        lamps.Add(Box("Bar", plus, Vector3.zero, new Vector3(2f, .03f, .55f), green, false).GetComponent<Renderer>());
        lamps.Add(Box("Bar", plus, Vector3.zero, new Vector3(.55f, .03f, 2f), green, false).GetComponent<Renderer>());
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + 90f * i) * Mathf.Deg2Rad;
            var p = new Vector3(Mathf.Cos(a) * 2.05f, 0f, Mathf.Sin(a) * 2.05f);
            Box("LampPost", pad, p + Vector3.up * .45f, new Vector3(.1f, .9f, .1f), darkWood);
            lamps.Add(Box("Lantern", pad, p + Vector3.up * .98f, new Vector3(.22f, .22f, .22f), green, false).GetComponent<Renderer>());
            Box("LanternCap", pad, p + Vector3.up * 1.13f, new Vector3(.28f, .06f, .28f), darkWood, false);
        }

        // Workshop behind the pad (+Z), open towards it.
        var shop = new GameObject("Workshop").transform;
        shop.SetParent(root, false);
        shop.localPosition = new Vector3(0, 0, 3.6f);
        Box("Floor", shop, new Vector3(0, .08f, 0), new Vector3(4.6f, .16f, 2.8f), darkWood);
        Box("BackWall", shop, new Vector3(0, 1.3f, 1.32f), new Vector3(4.6f, 2.4f, .16f), wood);
        Box("SideWall", shop, new Vector3(2.22f, 1.1f, .6f), new Vector3(.16f, 2f, 1.6f), wood);
        foreach (float x in new[] { -2.2f, 2.2f }) Box("Post", shop, new Vector3(x, 1.35f, -1.3f), new Vector3(.2f, 2.7f, .2f), darkWood);
        Box("RoofBeam", shop, new Vector3(0, 2.72f, -1.3f), new Vector3(4.8f, .16f, .2f), darkWood);
        foreach (float side in new[] { -1f, 1f })
            Box("Roof", shop, new Vector3(0, 3.05f + (side < 0 ? 0 : 0), side * .72f), new Vector3(5f, .12f, 1.75f), cloth, true,
                new Vector3(side * 22f, 0, 0));
        Box("RoofRidge", shop, new Vector3(0, 3.4f, 0), new Vector3(5.1f, .14f, .18f), darkWood, false);
        // Workbench with an anvil and tools; spare planks; tools on the back wall; a lantern under the roof.
        Box("BenchTop", shop, new Vector3(-.6f, .95f, .55f), new Vector3(2.2f, .12f, .8f), darkWood);
        foreach (var lp in new[] { new Vector3(-1.6f, .5f, .25f), new Vector3(.4f, .5f, .25f), new Vector3(-1.6f, .5f, .85f), new Vector3(.4f, .5f, .85f) })
            Box("BenchLeg", shop, lp, new Vector3(.1f, .8f, .1f), darkWood);
        Box("AnvilBase", shop, new Vector3(1.3f, .4f, .2f), new Vector3(.35f, .5f, .3f), iron);
        Box("AnvilTop", shop, new Vector3(1.3f, .72f, .2f), new Vector3(.75f, .18f, .32f), iron);
        Box("HammerHandle", shop, new Vector3(-1.1f, 1.04f, .45f), new Vector3(.06f, .06f, .5f), wood, false, new Vector3(0, 30, 0));
        Box("HammerHead", shop, new Vector3(-1.22f, 1.07f, .25f), new Vector3(.24f, .1f, .1f), iron, false, new Vector3(0, 30, 0));
        Box("Plank", shop, new Vector3(-.1f, 1.06f, .6f), new Vector3(.9f, .08f, .22f), wood, false, new Vector3(0, -8, 0));
        for (int i = 0; i < 3; i++) Box("SparePlank", shop, new Vector3(-1.95f, .22f + i * .1f, -.2f), new Vector3(.25f, .1f, 1.4f), wood, false, new Vector3(0, i * 6f, 0));
        Box("SawBlade", shop, new Vector3(.9f, 1.75f, 1.22f), new Vector3(.7f, .22f, .02f), iron, false);
        Box("SawHandle", shop, new Vector3(1.32f, 1.75f, 1.22f), new Vector3(.16f, .2f, .05f), darkWood, false);
        Box("WrenchBar", shop, new Vector3(-.2f, 1.75f, 1.22f), new Vector3(.08f, .6f, .03f), iron, false, new Vector3(0, 0, 20));
        Box("WrenchHead", shop, new Vector3(-.3f, 2.05f, 1.22f), new Vector3(.22f, .12f, .03f), iron, false, new Vector3(0, 0, 20));
        Box("Hook", shop, new Vector3(0, 2.55f, -1.3f), new Vector3(.04f, .25f, .04f), iron, false);
        Box("Lantern", shop, new Vector3(0, 2.35f, -1.3f), new Vector3(.22f, .28f, .22f), lantern, false);
        // Floating green "+" over the roof (turns and bobs while the repair is still free, see RepairShopFx).
        var sign = new GameObject("PlusSign").transform;
        sign.SetParent(root, false);
        sign.localPosition = new Vector3(0, 4.6f, 3.6f);
        Box("Bar", sign, Vector3.zero, new Vector3(.95f, .3f, .3f), green, false);
        Box("Bar", sign, Vector3.zero, new Vector3(.3f, .95f, .3f), green, false);

        var fx = root.gameObject.AddComponent<RepairShopFx>();
        var fso = new SerializedObject(fx);
        fso.FindProperty("_station").objectReferenceValue = station;
        fso.FindProperty("_sign").objectReferenceValue = sign;
        fso.FindProperty("_padPlus").objectReferenceValue = plus;
        fso.FindProperty("_ringMaterial").objectReferenceValue = Resources.Load<Material>("BeaconRing");
        fso.ApplyModifiedPropertiesWithoutUndo();

        so.FindProperty("_pad").objectReferenceValue = pad;
        so.FindProperty("_label").objectReferenceValue = null;
        so.FindProperty("_readyMaterial").objectReferenceValue = green;
        so.FindProperty("_usedMaterial").objectReferenceValue = grey;
        var lampProp = so.FindProperty("_lamps");
        lampProp.arraySize = lamps.Count;
        for (int i = 0; i < lamps.Count; i++) lampProp.GetArrayElementAtIndex(i).objectReferenceValue = lamps[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(station.gameObject.scene);
        return $"built at {centre}, facing {back}";
    }

    private static Material Find(string name)
    {
        foreach (var guid in AssetDatabase.FindAssets(name + " t:Material"))
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && m.name == name) return m;
        }
        return null;
    }

    private static Material Mat(string name, Color color, Color emission)
    {
        string path = $"{MatFolder}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Prefabs/Decor", "Repair");
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        m.SetColor("_BaseColor", color);
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private static GameObject Shape(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = true)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = true, Vector3 euler = default)
    {
        var go = Shape(PrimitiveType.Cube, name, parent, pos, scale, mat, collider);
        go.transform.localRotation = Quaternion.Euler(euler);
        return go;
    }
}
#endif
