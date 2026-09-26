using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Tests.Characters;
using MySqlConnector;

namespace EQClassic.Tests.Accounts;

/// <summary>Zone loading SQL against MariaDB: prefixed test tables, and the live Qeynos when present.</summary>
public sealed class MySqlZoneDataTests : IDisposable
{
    private readonly string _prefix = "rwtest_" + Guid.NewGuid().ToString("N")[..8] + "_";
    private readonly bool _created;

    public MySqlZoneDataTests()
    {
        if (DbFactAttribute.ConnectionString is not { Length: > 0 } cs)
            return;
        Execute(cs, $"CREATE TABLE `{_prefix}zone_ids` (zoneidnumber int, short_name varchar(32))");
        Execute(cs, $"INSERT INTO `{_prefix}zone_ids` VALUES (2, 'qeynos2')");
        Execute(cs, $"CREATE TABLE `{_prefix}spawn2` (id int, spawngroupID int, zone varchar(16), x float, y float, z float, heading float, pathgrid int, respawntime int, variance int)");
        Execute(cs, $"INSERT INTO `{_prefix}spawn2` VALUES (10, 100, 'qeynos2', 134, 10, 3.75, 64, 7, 1200, 10), (11, 101, 'qeynos2', 1, 2, 3, 0, 0, 640, 0), (12, 102, 'qeynos', 0, 0, 0, 0, 0, 640, 0)");
        Execute(cs, $"CREATE TABLE `{_prefix}spawnentry` (spawngroupID int, npcID int, chance int)");
        Execute(cs, $"INSERT INTO `{_prefix}spawnentry` VALUES (100, 2007, 100), (101, 1, 50), (101, 2, 50), (102, 1, 100)");
        Execute(cs, $"CREATE TABLE `{_prefix}npc_types_without` (id int, name varchar(64), race int, gender int, level int, size float, runspeed float, bodytype int, npc_faction_id int, " +
                    "class int, hp int, mindmg int, maxdmg int, AC smallint, ATK int, Accuracy int, avoidance int, attack_speed float, STR int)");
        Execute(cs, $"INSERT INTO `{_prefix}npc_types_without` VALUES " +
                    "(2007, 'Guard_Hewet', 71, 0, 10, 6, 1.25, 1, 219, 1, 350, 1, 12, 15, 0, 0, 0, -25, 90), " +
                    "(1, 'a_rat', 36, 2, 1, -1, 1.3, 21, 0, 1, 16, 1, 4, 5, 0, 0, 0, 0, 75), (2, 'a_snake', 37, 2, 2, 3, 0, 3, 0, 1, 32, 1, 6, 8, 0, 0, 0, 0, 75)");
        Execute(cs, $"CREATE TABLE `{_prefix}zone_points` (id int, zone varchar(16), x float, y float, z float, target_zone varchar(16), target_x float, target_y float, target_z float, Zrange int, keepX int, keepY int)");
        Execute(cs, $"INSERT INTO `{_prefix}zone_points` VALUES (977, 'qeynos2', 2.66, -148.38, 2.13, 'qeynos', -410.68, 456.42, 2.13, 8, 0, 0), (7, 'qeynos2', 73, 1350, 2.5, 'qeytoqrg', 95, -380, 0, 5, 1, 0), (1, 'qeynos', 0, 0, 0, 'qeynos2', 0, 0, 0, 5, 0, 0)");
        // Same types as the live doors table (dest_zone may be NULL).
        Execute(cs, $"CREATE TABLE `{_prefix}doors` (id int, doorid smallint, zone varchar(16), name varchar(16), pos_y float, pos_x float, pos_z float, heading float, " +
                    "opentype smallint, size smallint unsigned, triggerdoor smallint, keyitem int, lockpick smallint, dest_zone varchar(16), dest_x float, dest_y float, dest_z float)");
        Execute(cs, $"INSERT INTO `{_prefix}doors` VALUES (1, 8, 'qeynos2', 'DOOR1', 324.921, 279.858, 0.001, 128, 0, 100, 0, 0, 0, 'NONE', 0, 0, 0), " +
                    "(2, 30, 'qeynos2', 'KEYDOOR', 10, 20, 0, 0, 0, 100, 8, 12345, 0, NULL, NULL, NULL, NULL), (3, 1, 'qeynos', 'DOOR2', 0, 0, 0, 0, 0, 100, 0, 0, 0, 'NONE', 0, 0, 0)");
        Execute(cs, $"CREATE TABLE `{_prefix}grid` (id int, zoneid int, type int)");
        Execute(cs, $"INSERT INTO `{_prefix}grid` VALUES (7, 2, 3), (8, 2, 0)");
        Execute(cs, $"CREATE TABLE `{_prefix}grid_entries` (gridid int, zoneid int, number int, x float, y float, z float, pause int)");
        Execute(cs, $"INSERT INTO `{_prefix}grid_entries` VALUES (7, 2, 2, 144, 23, 3.75, 0), (7, 2, 1, 134, 10, 3.75, 5), (8, 2, 1, 0, 0, 0, 0), (8, 2, 2, 1, 1, 0, 0), (7, 1, 1, 9, 9, 9, 0)");
        _created = true;
    }

    public void Dispose()
    {
        if (_created)
            Execute(DbFactAttribute.ConnectionString!, $"DROP TABLE IF EXISTS `{_prefix}zone_ids`, `{_prefix}spawn2`, `{_prefix}spawnentry`, `{_prefix}npc_types_without`, `{_prefix}grid`, `{_prefix}grid_entries`, `{_prefix}zone_points`, `{_prefix}doors`");
    }

    [DbFact]
    public void Loads_spawns_candidates_and_grids_of_one_zone()
    {
        var data = new MySqlZoneDataSource(DbFactAttribute.ConnectionString!, _prefix).Load("qeynos2")!;

        Assert.Equal([10, 11], data.Spawns.Select(s => s.Id).OrderBy(i => i));
        var guard = data.Spawns.Single(s => s.Id == 10);
        Assert.Equal((new Vec3(134, 10, 3.75f), 7), (guard.Position, guard.GridId));
        Assert.Equal("Guard_Hewet", Assert.Single(guard.Candidates).Npc.Name);
        Assert.Equal(6f, data.Spawns.Single(s => s.Id == 11).Candidates.Single(c => c.Npc.Name == "a_rat").Npc.Size); // size -1 → default
        var grid = data.Grids[7];
        Assert.Equal(GridType.BackAndForth, grid.Type);
        Assert.Equal([new Vec3(134, 10, 3.75f), new Vec3(144, 23, 3.75f)], grid.Waypoints.Select(w => w.Position)); // ordered by number
        Assert.Equal(5, grid.Waypoints[0].PauseSeconds);
        Assert.Equal(GridType.Circular, data.Grids[8].Type);
        Assert.Equal((1200, 10), (guard.RespawnSeconds, guard.Variance));
        var hewet = Assert.Single(guard.Candidates).Npc;
        Assert.Equal((1.25f, false, 219), (hewet.RunSpeed, hewet.Undead, hewet.PrimaryFaction));
        Assert.Equal(new NpcCombatStats(1, 350, 1, 12, 15, 0, 0, 0, -25, 90), hewet.Combat);
        Assert.Equal(1.5f, hewet.Combat.DelaySeconds); // 2 s × (100 - 25) / 100
        var snake = data.Spawns.Single(s => s.Id == 11).Candidates.Single(c => c.Npc.Name == "a_snake").Npc;
        Assert.Equal((1.25f, true), (snake.RunSpeed, snake.Undead)); // runspeed 0 → default; bodytype 3 = undead
        Assert.Equal(2, data.Lines.Count);
        Assert.Equal([8, 30], data.Doors.Select(d => d.Id));
        var door = data.Doors[0];
        Assert.Equal(("DOOR1", new Vec3(279.858f, 324.921f, 0.001f), 128f, false, false), (door.Name, door.Position, door.Heading, door.Locked, door.Teleports));
        var keyDoor = data.Doors[1];
        Assert.True(keyDoor.Locked);
        Assert.Equal(8, keyDoor.TriggerDoor);
        Assert.False(keyDoor.Teleports); // NULL dest_zone
        var toQrg = data.Lines.Single(l => l.TargetZone == "qeytoqrg");
        Assert.Equal((5f, true, false), (toQrg.Range, toQrg.KeepX, toQrg.KeepY));
    }

    [DbFact]
    public void Unknown_zone_is_null() => Assert.Null(new MySqlZoneDataSource(DbFactAttribute.ConnectionString!, _prefix).Load("atlantis"));

    /// <summary>
    /// M3 acceptance on real data, when this machine has the live database and a Lantern export of
    /// Qeynos (tools/lantern/extract.sh): two simulated minutes of the whole zone. No NPC ever pops
    /// up more than a stair step in one tick (the legacy "guards fall from the sky" bug started with
    /// a pop onto a roof), and none drops further than a ledge (grids sometimes cut straight over
    /// the edge of a floor, e.g. Nax_Ghruna's way down to the sewers, a 13-unit step).
    /// </summary>
    [DbFact]
    public void Live_qeynos_guards_stay_on_the_ground()
    {
        var mesh = Path.Combine(PlayerProfileTests.RepoRoot(), "build", "lantern-work", "Exports", "qeynos2", "Zone", "Meshes", "qeynos2_collision.txt");
        if (!File.Exists(mesh) || !TableExists("spawn2") || !TableExists("npc_types_without"))
            return;
        var data = new MySqlZoneDataSource(DbFactAttribute.ConnectionString!).Load("qeynos2");
        if (data is null || data.Spawns.Count == 0)
            return;

        var exports = Path.Combine(PlayerProfileTests.RepoRoot(), "build", "lantern-work", "Exports");
        var zone = new ZoneInstance(data, ZoneCollisionMesh.LoadLanternZone(exports, "qeynos2")); // as the server loads it
        var last = zone.Entities.ToDictionary(e => e.Id, e => e.Position.Z);
        (float Up, string UpName, float Down, string DownName) worst = (0, "", 0, "");
        for (int i = 0; i < 20 * 120; i++)
        {
            foreach (var e in zone.Tick(0.05f))
            {
                float step = e.Position.Z - last[e.Id];
                if (step > worst.Up) worst = (step, e.Name, worst.Down, worst.DownName);
                if (-step > worst.Down) worst = (worst.Up, worst.UpName, -step, e.Name);
                last[e.Id] = e.Position.Z;
            }
        }
        Assert.True(zone.Entities.Count(e => e.Name.Contains("Guard")) >= 5);
        Assert.True(worst.Up <= 6f, $"{worst.UpName} popped up {worst.Up:0.0} units in one tick");
        Assert.True(worst.Down <= 15f, $"{worst.DownName} dropped {worst.Down:0.0} units in one tick");
    }

    private static bool TableExists(string table)
    {
        using var c = new MySqlConnection(DbFactAttribute.ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @t";
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static void Execute(string cs, string sql)
    {
        using var c = new MySqlConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
