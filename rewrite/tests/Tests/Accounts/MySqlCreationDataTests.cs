using EQClassic.Server.Characters;
using MySqlConnector;

namespace EQClassic.Tests.Accounts;

/// <summary>The creation rules' SQL against MariaDB, on prefixed copies of the legacy tables.</summary>
public sealed class MySqlCreationDataTests : IDisposable
{
    private readonly string _prefix = "rwtest_" + Guid.NewGuid().ToString("N")[..8] + "_";
    private readonly bool _created;

    public MySqlCreationDataTests()
    {
        if (DbFactAttribute.ConnectionString is not { Length: > 0 } cs)
            return;
        Execute(cs, $"CREATE TABLE `{_prefix}name_filter` (name varchar(30) NOT NULL) DEFAULT CHARSET=latin1");
        Execute(cs, $"INSERT INTO `{_prefix}name_filter` VALUES ('banana'), ('drizzt'), ('Kamin')");
        Execute(cs, $"CREATE TABLE `{_prefix}zone_ids` (zoneidnumber int, short_name varchar(32)) DEFAULT CHARSET=latin1");
        Execute(cs, $"INSERT INTO `{_prefix}zone_ids` VALUES (52, 'grobb'), (40, 'neriaka')");
        Execute(cs, $"CREATE TABLE `{_prefix}start_zones` (x float, y float, z float, zone_id int, player_class int, player_race int, player_deity int) DEFAULT CHARSET=latin1");
        Execute(cs, $"INSERT INTO `{_prefix}start_zones` VALUES (-366.4, -581.9, 24.7, 40, 10, 9, 203), (10, 20, 30, 52, 10, 9, 203), (11, 21, 31, 52, 10, 9, 206)");
        Execute(cs, $"CREATE TABLE `{_prefix}starting_items` (id int AUTO_INCREMENT PRIMARY KEY, race int, class int, itemid int) DEFAULT CHARSET=latin1");
        Execute(cs, $"INSERT INTO `{_prefix}starting_items` (race, class, itemid) VALUES (0,0,9990), (0,0,9991), (0,10,9999), (9,10,18791), (1,1,5)");
        _created = true;
    }

    public void Dispose()
    {
        if (_created)
            Execute(DbFactAttribute.ConnectionString!, $"DROP TABLE IF EXISTS `{_prefix}name_filter`, `{_prefix}zone_ids`, `{_prefix}start_zones`, `{_prefix}starting_items`");
    }

    private MySqlCreationData Data => new(DbFactAttribute.ConnectionString!, _prefix);

    [DbFact]
    public void Name_filter_matches_like_mysql()
    {
        Assert.True(Data.IsNameFiltered("Drizzt"));
        Assert.True(Data.IsNameFiltered("kamin"));
        Assert.False(Data.IsNameFiltered("Qbot"));
    }

    [DbFact]
    public void Start_position_is_the_last_match_in_whole_units()
    {
        Assert.Equal((11f, 21f, 31f), Data.StartPosition("grobb", 9, 10));
        Assert.Equal((-366f, -581f, 24f), Data.StartPosition("neriaka", 9, 10));
        Assert.Null(Data.StartPosition("qeynos", 9, 10));
    }

    [DbFact]
    public void Starting_items_by_race_and_class_in_id_order()
    {
        Assert.Equal([9990, 9991, 9999, 18791], Data.StartingItems(9, 10));
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
