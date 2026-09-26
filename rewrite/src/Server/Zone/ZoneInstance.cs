using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Server.Zone;

/// <summary>
/// One running zone: its NPCs and players, advanced by <see cref="Tick"/>. No static or global
/// state, so any number of instances live in one process (the legacy zone processes crashed when
/// reused for another zone, on stale globals).
/// </summary>
public sealed class ZoneInstance
{
    /// <summary>Fastest a player may move, units per second (EverQuest run speed with haste and SoW stays well under).</summary>
    public const float MaxPlayerSpeed = 70f;
    private const float MoveTolerance = 5f;

    public sealed class Entity
    {
        internal Entity(int id, string name, bool isPlayer, int race, int gender, int level, float size, Vec3 position, float heading)
        {
            Id = id; Name = name; IsPlayer = isPlayer; Race = race; Gender = gender; Level = level; Size = size; Position = position; Heading = heading;
        }

        public int Id { get; }
        public string Name { get; }
        public bool IsPlayer { get; }
        public int Race { get; }
        public int Gender { get; }
        public int Level { get; }
        public float Size { get; }
        public Vec3 Position { get; internal set; }
        public float Heading { get; internal set; }
        internal WaypointWalker? Walker;
        internal Grid? Grid;
        internal float PauseLeft;
        internal float Speed;
        internal bool Moved;
        internal double LastMoveTime;

        public EntitySpawn ToSpawn() => new(Id, Name, IsPlayer, Race, Gender, Level, Size, Position.X, Position.Y, Position.Z, Heading);
    }

    private readonly Dictionary<int, Entity> _entities = new();
    private int _nextId = 1;
    private double _time;

    public string ShortName { get; }
    public ZoneCollisionMesh? Mesh { get; }
    public uint TickCount { get; private set; }
    public IEnumerable<Entity> Entities => _entities.Values;

    public ZoneInstance(ZoneData data, ZoneCollisionMesh? mesh = null, int seed = 0)
    {
        ShortName = data.ShortName;
        Mesh = mesh;
        var random = new Random(seed);
        foreach (var spawn in data.Spawns)
        {
            if (Pick(spawn.Candidates, random) is not { } npc)
                continue;
            var start = spawn.Position;
            if (mesh?.GroundZ(start.X, start.Y, start.Z, 10f) is float ground)
                start = start with { Z = ground };
            var entity = Add(npc.Name, false, npc.Race, npc.Gender, npc.Level, npc.Size, start, spawn.Heading);
            entity.Speed = npc.WalkUnitsPerSecond;
            if (spawn.GridId > 0 && data.Grids.TryGetValue(spawn.GridId, out var grid) && grid.Waypoints.Count >= 2)
            {
                entity.Grid = grid;
                entity.Walker = new WaypointWalker(grid.Waypoints.Select(w => w.Position).ToList(), mesh, grid.Type, ClosestWaypoint(grid, start));
                entity.Position = entity.Walker.Position;
            }
        }
    }

    public Entity AddPlayer(string name, int race, int gender, int level, Vec3 position, float heading = 0)
    {
        var player = Add(name, true, race, gender, level, 6f, position, heading);
        player.LastMoveTime = _time;
        return player;
    }

    public bool RemovePlayer(int id) => _entities.TryGetValue(id, out var e) && e.IsPlayer && _entities.Remove(id);

    public Entity? Get(int id) => _entities.GetValueOrDefault(id);

    /// <summary>
    /// Validates a player's reported position: refused when faster than <see cref="MaxPlayerSpeed"/>
    /// since the last accepted move (teleport hacks, lag spikes are corrected, not trusted).
    /// Returns null when accepted, otherwise the reason (the position stays the last valid one).
    /// </summary>
    public string? MovePlayer(int id, Vec3 to, float heading)
    {
        if (!_entities.TryGetValue(id, out var player) || !player.IsPlayer)
            return "not in zone";
        double elapsed = Math.Max(_time - player.LastMoveTime, 0.05);
        float dx = to.X - player.Position.X, dy = to.Y - player.Position.Y, dz = to.Z - player.Position.Z;
        float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance > MaxPlayerSpeed * elapsed + MoveTolerance)
            return $"moved {distance:0} units in {elapsed:0.00} s";
        player.Position = to;
        player.Heading = heading;
        player.LastMoveTime = _time;
        player.Moved = true;
        return null;
    }

    /// <summary>Advances the zone by <paramref name="seconds"/> and returns what moved since the last tick.</summary>
    public IReadOnlyList<Entity> Tick(float seconds)
    {
        _time += seconds;
        TickCount++;
        foreach (var e in _entities.Values)
        {
            if (e.Walker is null)
                continue;
            if (e.PauseLeft > 0)
            {
                e.PauseLeft -= seconds;
                continue;
            }
            var before = e.Position;
            int target = e.Walker.TargetIndex;
            bool arrived = e.Walker.Step(e.Speed * seconds);
            e.Position = e.Walker.Position;
            e.Heading = Heading(before, e.Position, e.Heading);
            e.Moved = true;
            if (arrived && e.Grid is not null)
                e.PauseLeft = Math.Max(e.Grid.Waypoints[target].PauseSeconds, 0);
        }
        var moved = _entities.Values.Where(e => e.Moved).ToList();
        foreach (var e in moved)
            e.Moved = false;
        return moved;
    }

    private Entity Add(string name, bool isPlayer, int race, int gender, int level, float size, Vec3 position, float heading)
    {
        var e = new Entity(_nextId++, name, isPlayer, race, gender, level, size, position, heading);
        _entities[e.Id] = e;
        return e;
    }

    /// <summary>Weighted by spawnentry.chance; a group whose chances are all 0 spawns its first NPC.</summary>
    private static NpcTemplate? Pick(IReadOnlyList<(NpcTemplate Npc, int Chance)> candidates, Random random)
    {
        if (candidates.Count == 0)
            return null;
        int total = candidates.Sum(c => Math.Max(c.Chance, 0));
        if (total == 0)
            return candidates[0].Npc;
        int roll = random.Next(total);
        foreach (var (npc, chance) in candidates)
        {
            roll -= Math.Max(chance, 0);
            if (roll < 0)
                return npc;
        }
        return candidates[^1].Npc;
    }

    private static int ClosestWaypoint(Grid grid, Vec3 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < grid.Waypoints.Count; i++)
        {
            var w = grid.Waypoints[i].Position;
            float d = (w.X - p.X) * (w.X - p.X) + (w.Y - p.Y) * (w.Y - p.Y);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    // EverQuest heading: 0-255 would be the legacy wire format; the rewrite keeps degrees.
    private static float Heading(Vec3 from, Vec3 to, float previous)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        return dx * dx + dy * dy < 1e-6f ? previous : MathF.Atan2(dx, dy) * 180f / MathF.PI;
    }
}
