using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace EQClassic.Server.Characters;

/// <summary>
/// Builds legacy profile blobs for new characters. The base is the OP_CharacterCreate payload
/// captured from the Trilogy client (tools/eqbot/charcreate_template.inc, a troll shaman): every
/// field the client fills (skills, languages, spell slots, empty inventory...) has a value the C++
/// zone server accepts. Race, class and the choices of the player are then written over it.
/// Known limit: skills and languages stay the troll shaman's until the race/class tables are ported.
/// </summary>
public static class ProfileTemplate
{
    public const int ProfileLength = 8104;
    private const int InventoryOffset = 168, InventorySlots = 30, ItemPropertiesOffset = 348, ItemPropertiesSize = 10, ChargesOffset = 2;
    public const int FirstGeneralSlot = 22;
    public const ushort NoItem = 0xFFFF;

    private static readonly Lazy<byte[]> Base = new(LoadBase);

    /// <summary>A fresh copy of the template, as a stored profile (4-byte checksum first).</summary>
    public static byte[] NewProfile() => (byte[])Base.Value.Clone();

    public static void SetName(byte[] p, string name) => WriteString(p, 4, 30, name);
    public static void SetGender(byte[] p, int gender) => p[54] = (byte)gender;
    public static void SetDeity(byte[] p, int deity) => p[55] = (byte)deity; // int8 in the struct: deities above 255 wrap as in the client
    public static void SetRace(byte[] p, int race) => BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(56), (short)race);
    public static void SetClass(byte[] p, int @class) => p[58] = (byte)@class;
    public static void SetLevel(byte[] p, int level) => p[60] = (byte)level;
    public static void SetFace(byte[] p, int face) => p[72] = (byte)face;
    public static void SetZone(byte[] p, string zone) => WriteString(p, 2424, 15, zone);
    /// <summary>languages[24] at 130: every language to 0, then the given skills.</summary>
    public static void SetLanguages(byte[] p, IReadOnlyDictionary<int, int> languages)
    {
        p.AsSpan(130, 24).Clear();
        foreach (var (id, skill) in languages)
            if (id is >= 0 and < 24)
                p[130 + id] = (byte)skill;
    }

    /// <summary>Bind point: zone, and the same place in the five bind_location slots (slot 0 is where death sends you).</summary>
    public static void SetBind(byte[] p, string zone, float x, float y, float z)
    {
        WriteString(p, PlayerProfile.BindZoneOffset, PlayerProfile.BindZoneLength, zone);
        for (int slot = 0; slot < 5; slot++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(PlayerProfile.BindYOffset + 4 * slot), y);
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(PlayerProfile.BindXOffset + 4 * slot), x);
            BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(PlayerProfile.BindZOffset + 4 * slot), z);
        }
    }

    public static void SetCoins(byte[] p, EQClassic.Server.Zone.Coins c)
    {
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(PlayerProfile.CoinsOffset), c.Platinum);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(PlayerProfile.CoinsOffset + 4), c.Gold);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(PlayerProfile.CoinsOffset + 8), c.Silver);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(PlayerProfile.CoinsOffset + 12), c.Copper);
    }

    public static void SetExp(byte[] p, uint exp) => BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(PlayerProfile.ExpOffset), exp);
    public static void SetCurHp(byte[] p, int hp) => BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(PlayerProfile.CurHpOffset), (short)Math.Clamp(hp, 0, short.MaxValue));

    public static void SetStats(byte[] p, int str, int sta, int cha, int dex, int @int, int agi, int wis)
    {
        p[123] = (byte)str; p[124] = (byte)sta; p[125] = (byte)cha; p[126] = (byte)dex;
        p[127] = (byte)@int; p[128] = (byte)agi; p[129] = (byte)wis;
    }

    public static void SetPosition(byte[] p, float x, float y, float z)
    {
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(2408), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(2412), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(2416), z);
    }

    public static ushort GetItem(byte[] p, int slot) => BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(InventoryOffset + 2 * slot));

    public static sbyte GetCharges(byte[] p, int slot) => (sbyte)p[ItemPropertiesOffset + ItemPropertiesSize * slot + ChargesOffset];

    public static void SetItem(byte[] p, int slot, ushort item, sbyte charges)
    {
        if ((uint)slot >= InventorySlots)
            throw new ArgumentOutOfRangeException(nameof(slot));
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(InventoryOffset + 2 * slot), item);
        p[ItemPropertiesOffset + ItemPropertiesSize * slot + ChargesOffset] = (byte)charges;
    }

    private static void WriteString(byte[] p, int offset, int length, string value)
    {
        p.AsSpan(offset, length).Clear();
        var bytes = Encoding.Latin1.GetBytes(value);
        bytes.AsSpan(0, Math.Min(bytes.Length, length - 1)).CopyTo(p.AsSpan(offset));
    }

    private static byte[] LoadBase()
    {
        using var stream = typeof(ProfileTemplate).Assembly.GetManifestResourceStream("charcreate_template.inc")
            ?? throw new InvalidOperationException("embedded charcreate_template.inc missing");
        using var reader = new StreamReader(stream);
        var payload = new List<byte>(ProfileLength);
        while (reader.ReadLine() is { } line)
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                continue;
            foreach (var token in line.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                payload.Add(byte.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        if (payload.Count != ProfileLength - PlayerProfile.ChecksumLength)
            throw new InvalidOperationException($"template has {payload.Count} bytes, expected {ProfileLength - PlayerProfile.ChecksumLength}");
        var profile = new byte[ProfileLength];
        payload.CopyTo(profile, PlayerProfile.ChecksumLength);
        return profile;
    }
}
