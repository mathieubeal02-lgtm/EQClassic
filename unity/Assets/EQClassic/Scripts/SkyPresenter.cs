using EQClassic.ClientCore;
using EQClassic.Shared.Zone;
using Lantern.EQ.Environment;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The sky and the day: LanternUnityTools' sky prefab (tools/unity/import-zones.sh --sky), a small
    /// dome that follows the camera and is drawn first without writing depth (its shader's Background
    /// queue, no fog; the sun and moons moved there too), so the zone draws over it whatever its clip distance; turned with Norrath's
    /// clock; and the day/night ambient light of the Lantern shaders (_DayNightColor).
    /// </summary>
    public sealed class SkyPresenter
    {
        private static readonly int DayNightColor = Shader.PropertyToID("_DayNightColor");
        private static readonly Color Night = new Color(0.35f, 0.35f, 0.5f);

        private SkyController _sky;

        /// <summary>Sets the sky for a zone: the zone header's sky type selects one of Lantern's five skies.</summary>
        public void Enter(Camera main, ZoneInfo info, System.Func<string, GameObject> loadPrefab)
        {
            if (_sky == null)
            {
                var prefab = loadPrefab("Assets/Content/AssetBundleContent/Sky/Sky.prefab");
                if (prefab == null)
                {
                    Debug.Log("EQClassic: no sky imported (tools/unity/import-zones.sh --sky); the fog colour stays behind the zone.");
                    return;
                }
                var go = Object.Instantiate(prefab);
                Object.DontDestroyOnLoad(go);
                _sky = go.GetComponent<SkyController>();
                _sky.SetSecondsPerDay((float)(EQClassic.Shared.World.EqClock.SecondsPerHour * 24));
            }
            main.cullingMask |= LayerMask.GetMask("Sky", "IgnoreTarget"); // SkyLayer moves its objects to IgnoreTarget
            main.nearClipPlane = 0.05f; // the dome is about a unit wide: the default 0.3 near plane cuts it away
            _sky.SetActiveCameraTransform(main.transform, true);
            _sky.SetEnabledSky(info.Sky + 1); // Lantern's group 0 is "no sky"
            DrawBehindTheZone(_sky.gameObject);
        }

        /// <summary>The queue of the sun, moons and planets: after the dome (1000), before the clouds (1200).</summary>
        public const int BodiesQueue = 1100;

        /// <summary>
        /// The sun, moons and planets come with transparent materials (queue 3000): drawn after the zone,
        /// and a unit from the camera, they showed in front of hills and walls. Everything of the sky is
        /// drawn before the zone instead, which then covers it.
        /// </summary>
        private static void DrawBehindTheZone(GameObject sky)
        {
            foreach (var renderer in sky.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.materials)
                    if (material != null && material.renderQueue >= 2000)
                        material.renderQueue = BodiesQueue;
        }

        /// <summary>Every frame: the sky follows the camera and turns with the clock; ambient light by the hour.</summary>
        public void Update(Camera main, GameClient client, float deltaTime)
        {
            float daylight = 1f;
            float fraction = 0.5f;
            if (client.Clock is { } clock)
            {
                daylight = clock.Daylight(client.Now);
                fraction = clock.DayFraction(client.Now);
            }
            Shader.SetGlobalColor(DayNightColor, Color.Lerp(Night, Color.white, daylight));
            if (_sky == null || main == null)
                return;
            _sky.UpdateSkyPosition();
            _sky.UpdateTime(fraction);
            _sky.UpdateTimeLate(deltaTime, fraction);
        }
    }
}
