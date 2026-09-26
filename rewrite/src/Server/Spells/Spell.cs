using System.Buffers.Binary;
using System.Text;

namespace EQClassic.Server.Spells;

/// <summary>Spell effect ids used by the rewrite (legacy spdat.h TSpellEffect).</summary>
public static class SpellEffect
{
    public const int CurrentHp = 0, ArmorClass = 1, Atk = 2, MovementSpeed = 3, Str = 4, Dex = 5, Agi = 6, Sta = 7, Int = 8, Wis = 9,
        Cha = 10, AttackSpeed = 11, SeeInvis = 13, WaterBreathing = 14, CurrentMana = 15, Blind = 20, ResistFire = 46, ResistCold = 47,
        ResistPoison = 48, ResistDisease = 49, ResistMagic = 50, DamageShield = 59, InfraVision = 65, UltraVision = 66, TotalHp = 69,
        CurrentHpOnce = 79, MagnifyVision = 87, HealOverTime = 100, CompleteHeal = 101,
        Invisibility = 12, Stun = 21, BindAffinity = 25, Gate = 26, InvisVsUndead = 28, Mez = 31, SummonItem = 32, Levitate = 57,
        Teleport = 83, Root = 99;
    /// <summary>Unused effect slot.</summary>
    public const int Blank = 254;
}

/// <summary>Spell target types used by the rewrite (legacy spdat.h TTargetType).</summary>
public static class SpellTarget
{
    public const int LineOfSight = 1, GroupV1 = 3, AECaster = 4, Single = 5, Self = 6, AETarget = 8, Animal = 9,
        Undead = 10, Summoned = 11, Tap = 13, Pet = 14, Corpse = 15, Plant = 16, Giant = 17, Dragon = 18, GroupV2 = 41;
}

/// <summary>
/// One record of spdat.eff, the client's spell file that the legacy zone reads at start
/// (SpellsHandler::LoadSpells): 3000 records of SPDat_Spell_Struct (Zone/Include/spdat.h, 608 bytes).
/// </summary>
public sealed record Spell(int Id, string Name)
{
    public const int RecordSize = 608, EffectCount = 12, ClassCount = 15;
    /// <summary>classes[] values above this mean the class never gets the spell (the file uses 61 and 255).</summary>
    public const int MaxLevel = 60;

    /// <summary>teleport_zone: the zone of a teleport, or the item a summoning spell makes.</summary>
    public string TeleportZone { get; init; } = "";
    public string YouCast { get; init; } = "";
    public string OtherCasts { get; init; } = "";
    public string CastOnYou { get; init; } = "";
    public string CastOnOther { get; init; } = "";
    public string Fades { get; init; } = "";
    public float Range { get; init; }
    public float AoeRange { get; init; }
    public int CastTimeMs { get; init; }
    public int RecoveryMs { get; init; }
    public int RecastMs { get; init; }
    public int DurationFormula { get; init; }
    public int Duration { get; init; }
    public int Mana { get; init; }
    public short[] Base { get; init; } = new short[EffectCount];
    public short[] Max { get; init; } = new short[EffectCount];
    public byte[] Formula { get; init; } = new byte[EffectCount];
    public byte[] Effect { get; init; } = new byte[EffectCount];
    public int Icon { get; init; }
    public int MemIcon { get; init; }
    public bool Beneficial { get; init; }
    public int ResistType { get; init; }
    public int TargetType { get; init; }
    public int BaseDifficulty { get; init; }
    public int Skill { get; init; }
    /// <summary>Level at which each class (index class − 1) gets the spell; above 60 when it never does.</summary>
    public byte[] Classes { get; init; } = new byte[ClassCount];

    /// <summary>Spell::IsBuffSpell: a spell with a duration formula lasts (as a buff or a debuff).</summary>
    public bool IsBuff => DurationFormula > 0;
    /// <summary>A real spell (records with no name or no effect are holes in the file).</summary>
    public bool IsValid => Name.Length > 0 && Effect.Any(e => e != SpellEffect.Blank);

    /// <summary>Level at which <paramref name="classId"/> (1-15) gets the spell, or null.</summary>
    public int? LevelFor(int classId) =>
        classId is >= 1 and <= ClassCount && Classes[classId - 1] is var l && l <= MaxLevel ? l : null;

    /// <summary>
    /// SpellsHandler::CalcSpellValue: effect <paramref name="index"/>'s amount for a caster of
    /// <paramref name="casterLevel"/>. Formula 100 (or 0) is the base; 101-105 add level/2, level,
    /// 2, 3 or 4 × level; 108-110 level/3, /4, /5; 119 level/8; 121 level/3; below 100, level ×
    /// formula. The amount stops at max (when set) and takes the sign of base (zero counts as positive).
    /// </summary>
    public int Value(int index, int casterLevel)
    {
        int formula = Formula[index], b = Base[index], max = Max[index];
        if (Effect[index] == SpellEffect.SummonItem)
            (b, max) = (0, 20);
        int ubase = Math.Abs(b);
        int result = formula switch
        {
            0x64 or 0x00 => ubase,
            0x65 => ubase + casterLevel / 2,
            0x66 => ubase + casterLevel,
            0x67 => ubase + casterLevel * 2,
            0x68 => ubase + casterLevel * 3,
            0x69 => ubase + casterLevel * 4,
            0x6c => ubase + casterLevel / 3,
            0x6d => ubase + casterLevel / 4,
            0x6e => ubase + casterLevel / 5,
            0x77 => ubase + casterLevel / 8,
            0x79 => ubase + casterLevel / 3,
            < 100 => ubase + casterLevel * formula,
            _ => 0, // unknown formula (the legacy zone logs it)
        };
        result = (short)result; // sint16 in the legacy zone
        int sign = b < 0 ? -1 : 1; // the legacy sign(): zero counts as positive
        return result >= max && max != 0 ? max * sign : result * sign;
    }

    private static readonly Encoding Latin1 = Encoding.Latin1;

    /// <summary>Reads every record of spdat.eff; ids are record indexes.</summary>
    public static IReadOnlyList<Spell> ReadFile(ReadOnlySpan<byte> file)
    {
        var spells = new Spell[file.Length / RecordSize];
        for (int i = 0; i < spells.Length; i++)
            spells[i] = Read(i, file.Slice(i * RecordSize, RecordSize));
        return spells;
    }

    public static Spell Read(int id, ReadOnlySpan<byte> r)
    {
        var effects = r.Slice(546, EffectCount).ToArray();
        var bases = new short[EffectCount];
        var maxes = new short[EffectCount];
        for (int i = 0; i < EffectCount; i++)
        {
            bases[i] = BinaryPrimitives.ReadInt16LittleEndian(r[(458 + 2 * i)..]);
            maxes[i] = BinaryPrimitives.ReadInt16LittleEndian(r[(482 + 2 * i)..]);
            // LoadSpells: the file uses CHA with base 0 as a spacer; it is no effect at all.
            if (effects[i] == SpellEffect.Cha && bases[i] == 0)
                effects[i] = SpellEffect.Blank;
        }
        return new Spell(id, Text(r.Slice(0, 32)))
        {
            TeleportZone = Text(r.Slice(64, 32)),
            YouCast = Text(r.Slice(96, 64)),
            OtherCasts = Text(r.Slice(160, 64)),
            CastOnYou = Text(r.Slice(224, 64)),
            CastOnOther = Text(r.Slice(288, 64)),
            Fades = Text(r.Slice(352, 64)),
            Range = BinaryPrimitives.ReadSingleLittleEndian(r[416..]),
            AoeRange = BinaryPrimitives.ReadSingleLittleEndian(r[420..]),
            CastTimeMs = BinaryPrimitives.ReadInt32LittleEndian(r[432..]),
            RecoveryMs = BinaryPrimitives.ReadInt32LittleEndian(r[436..]),
            RecastMs = BinaryPrimitives.ReadInt32LittleEndian(r[440..]),
            DurationFormula = r[444],
            Duration = BinaryPrimitives.ReadInt32LittleEndian(r[448..]),
            Mana = BinaryPrimitives.ReadUInt16LittleEndian(r[456..]),
            Base = bases,
            Max = maxes,
            Icon = BinaryPrimitives.ReadUInt16LittleEndian(r[506..]),
            MemIcon = BinaryPrimitives.ReadUInt16LittleEndian(r[508..]),
            Formula = r.Slice(530, EffectCount).ToArray(),
            Beneficial = r[543] != 0,
            ResistType = r[545],
            Effect = effects,
            TargetType = r[558],
            BaseDifficulty = r[559],
            Skill = r[560],
            Classes = r.Slice(565, ClassCount).ToArray(),
        };
    }

    private static string Text(ReadOnlySpan<byte> s)
    {
        int end = s.IndexOf((byte)0);
        return Latin1.GetString(end < 0 ? s : s[..end]);
    }
}
