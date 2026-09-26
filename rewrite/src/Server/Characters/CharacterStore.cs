using MySqlConnector;

namespace EQClassic.Server.Characters;

/// <summary>A character_ row: its world account and decoded profile.</summary>
public sealed record CharacterRecord(int Id, int AccountId, PlayerProfile Profile);

public interface ICharacterStore
{
    /// <summary>Created characters of a world account, by name (legacy GetCharSelectInfo: "order by name", 10 slots).</summary>
    IReadOnlyList<CharacterRecord> ListForAccount(int accountId);
}

public static class CharacterStoreExtensions
{
    public const int MaxCharacters = 10;

    public static CharacterRecord? Find(this ICharacterStore store, int accountId, string name) =>
        store.ListForAccount(accountId).FirstOrDefault(c => string.Equals(c.Profile.Name, name, StringComparison.OrdinalIgnoreCase));
}

public sealed class InMemoryCharacterStore : ICharacterStore
{
    private readonly List<CharacterRecord> _characters = new();

    public void Add(CharacterRecord character) => _characters.Add(character);

    public IReadOnlyList<CharacterRecord> ListForAccount(int accountId) =>
        _characters.Where(c => c.AccountId == accountId)
                   .OrderBy(c => c.Profile.Name, StringComparer.OrdinalIgnoreCase)
                   .Take(CharacterStoreExtensions.MaxCharacters)
                   .ToList();
}

/// <summary>Reads character_ (sql/schema.sql). Rows whose profile is missing or truncated (names only reserved) are skipped.</summary>
public sealed class MySqlCharacterStore : ICharacterStore
{
    private readonly string _connectionString;
    private readonly string _table;

    public MySqlCharacterStore(string connectionString, string table = "character_")
    {
        _connectionString = connectionString;
        _table = SqlIdentifier.Check(table);
    }

    public IReadOnlyList<CharacterRecord> ListForAccount(int accountId)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, account_id, profile FROM `{_table}` WHERE account_id = @account ORDER BY name";
        command.Parameters.AddWithValue("@account", accountId);
        using var reader = command.ExecuteReader();
        var result = new List<CharacterRecord>();
        while (reader.Read() && result.Count < CharacterStoreExtensions.MaxCharacters)
        {
            if (reader.IsDBNull(2))
                continue;
            if (PlayerProfile.Read((byte[])reader.GetValue(2)) is { } profile)
                result.Add(new CharacterRecord(reader.GetInt32(0), reader.GetInt32(1), profile));
        }
        return result;
    }
}

internal static class SqlIdentifier
{
    public static string Check(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? name
            : throw new ArgumentException("must be a plain SQL identifier", nameof(name));
}
