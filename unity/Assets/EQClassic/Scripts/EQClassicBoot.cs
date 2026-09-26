using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// Starts the client in any scene: no scene or prefab has to be set up by hand, so the project
    /// works right after tools/unity/setup-client.sh. Press Play in an empty scene.
    /// </summary>
    public static class EQClassicBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Object.FindObjectOfType<EQClassicClient>() != null)
                return;
            var root = new GameObject("EQClassic");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<EQClassicClient>();
        }
    }
}
