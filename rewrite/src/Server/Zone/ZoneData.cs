using EQClassic.Shared.World;
using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>An NPC type as the zone needs it (legacy npc_types_without, the table the C++ zone reads).</summary>
public sealed record NpcTemplate(int Id, string Name, int Race, int Gender, int Level, float Size, float WalkSpeed = NpcTemplate.DefaultWalkSpeed,
    float RunSpeed = 1.25f, bool Undead = false, int PrimaryFaction = 0)
{
    /// <summary>Legacy NPCs walk at walkspeed 0.7 (Mob::GetWalkSpeed; the charm helper in client.cpp uses it too).</summary>
    public const float DefaultWalkSpeed = 0.7f;

    /// <summary>Legacy NPC::CheckMyWalkingStatus: animation = walkspeed * 4, 2.3 units per second per animation step.</summary>
    public float WalkUnitsPerSecond => WalkSpeed * 4f * 2.3f;

    /// <summary>Engaged NPCs run: animation = runspeed * 7 (NPC::CheckMyWalkingStatus), 2.3 units per step.</summary>
    public float RunUnitsPerSecond => RunSpeed * 7f * 2.3f;

    /// <summary>Melee statistics (npc_types_without columns); tests without them get level-based defaults.</summary>
    public NpcCombatStats Combat { get; init; } = NpcCombatStats.Default(Level);

    /// <summary>What it drops when it dies (npc_types_without.loottable_id; 0: nothing).</summary>
    public int LoottableId { get; init; }
    /// <summary>Merchants: their merchantlist (npc_types_without.merchant_id; 0: none).</summary>
    public int MerchantId { get; init; }
    /// <summary>Hit points per tic (hp_regen_rate), when above the level's regeneration.</summary>
    public int RegenRate { get; init; }
}

/// <summary>
/// The npc_types_without columns melee combat reads (NPC::NPC, Mob::Combat*): class, hit points,
/// min/max damage, AC, ATK, accuracy, avoidance bonus, attack speed (percent: delay = 2 s × (100 +
/// speed) / 100, negative is faster) and STR; the resists spells read (MR, CR, DR, FR, PR).
/// </summary>
public sealed record NpcCombatStats(int Class, int Hp, int MinDamage, int MaxDamage, int AC = 0, int Atk = 0, int Accuracy = 0,
    int Avoidance = 0, int AttackSpeed = 0, int Str = 75)
{
    public int MR { get; init; }
    public int CR { get; init; }
    public int DR { get; init; }
    public int FR { get; init; }
    public int PR { get; init; }

    public static NpcCombatStats Default(int level) => new(1, Math.Max(1, level * 10 + 6), 1, Math.Max(2, level * 2 + 2));

    /// <summary>Seconds between swings (Mob::Mob: 2000 ms × (100 + attack_speed) / 100).</summary>
    public float DelaySeconds => Math.Max(0.2f, 2f * (100 + AttackSpeed) / 100f);
}

/// <summary>A spawn2 row: a place, its spawn group's candidates (spawnentry, chance) and its waypoint grid.</summary>
public sealed record SpawnPoint(int Id, Vec3 Position, float Heading, int GridId, IReadOnlyList<(NpcTemplate Npc, int Chance)> Candidates,
    int RespawnSeconds = 640, int Variance = 0);

/// <summary>
/// A zone_points row: a player within Range (Zrange) of Position goes to TargetZone at Target;
/// KeepX/KeepY keep that coordinate of the player instead (the lines up between zones).
/// </summary>
public sealed record ZoneLine(int Id, Vec3 Position, float Range, string TargetZone, Vec3 Target, bool KeepX = false, bool KeepY = false);

public sealed record Waypoint(Vec3 Position, int PauseSeconds);

/// <summary>A grid (grid + grid_entries by number).</summary>
public sealed record Grid(int Id, GridType Type, IReadOnlyList<Waypoint> Waypoints);

/// <summary>
/// A doors row (legacy Door_Struct, Client::ProcessOP_ClickDoor). Heading in the table's 0-512
/// units, Size in percent. A door with a KeyItem or Lockpick is locked; one with a DestZone
/// teleports whoever uses it; TriggerDoor moves another door with it.
/// </summary>
public sealed record Door(int Id, string Name, Vec3 Position, float Heading, int OpenType, int Size = 100,
    int TriggerDoor = 0, int KeyItem = 0, int Lockpick = 0, string? DestZone = null, Vec3 Destination = default)
{
    public const int InvisibleOpenType = 54;
    public bool Locked => KeyItem > 0 || Lockpick > 0;
    public bool Teleports => !string.IsNullOrEmpty(DestZone) && DestZone != "NONE";
}

public sealed record ZoneData(string ShortName, IReadOnlyList<SpawnPoint> Spawns, IReadOnlyDictionary<int, Grid> Grids,
    IReadOnlyList<ZoneLine>? ZoneLines = null, IReadOnlyList<Door>? ZoneDoors = null)
{
    public IReadOnlyList<ZoneLine> Lines => ZoneLines ?? Array.Empty<ZoneLine>();
    public IReadOnlyList<Door> Doors => ZoneDoors ?? Array.Empty<Door>();
    public ZoneRules Rules { get; init; } = ZoneRules.Anything;
    /// <summary>zone.weather: 0 none, 1 rain, 2 snow.</summary>
    public int Weather { get; init; }
}

/// <summary>
/// A zone_rules row (Database::LoadZoneRules): who may bind here (0 nobody, 1 only yourself,
/// 2 anyone, as in the cities), whether levitation works, whether outdoor spells (movement speed,
/// levitation, harmony) may be cast.
/// </summary>
public sealed record ZoneRules(int CanBind, bool CanLevitate, bool Outdoor)
{
    public static readonly ZoneRules Anything = new(2, true, true);
}

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
    private static int Int(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));

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

        var spawns = new Dictionary<int, (Vec3 Pos, float Heading, int Grid, int Respawn, int Variance, List<(NpcTemplate, int)> Candidates)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT s.id, s.x, s.y, s.z, s.heading, s.pathgrid, n.id, n.name, n.race, n.gender, n.level, n.size, e.chance,
                       s.respawntime, s.variance, n.runspeed, n.bodytype, n.npc_faction_id,
                       n.class, n.hp, n.mindmg, n.maxdmg, n.AC, n.ATK, n.Accuracy, n.avoidance, n.attack_speed, n.STR, n.loottable_id,
                       n.MR, n.CR, n.DR, n.FR, n.PR, n.merchant_id, n.hp_regen_rate
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
                    spawns[id] = spawn = (new Vec3(r.GetFloat(1), r.GetFloat(2), r.GetFloat(3)), r.GetFloat(4), r.GetInt32(5),
                        Convert.ToInt32(r.GetValue(13)), Convert.ToInt32(r.GetValue(14)), new List<(NpcTemplate, int)>());
                float size = Convert.ToSingle(r.GetValue(11));
                float runspeed = Convert.ToSingle(r.GetValue(15));
                var npc = new NpcTemplate(r.GetInt32(6), r.GetString(7), Convert.ToInt32(r.GetValue(8)), Convert.ToInt32(r.GetValue(9)),
                    Convert.ToInt32(r.GetValue(10)), size > 0 ? size : 6f,
                    RunSpeed: runspeed > 0 ? runspeed : 1.25f,
                    Undead: Convert.ToInt32(r.GetValue(16)) == 3, // BT_Undead
                    PrimaryFaction: Convert.ToInt32(r.GetValue(17)))
                {
                    Combat = new NpcCombatStats(Int(r, 18), Math.Max(1, Int(r, 19)), Int(r, 20), Int(r, 21), Int(r, 22), Int(r, 23),
                        Int(r, 24), Int(r, 25), Int(r, 26), Int(r, 27))
                    {
                        MR = Int(r, 29), CR = Int(r, 30), DR = Int(r, 31), FR = Int(r, 32), PR = Int(r, 33),
                    },
                    LoottableId = Int(r, 28),
                    MerchantId = Int(r, 34),
                    RegenRate = Int(r, 35),
                };
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
        var lines = new List<ZoneLine>();
        using (var cmd = connection.CreateCommand())
        {
            // Same columns as Database::loadZoneLines (range = Zrange).
            cmd.CommandText = $"SELECT id, x, y, z, target_zone, target_x, target_y, target_z, Zrange, keepX, keepY FROM `{_p}zone_points` WHERE zone = @zone";
            cmd.Parameters.AddWithValue("@zone", shortName);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                lines.Add(new ZoneLine(r.GetInt32(0), new Vec3(r.GetFloat(1), r.GetFloat(2), r.GetFloat(3)), Convert.ToSingle(r.GetValue(8)),
                    r.GetString(4), new Vec3(r.GetFloat(5), r.GetFloat(6), r.GetFloat(7)), Convert.ToInt32(r.GetValue(9)) == 1, Convert.ToInt32(r.GetValue(10)) == 1));
        }

        var doors = new List<Door>();
        using (var cmd = connection.CreateCommand())
        {
            // Database::LoadDoors reads name, position, heading, opentype and doorid; the click
            // handler also uses triggerdoor, keyitem, lockpick and the destination.
            cmd.CommandText = $"SELECT doorid, name, pos_x, pos_y, pos_z, heading, opentype, size, triggerdoor, keyitem, lockpick, " +
                              $"dest_zone, dest_x, dest_y, dest_z FROM `{_p}doors` WHERE zone = @zone ORDER BY doorid";
            cmd.Parameters.AddWithValue("@zone", shortName);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                doors.Add(new Door(Convert.ToInt32(r.GetValue(0)), r.GetString(1), new Vec3(r.GetFloat(2), r.GetFloat(3), r.GetFloat(4)),
                    r.GetFloat(5), Convert.ToInt32(r.GetValue(6)), Convert.ToInt32(r.GetValue(7)), Convert.ToInt32(r.GetValue(8)),
                    Convert.ToInt32(r.GetValue(9)), Convert.ToInt32(r.GetValue(10)), r.IsDBNull(11) ? null : r.GetString(11),
                    new Vec3(r.IsDBNull(12) ? 0 : r.GetFloat(12), r.IsDBNull(13) ? 0 : r.GetFloat(13), r.IsDBNull(14) ? 0 : r.GetFloat(14))));
        }

        int weather = 0;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT weather FROM `{_p}zone` WHERE short_name = @zone";
            cmd.Parameters.AddWithValue("@zone", shortName);
            try
            {
                if (cmd.ExecuteScalar() is { } w and not DBNull)
                    weather = Convert.ToInt32(w);
            }
            catch (MySqlException)
            {
                // no zone table: no weather
            }
        }

        var rules = ZoneRules.Anything;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT can_bind, can_lev, castoutdoor FROM `{_p}zone_rules` WHERE short_name = @zone";
            cmd.Parameters.AddWithValue("@zone", shortName);
            try
            {
                using var r = cmd.ExecuteReader();
                if (r.Read())
                    rules = new ZoneRules(Int(r, 0), Int(r, 1) != 0, Int(r, 2) != 0);
            }
            catch (MySqlException)
            {
                // no zone_rules table: no restriction
            }
        }

        var grids = points.ToDictionary(kv => kv.Key,
            kv => new Grid(kv.Key, gridTypes.GetValueOrDefault(kv.Key, 3) == 0 ? GridType.Circular : GridType.BackAndForth, kv.Value));

        return new ZoneData(shortName,
            spawns.Select(kv => new SpawnPoint(kv.Key, kv.Value.Pos, kv.Value.Heading, kv.Value.Grid, kv.Value.Candidates, kv.Value.Respawn, kv.Value.Variance)).ToList(),
            grids, lines, doors) { Rules = rules, Weather = weather };
    }
}
