using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The zone's weather from the server (0 clear, 1 rain, 2 snow): a box of falling particles kept
    /// above the camera, as the Trilogy client drew rain and snow around the viewer.
    /// </summary>
    public sealed class WeatherPresenter
    {
        private ParticleSystem _particles;
        private int _shown;

        /// <param name="sheltered">A roof above the player: nothing falls (the rain stops where it is).</param>
        public void Update(Camera camera, int weather, bool sheltered = false)
        {
            if (camera == null)
                return;
            if (weather != _shown)
            {
                _shown = weather;
                Build(weather);
            }
            if (_particles == null)
                return;
            _particles.transform.position = camera.transform.position + new Vector3(0f, 8f, 0f);
            if (sheltered && _particles.isEmitting)
                _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            else if (!sheltered && !_particles.isEmitting)
                _particles.Play();
        }

        private void Build(int weather)
        {
            if (_particles != null)
                Object.Destroy(_particles.gameObject);
            _particles = null;
            if (weather is not (1 or 2))
                return;
            bool rain = weather == 1;
            var go = new GameObject(rain ? "Rain" : "Snow");
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // the box emits along its forward axis: downwards
            _particles = go.AddComponent<ParticleSystem>();
            var main = _particles.main;
            main.loop = true;
            main.startLifetime = rain ? 1.2f : 7f;
            main.startSpeed = rain ? 16f : 1.2f;
            main.startSize = rain ? 0.04f : 0.1f;
            main.startColor = rain ? new Color(0.7f, 0.75f, 0.85f, 0.5f) : new Color(1f, 1f, 1f, 0.9f);
            main.gravityModifier = rain ? 1f : 0.02f;
            main.maxParticles = rain ? 5000 : 2500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _particles.emission;
            emission.rateOverTime = rain ? 3000f : 400f;
            var shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 40f, 1f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            // The URP particle shader is not in every build (it is found only when something references it):
            // without a material the particles draw magenta, so fall back on Sprites/Default, always included.
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (renderer != null && shader != null)
            {
                renderer.material = new Material(shader);
                if (rain)
                {
                    renderer.renderMode = ParticleSystemRenderMode.Stretch; // streaks
                    renderer.lengthScale = 6f;
                }
            }
            _particles.Play();
        }
    }
}
