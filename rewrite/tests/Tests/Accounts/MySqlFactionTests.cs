using EQClassic.Server.Zone;
using MySqlConnector;

namespace EQClassic.Tests.Accounts;

/// <summary>The faction tables of the live database (skipped when they are not there).</summary>
public class MySqlFactionTests
{
    private static bool TablesExist()
    {
        using var c = new MySqlConnection(DbFactAttribute.ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() " +
                          "AND table_name IN ('faction_list', 'npc_faction', 'npc_faction_entries')";
        return Convert.ToInt64(cmd.ExecuteScalar()) == 3;
    }

    [DbFact]
    public void Blackburrow_gnolls_hate_everyone_and_their_death_pleases_qeynos()
    {
        if (!TablesExist())
            return;
        var data = new MySqlFactionData(DbFactAttribute.ConnectionString!);
        var sabertooths = data.Faction(279)!;
        Assert.Equal(("Sabertooths of Blackburrow", -900), (sabertooths.Name, sabertooths.Base));
        Assert.Equal(-900 - 200, sabertooths.Modifiers(10, 9, 140)); // a troll shaman
        var gnoll = data.NpcList(567)!;
        Assert.Equal(279, gnoll.PrimaryFaction);
        Assert.Contains((279, -30), gnoll.Hits);
        Assert.Contains((135, 10), gnoll.Hits);
        Assert.Equal(1500, data.Faction(135)!.Modifiers(1, 1, 140)); // humans and the Guards of Qeynos
    }
}
