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
        /// <summary>Walking (Shift held): about a third of the run speed, as in the Trilogy client.</summary>
        public const float WalkSpeed = 15f;
        public const float TurnDegreesPerSecond = 120f;
        /// <summary>Movement speed from buffs (1.4 with a +40% spirit of wolf, 0.6 snared by 40%), set by the server's buff list.</summary>
        public float SpeedFactor { get; set; } = 1f;
        public const float SendRate = 10f;
        /// <summary>Highest step (units) the character climbs without jumping.</summary>
        public const float StepUp = 6f;
        /// <summary>Height of the wall check above the feet: above <see cref="StepUp"/>, so steps are not walls.</summary>
        public const float WaistHeight = 8f;
        /// <summary>How far the body keeps from walls (units): the eye and the camera never touch a wall.</summary>
        public const float BodyRadius = 2.5f;

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
        /// <summary>Walk instead of run.</summary>
        public bool Walking { get; set; }

        /// <summary>Turns by <paramref name="degrees"/> (positive = clockwise), as the mouse does with the right button held.</summary>
        public void Turn(float degrees)
        {
            if (degrees == 0)
                return;
            // Positive turns right on screen: the world is drawn mirrored, so the heading goes down.
            Heading = ((Heading - degrees) % 360f + 360f) % 360f;
            _dirty = true;
        }

        /// <summary>Units per second² pulling the player down; a jump starts at <see cref="JumpSpeed"/> (about 5.7 units high).</summary>
        public const float Gravity = 90f, JumpSpeed = 32f, MaxFallSpeed = 200f;
        /// <summary>A floor further below than this is a drop: the player falls instead of snapping to it.</summary>
        public const float StepDown = 2f;

        /// <summary>Vertical speed (units per second, up positive) while in the air.</summary>
        public float VerticalSpeed { get; private set; }
        public bool Airborne { get; private set; }
        /// <summary>Levitating (a buff): drops are floated down at <see cref="LevitateFallSpeed"/>.</summary>
        public bool Levitating { get; set; }
        /// <summary>#flymode: no gravity; <see cref="SwimInput"/> climbs and dives at <see cref="FlyVerticalSpeed"/>.</summary>
        public bool Flying { get; set; }
        public const float FlyVerticalSpeed = 30f;
        public const float LevitateFallSpeed = 8f;

        /// <summary>The zone's water, lava and zone line regions (bsp_tree), when known.</summary>
        public ZoneRegions? Regions { get; set; }
        /// <summary>In water up to the chest: no gravity, slower, <see cref="SwimInput"/> moves up and down.</summary>
        public bool Swimming { get; private set; }
        /// <summary>+1 swims up (Space), −1 down, 0 floats.</summary>
        public float SwimInput { get; set; }
        /// <summary>Height of the chest above the feet: the character swims when it is under water.</summary>
        public const float ChestHeight = 4f;
        public const float SwimVerticalSpeed = 20f, SwimSpeedFactor = 0.6f;

        private bool InWater(float x, float y, float z) => Regions?.InWater(new Vec3(x, y, z + ChestHeight)) ?? false;

        /// <summary>Jump (Space), from the ground only (in water, Space swims up instead).</summary>
        public void Jump()
        {
            if (Airborne || Swimming || Flying)
                return;
            Airborne = true;
            VerticalSpeed = JumpSpeed;
            _dirty = true;
        }

        /// <summary>
        /// Moves on the zone's collision mesh: the character follows the ground (climbing at most
        /// <see cref="StepUp"/>), falls from ledges and jumps with gravity, and does not move when a
        /// wall crosses the path at waist height or when there is no ground at the destination
        /// (outside the zone). Returns false when the horizontal move was blocked.
        /// </summary>
        public bool Move(float forward, float strafe, float turn, float seconds, ZoneCollisionMesh mesh)
        {
            var from = Position;
            bool blocked = false;
            Move(forward, strafe, turn, seconds, (x, y, z) =>
            {
                var ground = mesh.GroundZ(x, y, z, StepUp);
                // The way ahead is checked a body's radius beyond the step, at the knees and at the waist.
                float dx = x - from.X, dy = y - from.Y, length = (float)Math.Sqrt(dx * dx + dy * dy);
                float ax = length > 1e-4f ? x + dx / length * BodyRadius : x, ay = length > 1e-4f ? y + dy / length * BodyRadius : y;
                if (ground is float g
                    && mesh.LineOfSight(new Vec3(from.X, from.Y, from.Z + WaistHeight), new Vec3(ax, ay, Math.Max(g, z) + WaistHeight))
                    && mesh.LineOfSight(new Vec3(from.X, from.Y, from.Z + StepUp + 1f), new Vec3(ax, ay, Math.Max(g, z) + StepUp + 1f)))
                    return z; // height is settled below, with gravity
                blocked = true;
                return z;
            });
            if (blocked)
                Position = from;
            Fall(seconds, mesh);
            return !blocked;
        }

        /// <summary>Vertical motion: swim, stay on the ground, or fly and fall until landing.</summary>
        private void Fall(float seconds, ZoneCollisionMesh mesh)
        {
            var p = Position;
            float? ground = mesh.GroundZ(p.X, p.Y, p.Z, StepUp);
            if (Flying)
            {
                Airborne = false;
                Swimming = false;
                VerticalSpeed = SwimInput * FlyVerticalSpeed;
                if (VerticalSpeed == 0f)
                    return;
                float flyZ = p.Z + VerticalSpeed * seconds;
                if (VerticalSpeed < 0f && ground is float under && flyZ < under)
                    flyZ = under; // down to the ground, not through it
                Position = new Vec3(p.X, p.Y, flyZ);
                _dirty = true;
                return;
            }
            Swimming = InWater(p.X, p.Y, p.Z);
            if (Swimming)
            {
                Airborne = false;
                VerticalSpeed = SwimInput * SwimVerticalSpeed;
                if (VerticalSpeed == 0f)
                    return;
                float swimZ = p.Z + VerticalSpeed * seconds;
                if (VerticalSpeed > 0f && !InWater(p.X, p.Y, swimZ))
                    return; // at the surface: treading water
                if (VerticalSpeed < 0f && ground is float bottom && swimZ < bottom)
                    swimZ = bottom;
                Position = new Vec3(p.X, p.Y, swimZ);
                _dirty = true;
                return;
            }
            if (!Airborne)
            {
                if (ground is float g && g >= p.Z - StepDown)
                {
                    if (g != p.Z)
                    {
                        Position = new Vec3(p.X, p.Y, g); // walk up a step or down a gentle slope
                        _dirty = true;
                    }
                    return;
                }
                Airborne = true; // walked off a ledge
                VerticalSpeed = 0f;
            }
            VerticalSpeed = Math.Max(VerticalSpeed - Gravity * seconds, Levitating ? -LevitateFallSpeed : -MaxFallSpeed);
            float z = p.Z + VerticalSpeed * seconds;
            float? landing = mesh.GroundZ(p.X, p.Y, Math.Max(z, p.Z), 0f);
            if (VerticalSpeed <= 0f && landing is float floor && z <= floor)
            {
                z = floor;
                Airborne = false;
                VerticalSpeed = 0f;
            }
            Position = new Vec3(p.X, p.Y, z);
            _dirty = true;
        }

        /// <summary>forward/strafe in [-1, 1], turn in [-1, 1] (positive = to the right as the player sees it).</summary>
        public void Move(float forward, float strafe, float turn, float seconds, Func<float, float, float, float>? groundZ = null)
        {
            if (turn != 0)
            {
                Heading = ((Heading - turn * TurnDegreesPerSecond * seconds) % 360f + 360f) % 360f; // positive: right on screen
                _dirty = true;
            }
            if (forward == 0 && strafe == 0)
                return;
            float rad = Heading * (float)Math.PI / 180f;
            float fx = (float)Math.Sin(rad), fy = (float)Math.Cos(rad);   // heading 0 → +Y
            // Right of the heading as it looks on screen: EverQuest's world is drawn mirrored (+X to the
            // west), so the player's right is (−fy, fx), not (fy, −fx).
            float sx = -fy, sy = fx;
            float length = Math.Min(1f, (float)Math.Sqrt(forward * forward + strafe * strafe));
            float norm = length / (float)Math.Sqrt(forward * forward + strafe * strafe);
            float speed = (Walking ? WalkSpeed : RunSpeed) * SpeedFactor * (Swimming ? SwimSpeedFactor : 1f);
            float dx = (fx * forward + sx * strafe) * norm * speed * seconds;
            float dy = (fy * forward + sy * strafe) * norm * speed * seconds;
            float x = Position.X + dx, y = Position.Y + dy;
            float z = groundZ?.Invoke(x, y, Position.Z) ?? Position.Z;
            Position = new Vec3(x, y, z);
            _dirty = true;
        }

        /// <summary>Turns to face a point (heading 0 = +Y north, 90 = +X east).</summary>
        public void Face(Vec3 point)
        {
            float dx = point.X - Position.X, dy = point.Y - Position.Y;
            if (dx * dx + dy * dy < 1e-6f)
                return;
            Heading = ((float)(Math.Atan2(dx, dy) * 180.0 / Math.PI) + 360f) % 360f;
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
