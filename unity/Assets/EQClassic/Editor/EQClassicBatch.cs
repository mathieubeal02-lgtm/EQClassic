using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using Lantern.EQ.Editor.Helpers;
using Lantern.EQ.Editor.Importers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EQClassic.Unity.Editor
{
    /// <summary>
    /// Runs the LanternUnityTools imports without their windows and "import finished" dialogs,
    /// from the menu (EQClassic/Import Zones and Characters) or the command line:
    ///   Unity -projectPath build/unity-client -executeMethod EQClassic.Unity.Editor.EQClassicBatch.ImportAll -quit
    /// Zones come from the EQC_ZONES environment variable (";"-separated, default "qeynos2").
    /// The importers' own steps run unchanged; their private methods are reached by reflection.
    /// They parse the Lantern text exports with the current culture, so the import runs under the
    /// invariant culture (on a French system "0.5" would otherwise be a FormatException).
    /// </summary>
    public static class EQClassicBatch
    {
        private const BindingFlags AnyMember =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        [MenuItem("EQClassic/Import Zones and Characters")]
        public static void ImportAll()
        {
            ImportZones();
            ImportCharacters();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Opens an empty scene and enters Play mode, for scripted test sessions:
        ///   Unity -projectPath build/unity-client -executeMethod EQClassic.Unity.Editor.EQClassicBatch.Play
        /// EQC_HOST, EQC_PORT, EQC_FINGERPRINT and EQC_USER, when set, prefill the connection screens.
        /// </summary>
        public static void Play()
        {
            Prefill("EQC_HOST", "eqc.host");
            Prefill("EQC_PORT", "eqc.port");
            Prefill("EQC_FINGERPRINT", "eqc.fingerprint");
            Prefill("EQC_USER", "eqc.user");
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Prefill(string variable, string key)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(value))
                PlayerPrefs.SetString(key, value);
        }

        public static void ImportZones() => Invariant(ImportZonesCore);

        public static void ImportCharacters() => Invariant(ImportCharactersCore);

        /// <summary>The skies (the "sky" export in Assets/EQAssets/sky) → Content/AssetBundleContent/Sky/Sky.prefab.</summary>
        public static void ImportSky() => Invariant(() =>
        {
            var importer = ScriptableObject.CreateInstance<SkyImporter>();
            try
            {
                Call(importer, "ImportSky"); // its closing dialog goes through HeadlessDialog (EQC_HEADLESS)
                Debug.Log("EQClassicBatch: sky imported");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(importer);
            }
        });

        private static void Invariant(Action import)
        {
            var culture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                import();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        private static void ImportZonesCore()
        {
            var zones = Environment.GetEnvironmentVariable("EQC_ZONES");
            if (string.IsNullOrEmpty(zones))
                zones = "qeynos2";
            var importer = ScriptableObject.CreateInstance<ZoneImporter>();
            try
            {
                foreach (var zone in zones.Split(';'))
                {
                    var ok = (bool)Call(importer, "ImportZone", zone.Trim());
                    Debug.Log($"EQClassicBatch: zone {zone}: {(ok ? "imported" : "FAILED")}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(importer);
            }
        }

        /// <summary>CharacterImporter.ImportCharacters without Close() and the final dialog.</summary>
        private static void ImportCharactersCore()
        {
            const string source = "characters";
            var type = typeof(CharacterImporter);
            type.GetField("_zoneShortname", AnyMember).SetValue(null, source);
            Call(null, type, "LoadData");
            try
            {
                TextureHelper.CopyTextures(source, AssetImportType.Characters);
                ActorStaticImporter.ImportList(source, AssetImportType.Characters, PostProcess(type, "PostProcess"));
                ActorSkeletalImporter.ImportList(source, AssetImportType.Characters,
                    PostProcess(type, "PostProcessSkeletal"));
                ImportHelper.TagAllAssetsForBundles(PathHelper.GetAssetBundleContentPath() + "Characters", "characters");
            }
            finally
            {
                Call(null, type, "Cleanup");
            }
            Debug.Log("EQClassicBatch: characters imported");
        }

        private static Action<GameObject> PostProcess(Type type, string name) =>
            (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>),
                type.GetMethod(name, AnyMember) ?? throw new MissingMethodException(type.Name, name));

        private static object Call(object target, string method, params object[] args) =>
            Call(target, target.GetType(), method, args);

        private static object Call(object target, Type type, string method, params object[] args)
        {
            // Match on the argument types: ZoneImporter has both ImportZone() and ImportZone(string).
            var m = type.GetMethod(method, AnyMember, null, Array.ConvertAll(args, a => a.GetType()), null)
                ?? throw new MissingMethodException(type.Name, method);
            try
            {
                return m.Invoke(target, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                throw;
            }
        }
    }
}
