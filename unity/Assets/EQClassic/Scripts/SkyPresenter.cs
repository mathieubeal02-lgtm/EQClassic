using EQClassic.ClientCore;
using EQClassic.Shared.Zone;
using Lantern.EQ.Environment;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace EQClassic.Unity
{
    /// <summary>
    /// The sky and the day: LanternUnityTools' sky prefab (EQClassic > Import: --sky) drawn by its own
    /// camera under the world (URP camera stack: the sky camera is the base, the main camera an
    /// overlay, so the zone's fog and clip distance do not touch the sky), turned with Norrath's clock;
    /// and the day/night ambient light of the Lantern shaders (_DayNightColor).
    /// </summary>
    public sealed class SkyPresenter
    {
        private static readonly int DayNightColor = Shader.PropertyToID("_DayNightColor");
        private static readonly Color Night = new Color(0.35f, 0.35f, 0.5f);

        private SkyController _sky;
        private Camera _skyCamera;

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
                CreateSkyCamera(main);
            }
            _sky.SetActiveCameraTransform(_skyCamera.transform, true);
            _sky.SetEnabledSky(info.Sky + 1); // Lantern's group 0 is "no sky"
        }

        private void CreateSkyCamera(Camera main)
        {
            var go = new GameObject("EQClassic sky camera");
            Object.DontDestroyOnLoad(go);
            _skyCamera = go.AddComponent<Camera>();
            _skyCamera.cullingMask = LayerMask.GetMask("Sky", "IgnoreTarget"); // SkyLayer moves its objects to IgnoreTarget
            _skyCamera.farClipPlane = 10000f;
            _skyCamera.clearFlags = CameraClearFlags.SolidColor;
            main.cullingMask &= ~LayerMask.GetMask("Sky", "IgnoreTarget");
            var skyData = _skyCamera.GetUniversalAdditionalCameraData();
            var mainData = main.GetUniversalAdditionalCameraData();
            mainData.renderType = CameraRenderType.Overlay;
            skyData.cameraStack.Add(main);
        }

        /// <summary>Every frame: the sky follows the main camera's rotation and turns with the clock; ambient light by the hour.</summary>
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
            if (_sky == null || _skyCamera == null || main == null)
                return;
            _skyCamera.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            _skyCamera.fieldOfView = main.fieldOfView;
            _skyCamera.backgroundColor = main.backgroundColor;
            _sky.UpdateSkyPosition();
            _sky.UpdateTime(fraction);
            _sky.UpdateTimeLate(deltaTime, fraction);
        }
    }
}
