using EQClassic.Server.Accounts;
using EQClassic.Server.Characters;
using EQClassic.Tests.Characters;
using MySqlConnector;

namespace EQClassic.Tests.Accounts;

/// <summary>World account creation and character listing against MariaDB, on throwaway copies of account and character_.</summary>
public sealed class MySqlWorldStoresTests : IDisposable
{
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];
    private string AccountTable => "account_rwtest_" + _suffix;
    private string CharacterTable => "character_rwtest_" + _suffix;
    private readonly bool _created;

    public MySqlWorldStoresTests()
    {
        if (DbFactAttribute.ConnectionString is not { Length: > 0 } cs)
            return;
        Execute(cs, $"""
            CREATE TABLE `{AccountTable}` (
              `id` int(11) NOT NULL AUTO_INCREMENT,
              `name` varchar(255) NOT NULL DEFAULT '',
              `password` varchar(255) NOT NULL DEFAULT '',
              `lsadmin` tinyint(2) unsigned NOT NULL DEFAULT '0',
              `lsstatus` int(11) unsigned DEFAULT NULL,
              `worldadmin` tinyint(3) unsigned NOT NULL DEFAULT '0',
              `user_active` tinyint(4) unsigned NOT NULL DEFAULT '0',
              `minilogin_ip` varchar(255) DEFAULT NULL,
              `status` int(11) DEFAULT NULL,
              `lsaccount_id` int(11) DEFAULT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `name` (`name`)
            ) AUTO_INCREMENT=24 DEFAULT CHARSET=latin1
            """);
        Execute(cs, $"""
            CREATE TABLE `{CharacterTable}` (
              `id` int(11) NOT NULL AUTO_INCREMENT,
              `account_id` int(11) NOT NULL DEFAULT '0',
              `name` varchar(16) NOT NULL DEFAULT '',
              `profile` blob,
              PRIMARY KEY (`id`),
              UNIQUE KEY `name` (`name`)
            ) DEFAULT CHARSET=latin1
            """);
        _created = true;
    }

    public void Dispose()
    {
        if (_created)
            Execute(DbFactAttribute.ConnectionString!, $"DROP TABLE IF EXISTS `{AccountTable}`, `{CharacterTable}`");
    }

    [DbFact]
    public void World_account_is_created_once_then_found()
    {
        var store = new MySqlWorldAccountStore(DbFactAttribute.ConnectionString!, AccountTable);
        int id = store.ResolveOrCreate(16, "bot");
        Assert.Equal(24, id);
        Assert.Equal(24, store.ResolveOrCreate(16, "bot"));
        Assert.Equal(1L, Scalar($"SELECT COUNT(*) FROM `{AccountTable}` WHERE lsaccount_id = 16 AND name = 'bot' AND password = '' AND status = 0"));
    }

    [DbFact]
    public void Name_already_taken_by_another_login_account_fails_like_the_legacy_world()
    {
        var store = new MySqlWorldAccountStore(DbFactAttribute.ConnectionString!, AccountTable);
        Assert.NotEqual(0, store.ResolveOrCreate(16, "bot"));
        Assert.Equal(0, store.ResolveOrCreate(99, "BOT"));
    }

    [DbFact]
    public void Characters_are_listed_by_name_and_reserved_names_are_skipped()
    {
        var cs = DbFactAttribute.ConnectionString!;
        Insert(cs, 24, "Qbottwo", ProfileBuilder.Build("Qbottwo", 9, 10, 1, "grobb"));
        Insert(cs, 24, "Qbot", ProfileBuilder.Build("Qbot", 9, 10, 1, "innothule"));
        Insert(cs, 24, "Reserved", null);
        Insert(cs, 1, "Qtest", ProfileBuilder.Build("Qtest", 9, 10, 59, "permafrost"));

        var list = new MySqlCharacterStore(cs, CharacterTable).ListForAccount(24);

        Assert.Equal(["Qbot", "Qbottwo"], list.Select(c => c.Profile.Name));
        Assert.Equal("innothule", list[0].Profile.Zone);
    }

    [DbFact]
    public void Saved_position_is_written_into_the_profile_blob()
    {
        var cs = DbFactAttribute.ConnectionString!;
        Insert(cs, 24, "Qwalker", ProfileBuilder.Build("Qwalker", 9, 10, 1, "grobb", 1, 2, 3));
        var store = new MySqlCharacterStore(cs, CharacterTable);

        store.SavePosition("Qwalker", "innothule", -612.29f, -2789.26f, -31.44f);

        var p = store.ListForAccount(24).Single().Profile;
        Assert.Equal(("innothule", -612.29f, -2789.26f, -31.44f), (p.Zone, p.X, p.Y, p.Z));
        Assert.Equal(("Qwalker", 9, 10), (p.Name, p.Race, p.Class)); // the rest of the blob is untouched

        store.SavePosition("Qwalker", "innothule", 0, 0, 0, hp: 57, exp: 123_456, level: 12);
        p = store.ListForAccount(24).Single().Profile;
        Assert.Equal((57, 123_456u, 12), (p.CurHp, p.Exp, p.Level));
    }

    private void Insert(string cs, int account, string name, byte[]? profile)
    {
        using var c = new MySqlConnection(cs);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"INSERT INTO `{CharacterTable}` (account_id, name, profile) VALUES (@a, @n, @p)";
        cmd.Parameters.AddWithValue("@a", account);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@p", (object?)profile ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static long Scalar(string sql)
    {
        using var c = new MySqlConnection(DbFactAttribute.ConnectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
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
