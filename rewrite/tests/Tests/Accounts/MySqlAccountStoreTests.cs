using EQClassic.Server.Accounts;
using EQClassic.Server.Login;
using EQClassic.Shared.Login;
using MySqlConnector;

namespace EQClassic.Tests.Accounts;

/// <summary>
/// The store against a real MariaDB, on a throwaway copy of the login_accounts structure
/// (sql/schema.sql) filled like the runbook does it (INSERT ... SHA1('test') ... user_active '1').
/// </summary>
public sealed class MySqlAccountStoreTests : IDisposable
{
    private readonly string _table = "login_accounts_rwtest_" + Guid.NewGuid().ToString("N")[..8];
    private readonly bool _created;

    public MySqlAccountStoreTests()
    {
        if (DbFactAttribute.ConnectionString is not { Length: > 0 } cs)
            return;
        Execute(cs, $"""
            CREATE TABLE `{_table}` (
              `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
              `name` varchar(18) NOT NULL DEFAULT '',
              `password` varchar(45) NOT NULL DEFAULT '',
              `ip` varchar(16) NOT NULL DEFAULT '',
              `auth` tinyblob,
              `lsadmin` tinyint(2) unsigned NOT NULL DEFAULT '0',
              `lsstatus` tinyint(2) unsigned NOT NULL DEFAULT '0',
              `worldadmin` varchar(45) NOT NULL DEFAULT '',
              `user_active` varchar(45) NOT NULL DEFAULT '',
              `user_lastvisit` varchar(45) NOT NULL DEFAULT '',
              `email` varchar(100) NOT NULL DEFAULT '',
              PRIMARY KEY (`id`),
              UNIQUE KEY `name` (`name`)
            ) DEFAULT CHARSET=latin1
            """);
        Execute(cs, $"""
            INSERT INTO `{_table}` (id, name, password, lsadmin, lsstatus, worldadmin, user_active) VALUES
              (1, 'test', SHA1('test'), 0, 0, '0', '1'),
              (16, 'bot', SHA1('bot'), 0, 0, '0', '1'),
              (20, 'newbie', SHA1('pw'), 0, 0, '0', ''),
              (21, 'gm', SHA1('gm'), 255, 50, '250', '0')
            """);
        _created = true;
    }

    public void Dispose()
    {
        if (_created)
            Execute(DbFactAttribute.ConnectionString!, $"DROP TABLE IF EXISTS `{_table}`");
    }

    private MySqlAccountStore Store => new(DbFactAttribute.ConnectionString!, _table);

    [DbFact]
    public void Reads_the_runbook_test_account()
    {
        var account = Store.FindByName("test");
        Assert.NotNull(account);
        Assert.Equal(1, account.Id);
        Assert.True(account.Verified);
        Assert.True(PasswordHash.Matches("test", account.PasswordSha1));
    }

    [DbFact]
    public void Name_lookup_follows_the_case_insensitive_collation()
    {
        Assert.Equal(16, Store.FindByName("BOT")?.Id);
    }

    [DbFact]
    public void Unknown_name_is_null() => Assert.Null(Store.FindByName("ghost"));

    [DbFact]
    public void Empty_user_active_means_not_verified()
    {
        var result = new LoginService(Store).Authenticate("newbie", "pw");
        Assert.Equal(LoginResult.NotVerified, result.Result);
    }

    [DbFact]
    public void Login_admin_with_banned_status_still_logs_in_and_worldadmin_is_parsed()
    {
        Assert.Equal(250, Store.FindByName("gm")?.WorldAdmin);
        Assert.Equal("LS#21", new LoginService(Store).Authenticate("gm", "gm").SessionId);
    }

    [DbFact]
    public void Full_login_against_the_database()
    {
        var service = new LoginService(Store);
        Assert.Equal("LS#1", service.Authenticate("test", "test").SessionId);
        Assert.Equal(LoginResult.BadCredentials, service.Authenticate("test", "TEST").Result);
    }

    [Fact]
    public void Table_name_must_be_an_identifier()
    {
        Assert.Throws<ArgumentException>(() => new MySqlAccountStore("Server=x", "login_accounts; DROP TABLE x"));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("", 0)]
    [InlineData("  250abc", 250)]
    [InlineData("-3", -3)]
    [InlineData("x", 0)]
    public void Varchar_columns_parse_like_atoi(string value, int expected) => Assert.Equal(expected, MySqlAccountStore.LegacyInt(value));

    private static void Execute(string connectionString, string sql)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
