using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

[InitializeOnLoad]
public static class AddMCPForUnityPackage
{
    // Package ID used in Packages/manifest.json
    const string PACKAGE_NAME = "com.coplaydev.unity-mcp";

    // Git URL (Unity Package Manager -> Add package from git URL...)
    const string GIT_URL = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity";

    static readonly string ManifestPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "manifest.json"));

    static AddRequest sAddRequest;
    static bool sHookedUpdate;

    static AddMCPForUnityPackage()
    {
        if (Application.isBatchMode)
            return;

        // If already in manifest - do nothing
        if (IsInManifest())
            return;

        // Ask once on editor start
        if (EditorUtility.DisplayDialog(
                "Install MCP for Unity",
                "Package is not found in Packages/manifest.json.\n\nAdd it from Git URL?",
                "Add",
                "Cancel"))
        {
            StartAdd();
        }
    }

    [MenuItem("Tools/MCP for Unity/Install (Git URL)")]
    static void MenuInstall()
    {
        if (IsInManifest())
        {
            EditorUtility.DisplayDialog("MCP for Unity", "Package is already present in manifest.json.", "OK");
            return;
        }

        StartAdd();
    }

    static bool IsInManifest()
    {
        // Simple and reliable: check manifest.json synchronously
        if (!File.Exists(ManifestPath))
            return false;

        var text = File.ReadAllText(ManifestPath);
        return text.Contains("\"" + PACKAGE_NAME + "\"");
    }

    static void StartAdd()
    {
        if (sAddRequest != null)
            return;

        sAddRequest = Client.Add(GIT_URL);

        if (!sHookedUpdate)
        {
            sHookedUpdate = true;
            EditorApplication.update += OnUpdate;
        }
    }

    static void OnUpdate()
    {
        if (sAddRequest == null)
            return;

        if (!sAddRequest.IsCompleted)
            return;

        if (sAddRequest.Status == StatusCode.Success)
        {
            Debug.Log("MCP for Unity installed: " + sAddRequest.Result.name + "@" + sAddRequest.Result.version);
        }
        else
        {
            Debug.LogError("Failed to install MCP for Unity: " + (sAddRequest.Error != null ? sAddRequest.Error.message : "Unknown error"));
        }

        sAddRequest = null;

        if (sHookedUpdate)
        {
            sHookedUpdate = false;
            EditorApplication.update -= OnUpdate;
        }
    }
}
