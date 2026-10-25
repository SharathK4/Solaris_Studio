using System.IO;
using System.Linq;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Solaris.EditorTools
{
    public static class BuildTool
    {
        public const string OutDir = "Builds/SolarisStudio";

        /// <summary><c>-executeMethod Solaris.EditorTools.BuildTool.BuildWindows</c></summary>
        [MenuItem("Solaris/Build Windows (x64)")]
        public static void BuildWindows()
        {
            Directory.CreateDirectory(OutDir);
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(OutDir, "SolarisStudio.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[Solaris] Build {s.result}: {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime.TotalSeconds:0}s -> {options.locationPathName}");
            if (Application.isBatchMode) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Logs every model's inputs and outputs (names + shapes) for verification.</summary>
        [MenuItem("Solaris/Inspect Models")]
        public static void InspectModels()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ModelAsset", new[] { "Assets/Solaris/Models" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
                var model = ModelLoader.Load(asset);
                var sb = new System.Text.StringBuilder();
                sb.Append($"[Solaris] MODEL {Path.GetFileName(path)}: ");
                foreach (var i in model.inputs) sb.Append($"in '{i.name}' {i.shape} {i.dataType}; ");
                foreach (var o in model.outputs) sb.Append($"out '{o.name}'; ");
                sb.Append($"layers {model.layers.Count}");
                Debug.Log(sb.ToString());
            }
        }
    }
}
