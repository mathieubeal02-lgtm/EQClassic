namespace EQClassic.Shared.World
{
    /// <summary>
    /// EverQuest (server) coordinates are X, Y on the ground and Z up. LanternExtractor, and so the
    /// Unity scenes LanternUnityTools builds, use Unity's Y-up axes with unity = (eqY, eqZ, eqX)
    /// (checked on Permafrost against the zone server's .map, see ZoneCollisionMesh).
    /// Headings: EverQuest degrees, 0 facing +Y (north), clockwise; Unity yaw 0 faces +Z.
    /// </summary>
    public static class Coordinates
    {
        /// <summary>LanternUnityTools scales imported zones by LanternConstants.WorldScale (0.5).</summary>
        public const float LanternWorldScale = 0.5f;

        public static (float X, float Y, float Z) ToUnity(Vec3 eq, float scale = 1f) => (eq.Y * scale, eq.Z * scale, eq.X * scale);

        public static Vec3 FromUnity(float x, float y, float z, float scale = 1f) => new Vec3(z / scale, x / scale, y / scale);

        /// <summary>EQ +Y (north) is Unity +X, EQ +X is Unity +Z: yaw = 90 - heading.</summary>
        public static float HeadingToUnityYaw(float eqHeadingDegrees) => 90f - eqHeadingDegrees;

        public static float UnityYawToHeading(float yawDegrees) => 90f - yawDegrees;
    }
}
