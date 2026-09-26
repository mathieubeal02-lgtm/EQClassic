using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>A faction_list row: base value and the class, race and deity modifiers.</summary>
public sealed record FactionInfo(int Id, string Name, int Base, IReadOnlyList<int> ClassMods, IReadOnlyDictionary<int, int> RaceMods,
    IReadOnlyDictionary<int, int> DeityMods)
{
    /// <summary>Database::GetFactionData: base + class + race + deity modifiers (a deity modifier above 1000 counts as 0).</summary>
    public int Modifiers(int playerClass, int race, int deity)
    {
        int c = playerClass >= 1 && playerClass <= ClassMods.Count ? ClassMods[playerClass - 1] : 0;
        int r = RaceMods.GetValueOrDefault(race);
        int d = DeityMods.GetValueOrDefault(deity);
        if (d > 1000)
            d = 0;
        return Base + c + r + d;
    }
}

/// <summary>An npc_faction row and its npc_faction_entries: the NPC's primary faction, and the hits a kill gives.</summary>
public sealed record NpcFactionList(int Id, int PrimaryFaction, IReadOnlyList<(int FactionId, int Value)> Hits);

public interface IFactionData
{
    FactionInfo? Faction(int factionId);
    NpcFactionList? NpcList(int npcFactionId);
}

public sealed class InMemoryFactionData : IFactionData
{
    public Dictionary<int, FactionInfo> Factions { get; } = new();
    public Dictionary<int, NpcFactionList> Lists { get; } = new();
    public FactionInfo? Faction(int factionId) => Factions.GetValueOrDefault(factionId);
    public NpcFactionList? NpcList(int npcFactionId) => Lists.GetValueOrDefault(npcFactionId);
}

/// <summary>
/// The legacy faction rules (Zone/Source/faction.cpp, mob.cpp GetSpecialFactionCon): the player's
/// value with the NPC's primary faction plus the faction's modifiers for their class, race and
/// deity gives the standing; NPCs without a primary faction scowl at everyone when their race is
/// one of the monster races, else are indifferent; merchants are never worse than dubious.
/// </summary>
public static class FactionRules
{
    public const int Max = 1500, Min = -1500;
    public const int AgnosticDeity = 140;

    /// <summary>CalculateFaction's thresholds.</summary>
    public static FactionStanding Standing(int total) => total switch
    {
        >= 1100 => FactionStanding.Ally,
        >= 750 => FactionStanding.Warmly,
        >= 500 => FactionStanding.Kindly,
        >= 101 => FactionStanding.Amiable,
        >= 0 => FactionStanding.Indifferent, // 100 falls in the gap of the legacy table, which returns indifferent
        >= -100 => FactionStanding.Apprehensive,
        >= -500 => FactionStanding.Dubious,
        >= -750 => FactionStanding.Threatenly,
        _ => FactionStanding.Scowls,
    };

    private static readonly HashSet<int> MonsterRaces =
    [
        14, 16, 17, 18, 21, 28, 29, 31, 32, 33, 37, 39, 40, 41, 42, 43, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 57, 58, 60,
        61, 62, 63, 64, 65, 66, 68, 70, 74, 75, 76, 80, 84, 85, 86, 89, 91, 96, 97, 98, 99, 100, 101, 104, 105, 109, 111, 116,
        117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 129, 131, 132, 133, 134, 135, 136, 137, 138, 140, 144, 145, 146, 147,
        148, 149, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 168, 169, 170, 171, 172, 173, 174, 175, 178, 181,
        185, 186, 187, 188, 189,
    ];

    /// <summary>Mob::GetSpecialFactionCon: NPCs with no primary faction, by their race.</summary>
    public static FactionStanding SpecialCon(int npcRace) => MonsterRaces.Contains(npcRace) ? FactionStanding.Scowls : FactionStanding.Indifferent;

    /// <summary>BuildFactionMessage.</summary>
    public static string? HitMessage(string name, int hit, int total) =>
        total >= Max ? $"Your faction standing with {name} could not possibly get any better!"
        : hit > 0 ? $"Your faction standing with {name} has gotten better!"
        : hit == 0 ? null
        : total > Min ? $"Your faction standing with {name} has gotten worse!"
        : $"Your faction standing with {name} could not possibly get any worse!";
}

/// <summary>
/// Standings from the faction tables and each player's values (faction_values). The legacy zone
/// asks for aggro with an agnostic deity and for consider with the player's deity; both are kept.
/// </summary>
public sealed class DatabaseFactions : IFactionStandings
{
    private readonly IFactionData _data;
    public DatabaseFactions(IFactionData data) => _data = data;

    public FactionStanding Standing(ZoneInstance.Entity player, NpcTemplate npc) => StandingFor(player, npc, FactionRules.AgnosticDeity);

    /// <summary>Client::GetFactionLevel.</summary>
    public FactionStanding StandingFor(ZoneInstance.Entity player, NpcTemplate npc, int deity)
    {
        var list = npc.PrimaryFaction > 0 ? _data.NpcList(npc.PrimaryFaction) : null;
        FactionStanding standing;
        if (list is null || list.PrimaryFaction <= 0)
            standing = FactionRules.SpecialCon(npc.Race);
        else if (_data.Faction(list.PrimaryFaction) is { } faction)
            standing = FactionRules.Standing(player.FactionValue(list.PrimaryFaction) + faction.Modifiers(player.Fighter.Class, player.Race, deity));
        else
            standing = FactionStanding.Indifferent;
        if (npc.Combat.Class is ZoneInstance.MerchantClass or ZoneInstance.BankerClass && standing is FactionStanding.Threatenly or FactionStanding.Scowls)
            standing = FactionStanding.Dubious;
        return standing;
    }

    /// <summary>
    /// Client::SetFactionLevel on a kill: each faction of the NPC's list moves by its hit, the total
    /// with the modifiers kept within ±1500. At the limit the legacy zone stores the limit itself as
    /// the value, which then counts the modifiers twice (a human's 1500 with the Qeynos guards became
    /// 3000); the rewrite stores the value that puts the total at the limit. Returns the messages.
    /// </summary>
    public IReadOnlyList<string> Kill(ZoneInstance.Entity player, NpcTemplate npc, int deity)
    {
        var messages = new List<string>();
        if (npc.PrimaryFaction <= 0 || _data.NpcList(npc.PrimaryFaction) is not { } list)
            return messages;
        foreach (var (factionId, hit) in list.Hits)
            if (Adjust(player, factionId, hit, deity) is { } message)
                messages.Add(message);
        return messages;
    }

    /// <summary>Client::GetCharacterFactionLevel: the stored value plus the class, race and deity modifiers (0 for no faction).</summary>
    public int Level(ZoneInstance.Entity player, int factionId, int deity) =>
        factionId <= 0 || _data.Faction(factionId) is not { } faction ? 0 : player.FactionValue(factionId) + faction.Modifiers(player.Fighter.Class, player.Race, deity);

    /// <summary>One faction moved by a hit (a kill, or quest::faction), the total kept within ±1500; the message.</summary>
    public string? Adjust(ZoneInstance.Entity player, int factionId, int hit, int deity)
    {
        if (factionId <= 0 || _data.Faction(factionId) is not { } faction)
            return null;
        int mods = faction.Modifiers(player.Fighter.Class, player.Race, deity);
        int total = player.FactionValue(factionId) + hit + mods;
        player.SetFactionValue(factionId, Math.Clamp(total, FactionRules.Min, FactionRules.Max) - mods);
        return FactionRules.HitMessage(faction.Name, hit, Math.Max(total, FactionRules.Min));
    }
}

/// <summary>faction_list and npc_faction(+_entries), read once; the values of a character from faction_values.</summary>
public sealed class MySqlFactionData : IFactionData
{
    private readonly Dictionary<int, FactionInfo> _factions = new();
    private readonly Dictionary<int, NpcFactionList> _lists = new();

    private static readonly int[] Races = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14, 60, 75, 108, 120, 128, 130, 161];
    private static readonly int[] Deities = [140, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215, 216];

    public MySqlFactionData(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            string cols = string.Join(", ", Enumerable.Range(1, 15).Select(c => $"mod_c{c}")
                .Concat(Races.Select(r => $"mod_r{r}")).Concat(Deities.Select(d => $"mod_d{d}")));
            cmd.CommandText = $"SELECT id, name, base, {cols} FROM faction_list";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int I(int i) => r.IsDBNull(i) ? 0 : Convert.ToInt32(r.GetValue(i));
                var classes = Enumerable.Range(0, 15).Select(c => I(3 + c)).ToArray();
                var races = Races.Select((race, i) => (race, I(18 + i))).ToDictionary(x => x.race, x => x.Item2);
                var deities = Deities.Select((deity, i) => (deity, I(18 + Races.Length + i))).ToDictionary(x => x.deity, x => x.Item2);
                _factions[I(0)] = new FactionInfo(I(0), r.IsDBNull(1) ? "" : r.GetString(1), I(2), classes, races, deities);
            }
        }
        var hits = new Dictionary<int, List<(int, int)>>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT npc_faction_id, faction_id, value FROM npc_faction_entries";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = Convert.ToInt32(r.GetValue(0));
                if (!hits.TryGetValue(id, out var l))
                    hits[id] = l = new List<(int, int)>();
                l.Add((Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2))));
            }
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, primaryfaction FROM npc_faction";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int id = Convert.ToInt32(r.GetValue(0));
                _lists[id] = new NpcFactionList(id, r.IsDBNull(1) ? 0 : Convert.ToInt32(r.GetValue(1)),
                    hits.TryGetValue(id, out var l) ? l : Array.Empty<(int, int)>());
            }
        }
    }

    public FactionInfo? Faction(int factionId) => _factions.GetValueOrDefault(factionId);
    public NpcFactionList? NpcList(int npcFactionId) => _lists.GetValueOrDefault(npcFactionId);
}

/// <summary>A character's faction values (faction_values: char_id, faction_id, current_value).</summary>
public interface IFactionValueStore
{
    IReadOnlyDictionary<int, int> Load(string characterName);
    void Save(string characterName, IReadOnlyDictionary<int, int> values);
}

public sealed class InMemoryFactionValueStore : IFactionValueStore
{
    public Dictionary<string, Dictionary<int, int>> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<int, int> Load(string characterName) =>
        Values.TryGetValue(characterName, out var v) ? new Dictionary<int, int>(v) : new Dictionary<int, int>();
    public void Save(string characterName, IReadOnlyDictionary<int, int> values) => Values[characterName] = new Dictionary<int, int>(values);
}

public sealed class MySqlFactionValueStore : IFactionValueStore
{
    private readonly string _connectionString;
    public MySqlFactionValueStore(string connectionString) => _connectionString = connectionString;

    public IReadOnlyDictionary<int, int> Load(string characterName)
    {
        var values = new Dictionary<int, int>();
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT v.faction_id, v.current_value FROM faction_values v JOIN character_ c ON c.id = v.char_id WHERE c.name = @name";
        cmd.Parameters.AddWithValue("@name", characterName);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            values[Convert.ToInt32(r.GetValue(0))] = Convert.ToInt32(r.GetValue(1));
        return values;
    }

    /// <summary>Database::SetCharacterFactionLevel for every value: the row is replaced.</summary>
    public void Save(string characterName, IReadOnlyDictionary<int, int> values)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var tx = connection.BeginTransaction();
        int charId;
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id FROM character_ WHERE name = @name";
            cmd.Parameters.AddWithValue("@name", characterName);
            if (cmd.ExecuteScalar() is not { } id || id is DBNull)
                return;
            charId = Convert.ToInt32(id);
        }
        foreach (var (factionId, value) in values)
        {
            using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM faction_values WHERE char_id = @c AND faction_id = @f; " +
                              "INSERT INTO faction_values (char_id, faction_id, current_value) VALUES (@c, @f, @v)";
            cmd.Parameters.AddWithValue("@c", charId);
            cmd.Parameters.AddWithValue("@f", factionId);
            cmd.Parameters.AddWithValue("@v", value);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
