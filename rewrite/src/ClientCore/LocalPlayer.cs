using System;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.ClientCore
{
    /// <summary>
    /// The player's own character: moved locally from input every frame (no waiting for the server),
    /// reported to the zone at <see cref="SendRate"/> Hz; a <see cref="MoveCorrection"/> from the
    /// server wins.
    /// </summary>
    public sealed class LocalPlayer
    {
        /// <summary>Units per second: EverQuest run speed, under the server's 70 u/s cap.</summary>
        public const float RunSpeed = 45f;
        public const float TurnDegreesPerSecond = 120f;
        public const float SendRate = 10f;

        private float _sendIn;
        private bool _dirty;

        public LocalPlayer(int entityId, Vec3 position, float heading)
        {
            EntityId = entityId;
            Position = position;
            Heading = heading;
        }

        public int EntityId { get; }
        public Vec3 Position { get; private set; }
        /// <summary>EverQuest degrees: 0 faces +Y (north), clockwise.</summary>
        public float Heading { get; private set; }
        public string? LastCorrection { get; private set; }

        /// <summary>forward/strafe in [-1, 1], turn in [-1, 1] (positive = clockwise).</summary>
        public void Move(float forward, float strafe, float turn, float seconds, Func<float, float, float, float>? groundZ = null)
        {
            if (turn != 0)
            {
                Heading = (Heading + turn * TurnDegreesPerSecond * seconds + 360f) % 360f;
                _dirty = true;
            }
            if (forward == 0 && strafe == 0)
                return;
            float rad = Heading * (float)Math.PI / 180f;
            float fx = (float)Math.Sin(rad), fy = (float)Math.Cos(rad);   // heading 0 → +Y
            float sx = fy, sy = -fx;                                      // right of the heading
            float length = Math.Min(1f, (float)Math.Sqrt(forward * forward + strafe * strafe));
            float norm = length / (float)Math.Sqrt(forward * forward + strafe * strafe);
            float dx = (fx * forward + sx * strafe) * norm * RunSpeed * seconds;
            float dy = (fy * forward + sy * strafe) * norm * RunSpeed * seconds;
            float x = Position.X + dx, y = Position.Y + dy;
            float z = groundZ?.Invoke(x, y, Position.Z) ?? Position.Z;
            Position = new Vec3(x, y, z);
            _dirty = true;
        }

        /// <summary>The move to send now, if any (rate-limited to <see cref="SendRate"/> Hz).</summary>
        public PlayerMove? Due(float seconds)
        {
            _sendIn -= seconds;
            if (!_dirty || _sendIn > 0)
                return null;
            _sendIn = 1f / SendRate;
            _dirty = false;
            return new PlayerMove(Position.X, Position.Y, Position.Z, Heading);
        }

        public void Apply(MoveCorrection correction)
        {
            Position = new Vec3(correction.X, correction.Y, correction.Z);
            LastCorrection = correction.Reason;
            _dirty = false;
        }
    }
}
