using System.Collections.Concurrent;
using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>Coins by denomination (profile platinum/gold/silver/copper, loot).</summary>
public readonly record struct Coins(int Platinum, int Gold, int Silver, int Copper)
{
    public static readonly Coins None = default;
    public bool IsZero => Platinum == 0 && Gold == 0 && Silver == 0 && Copper == 0;
    public Coins Add(Coins o) => new(Platinum + o.Platinum, Gold + o.Gold, Silver + o.Silver, Copper + o.Copper);

    public long TotalCopper => Copper + Silver * 10L + Gold * 100L + Platinum * 1000L;

    /// <summary>Client::TakeMoneyFromPP: the rest, counted again in the biggest coins; null when short.</summary>
    public Coins? Take(int copper)
    {
        long left = TotalCopper - copper;
        if (left < 0)
            return null;
        return new Coins((int)(left / 1000), (int)(left / 100 % 10), (int)(left / 10 % 10), (int)(left % 10));
    }

    /// <summary>Client::AddMoneyToPP: the amount split into platinum, gold, silver and copper, added to each.</summary>
    public Coins AddCopper(int copper) =>
        new(Platinum + copper / 1000, Gold + copper / 100 % 10, Silver + copper / 10 % 10, Copper + copper % 10);

    /// <summary>"3 platinum, 2 gold and 5 copper" (the legacy loot message's list).</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Platinum > 0) parts.Add($"{Platinum} platinum");
        if (Gold > 0) parts.Add($"{Gold} gold");
        if (Silver > 0) parts.Add($"{Silver} silver");
        if (Copper > 0) parts.Add($"{Copper} copper");
        return parts.Count switch
        {
            0 => "nothing",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }
}

public sealed record LootDrop(int ItemId, int Charges);

/// <summary>A loottable row and its entries (loottable, loottable_entries).</summary>
public sealed record LootTable(int Id, int MinCash, int MaxCash, int AvgCoin, IReadOnlyList<(int DropId, int Multiplier, int Probability)> Entries);

/// <summary>
/// Database::AddLootTableToNPC / AddLootDropToNPC (Zone/Source/loottables.cpp): coins between the
/// table's min and max cash split around its average coin (with the legacy code's quirk: gold is
/// taken off the cash at ×10, not ×100), then each entry tried `multiplier` times at `probability`
/// percent, each success drawing one item of its drop by weight (chance).
/// </summary>
public static class LootRoller
{
    public static (List<LootDrop> Items, Coins Coins) Roll(LootTable table, Func<int, IReadOnlyList<(int ItemId, int Charges, int Chance)>> drop, Random random)
    {
        var coins = Coins.None;
        if (table.MinCash <= table.MaxCash && table.MaxCash != 0)
        {
            int cash = table.MinCash == table.MaxCash ? table.MinCash : random.Next(table.MaxCash - table.MinCash) + table.MinCash;
            if (cash != 0)
            {
                int copper = 0, silver = 0, gold = 0;
                if (table.AvgCoin != 0)
                {
                    int min = (int)(table.AvgCoin * 0.75 + 1), max = (int)(table.AvgCoin * 1.25 + 1);
                    int Draw() => max > min ? random.Next(max - min) + min - 1 : min - 1;
                    copper = Draw();
                    silver = Draw();
                    gold = Draw();
                    cash -= copper;
                    cash -= silver * 10;
                    cash -= gold * 10; // sic (legacy)
                }
                int platinum = cash / 1000;
                cash -= platinum * 1000;
                int gold2 = cash / 100;
                cash -= gold2 * 100;
                int silver2 = cash / 10;
                cash -= silver2 * 10;
                coins = new Coins(platinum, gold + gold2, silver + silver2, copper + cash);
            }
        }
        var items = new List<LootDrop>();
        foreach (var (dropId, multiplier, probability) in table.Entries)
            for (int i = 1; i <= multiplier; i++)
                if (random.Next(100) < probability && Pick(drop(dropId), random) is { } item)
                    items.Add(item);
        return (items, coins);
    }

    private static LootDrop? Pick(IReadOnlyList<(int ItemId, int Charges, int Chance)> entries, Random random)
    {
        int total = entries.Sum(e => e.Chance);
        if (total <= 0)
            return null;
        int roll = random.Next(total), x = 0;
        foreach (var (item, charges, chance) in entries)
        {
            x += chance;
            if (x > roll)
                return new LootDrop(item, charges);
        }
        return null;
    }
}

public interface ILootSource
{
    /// <summary>What an NPC of this loottable carries when it dies; nothing for an unknown table.</summary>
    (List<LootDrop> Items, Coins Coins) Roll(int loottableId, Random random);
}

public sealed class InMemoryLootSource : ILootSource
{
    public Dictionary<int, LootTable> Tables { get; } = new();
    public Dictionary<int, List<(int ItemId, int Charges, int Chance)>> Drops { get; } = new();

    public (List<LootDrop> Items, Coins Coins) Roll(int loottableId, Random random) =>
        Tables.TryGetValue(loottableId, out var table)
            ? LootRoller.Roll(table, id => Drops.GetValueOrDefault(id) ?? new(), random)
            : (new List<LootDrop>(), Coins.None);
}

/// <summary>loottable, loottable_entries, lootdrop_entries, read once per table.</summary>
public sealed class MySqlLootSource : ILootSource
{
    private readonly string _connectionString;
    private readonly string _p;
    private readonly ConcurrentDictionary<int, LootTable?> _tables = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<(int, int, int)>> _drops = new();

    public MySqlLootSource(string connectionString, string tablePrefix = "")
    {
        _connectionString = connectionString;
        _p = tablePrefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? tablePrefix : throw new ArgumentException("plain SQL identifier expected", nameof(tablePrefix));
    }

    public (List<LootDrop> Items, Coins Coins) Roll(int loottableId, Random random) =>
        loottableId > 0 && _tables.GetOrAdd(loottableId, LoadTable) is { } table
            ? LootRoller.Roll(table, id => _drops.GetOrAdd(id, LoadDrop), random)
            : (new List<LootDrop>(), Coins.None);

    private static int Int(MySqlDataReader r, int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));

    private LootTable? LoadTable(int id)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        int min, max, avg;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT mincash, maxcash, avgcoin FROM `{_p}loottable` WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return null;
            (min, max, avg) = (Int(r, 0), Int(r, 1), Int(r, 2));
        }
        var entries = new List<(int, int, int)>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = $"SELECT lootdrop_id, multiplier, probability FROM `{_p}loottable_entries` WHERE loottable_id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                entries.Add((Int(r, 0), Int(r, 1), Int(r, 2)));
        }
        return new LootTable(id, min, max, avg, entries);
    }

    private IReadOnlyList<(int, int, int)> LoadDrop(int id)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT item_id, item_charges, chance FROM `{_p}lootdrop_entries` WHERE lootdrop_id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        var list = new List<(int, int, int)>();
        while (r.Read())
            list.Add((Int(r, 0), Int(r, 1), Int(r, 2)));
        return list;
    }
}
