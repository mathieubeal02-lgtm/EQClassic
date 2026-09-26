using System.Collections.Concurrent;
using MySqlConnector;

namespace EQClassic.Server.Combat;

/// <summary>What melee combat reads of an item: weapon damage and delay (tenths of a second), item type and AC.</summary>
public sealed record ItemStats(int Id, string Name, int Damage, int Delay, int ItemType, int AC)
{
    // Item types (Common/Include/eq_constants.h ItemType*): weapons and the skill they train.
    public const int OneHandSlash = 0, TwoHandSlash = 1, Pierce = 2, OneHandBlunt = 3, TwoHandBlunt = 4, TwoHandPierce = 35, HandToHand = 45;

    public bool IsWeapon => Damage > 0 && Delay > 0;
    public bool TwoHanded => ItemType is TwoHandSlash or TwoHandBlunt or TwoHandPierce;

    /// <summary>The skill a weapon of this type uses.</summary>
    public int Skill => ItemType switch
    {
        OneHandSlash => SkillCaps.OneHandSlashing,
        TwoHandSlash => SkillCaps.TwoHandSlashing,
        Pierce or TwoHandPierce => SkillCaps.Piercing,
        OneHandBlunt => SkillCaps.OneHandBlunt,
        TwoHandBlunt => SkillCaps.TwoHandBlunt,
        _ => SkillCaps.HandToHand,
    };
}

public interface IItemSource
{
    ItemStats? Get(int id);
}

public sealed class InMemoryItemSource : IItemSource
{
    public Dictionary<int, ItemStats> Items { get; } = new();
    public ItemStats? Get(int id) => Items.GetValueOrDefault(id);
}

/// <summary>items_axclassic, the item table the servers use (the dump's blob table is corrupt); cached.</summary>
public sealed class MySqlItemSource : IItemSource
{
    private readonly string _connectionString;
    private readonly string _table;
    private readonly ConcurrentDictionary<int, ItemStats?> _cache = new();

    public MySqlItemSource(string connectionString, string table = "items_axclassic")
    {
        _connectionString = connectionString;
        _table = table.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? table : throw new ArgumentException("plain SQL identifier expected", nameof(table));
    }

    public ItemStats? Get(int id) => id <= 0 ? null : _cache.GetOrAdd(id, Load);

    private ItemStats? Load(int id)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT id, Name, damage, delay, itemtype, ac FROM `{_table}` WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        int I(int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
        return new ItemStats(I(0), r.IsDBNull(1) ? "" : r.GetString(1), I(2), I(3), I(4), I(5));
    }
}
