using System.Buffers.Binary;
using System.Text;

namespace EQClassic.Server.Characters;

/// <summary>
/// The fields of the legacy PlayerProfile_Struct (Common/Include/PlayerProfile.h) that World needs,
/// read from character_.profile. Offsets are the header's, which count a 4-byte checksum first; the
/// client's OP_CharacterCreate payload omits it (<see cref="ReadWithoutChecksum"/>).
/// </summary>
public sealed record PlayerProfile(
    string Name, int Gender, int Deity, int Race, int Class, int Level,
    float X, float Y, float Z, float Heading, string Zone)
{
    public const int ChecksumLength = 4;
    public const int MinimumLength = 2439; // up to the end of current_zone[15]

    private const int NameOffset = 4, NameLength = 30;
    private const int GenderOffset = 54, DeityOffset = 55, RaceOffset = 56, ClassOffset = 58, LevelOffset = 60;
    private const int YOffset = 2408, XOffset = 2412, ZOffset = 2416, HeadingOffset = 2420;
    private const int ZoneOffset = 2424, ZoneLength = 15;
    public const int CurHpOffset = 120;
    private const int StrOffset = 123, StaOffset = 124, DexOffset = 126, AgiOffset = 128;
    private const int InventoryOffset = 168, InventorySlots = 30;
    private const int SkillsOffset = 2508, SkillCount = 74;

    // Combat fields (zone server). Empty arrays when the profile is too short to hold them.
    public int CurHp { get; init; }
    public int Str { get; init; }
    public int Sta { get; init; }
    public int Dex { get; init; }
    public int Agi { get; init; }
    /// <summary>Item ids by slot (Trilogy slots: 0-21 worn, 13 primary, 14 secondary, 22-29 general).</summary>
    public IReadOnlyList<int> Inventory { get; init; } = Array.Empty<int>();
    /// <summary>Skill values by skill id; 254 (not trained yet) and 255 (cannot learn) read as 0.</summary>
    public IReadOnlyList<int> Skills { get; init; } = Array.Empty<int>();

    public int Skill(int id) => id < Skills.Count ? Skills[id] : 0;

    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>Returns null for a missing or truncated profile (a name reserved but never created).</summary>
    public static PlayerProfile? Read(ReadOnlySpan<byte> profile)
    {
        if (profile.Length < MinimumLength)
            return null;
        return new PlayerProfile(
            Name: CString(profile.Slice(NameOffset, NameLength)),
            Gender: profile[GenderOffset],
            Deity: profile[DeityOffset],
            Race: BinaryPrimitives.ReadInt16LittleEndian(profile[RaceOffset..]),
            Class: profile[ClassOffset],
            Level: profile[LevelOffset],
            X: BinaryPrimitives.ReadSingleLittleEndian(profile[XOffset..]),
            Y: BinaryPrimitives.ReadSingleLittleEndian(profile[YOffset..]),
            Z: BinaryPrimitives.ReadSingleLittleEndian(profile[ZOffset..]),
            Heading: BinaryPrimitives.ReadSingleLittleEndian(profile[HeadingOffset..]),
            Zone: CString(profile.Slice(ZoneOffset, ZoneLength)))
        {
            CurHp = BinaryPrimitives.ReadInt16LittleEndian(profile[CurHpOffset..]),
            Str = profile[StrOffset],
            Sta = profile[StaOffset],
            Dex = profile[DexOffset],
            Agi = profile[AgiOffset],
            Inventory = ReadInventory(profile),
            Skills = profile.Length >= SkillsOffset + SkillCount
                ? profile.Slice(SkillsOffset, SkillCount).ToArray().Select(b => b >= 254 ? 0 : (int)b).ToArray()
                : Array.Empty<int>(),
        };
    }

    private static int[] ReadInventory(ReadOnlySpan<byte> profile)
    {
        var slots = new int[InventorySlots];
        for (int i = 0; i < InventorySlots; i++)
            slots[i] = BinaryPrimitives.ReadUInt16LittleEndian(profile[(InventoryOffset + 2 * i)..]) is var id && id != 0xFFFF ? id : 0; // 0xFFFF: empty
        return slots;
    }

    /// <summary>For the client's creation packet, which is the profile without its checksum.</summary>
    public static PlayerProfile? ReadWithoutChecksum(ReadOnlySpan<byte> payload)
    {
        var withChecksum = new byte[payload.Length + ChecksumLength];
        payload.CopyTo(withChecksum.AsSpan(ChecksumLength));
        return Read(withChecksum);
    }

    private static string CString(ReadOnlySpan<byte> field)
    {
        int end = field.IndexOf((byte)0);
        return Latin1.GetString(end < 0 ? field : field[..end]);
    }
}
