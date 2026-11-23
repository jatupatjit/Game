#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Reusable development build entry point. Runs outside the Editor automation request timeout.</summary>
public static class ExpeditionVerificationBuild
{
    private static string _output;

    [MenuItem("Tools/Expedition/Build Local Verification Player")]
    private static void BuildFromMenu() => QueueBuild(Path.GetFullPath("../Verification/ExpeditionLatest/Expedition.exe"));

    public static void QueueBuild(string executablePath)
    {
        if (EditorApplication.isPlaying || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Stop Play Mode and wait for the current build first.");
        _output = Path.GetFullPath(executablePath);
        EditorApplication.update -= Execute;
        EditorApplication.update += Execute;
    }

    private static void Execute()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        EditorApplication.update -= Execute;
        string output = _output;
        _output = null;
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            locationPathName = output,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.CleanBuildCache
        });
        var summary = new Summary
        {
            result = report.summary.result.ToString(),
            errors = report.summary.totalErrors,
            warnings = report.summary.totalWarnings,
            duration = report.summary.totalTime.ToString(),
            messages = report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Error || message.type == LogType.Exception || message.type == LogType.Assert)
                .Select(message => message.content).Distinct().ToArray(),
            warningExamples = report.steps.SelectMany(step => step.messages)
                .Where(message => message.type == LogType.Warning).Select(message => message.content).Distinct().Take(10).ToArray()
        };
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "BuildResult.json"), JsonUtility.ToJson(summary, true));
        Debug.Log($"[ExpeditionVerificationBuild] {report.summary.result}: {summary.errors} errors, {summary.warnings} warnings. {output}");
    }

    [Serializable]
    private sealed class Summary
    {
        public string result, duration;
        public int errors, warnings;
        public string[] messages, warningExamples;
    }
}
#endif
