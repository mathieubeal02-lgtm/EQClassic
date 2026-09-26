using EQClassic.Shared.World;
using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>An NPC type as the zone needs it (legacy npc_types_without, the table the C++ zone reads).</summary>
public sealed record NpcTemplate(int Id, string Name, int Race, int Gender, int Level, float Size, float WalkSpeed = NpcTemplate.DefaultWalkSpeed)
{
    /// <summary>Legacy NPCs walk at walkspeed 0.7 (Mob::GetWalkSpeed; the charm helper in client.cpp uses it too).</summary>
    public const float DefaultWalkSpeed = 0.7f;

    /// <summary>Legacy NPC::CheckMyWalkingStatus: animation = walkspeed * 4, 2.3 units per second per animation step.</summary>
    public float WalkUnitsPerSecond => WalkSpeed * 4f * 2.3f;
}

/// <summary>A spawn2 row: a place, its spawn group's candidates (spawnentry, chance) and its waypoint grid.</summary>
public sealed record SpawnPoint(int Id, Vec3 Position, float Heading, int GridId, IReadOnlyList<(NpcTemplate Npc, int Chance)> Candidates);

public sealed record Waypoint(Vec3 Position, int PauseSeconds);

/// <summary>A grid (grid + grid_entries by number).</summary>
public sealed record Grid(int Id, GridType Type, IReadOnlyList<Waypoint> Waypoints);

public sealed record ZoneData(string ShortName, IReadOnlyList<SpawnPoint> Spawns, IReadOnlyDictionary<int, Grid> Grids);

public interface IZoneDataSource
{
    /// <summary>Null when the zone is unknown.</summary>
    ZoneData? Load(string shortName);
}

public sealed class InMemoryZoneDataSource : IZoneDataSource
{
    public Dictionary<string, ZoneData> Zones { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ZoneData? Load(string shortName) => Zones.GetValueOrDefault(shortName);
}

/// <summary>Loads a zone the way the legacy zone does (spawn2 + spawnentry + npc_types_without, grid + grid_entries).</summary>
public sealed class MySqlZoneDataSource : IZoneDataSource
{
    private readonly string _connectionString;
    private readonly string _p;

    public MySqlZoneDataSource(string connectionString, string tablePrefix = "")
    {
        _connectionString = connectionString;
        _p = tablePrefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? tablePrefix : throw new ArgumentException("must be a plain SQL identifier prefix", nameof(tablePrefix));
    }

    public ZoneData? Load(string shortName)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();

        int? zoneId = null;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT zoneidnumber FROM `{_p}zone_ids` WHERE short_name = @zone";
            cmd.Parameters.AddWithValue("@zone", shortName);
            if (cmd.ExecuteScalar() is { } v and not DBNull)
                zoneId = Convert.ToInt32(v);
        }
        if (zoneId is null)
            return null;

        var spawns = new Dictionary<int, (Vec3 Pos, float Heading, int Grid, List<(NpcTemplate, int)> Candidates)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT s.id, s.x, s.y, s.z, s.heading, s.pathgrid, n.id, n.name, n.race, n.gender, n.level, n.size, e.chance
                FROM `{_p}spawn2` s
                JOIN `{_p}spawnentry` e ON e.spawngroupID = s.spawngroupID
                JOIN `{_p}npc_types_without` n ON n.id = e.npcID
                WHERE s.zone = @zone
                ORDER BY s.id, e.npcID
                """;
            cmd.Parameters.AddWithValue("@zone", shortName);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = r.GetInt32(0);
                if (!spawns.TryGetValue(id, out var spawn))
                    spawns[id] = spawn = (new Vec3(r.GetFloat(1), r.GetFloat(2), r.GetFloat(3)), r.GetFloat(4), r.GetInt32(5), new List<(NpcTemplate, int)>());
                float size = Convert.ToSingle(r.GetValue(11));
                var npc = new NpcTemplate(r.GetInt32(6), r.GetString(7), Convert.ToInt32(r.GetValue(8)), Convert.ToInt32(r.GetValue(9)),
                    Convert.ToInt32(r.GetValue(10)), size > 0 ? size : 6f);
                spawn.Candidates.Add((npc, Convert.ToInt32(r.GetValue(12))));
            }
        }

        var gridTypes = new Dictionary<int, int>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT id, type FROM `{_p}grid` WHERE zoneid = @zone";
            cmd.Parameters.AddWithValue("@zone", zoneId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                gridTypes[r.GetInt32(0)] = r.GetInt32(1);
        }
        var points = new Dictionary<int, List<Waypoint>>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT gridid, x, y, z, pause FROM `{_p}grid_entries` WHERE zoneid = @zone ORDER BY gridid, number";
            cmd.Parameters.AddWithValue("@zone", zoneId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int grid = r.GetInt32(0);
                if (!points.TryGetValue(grid, out var list))
                    points[grid] = list = new List<Waypoint>();
                list.Add(new Waypoint(new Vec3(r.GetFloat(1), r.GetFloat(2), r.GetFloat(3)), r.GetInt32(4)));
            }
        }
        var grids = points.ToDictionary(kv => kv.Key,
            kv => new Grid(kv.Key, gridTypes.GetValueOrDefault(kv.Key, 3) == 0 ? GridType.Circular : GridType.BackAndForth, kv.Value));

        return new ZoneData(shortName,
            spawns.Select(kv => new SpawnPoint(kv.Key, kv.Value.Pos, kv.Value.Heading, kv.Value.Grid, kv.Value.Candidates)).ToList(),
            grids);
    }
}
