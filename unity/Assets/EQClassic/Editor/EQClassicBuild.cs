using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EQClassic.Unity.Editor
{
    /// <summary>
    /// Builds a standalone client: asset bundles of the imported zones and characters, the zones'
    /// collision meshes (StreamingAssets/EQClassic), a boot scene, then the player.
    ///   Unity -projectPath build/unity-client -executeMethod EQClassic.Unity.Editor.EQClassicBuild.BuildLinux -quit
    /// Output: build/unity-player/&lt;target&gt;/ (EQC_PLAYER_DIR overrides it).
    /// </summary>
    public static class EQClassicBuild
    {
        private const string Content = "Assets/Content/AssetBundleContent/";
        private const string Scene = "Assets/EQClassic/Client.unity";
        private static string StreamingDir => Path.Combine(Application.streamingAssetsPath, "EQClassic");

        [MenuItem("EQClassic/Build Asset Bundles")]
        public static void BuildBundles() => BuildBundles(EditorUserBuildSettings.activeBuildTarget);

        [MenuItem("EQClassic/Build Linux Player")]
        public static void BuildLinux() => BuildPlayer(BuildTarget.StandaloneLinux64, "linux", "EQClassic.x86_64");

        [MenuItem("EQClassic/Build Windows Player")]
        public static void BuildWindows() => BuildPlayer(BuildTarget.StandaloneWindows64, "windows", "EQClassic.exe");

        public static void BuildBundles(BuildTarget target)
        {
            Directory.CreateDirectory(StreamingDir);
            var builds = new List<AssetBundleBuild>();
            foreach (var zoneDir in Directory.GetDirectories(Content + "Zones"))
            {
                var zone = Path.GetFileName(zoneDir);
                var prefab = $"{Content}Zones/{zone}/{zone}.prefab";
                if (!File.Exists(prefab))
                    continue;
                // The zone and its object models (placed at run time from <zone>_objects.txt).
                var assets = new List<string> { prefab };
                var objects = $"{Content}Zones/{zone}/Objects";
                if (Directory.Exists(objects))
                    assets.AddRange(Directory.GetFiles(objects, "*.prefab").Select(p => p.Replace('\\', '/')));
                builds.Add(new AssetBundleBuild { assetBundleName = ClientBundles.ZoneBundle(zone), assetNames = assets.ToArray() });
                var instances = Path.Combine(ClientPaths.Exports, zone, "Zone", "object_instances.txt");
                if (File.Exists(instances))
                    File.Copy(instances, Path.Combine(StreamingDir, zone + "_objects.txt"), overwrite: true);
                var exports = ClientPaths.Exports;
                if (File.Exists(Path.Combine(exports, zone, "Zone", "Meshes", zone + "_collision.txt")))
                {
                    // Zone and solid objects in one file: the player build has no Lantern export.
                    var mesh = EQClassic.Shared.World.ZoneCollisionMesh.LoadLanternZone(exports, zone);
                    using var writer = new StreamWriter(Path.Combine(StreamingDir, zone + "_collision.txt"));
                    mesh.WriteLantern(writer);
                }
            }
            var characters = Directory.GetFiles(Content + "Characters", "*.prefab").Select(p => p.Replace('\\', '/')).ToArray();
            if (characters.Length > 0)
                builds.Add(new AssetBundleBuild { assetBundleName = ClientBundles.CharacterBundle, assetNames = characters });
            if (builds.Count == 0)
                throw new InvalidOperationException("nothing imported: run EQClassic > Import Zones and Characters first");

            var manifest = BuildPipeline.BuildAssetBundles(StreamingDir, builds.ToArray(),
                BuildAssetBundleOptions.ChunkBasedCompression, target);
            if (manifest == null)
                throw new InvalidOperationException("asset bundle build failed (see the log)");
            AssetDatabase.Refresh();
            Debug.Log($"EQClassicBuild: {builds.Count} bundle(s) in {StreamingDir}");
        }

        private static void BuildPlayer(BuildTarget target, string folder, string executable)
        {
            BuildBundles(target);
            // LanternUnityTools' settings select IL2CPP, a separate Unity module; Mono comes with the
            // editor's build support and is enough for the client.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.productName = "EQClassic"; // window title and settings folder
            PlayerSettings.companyName = "EQClassic";
            // The client builds itself at start-up (EQClassicBoot): an empty scene is enough. Linear fog
            // is on in it so the build keeps the fog shader variants the zones use (Unity strips the
            // fog modes no scene uses).
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            EditorSceneManager.SaveScene(scene, Scene);
            var output = Environment.GetEnvironmentVariable("EQC_PLAYER_DIR");
            if (string.IsNullOrEmpty(output))
                output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "unity-player", folder));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                target = target,
                locationPathName = Path.Combine(output, executable),
                options = BuildOptions.None,
            });
            Debug.Log($"EQClassicBuild: {target} player {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB in {output}");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException($"player build {report.summary.result}");
        }
    }
}
