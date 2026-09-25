using EQClassic.Shared.World;

namespace EQClassic.Server.Zone;

/// <summary>How an NPC goes through its grid once it reaches an end (legacy grid.type).</summary>
public enum GridType
{
    /// <summary>0: after the last waypoint, back to the first.</summary>
    Circular = 0,
    /// <summary>Any other value in the legacy data: back and forth (3 = patrol, the common case).</summary>
    BackAndForth = 3,
}

/// <summary>
/// Walks an NPC along its waypoints (grid_entries) on a zone's collision mesh. Each step follows
/// the height of the current leg, interpolated between its two waypoints, and snaps to the ground
/// just above it. Searching far above instead is what made the legacy zone's guards pop onto
/// roofs and "fall from the sky" (NPC::WaypointPathZ in Zone/Source/npc.cpp holds the same fix).
/// Pauses at waypoints are left to the caller (zone tick), which also decides when to start.
/// </summary>
public sealed class WaypointWalker
{
    private const float GroundSearchAbovePath = 10f;
    private const float MaxDropBelowPath = 50f;

    private readonly IReadOnlyList<Vec3> _waypoints;
    private readonly ZoneCollisionMesh? _mesh;
    private readonly GridType _type;
    private int _target;
    private int _direction = 1;
    private Vec3 _legStart;

    public Vec3 Position { get; private set; }
    public int TargetIndex => _target;

    public WaypointWalker(IReadOnlyList<Vec3> waypoints, ZoneCollisionMesh? mesh, GridType type = GridType.BackAndForth, int startIndex = 0)
    {
        if (waypoints.Count < 2)
            throw new ArgumentException("a grid needs at least two waypoints", nameof(waypoints));
        _waypoints = waypoints;
        _mesh = mesh;
        _type = type;
        Position = _legStart = waypoints[startIndex];
        _target = startIndex;
        AdvanceTarget();
    }

    /// <summary>Moves up to <paramref name="distance"/> units; returns true when a waypoint was reached.</summary>
    public bool Step(float distance)
    {
        var target = _waypoints[_target];
        float dx = target.X - Position.X, dy = target.Y - Position.Y;
        float remaining = MathF.Sqrt(dx * dx + dy * dy);
        if (remaining <= distance)
        {
            Position = target with { Z = GroundAt(target.X, target.Y) };
            _legStart = target;
            AdvanceTarget();
            return true;
        }
        float x = Position.X + dx / remaining * distance, y = Position.Y + dy / remaining * distance;
        Position = new Vec3(x, y, GroundAt(x, y));
        return false;
    }

    private float GroundAt(float x, float y)
    {
        var target = _waypoints[_target];
        float legX = target.X - _legStart.X, legY = target.Y - _legStart.Y;
        float legLength = MathF.Sqrt(legX * legX + legY * legY);
        float t = legLength < 0.1f ? 1f : Math.Clamp(1f - Distance(x, y, target) / legLength, 0f, 1f);
        float pathZ = _legStart.Z + (target.Z - _legStart.Z) * t;
        var ground = _mesh?.GroundZ(x, y, pathZ, GroundSearchAbovePath);
        return ground is float z && z >= pathZ - MaxDropBelowPath ? z : pathZ;
    }

    private void AdvanceTarget()
    {
        if (_type == GridType.Circular)
        {
            _target = (_target + 1) % _waypoints.Count;
            return;
        }
        if (_target + _direction < 0 || _target + _direction >= _waypoints.Count)
            _direction = -_direction;
        _target += _direction;
    }

    private static float Distance(float x, float y, Vec3 p) => MathF.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
}
