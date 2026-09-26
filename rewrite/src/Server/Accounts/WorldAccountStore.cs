using MySqlConnector;

namespace EQClassic.Server.Accounts;

/// <summary>
/// World's own accounts (table account), linked to login accounts by lsaccount_id. Like the legacy
/// World (client_process.cpp, OP_SendLoginInfo), the first visit creates the row with the login
/// name, an empty password and status 0.
/// </summary>
public interface IWorldAccountStore
{
    /// <returns>The world account id, or 0 when it cannot be created (e.g. the name is taken).</returns>
    int ResolveOrCreate(int lsAccountId, string lsAccountName);
}

public sealed class InMemoryWorldAccountStore : IWorldAccountStore
{
    private readonly Dictionary<int, int> _byLsId = new();
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private int _nextId = 1;

    public InMemoryWorldAccountStore(int firstId = 1) => _nextId = firstId;

    public void Link(int lsAccountId, int worldAccountId, string name)
    {
        _byLsId[lsAccountId] = worldAccountId;
        _names.Add(name);
        _nextId = Math.Max(_nextId, worldAccountId + 1);
    }

    public int ResolveOrCreate(int lsAccountId, string lsAccountName)
    {
        if (_byLsId.TryGetValue(lsAccountId, out var id))
            return id;
        if (!_names.Add(lsAccountName))
            return 0;
        id = _nextId++;
        _byLsId[lsAccountId] = id;
        return id;
    }
}

public sealed class MySqlWorldAccountStore : IWorldAccountStore
{
    private readonly string _connectionString;
    private readonly string _table;

    public MySqlWorldAccountStore(string connectionString, string table = "account")
    {
        _connectionString = connectionString;
        _table = table.Length > 0 && table.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? table : throw new ArgumentException("must be a plain SQL identifier", nameof(table));
    }

    public int ResolveOrCreate(int lsAccountId, string lsAccountName)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        if (Find(connection, lsAccountId) is int existing)
            return existing;
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = $"INSERT INTO `{_table}` SET name = @name, password = '', status = 0, lsaccount_id = @ls";
            insert.Parameters.AddWithValue("@name", lsAccountName);
            insert.Parameters.AddWithValue("@ls", lsAccountId);
            try
            {
                insert.ExecuteNonQuery();
            }
            catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                return 0; // legacy: "Error adding local account for LS login ... duplicate name?"
            }
        }
        return Find(connection, lsAccountId) ?? 0;
    }

    private int? Find(MySqlConnection connection, int lsAccountId)
    {
        using var select = connection.CreateCommand();
        select.CommandText = $"SELECT id FROM `{_table}` WHERE lsaccount_id = @ls";
        select.Parameters.AddWithValue("@ls", lsAccountId);
        return select.ExecuteScalar() is { } value and not DBNull ? Convert.ToInt32(value) : null;
    }
}
