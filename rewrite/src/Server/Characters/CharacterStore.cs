using MySqlConnector;

namespace EQClassic.Server.Characters;

/// <summary>A character_ row: its world account and decoded profile.</summary>
public sealed record CharacterRecord(int Id, int AccountId, PlayerProfile Profile);

public interface ICharacterStore
{
    /// <summary>Created characters of a world account, by name (legacy GetCharSelectInfo: "order by name", 10 slots).</summary>
    IReadOnlyList<CharacterRecord> ListForAccount(int accountId);

    /// <summary>Inserts a created character; false when the name is taken (character_.name is unique).</summary>
    bool TryCreate(int accountId, string name, byte[] profile);

    /// <summary>
    /// Writes the zone and position into the character's profile (the fields the legacy zone saves
    /// on camp and zoning), so both server generations see where the character is.
    /// </summary>
    /// <param name="hp">Current hit points to save too (cur_hp), or null to keep them; the same for experience and level.</param>
    void SavePosition(string name, string zone, float x, float y, float z, int? hp = null, uint? exp = null, int? level = null);
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

    public bool TryCreate(int accountId, string name, byte[] profile)
    {
        if (_characters.Any(c => string.Equals(c.Profile.Name, name, StringComparison.OrdinalIgnoreCase)))
            return false;
        _characters.Add(new CharacterRecord(_characters.Count + 1, accountId, PlayerProfile.Read(profile)!));
        Profiles[name] = profile;
        return true;
    }

    public void SavePosition(string name, string zone, float x, float y, float z, int? hp = null, uint? exp = null, int? level = null)
    {
        int i = _characters.FindIndex(c => string.Equals(c.Profile.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            return;
        var c = _characters[i];
        _characters[i] = c with
        {
            Profile = c.Profile with
            {
                Zone = zone, X = x, Y = y, Z = z, CurHp = hp ?? c.Profile.CurHp, Exp = exp ?? c.Profile.Exp, Level = level ?? c.Profile.Level,
            },
        };
        if (Profiles.TryGetValue(name, out var raw))
        {
            ProfileTemplate.SetZone(raw, zone);
            ProfileTemplate.SetPosition(raw, x, y, z);
            if (hp is int h)
                ProfileTemplate.SetCurHp(raw, h);
            if (exp is uint e)
                ProfileTemplate.SetExp(raw, e);
            if (level is int l)
                ProfileTemplate.SetLevel(raw, l);
        }
    }

    /// <summary>Raw profiles of created characters (tests).</summary>
    public Dictionary<string, byte[]> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase);

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

    public void SavePosition(string name, string zone, float x, float y, float z, int? hp = null, uint? exp = null, int? level = null)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();
        byte[]? profile;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = $"SELECT profile FROM `{_table}` WHERE name = @name FOR UPDATE";
            select.Parameters.AddWithValue("@name", name);
            profile = select.ExecuteScalar() as byte[];
        }
        if (profile is null || profile.Length < PlayerProfile.MinimumLength)
            return;
        ProfileTemplate.SetZone(profile, zone);
        ProfileTemplate.SetPosition(profile, x, y, z);
        if (hp is int h)
            ProfileTemplate.SetCurHp(profile, h);
        if (exp is uint e)
            ProfileTemplate.SetExp(profile, e);
        if (level is int l)
            ProfileTemplate.SetLevel(profile, l);
        using (var update = connection.CreateCommand())
        {
            update.Transaction = tx;
            update.CommandText = $"UPDATE `{_table}` SET profile = @profile WHERE name = @name";
            update.Parameters.AddWithValue("@profile", profile);
            update.Parameters.AddWithValue("@name", name);
            update.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>One INSERT with the full profile (the legacy two steps: reserve with NULL, then UPDATE).</summary>
    public bool TryCreate(int accountId, string name, byte[] profile)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO `{_table}` SET account_id = @account, name = @name, profile = @profile";
        command.Parameters.AddWithValue("@account", accountId);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@profile", profile);
        try
        {
            command.ExecuteNonQuery();
            return true;
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            return false;
        }
    }
}

internal static class SqlIdentifier
{
    public static string Check(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? name
            : throw new ArgumentException("must be a plain SQL identifier", nameof(name));
}
