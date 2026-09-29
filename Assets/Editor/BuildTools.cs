using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UntitledGame.EditorTools
{
    /// <summary>Builds the Windows player to Builds/WillowLake/WillowLake.exe.</summary>
    public static class BuildTools
    {
        public const string OutputPath = "Builds/WillowLake/WillowLake.exe";

        [MenuItem("Untitled Game/Build Windows Player")]
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { SceneBuilder.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[Build] {s.result} in {s.totalTime.TotalSeconds:0}s, {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalWarnings} warnings -> {OutputPath}");
            foreach (var step in report.steps)
            foreach (var m in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                Debug.Log($"[Build] ERROR {m.content}");
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        /// <summary>Batch helper: regenerate the scene and build the player.</summary>
        public static void RebuildAll()
        {
            SceneBuilder.Build();
            BuildWindows();
        }
    }
}
