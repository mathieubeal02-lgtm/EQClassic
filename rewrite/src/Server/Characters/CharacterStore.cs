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

    /// <summary>The 30 inventory slots (item ids and charges) and the money, into the profile.</summary>
    void SaveInventory(string name, IReadOnlyList<int> items, IReadOnlyList<int> charges, EQClassic.Server.Zone.Coins coins);

    /// <summary>Skill values after skill-ups.</summary>
    void SaveSkills(string name, IReadOnlyList<int> skills);

    /// <summary>Where death and gate send the character (bind_point_zone and bind_location slot 0).</summary>
    void SaveBind(string name, string zone, float x, float y, float z);

    /// <summary>Spell book, memorised gems, current mana and buffs, into the profile.</summary>
    void SaveSpells(string name, IReadOnlyList<int> book, IReadOnlyList<int> gems, int mana, IReadOnlyList<(int SpellId, int CasterLevel, int Tics)> buffs);
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

    public void SaveInventory(string name, IReadOnlyList<int> items, IReadOnlyList<int> charges, EQClassic.Server.Zone.Coins coins)
    {
        int i = _characters.FindIndex(c => string.Equals(c.Profile.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            return;
        var c = _characters[i];
        _characters[i] = c with { Profile = c.Profile with { Inventory = items.ToArray(), Charges = charges.ToArray(), Coins = coins } };
        if (Profiles.TryGetValue(name, out var raw))
            WriteInventory(raw, items, charges, coins);
    }

    public void SaveSpells(string name, IReadOnlyList<int> book, IReadOnlyList<int> gems, int mana, IReadOnlyList<(int SpellId, int CasterLevel, int Tics)> buffs)
    {
        int i = _characters.FindIndex(c => string.Equals(c.Profile.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            return;
        var c = _characters[i];
        _characters[i] = c with { Profile = c.Profile with { SpellBook = book.ToArray(), SpellGemIds = gems.ToArray(), Mana = mana, Buffs = buffs.ToArray() } };
        if (Profiles.TryGetValue(name, out var raw))
            WriteSpells(raw, book, gems, mana, buffs);
    }

    public void SaveSkills(string name, IReadOnlyList<int> skills)
    {
        int i = _characters.FindIndex(c => string.Equals(c.Profile.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            return;
        var c = _characters[i];
        _characters[i] = c with { Profile = c.Profile with { Skills = skills.ToArray() } };
        if (Profiles.TryGetValue(name, out var raw))
            ProfileTemplate.SetSkills(raw, skills);
    }

    public void SaveBind(string name, string zone, float x, float y, float z)
    {
        int i = _characters.FindIndex(c => string.Equals(c.Profile.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i < 0)
            return;
        var c = _characters[i];
        _characters[i] = c with { Profile = c.Profile with { BindZone = zone, BindX = x, BindY = y, BindZ = z } };
        if (Profiles.TryGetValue(name, out var raw))
            ProfileTemplate.SetBind(raw, zone, x, y, z);
    }

    internal static void WriteSpells(byte[] profile, IReadOnlyList<int> book, IReadOnlyList<int> gems, int mana, IReadOnlyList<(int SpellId, int CasterLevel, int Tics)> buffs)
    {
        ProfileTemplate.SetSpells(profile, book, gems);
        ProfileTemplate.SetMana(profile, mana);
        ProfileTemplate.SetBuffs(profile, buffs);
    }

    internal static void WriteInventory(byte[] profile, IReadOnlyList<int> items, IReadOnlyList<int> charges, EQClassic.Server.Zone.Coins coins)
    {
        for (int slot = 0; slot < Math.Min(30, items.Count); slot++)
            ProfileTemplate.SetItem(profile, slot, items[slot] == 0 ? (ushort)0xFFFF : (ushort)items[slot], (sbyte)Math.Clamp(charges[slot], sbyte.MinValue, sbyte.MaxValue));
        ProfileTemplate.SetCoins(profile, coins);
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

    public void SaveInventory(string name, IReadOnlyList<int> items, IReadOnlyList<int> charges, EQClassic.Server.Zone.Coins coins) =>
        Update(name, profile => InMemoryCharacterStore.WriteInventory(profile, items, charges, coins));

    public void SaveBind(string name, string zone, float x, float y, float z) =>
        Update(name, profile => ProfileTemplate.SetBind(profile, zone, x, y, z));

    public void SaveSkills(string name, IReadOnlyList<int> skills) =>
        Update(name, profile => ProfileTemplate.SetSkills(profile, skills));

    public void SaveSpells(string name, IReadOnlyList<int> book, IReadOnlyList<int> gems, int mana, IReadOnlyList<(int SpellId, int CasterLevel, int Tics)> buffs) =>
        Update(name, profile => InMemoryCharacterStore.WriteSpells(profile, book, gems, mana, buffs));

    /// <summary>Read-modify-write of one profile in a transaction.</summary>
    private void Update(string name, Action<byte[]> change)
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
        change(profile);
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
