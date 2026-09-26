using System.Collections.Concurrent;
using MySqlConnector;

namespace EQClassic.Server.Combat;

/// <summary>What melee combat reads of an item: weapon damage and delay (tenths of a second), item type and AC.</summary>
public sealed record ItemStats(int Id, string Name, int Damage, int Delay, int ItemType, int AC, int Slots = 0, int Classes = 65535, int Races = 65535)
{
    /// <summary>Spell scrolls (item type 20): the spell scribing teaches (scrolleffect).</summary>
    public int ScrollSpell { get; init; }
    public const int SpellScroll = 20;

    /// <summary>Whether it can be worn in this worn slot (0-21): bit <c>slot</c> of the slots bitmask.</summary>
    public bool FitsSlot(int slot) => slot is >= 0 and < 22 && (Slots & (1 << slot)) != 0;

    public bool UsableByClass(int playerClass) => playerClass is >= 1 and <= 16 && (Classes & (1 << (playerClass - 1))) != 0;

    /// <summary>Races bitmask: human bit 0 … gnome bit 11, Iksar bit 12, Vah Shir bit 13.</summary>
    public bool UsableByRace(int race)
    {
        int bit = race switch { >= 1 and <= 12 => race - 1, 128 => 12, 130 => 13, _ => -1 };
        return bit >= 0 && (Races & (1 << bit)) != 0;
    }

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
        cmd.CommandText = $"SELECT id, Name, damage, delay, itemtype, ac, slots, classes, races, scrolleffect FROM `{_table}` WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        int I(int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
        return new ItemStats(I(0), r.IsDBNull(1) ? "" : r.GetString(1), I(2), I(3), I(4), I(5), I(6), I(7), I(8)) { ScrollSpell = I(9) };
    }
}
