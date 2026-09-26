using EQClassic.Shared.World;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The Trilogy client's views: first person (its default), chase and overhead, cycled with F9;
    /// the mouse wheel zooms the third-person views (all the way in is first person); Page Up / Page
    /// Down look up and down, Home centres; with the right button held the mouse looks up and down
    /// (turning is done by the caller, on the player). Third-person cameras come closer when a wall
    /// of the collision mesh is in the way.
    /// </summary>
    public sealed class CameraRig
    {
        public enum View { FirstPerson, Chase, Overhead }

        private const float MinDistance = 4f, MaxDistance = 40f; // Unity units behind the head
        private const float WallMargin = 1.5f; // Unity units kept between the camera and a wall
        private const float PitchLimit = 80f;
        private readonly float _scale;

        public CameraRig(float lanternScale) => _scale = lanternScale;

        public View Mode { get; private set; } = View.Chase;
        public float Distance { get; private set; } = 12f;
        /// <summary>Degrees above the horizon the camera looks down from (third person) or up (first person).</summary>
        public float Pitch { get; private set; }

        public void CycleView()
        {
            Mode = Mode == View.FirstPerson ? View.Chase : Mode == View.Chase ? View.Overhead : View.FirstPerson;
            if (Mode != View.FirstPerson && Distance < MinDistance + 0.5f)
                Distance = 12f;
        }

        /// <summary>Wheel: positive zooms in. Zooming all the way in switches to first person, out of it back to chase.</summary>
        public void Zoom(float steps)
        {
            if (steps == 0)
                return;
            if (Mode == View.FirstPerson)
            {
                if (steps < 0)
                {
                    Mode = View.Chase;
                    Distance = MinDistance + 1f;
                }
                return;
            }
            Distance = Mathf.Clamp(Distance * (steps > 0 ? 0.85f : 1.18f), MinDistance, MaxDistance);
            if (steps > 0 && Distance <= MinDistance)
                Mode = View.FirstPerson;
        }

        public void Look(float degreesUp) => Pitch = Mathf.Clamp(Pitch + degreesUp, -PitchLimit, PitchLimit);
        public void Centre() => Pitch = 0f;

        /// <summary>Places the camera for a player whose model stands at <paramref name="feet"/>, facing <paramref name="rotation"/>.</summary>
        public void Place(Camera camera, Vector3 feet, Quaternion rotation, float eyeHeight, ZoneCollisionMesh mesh)
        {
            var eye = feet + new Vector3(0f, eyeHeight, 0f);
            if (Mode == View.FirstPerson)
            {
                camera.transform.position = eye;
                camera.transform.rotation = rotation * Quaternion.Euler(-Pitch, 0f, 0f);
                return;
            }
            float elevation = Mode == View.Overhead ? 60f : 15f;
            var offset = rotation * Quaternion.Euler(elevation - Pitch, 0f, 0f) * new Vector3(0f, 0f, -Distance);
            float fraction = Clearance(eye, offset, mesh);
            camera.transform.position = eye + offset * fraction;
            camera.transform.LookAt(eye);
        }

        /// <summary>Fraction (0.05 to 1) of the offset that keeps the camera on the player's side of the walls.</summary>
        private float Clearance(Vector3 eye, Vector3 offset, ZoneCollisionMesh mesh)
        {
            if (mesh == null)
                return 1f;
            var from = Coordinates.FromUnity(eye.x, eye.y, eye.z, _scale);
            // The camera keeps a margin from the wall behind it, or the view plane would cut through it.
            float margin = WallMargin / Mathf.Max(offset.magnitude, 0.01f);
            for (float f = 1f; f > 0.05f; f -= 0.05f)
            {
                var cam = eye + offset * (f + margin);
                if (mesh.LineOfSight(from, Coordinates.FromUnity(cam.x, cam.y, cam.z, _scale)))
                    return f;
            }
            return 0.05f;
        }
    }
}
