using System.Globalization;
using MySqlConnector;

namespace EQClassic.Server.Accounts;

/// <summary>
/// Reads the legacy login_accounts table (sql/schema.sql) of the MariaDB database the current
/// servers use, so both generations share the same accounts. Read-only for now.
/// </summary>
public sealed class MySqlAccountStore : IAccountStore
{
    private readonly string _connectionString;
    private readonly string _table;

    /// <param name="table">Overridable for tests; must be a plain identifier.</param>
    public MySqlAccountStore(string connectionString, string table = "login_accounts")
    {
        if (table.Length == 0 || !table.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("table must be a plain identifier", nameof(table));
        _connectionString = connectionString;
        _table = table;
    }

    public LoginAccount? FindByName(string name)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        // Same lookup as Database::GetLSLoginInfo; the name match follows the column's collation
        // (latin1, case-insensitive). The password is compared by LoginService, not in SQL.
        command.CommandText = $"SELECT id, name, password, lsadmin, lsstatus, worldadmin, user_active FROM `{_table}` WHERE name = @name";
        command.Parameters.AddWithValue("@name", name);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        var account = new LoginAccount(
            Id: Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            Name: reader.GetString(1),
            PasswordSha1: reader.GetString(2),
            LsAdmin: Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
            LsStatus: Convert.ToInt32(reader.GetValue(4), CultureInfo.InvariantCulture),
            Verified: LegacyInt(reader.GetString(6)) != 0,
            WorldAdmin: LegacyInt(reader.GetString(5)));
        // Names are unique, but a second row would mean the legacy query (which expects exactly
        // one) refuses the login: do the same.
        return reader.Read() ? null : account;
    }

    /// <summary>atoi() semantics for the varchar columns (worldadmin, user_active): leading digits, else 0.</summary>
    internal static int LegacyInt(string value)
    {
        var span = value.AsSpan().TrimStart();
        int sign = 1, i = 0, result = 0;
        if (i < span.Length && (span[i] == '-' || span[i] == '+'))
        {
            sign = span[i] == '-' ? -1 : 1;
            i++;
        }
        for (; i < span.Length && char.IsAsciiDigit(span[i]); i++)
            result = unchecked(result * 10 + (span[i] - '0'));
        return sign * result;
    }
}
