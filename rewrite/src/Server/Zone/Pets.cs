using EQClassic.Shared.World;
using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>A pets row: the pet a summoning spell makes.</summary>
public sealed record PetType(int Id, int MaxHp, int MinDamage, int MaxDamage, int Race, int Class, int Level, float Size);

public interface IPetSource
{
    PetType? Get(int id);
}

public sealed class InMemoryPetSource : IPetSource
{
    public Dictionary<int, PetType> Pets { get; } = new();
    public PetType? Get(int id) => Pets.GetValueOrDefault(id);
}

public sealed class MySqlPetSource : IPetSource
{
    private readonly Dictionary<int, PetType> _pets = new();

    public MySqlPetSource(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, max_hp, min_dmg, max_dmg, race, class, level, size FROM pets";
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int I(int i) => Convert.ToInt32(r.GetValue(i));
                _pets[I(0)] = new PetType(I(0), I(1), I(2), I(3), I(4), I(5), I(6), Convert.ToSingle(r.GetValue(7)));
            }
        }
        catch (MySqlException)
        {
            // no pets table
        }
    }

    public PetType? Get(int id) => _pets.GetValueOrDefault(id);
}

/// <summary>
/// Mob::MakePet (Zone/Source/spells.cpp): the pet name a summoning spell carries (its teleport_zone
/// field, as "SumEarthR5" or "Skeleton217") picks a pets row; the pet gets one of the legacy names.
/// </summary>
public static class PetRules
{
    /// <summary>The legacy switch, generated from spells.cpp: (name prefix, number after it; −1 for none) → pets id.</summary>
    private static readonly Dictionary<(string Prefix, int Number), int> Types = new()
    {
        [("SumEarthR", 2)] = 74,
        [("SumEarthR", 3)] = 75,
        [("SumEarthR", 4)] = 76,
        [("SumEarthR", 5)] = 77,
        [("SumEarthR", 6)] = 78,
        [("SumEarthR", 7)] = 79,
        [("SumEarthR", 8)] = 80,
        [("SumEarthR", 9)] = 81,
        [("SumEarthR", 10)] = 82,
        [("SumEarthR", 11)] = 83,
        [("SumEarthR", 12)] = 84,
        [("SumEarthR", 13)] = 85,
        [("SumEarthR", 14)] = 86,
        [("SumFireR", 2)] = 88,
        [("SumFireR", 3)] = 89,
        [("SumFireR", 4)] = 90,
        [("SumFireR", 5)] = 91,
        [("SumFireR", 6)] = 92,
        [("SumFireR", 7)] = 93,
        [("SumFireR", 8)] = 94,
        [("SumFireR", 9)] = 95,
        [("SumFireR", 10)] = 96,
        [("SumFireR", 11)] = 97,
        [("SumFireR", 12)] = 98,
        [("SumFireR", 13)] = 99,
        [("SumFireR", 14)] = 100,
        [("SumAirR", 2)] = 60,
        [("SumAirR", 3)] = 61,
        [("SumAirR", 4)] = 62,
        [("SumAirR", 5)] = 63,
        [("SumAirR", 6)] = 64,
        [("SumAirR", 7)] = 65,
        [("SumAirR", 8)] = 66,
        [("SumAirR", 9)] = 67,
        [("SumAirR", 10)] = 68,
        [("SumAirR", 11)] = 69,
        [("SumAirR", 12)] = 70,
        [("SumAirR", 13)] = 71,
        [("SumAirR", 14)] = 72,
        [("SumWaterR", 2)] = 102,
        [("SumWaterR", 3)] = 103,
        [("SumWaterR", 4)] = 104,
        [("SumWaterR", 5)] = 105,
        [("SumWaterR", 6)] = 106,
        [("SumWaterR", 7)] = 107,
        [("SumWaterR", 8)] = 108,
        [("SumWaterR", 9)] = 109,
        [("SumWaterR", 10)] = 110,
        [("SumWaterR", 11)] = 111,
        [("SumWaterR", 12)] = 112,
        [("SumWaterR", 13)] = 113,
        [("SumWaterR", 14)] = 114,
        [("SpiritWolf", 242)] = 45,
        [("SpiritWolf", 237)] = 44,
        [("SpiritWolf", 234)] = 43,
        [("SpiritWolf", 230)] = 42,
        [("SpiritWolf", 227)] = 41,
        [("SpiritWolf", 224)] = 40,
        [("Animation", 14)] = 59,
        [("Animation", 13)] = 58,
        [("Animation", 12)] = 57,
        [("Animation", 11)] = 56,
        [("Animation", 10)] = 55,
        [("Animation", 9)] = 54,
        [("Animation", 8)] = 53,
        [("Animation", 7)] = 52,
        [("Animation", 6)] = 51,
        [("Animation", 5)] = 50,
        [("Animation", 4)] = 49,
        [("Animation", 3)] = 48,
        [("Animation", 2)] = 47,
        [("Animation", 1)] = 46,
        [("Skeleton", 1)] = 22,
        [("Skeleton", 104)] = 23,
        [("Skeleton", 108)] = 24,
        [("Skeleton", 110)] = 25,
        [("Skeleton", 214)] = 26,
        [("Skeleton", 217)] = 27,
        [("Skeleton", 220)] = 28,
        [("Skeleton", 223)] = 29,
        [("Skeleton", 226)] = 30,
        [("Skeleton", 229)] = 31,
        [("Skeleton", 232)] = 32,
        [("Skeleton", 237)] = 33,
        [("Skeleton", 240)] = 34,
        [("Skeleton", 241)] = 35,
        [("Skeleton", 245)] = 36,
        [("DruidPet", -1)] = 11,
        [("SumMageMultiElement", -1)] = 4,
    };

    public static readonly string[] Names =
    [
        "Gabeker", "Gann", "Garanab", "Garn", "Gartik", "Gebann", "Gebekn", "Gekn", "Geraner", "Gobeker", "Gonobtik", "Jabantik", "Jasarab",
        "Jasober", "Jeker", "Jenaner", "Jenarer", "Jobantik", "Jobekn", "Jonartik", "Kabann", "Kabartik", "Karn", "Kasarer", "Kasekn",
        "Kebekn", "Keber", "Kebtik", "Kenantik", "Kenn", "Kentik", "Kibekab", "Kobarer", "Kobobtik", "Konaner", "Konarer", "Konekn", "Konn",
        "Labann", "Lararer", "Lasobtik", "Lebantik", "Lebarab", "Libantik", "Libtik", "Lobn", "Lobtik", "Lonaner", "Lonobtik", "Varekab",
        "Vaseker", "Vebobab", "Venarn", "Venekn", "Vener", "Vibobn", "Vobtik", "Vonarer", "Vonartik", "Xabtik", "Xarantik", "Xarar",
        "Xarer", "Xeber", "Xebn", "Xenartik", "Xeratik", "Xesekn", "Xonartik", "Zabantik", "Zabn", "Zabeker", "Zanab", "Zaner", "Zenann",
        "Zonarer", "Zonarn",
    ];

    /// <summary>The pets id for a spell's pet name, or null.</summary>
    public static int? TypeFor(string petName)
    {
        foreach (var ((prefix, number), id) in Types)
        {
            if (!petName.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (number < 0)
                return id;
            // atoi of the rest, kept in an 8-bit variable as the legacy zone did.
            var digits = new string(petName.Substring(prefix.Length).TakeWhile(char.IsAsciiDigit).ToArray());
            if (int.TryParse(digits, out int n) && (n & 0xFF) == number)
                return id;
        }
        return null;
    }
}
