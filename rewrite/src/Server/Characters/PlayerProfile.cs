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
    public const int ExpOffset = 64;
    // bind_point_zone[20], then bind_location as y[5], x[5], z[5] (slot 0 is where death sends you).
    // The header puts bind_location at 3828; the stored profiles have it 4 bytes later.
    public const int BindZoneOffset = 2844, BindZoneLength = 20, BindYOffset = 3832, BindXOffset = 3852, BindZOffset = 3872;
    private const int StrOffset = 123, StaOffset = 124, DexOffset = 126, AgiOffset = 128;
    private const int InventoryOffset = 168, InventorySlots = 30;
    // invItemProprieties[30] at 348, 10 bytes each: charges at +2. Money at 2460 (platinum, gold, silver, copper).
    private const int ItemPropertiesOffset = 348, ItemPropertiesSize = 10;
    public const int CoinsOffset = 2460;
    private const int SkillsOffset = 2508, SkillCount = 74;
    // mana (int16) at 70, INT 127, WIS 129; spell_book[256] and spell_memory[8] (int16, 0xFFFF: empty).
    /// <summary>hungerlevel and thirstlevel (int32): 0 starving, up to 32000.</summary>
    public const int HungerOffset = 2812, ThirstOffset = 2816;
    public const int ManaOffset = 70, IntOffset = 127, WisOffset = 129;
    public const int SpellBookOffset = 1878, SpellBookSlots = 256, SpellGemsOffset = 2390, SpellGems = 8;
    // buffs[15] at 648, SpellBuff_Struct of 10 bytes: caster level at +1, spell id (0xFFFF: none) at +4, tics at +6.
    public const int BuffsOffset = 648, BuffSize = 10, BuffSlots = 15;

    // Combat fields (zone server). Empty arrays when the profile is too short to hold them.
    public int CurHp { get; init; }
    public int Str { get; init; }
    public int Sta { get; init; }
    public int Dex { get; init; }
    public int Agi { get; init; }
    /// <summary>Item ids by slot (Trilogy slots: 0-21 worn, 13 primary, 14 secondary, 22-29 general).</summary>
    public IReadOnlyList<int> Inventory { get; init; } = Array.Empty<int>();
    /// <summary>Charges of the item in each slot (stack size, food and drink portions).</summary>
    public IReadOnlyList<int> Charges { get; init; } = Array.Empty<int>();
    /// <summary>The contents of the bags in the general slots (containerinv[80], 10 per bag) and their charges.</summary>
    public IReadOnlyList<int> BagItems { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> BagCharges { get; init; } = Array.Empty<int>();
    public const int BagItemsOffset = 798, BagPropertiesOffset = 978, BagSlotsTotal = 80;
    /// <summary>The bank: bank_inv[8] and bank_cont_inv[80] (item ids), their properties (charges at +2), the bank's money.</summary>
    public IReadOnlyList<int> BankItems { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> BankCharges { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> BankBagItems { get; init; } = Array.Empty<int>();
    public IReadOnlyList<int> BankBagCharges { get; init; } = Array.Empty<int>();
    public EQClassic.Server.Zone.Coins BankCoins { get; init; }
    public const int BankItemsOffset = 3980, BankBagItemsOffset = 3996, BankPropertiesOffset = 2944, BankBagPropertiesOffset = 3024,
        BankCoinsOffset = 2476, BankSlots = 8;
    public EQClassic.Server.Zone.Coins Coins { get; init; }
    /// <summary>Skill values by skill id; 254 (not trained yet) and 255 (cannot learn) read as 0.</summary>
    public IReadOnlyList<int> Skills { get; init; } = Array.Empty<int>();

    public int Skill(int id) => id < Skills.Count ? Skills[id] : 0;

    public int Int { get; init; }
    public int Wis { get; init; }
    public int Mana { get; init; }
    /// <summary>Food and drink levels: 0 starving or parched, 6000 full (a truncated profile reads full).</summary>
    public int Hunger { get; init; } = 6000;
    public int Thirst { get; init; } = 6000;
    /// <summary>Spell ids by spell book page slot, −1 for an empty slot.</summary>
    public IReadOnlyList<int> SpellBook { get; init; } = Array.Empty<int>();
    /// <summary>Spell ids memorised in the 8 gems, −1 for an empty gem.</summary>
    public IReadOnlyList<int> SpellGemIds { get; init; } = Array.Empty<int>();
    /// <summary>Buffs on the character: spell id, caster level, tics left.</summary>
    public IReadOnlyList<(int SpellId, int CasterLevel, int Tics)> Buffs { get; init; } = Array.Empty<(int, int, int)>();

    public uint Exp { get; init; }
    /// <summary>Where death sends the character (bind point, slot 0); empty zone when unknown.</summary>
    public string BindZone { get; init; } = "";
    public float BindX { get; init; }
    public float BindY { get; init; }
    public float BindZ { get; init; }

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
            Exp = BinaryPrimitives.ReadUInt32LittleEndian(profile[ExpOffset..]),
            BindZone = profile.Length >= BindZOffset + 4 ? CString(profile.Slice(BindZoneOffset, BindZoneLength)) : "",
            BindX = profile.Length >= BindZOffset + 4 ? BinaryPrimitives.ReadSingleLittleEndian(profile[BindXOffset..]) : 0,
            BindY = profile.Length >= BindZOffset + 4 ? BinaryPrimitives.ReadSingleLittleEndian(profile[BindYOffset..]) : 0,
            BindZ = profile.Length >= BindZOffset + 4 ? BinaryPrimitives.ReadSingleLittleEndian(profile[BindZOffset..]) : 0,
            Str = profile[StrOffset],
            Sta = profile[StaOffset],
            Dex = profile[DexOffset],
            Agi = profile[AgiOffset],
            Int = profile[IntOffset],
            Wis = profile[WisOffset],
            Mana = BinaryPrimitives.ReadInt16LittleEndian(profile[ManaOffset..]),
            Hunger = profile.Length >= ThirstOffset + 4 ? BinaryPrimitives.ReadInt32LittleEndian(profile[HungerOffset..]) : 6000,
            Thirst = profile.Length >= ThirstOffset + 4 ? BinaryPrimitives.ReadInt32LittleEndian(profile[ThirstOffset..]) : 6000,
            SpellBook = ReadSpells(profile, SpellBookOffset, SpellBookSlots),
            SpellGemIds = ReadSpells(profile, SpellGemsOffset, SpellGems),
            Buffs = ReadBuffs(profile),
            Inventory = ReadInventory(profile),
            Charges = ReadCharges(profile),
            BagItems = ReadIds(profile, BagItemsOffset, BagSlotsTotal),
            BagCharges = ReadBagCharges(profile),
            BankItems = profile.Length >= BankBagItemsOffset + 2 * BagSlotsTotal ? ReadIds(profile, BankItemsOffset, BankSlots) : Array.Empty<int>(),
            BankBagItems = profile.Length >= BankBagItemsOffset + 2 * BagSlotsTotal ? ReadIds(profile, BankBagItemsOffset, BagSlotsTotal) : Array.Empty<int>(),
            BankCharges = ReadChargesAt(profile, BankPropertiesOffset, BankSlots),
            BankBagCharges = ReadChargesAt(profile, BankBagPropertiesOffset, BagSlotsTotal),
            BankCoins = new EQClassic.Server.Zone.Coins(
                BinaryPrimitives.ReadInt32LittleEndian(profile[BankCoinsOffset..]), BinaryPrimitives.ReadInt32LittleEndian(profile[(BankCoinsOffset + 4)..]),
                BinaryPrimitives.ReadInt32LittleEndian(profile[(BankCoinsOffset + 8)..]), BinaryPrimitives.ReadInt32LittleEndian(profile[(BankCoinsOffset + 12)..])),
            Coins = new EQClassic.Server.Zone.Coins(
                BinaryPrimitives.ReadInt32LittleEndian(profile[CoinsOffset..]), BinaryPrimitives.ReadInt32LittleEndian(profile[(CoinsOffset + 4)..]),
                BinaryPrimitives.ReadInt32LittleEndian(profile[(CoinsOffset + 8)..]), BinaryPrimitives.ReadInt32LittleEndian(profile[(CoinsOffset + 12)..])),
            Skills = profile.Length >= SkillsOffset + SkillCount
                ? profile.Slice(SkillsOffset, SkillCount).ToArray().Select(b => b >= 254 ? 0 : (int)b).ToArray()
                : Array.Empty<int>(),
        };
    }

    private static int[] ReadChargesAt(ReadOnlySpan<byte> profile, int offset, int count)
    {
        var charges = new int[count];
        for (int i = 0; i < count && offset + ItemPropertiesSize * i + 2 < profile.Length; i++)
            charges[i] = (sbyte)profile[offset + ItemPropertiesSize * i + 2];
        return charges;
    }

    private static int[] ReadBagCharges(ReadOnlySpan<byte> profile)
    {
        var charges = new int[BagSlotsTotal];
        for (int i = 0; i < BagSlotsTotal; i++)
            charges[i] = (sbyte)profile[BagPropertiesOffset + ItemPropertiesSize * i + 2];
        return charges;
    }

    private static int[] ReadIds(ReadOnlySpan<byte> profile, int offset, int count)
    {
        var ids = new int[count];
        for (int i = 0; i < count; i++)
            ids[i] = BinaryPrimitives.ReadUInt16LittleEndian(profile[(offset + 2 * i)..]) is var id && id != 0xFFFF ? id : 0;
        return ids;
    }

    private static int[] ReadInventory(ReadOnlySpan<byte> profile)
    {
        var slots = new int[InventorySlots];
        for (int i = 0; i < InventorySlots; i++)
            slots[i] = BinaryPrimitives.ReadUInt16LittleEndian(profile[(InventoryOffset + 2 * i)..]) is var id && id != 0xFFFF ? id : 0; // 0xFFFF: empty
        return slots;
    }

    private static List<(int, int, int)> ReadBuffs(ReadOnlySpan<byte> profile)
    {
        var buffs = new List<(int, int, int)>();
        for (int i = 0; i < BuffSlots; i++)
        {
            var b = profile.Slice(BuffsOffset + BuffSize * i, BuffSize);
            int spell = BinaryPrimitives.ReadUInt16LittleEndian(b[4..]);
            int tics = BinaryPrimitives.ReadInt32LittleEndian(b[6..]);
            if (spell is not (0xFFFF or 0) && tics > 0)
                buffs.Add((spell, b[1], tics));
        }
        return buffs;
    }

    private static int[] ReadSpells(ReadOnlySpan<byte> profile, int offset, int count)
    {
        var spells = new int[count];
        for (int i = 0; i < count; i++)
            spells[i] = BinaryPrimitives.ReadUInt16LittleEndian(profile[(offset + 2 * i)..]) is var id && id is not (0xFFFF or 0) ? id : -1; // 0 is no spell either
        return spells;
    }

    private static int[] ReadCharges(ReadOnlySpan<byte> profile)
    {
        var charges = new int[InventorySlots];
        for (int i = 0; i < InventorySlots; i++)
            charges[i] = (sbyte)profile[ItemPropertiesOffset + ItemPropertiesSize * i + 2];
        return charges;
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
