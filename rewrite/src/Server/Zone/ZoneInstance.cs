using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Server.Zone;

/// <summary>
/// One running zone: its NPCs and players, advanced by <see cref="Tick"/>. No static or global
/// state, so any number of instances live in one process (the legacy zone processes crashed when
/// reused for another zone, on stale globals). Spawns, deaths and zone line crossings are queued
/// as events for the server to broadcast (<see cref="DrainEvents"/>).
/// </summary>
public sealed class ZoneInstance
{
    /// <summary>Fastest a player may move, units per second (EverQuest run speed with haste and SoW stays well under).</summary>
    public const float MaxPlayerSpeed = 70f;
    /// <summary>An engaged NPC stops this close to its target (melee reach in the rewrite until combat, M5).</summary>
    public const float MeleeRange = 10f;
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
        public bool Sitting { get; set; }
        /// <summary>For NPCs: the player they are after, or null.</summary>
        public int? TargetId { get; internal set; }
        internal NpcTemplate? Npc;
        internal SpawnPoint? Spawn;
        internal WaypointWalker? Walker;
        internal Grid? Grid;
        internal float PauseLeft;
        internal float Speed;
        internal float ScanIn;
        internal bool Moved;
        internal double LastMoveTime;

        public EntitySpawn ToSpawn() => new(Id, Name, IsPlayer, Race, Gender, Level, Size, Position.X, Position.Y, Position.Z, Heading);
    }

    public abstract record ZoneEvent;
    public sealed record Spawned(Entity Entity) : ZoneEvent;
    public sealed record Removed(int EntityId) : ZoneEvent;
    public sealed record Engaged(int NpcId, int PlayerId) : ZoneEvent;
    public sealed record CrossedZoneLine(int PlayerId, ZoneLine Line, Vec3 Destination) : ZoneEvent;

    private readonly Dictionary<int, Entity> _entities = new();
    private readonly List<ZoneEvent> _events = new();
    private readonly List<(SpawnPoint Spawn, double At)> _respawns = new();
    private readonly IReadOnlyDictionary<int, Grid> _grids;
    private readonly IReadOnlyList<ZoneLine> _lines;
    private readonly Random _random;
    private int _nextId = 1;
    private double _time;

    public string ShortName { get; }
    public ZoneCollisionMesh? Mesh { get; }
    public IFactionStandings Factions { get; set; } = new IndifferentFactions();
    public uint TickCount { get; private set; }
    public IEnumerable<Entity> Entities => _entities.Values;

    public ZoneInstance(ZoneData data, ZoneCollisionMesh? mesh = null, int seed = 0)
    {
        ShortName = data.ShortName;
        Mesh = mesh;
        _grids = data.Grids;
        _lines = data.Lines;
        _random = new Random(seed);
        foreach (var spawn in data.Spawns)
            SpawnAt(spawn);
        _events.Clear(); // the initial population is the entity list, not news
    }

    public Entity AddPlayer(string name, int race, int gender, int level, Vec3 position, float heading = 0)
    {
        var player = Add(name, true, race, gender, level, 6f, position, heading);
        player.LastMoveTime = _time;
        return player;
    }

    public bool RemovePlayer(int id)
    {
        if (!_entities.TryGetValue(id, out var e) || !e.IsPlayer)
            return false;
        _entities.Remove(id);
        foreach (var npc in _entities.Values.Where(n => n.TargetId == id))
            Disengage(npc);
        return true;
    }

    public Entity? Get(int id) => _entities.GetValueOrDefault(id);

    /// <summary>
    /// Validates a player's reported position: refused when faster than <see cref="MaxPlayerSpeed"/>
    /// since the last accepted move (teleport hacks, lag spikes are corrected, not trusted).
    /// Returns null when accepted, otherwise the reason (the position stays the last valid one).
    /// An accepted move onto a zone line queues <see cref="CrossedZoneLine"/>.
    /// </summary>
    public string? MovePlayer(int id, Vec3 to, float heading)
    {
        if (!_entities.TryGetValue(id, out var player) || !player.IsPlayer)
            return "not in zone";
        double elapsed = Math.Max(_time - player.LastMoveTime, 0.05);
        float distance = MathF.Sqrt(Distance2(player.Position, to));
        if (distance > MaxPlayerSpeed * elapsed + MoveTolerance)
            return $"moved {distance:0} units in {elapsed:0.00} s";
        player.Position = to;
        player.Heading = heading;
        player.LastMoveTime = _time;
        player.Moved = true;
        foreach (var line in _lines)
        {
            float dx = to.X - line.Position.X, dy = to.Y - line.Position.Y;
            if (dx * dx + dy * dy <= line.Range * line.Range && MathF.Abs(to.Z - line.Position.Z) <= Math.Max(line.Range, 20f))
            {
                var destination = new Vec3(line.KeepX ? to.X : line.Target.X, line.KeepY ? to.Y : line.Target.Y, line.Target.Z);
                _events.Add(new CrossedZoneLine(id, line, destination));
                break;
            }
        }
        return null;
    }

    /// <summary>
    /// An NPC dies (combat is M5; GM commands and tests call this). Its spawn point respawns after
    /// spawn2.respawntime shortened by up to `variance` percent (Spawn2::resetTimer; the legacy
    /// direction roll, rand()%50 &lt; 50, always shortens, and the rewrite keeps it).
    /// </summary>
    public bool Kill(int npcId)
    {
        if (!_entities.TryGetValue(npcId, out var npc) || npc.IsPlayer)
            return false;
        _entities.Remove(npcId);
        _events.Add(new Removed(npcId));
        if (npc.Spawn is { } spawn)
            _respawns.Add((spawn, _time + RespawnDelay(spawn)));
        return true;
    }

    public double RespawnDelay(SpawnPoint spawn)
    {
        double seconds = spawn.RespawnSeconds;
        if (spawn.Variance > 0)
            seconds -= seconds * _random.Next(spawn.Variance) / 100.0;
        return seconds;
    }

    /// <summary>Spawns, deaths, aggro and zone line crossings since the last call.</summary>
    public IReadOnlyList<ZoneEvent> DrainEvents()
    {
        var events = _events.ToList();
        _events.Clear();
        return events;
    }

    /// <summary>Advances the zone by <paramref name="seconds"/> and returns what moved since the last tick.</summary>
    public IReadOnlyList<Entity> Tick(float seconds)
    {
        _time += seconds;
        TickCount++;

        for (int i = _respawns.Count - 1; i >= 0; i--)
            if (_respawns[i].At <= _time)
            {
                var spawn = _respawns[i].Spawn;
                _respawns.RemoveAt(i);
                SpawnAt(spawn);
            }

        foreach (var e in _entities.Values.ToList())
        {
            if (e.IsPlayer)
                continue;
            if (e.TargetId is int targetId)
            {
                Chase(e, targetId, seconds);
                continue;
            }
            e.ScanIn -= seconds;
            if (e.ScanIn <= 0)
            {
                e.ScanIn += AggroRules.ScanSeconds;
                if (FindAggroTarget(e) is { } target)
                {
                    e.TargetId = target.Id;
                    _events.Add(new Engaged(e.Id, target.Id));
                    continue;
                }
            }
            if (e.Walker is null)
                continue;
            if (e.PauseLeft > 0)
            {
                e.PauseLeft -= seconds;
                continue;
            }
            var before = e.Position;
            int waypoint = e.Walker.TargetIndex;
            bool arrived = e.Walker.Step(e.Speed * seconds);
            e.Position = e.Walker.Position;
            e.Heading = Heading(before, e.Position, e.Heading);
            e.Moved = true;
            if (arrived && e.Grid is not null)
                e.PauseLeft = Math.Max(e.Grid.Waypoints[waypoint].PauseSeconds, 0);
        }

        var moved = _entities.Values.Where(e => e.Moved).ToList();
        foreach (var e in moved)
            e.Moved = false;
        return moved;
    }

    private Entity? FindAggroTarget(Entity npc)
    {
        Entity? best = null;
        float bestD2 = float.MaxValue;
        foreach (var p in _entities.Values)
        {
            if (!p.IsPlayer)
                continue;
            var r2 = AggroRules.RadiusSquared(Factions.Standing(p, npc.Npc!), p.Level, npc.Level, p.Sitting, npc.Npc!.Undead);
            float d2 = Distance2(p.Position, npc.Position);
            if (r2 is null || d2 > r2 || d2 >= bestD2)
                continue;
            if (Mesh is not null && !Mesh.LineOfSight(Eye(npc.Position), Eye(p.Position)))
                continue;
            best = p;
            bestD2 = d2;
        }
        return best;
    }

    private void Chase(Entity npc, int targetId, float seconds)
    {
        if (!_entities.TryGetValue(targetId, out var target))
        {
            Disengage(npc);
            return;
        }
        float dx = target.Position.X - npc.Position.X, dy = target.Position.Y - npc.Position.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= MeleeRange)
            return;
        float step = MathF.Min(npc.Npc!.RunUnitsPerSecond * seconds, distance - MeleeRange);
        float x = npc.Position.X + dx / distance * step, y = npc.Position.Y + dy / distance * step;
        float z = Mesh?.GroundZ(x, y, npc.Position.Z, 5f) is float g && g >= npc.Position.Z - 15f ? g : npc.Position.Z;
        var before = npc.Position;
        npc.Position = new Vec3(x, y, z);
        npc.Heading = Heading(before, npc.Position, npc.Heading);
        npc.Moved = true;
    }

    private void Disengage(Entity npc)
    {
        npc.TargetId = null;
        // Back to the grid from where the chase ended (the legacy NPC walks home, then resumes).
        if (npc.Grid is not null)
        {
            npc.Walker = new WaypointWalker(npc.Grid.Waypoints.Select(w => w.Position).ToList(), Mesh, npc.Grid.Type, ClosestWaypoint(npc.Grid, npc.Position));
            npc.Moved = true;
        }
    }

    private void SpawnAt(SpawnPoint spawn)
    {
        if (Pick(spawn.Candidates, _random) is not { } npc)
            return;
        var start = spawn.Position;
        if (Mesh?.GroundZ(start.X, start.Y, start.Z, 5f) is float ground)
            start = start with { Z = ground };
        var entity = Add(npc.Name, false, npc.Race, npc.Gender, npc.Level, npc.Size, start, spawn.Heading);
        entity.Npc = npc;
        entity.Spawn = spawn;
        entity.Speed = npc.WalkUnitsPerSecond;
        entity.ScanIn = (float)_random.NextDouble() * AggroRules.ScanSeconds; // legacy: random first scan
        if (spawn.GridId > 0 && _grids.TryGetValue(spawn.GridId, out var grid) && grid.Waypoints.Count >= 2)
        {
            entity.Grid = grid;
            entity.Walker = new WaypointWalker(grid.Waypoints.Select(w => w.Position).ToList(), Mesh, grid.Type, ClosestWaypoint(grid, start));
            entity.Position = entity.Walker.Position;
        }
        _events.Add(new Spawned(entity));
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
            float d = Distance2(grid.Waypoints[i].Position with { Z = p.Z }, p);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    private static Vec3 Eye(Vec3 p) => p with { Z = p.Z + 5f };

    private static float Distance2(Vec3 a, Vec3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);

    // Degrees; 0 faces +Y.
    private static float Heading(Vec3 from, Vec3 to, float previous)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        return dx * dx + dy * dy < 1e-6f ? previous : MathF.Atan2(dx, dy) * 180f / MathF.PI;
    }
}
