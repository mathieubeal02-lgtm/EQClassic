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
    // Grid z sits about 3.75 above the floor (the legacy model offset). Searching 5 above the path
    // finds that floor but not a ledge or crate a few units higher; accepting at most 15 below keeps
    // an NPC walking over a gap on its path instead of dropping to a lower level. Measured on the
    // live Qeynos (MySqlZoneDataTests.Live_qeynos_guards_stay_on_the_ground): with 10 above / 50
    // below, an NPC going down to the sewers fell 48 units in one tick.
    private const float GroundSearchAbovePath = 5f;
    private const float MaxDropBelowPath = 15f;
    // Vertical speed limits relative to the horizontal distance walked: at most a 45-degree climb,
    // a descent up to three times steeper. Where a grid leg cuts across a ledge, the NPC climbs or
    // steps down over a few ticks instead of teleporting between floors.
    private const float MaxClimbPerUnit = 1f;
    private const float MaxDescentPerUnit = 3f;

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
        // Stand on the ground under the first waypoint (grid z is ~3.75 above the floor).
        Position = Position with { Z = GroundAt(Position.X, Position.Y) };
    }

    /// <summary>Moves up to <paramref name="distance"/> units; returns true when a waypoint was reached.</summary>
    public bool Step(float distance)
    {
        var target = _waypoints[_target];
        float dx = target.X - Position.X, dy = target.Y - Position.Y;
        float remaining = MathF.Sqrt(dx * dx + dy * dy);
        if (remaining <= distance)
        {
            Position = target with { Z = LimitVertical(GroundAt(target.X, target.Y), remaining) };
            _legStart = target with { Z = Position.Z };
            AdvanceTarget();
            return true;
        }
        float x = Position.X + dx / remaining * distance, y = Position.Y + dy / remaining * distance;
        Position = new Vec3(x, y, LimitVertical(GroundAt(x, y), distance));
        return false;
    }

    private float LimitVertical(float desiredZ, float horizontal)
    {
        float dz = desiredZ - Position.Z;
        float up = MaxClimbPerUnit * horizontal, down = MaxDescentPerUnit * horizontal;
        return Position.Z + Math.Clamp(dz, -down, up);
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
