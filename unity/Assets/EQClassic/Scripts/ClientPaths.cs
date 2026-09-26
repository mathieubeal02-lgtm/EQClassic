using System;
using System.IO;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// Where the LanternExtractor exports are for the Editor: EQC_EXPORTS, or build/lantern-work/Exports
    /// next to the generated project (build/unity-client). They stay outside Assets: Unity would scan
    /// every one of their files (125,000 for the Trilogy zones) before anything else.
    /// tools/unity/import-zones.sh copies a few at a time into Assets/EQAssets for the importers.
    /// </summary>
    public static class ClientPaths
    {
        public static string Exports
        {
            get
            {
                var configured = Environment.GetEnvironmentVariable("EQC_EXPORTS");
                return !string.IsNullOrEmpty(configured)
                    ? configured
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "lantern-work", "Exports"));
            }
        }
    }
}
