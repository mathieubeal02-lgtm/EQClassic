using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// Standalone builds load the imported zone and character prefabs from the asset bundles
    /// EQClassicBuild writes to StreamingAssets/EQClassic (one per zone, one for all characters).
    /// Prefabs are looked up by the same asset paths the Editor uses.
    /// </summary>
    public static class ClientBundles
    {
        public const string CharacterBundle = "characters";
        public static string ZoneBundle(string zone) => "zone-" + zone.ToLowerInvariant();

        public static string Directory => Path.Combine(Application.streamingAssetsPath, "EQClassic");

        private static readonly Dictionary<string, AssetBundle> Loaded = new Dictionary<string, AssetBundle>();

        /// <summary>The prefab at an asset path under Assets/Content/AssetBundleContent, or null.</summary>
        public static GameObject Load(string assetPath)
        {
            string bundle = BundleFor(assetPath);
            if (bundle == null)
                return null;
            if (!Loaded.TryGetValue(bundle, out var loaded))
            {
                var file = Path.Combine(Directory, bundle);
                loaded = File.Exists(file) ? AssetBundle.LoadFromFile(file) : null;
                Loaded[bundle] = loaded; // remember misses too
            }
            return loaded != null ? loaded.LoadAsset<GameObject>(assetPath) : null;
        }

        private static string BundleFor(string assetPath)
        {
            var parts = assetPath.Split('/');
            int i = System.Array.IndexOf(parts, "Zones");
            if (i >= 0 && i + 1 < parts.Length)
                return ZoneBundle(parts[i + 1]);
            return System.Array.IndexOf(parts, "Characters") >= 0 ? CharacterBundle : null;
        }
    }
}
