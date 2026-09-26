// Added by tools/unity/setup-client.sh to the copy of LanternUnityTools in build/unity-client (not
// to the submodule): its importers end with EditorUtility.DisplayDialog, which blocks an unattended
// import. setup-client.sh points those calls here.
using UnityEditor;
using UnityEngine;

namespace Lantern.EQ.Editor
{
    public static class HeadlessDialog
    {
        /// <summary>With EQC_HEADLESS set, logs the message and answers "OK"; otherwise the normal dialog.</summary>
        public static bool Show(string title, string message, string ok, string cancel = "")
        {
            if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("EQC_HEADLESS")))
            {
                Debug.Log($"{title}: {message}");
                return true;
            }
            return string.IsNullOrEmpty(cancel)
                ? EditorUtility.DisplayDialog(title, message, ok)
                : EditorUtility.DisplayDialog(title, message, ok, cancel);
        }
    }
}
