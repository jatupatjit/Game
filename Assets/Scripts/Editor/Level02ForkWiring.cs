#if UNITY_EDITOR
using System.Collections.Generic;
using CoopGame.CarrySystem;
using CoopGame.Network;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Hooks George's Level02 fork (2026-11-07) to BranchChoice: fork island (31); left/easy = (32), (36)-(42);
/// right/hard = (33), (34), (35), (44)-(54) + stepping stones L2_Mid_Fork (6)-(25) + the repair shop on (35).
/// The cargo entering one side makes the other side sink away. Safe to re-run.
/// </summary>
public static class Level02ForkWiring
{
    [MenuItem("Tools/Expedition/Level02 Wire Fork (31) + Repair Shop")]
    public static void Menu() => Debug.Log(Wire());

    public static string Wire()
    {
        var sky = GameObject.Find("Level02_SkyKingdom_v1").transform;
        var mid = GameObject.Find("L2_Mid_Spiral").transform;
        Transform Islet(int n) => sky.Find($"L2_02_SpikeIslet ({n})");
        var fork = Islet(31);

        var main = new List<GameObject>();
        foreach (int n in new[] { 32, 36, 37, 38, 39, 40, 41, 42 }) Add(main, Islet(n), n);
        var hard = new List<GameObject>();
        foreach (int n in new[] { 33, 34, 35, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54 }) Add(hard, Islet(n), n);
        for (int i = 6; i <= 25; i++) Add(hard, mid.Find($"L2_Mid_Fork ({i})"), i);

        // Repair shop on (35), beside the path (away from where the team comes in and goes out).
        var shopIsland = Islet(35);
        Vector3 c = shopIsland.position;
        Vector3 inDir = Flat(c - Islet(34).position).normalized, outDir = Flat(Islet(44).position - c).normalized;
        Vector3 side = (inDir - outDir).normalized;
        var repairRoot = mid.Find("Branch_Repair");
        var stall = repairRoot.Find("ShopStall");
        var pad = repairRoot.Find("RepairPad");
        stall.SetPositionAndRotation(c + side * 4.6f, Quaternion.LookRotation(-side));
        pad.SetPositionAndRotation(c + side * 1.2f, Quaternion.LookRotation(-side));
        hard.Add(stall.gameObject);
        hard.Add(pad.gameObject);
        var station = Object.FindFirstObjectByType<RepairStation>(FindObjectsInactive.Include);
        station.transform.SetPositionAndRotation(pad.position, pad.rotation);
        Set(station, so => { so.FindProperty("_branchRoot").objectReferenceValue = shopIsland.gameObject; so.FindProperty("_pad").objectReferenceValue = pad; });

        // Sign and arrows on the fork island.
        Vector3 arrive = Flat(fork.position - Islet(30).position).normalized;
        var sign = mid.Find("ForkSign");
        sign.SetPositionAndRotation(fork.position + arrive * 1.5f, Quaternion.LookRotation(arrive));
        Arrow(mid.Find("ArrowMain"), fork.position, Islet(32).position);
        Arrow(mid.Find("ArrowRepair"), fork.position, mid.Find("L2_Mid_Fork (9)").position);

        var choice = Object.FindFirstObjectByType<BranchChoice>(FindObjectsInactive.Include);
        choice.transform.position = fork.position;
        var cargo = Object.FindFirstObjectByType<FragileCargo>();
        Set(choice, so =>
        {
            so.FindProperty("_cargo").objectReferenceValue = cargo;
            Fill(so.FindProperty("_mainObjects"), main);
            Fill(so.FindProperty("_repairObjects"), hard);
            so.FindProperty("_sign").objectReferenceValue = sign.GetComponentInChildren<TextMesh>();
        });
        EditorSceneManager.MarkSceneDirty(sky.gameObject.scene);
        return $"fork (31) wired: easy {main.Count} objects, hard {hard.Count} objects, shop on (35) at {pad.position}.";
    }

    private static void Add(List<GameObject> list, Transform t, int n)
    {
        if (t == null) { Debug.LogWarning("Level02 fork: missing object " + n); return; }
        list.Add(t.gameObject);
    }

    private static void Arrow(Transform arrow, Vector3 from, Vector3 to)
    {
        Vector3 d = Flat(to - from).normalized;
        arrow.SetPositionAndRotation(from + d * 3.6f + Vector3.up * .03f, Quaternion.LookRotation(d));
    }

    private static void Fill(SerializedProperty p, List<GameObject> items)
    {
        p.arraySize = items.Count;
        for (int i = 0; i < items.Count; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    private static void Set(Object target, System.Action<SerializedObject> edit)
    {
        var so = new SerializedObject(target);
        edit(so);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
}
#endif
