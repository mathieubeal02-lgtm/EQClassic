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
        Execute(cs, $"CREATE TABLE `{_prefix}spawn2` (id int, spawngroupID int, zone varchar(16), x float, y float, z float, heading float, pathgrid int)");
        Execute(cs, $"INSERT INTO `{_prefix}spawn2` VALUES (10, 100, 'qeynos2', 134, 10, 3.75, 64, 7), (11, 101, 'qeynos2', 1, 2, 3, 0, 0), (12, 102, 'qeynos', 0, 0, 0, 0, 0)");
        Execute(cs, $"CREATE TABLE `{_prefix}spawnentry` (spawngroupID int, npcID int, chance int)");
        Execute(cs, $"INSERT INTO `{_prefix}spawnentry` VALUES (100, 2007, 100), (101, 1, 50), (101, 2, 50), (102, 1, 100)");
        Execute(cs, $"CREATE TABLE `{_prefix}npc_types_without` (id int, name varchar(64), race int, gender int, level int, size float)");
        Execute(cs, $"INSERT INTO `{_prefix}npc_types_without` VALUES (2007, 'Guard_Hewet', 71, 0, 10, 6), (1, 'a_rat', 36, 2, 1, -1), (2, 'a_snake', 37, 2, 2, 3)");
        Execute(cs, $"CREATE TABLE `{_prefix}grid` (id int, zoneid int, type int)");
        Execute(cs, $"INSERT INTO `{_prefix}grid` VALUES (7, 2, 3), (8, 2, 0)");
        Execute(cs, $"CREATE TABLE `{_prefix}grid_entries` (gridid int, zoneid int, number int, x float, y float, z float, pause int)");
        Execute(cs, $"INSERT INTO `{_prefix}grid_entries` VALUES (7, 2, 2, 144, 23, 3.75, 0), (7, 2, 1, 134, 10, 3.75, 5), (8, 2, 1, 0, 0, 0, 0), (8, 2, 2, 1, 1, 0, 0), (7, 1, 1, 9, 9, 9, 0)");
        _created = true;
    }

    public void Dispose()
    {
        if (_created)
            Execute(DbFactAttribute.ConnectionString!, $"DROP TABLE IF EXISTS `{_prefix}zone_ids`, `{_prefix}spawn2`, `{_prefix}spawnentry`, `{_prefix}npc_types_without`, `{_prefix}grid`, `{_prefix}grid_entries`");
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

        var zone = new ZoneInstance(data, ZoneCollisionMesh.LoadLantern(mesh));
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
