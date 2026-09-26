namespace EQClassic.Server.Spells;

/// <summary>A spell effect lasting on someone: the spell, who cast it at what level, tics (6 s) left.</summary>
public sealed record Buff(Spell Spell, int CasterId, int CasterLevel, int TicsLeft)
{
    public int TicsLeft { get; set; } = TicsLeft;
    /// <summary>What is left of a rune (Spell::GetRuneAmount: the base of its rune effect) to absorb damage.</summary>
    public int RuneLeft { get; set; } = RuneAmount(Spell);

    public static int RuneAmount(Spell spell)
    {
        for (int i = 0; i < Spell.EffectCount; i++)
            if (spell.Effect[i] == SpellEffect.Rune)
                return spell.Base[i];
        return 0;
    }
}

/// <summary>
/// What the buffs on someone add up to (Mob::ApplySpellsBonuses, Zone/Source/SpellBonuses.cpp):
/// armour class, attack, stats, maximum hit points, hit points and mana per tic (damage and heals
/// over time), resists, the best haste and the worst slow, movement speed.
/// </summary>
public sealed record StatBonuses
{
    public static readonly StatBonuses None = new();

    public int AC { get; init; }
    public int Atk { get; init; }
    public int Str { get; init; }
    public int Sta { get; init; }
    public int Agi { get; init; }
    public int Dex { get; init; }
    public int Int { get; init; }
    public int Wis { get; init; }
    public int Cha { get; init; }
    public int Hp { get; init; }
    /// <summary>Hit points per tic from damage and heals over time (negative: damage).</summary>
    public int HpPerTic { get; init; }
    public int ManaPerTic { get; init; }
    public int MR { get; init; }
    public int FR { get; init; }
    public int CR { get; init; }
    public int PR { get; init; }
    public int DR { get; init; }
    /// <summary>Percent faster (haste) and slower (slow) swings; only the strongest of each counts.</summary>
    public int Haste { get; init; }
    public int Slow { get; init; }
    /// <summary>Percent added to movement speed (negative: snare).</summary>
    public int MovementSpeed { get; init; }
    public bool Invisible { get; init; }
    public bool InvisibleToUndead { get; init; }
    public bool Rooted { get; init; }
    public bool Mezzed { get; init; }
    public bool Levitating { get; init; }
    /// <summary>Damage shields: damage to whoever hits in melee, with the spell's resist type for the message; reverse ones heal them.</summary>
    public int DamageShield { get; init; }
    public int DamageShieldType { get; init; }
    public int ReverseDamageShield { get; init; }
    /// <summary>Lull spells (SE_ChangeFrenzyRad): the NPC's aggro range becomes 145 − value².</summary>
    public int FrenzyRadius { get; init; }
    public bool Feared { get; init; }
    /// <summary>Illusions: the race the entity looks like (the base of the newest illusion), 0 for its own.</summary>
    public int IllusionRace { get; init; }

    /// <summary>
    /// Adds up the buffs. AC on beneficial spells goes through the legacy AproximateSpellAC curve;
    /// attack speed values are percentages around 100 (the legacy zone has a per-spell table for
    /// the songs and a few spells, not reproduced).
    /// </summary>
    public static StatBonuses From(IEnumerable<Buff> buffs)
    {
        int ac = 0, atk = 0, str = 0, sta = 0, agi = 0, dex = 0, @int = 0, wis = 0, cha = 0, hp = 0, hpTic = 0, manaTic = 0;
        int mr = 0, fr = 0, cr = 0, pr = 0, dr = 0, haste = 0, slow = 0, speed = 0;
        bool invisible = false, invisibleToUndead = false, rooted = false, mezzed = false, levitating = false, feared = false;
        int damageShield = 0, damageShieldType = 0, reverseDamageShield = 0, frenzyRadius = 0, illusion = 0;
        foreach (var buff in buffs)
        {
            var s = buff.Spell;
            for (int i = 0; i < Spell.EffectCount; i++)
            {
                int effect = s.Effect[i];
                if (effect == SpellEffect.Blank)
                    continue;
                int v = s.Value(i, buff.CasterLevel);
                switch (effect)
                {
                    case SpellEffect.ArmorClass: ac += s.Beneficial ? ApproximateSpellAC(v) : v; break;
                    case SpellEffect.Atk: atk += v; break;
                    case SpellEffect.Str: str += v; break;
                    case SpellEffect.Dex: dex += v; break;
                    case SpellEffect.Agi: agi += v; break;
                    case SpellEffect.Sta: sta += v; break;
                    case SpellEffect.Int: @int += v; break;
                    case SpellEffect.Wis: wis += v; break;
                    case SpellEffect.Cha: cha += v; break;
                    case SpellEffect.TotalHp: hp += v; break;
                    case SpellEffect.CurrentHp:
                    case SpellEffect.HealOverTime: hpTic += v; break;
                    case SpellEffect.CurrentMana: manaTic += v; break;
                    case SpellEffect.ResistMagic: mr += v; break;
                    case SpellEffect.ResistFire: fr += v; break;
                    case SpellEffect.ResistCold: cr += v; break;
                    case SpellEffect.ResistPoison: pr += v; break;
                    case SpellEffect.ResistDisease: dr += v; break;
                    case SpellEffect.MovementSpeed: speed += v; break;
                    case SpellEffect.Invisibility: invisible = true; break;
                    case SpellEffect.InvisVsUndead: invisibleToUndead = true; break;
                    case SpellEffect.Root: rooted = true; break;
                    case SpellEffect.Mez: mezzed = true; break;
                    case SpellEffect.Levitate: levitating = true; break;
                    case SpellEffect.DamageShield when v < 0:
                        damageShield += -v;
                        damageShieldType = s.ResistType;
                        break;
                    case SpellEffect.DamageShield: reverseDamageShield += v; break;
                    case SpellEffect.FrenzyRadius: frenzyRadius += v; break;
                    case SpellEffect.Fear: feared = true; break;
                    case SpellEffect.Illusion: illusion = s.Base[i]; break;
                    case SpellEffect.AttackSpeed:
                        if (v > 100) haste = Math.Max(haste, v - 100);
                        else if (v > 0 && v < 100) slow = Math.Max(slow, 100 - v);
                        break;
                }
            }
        }
        return new StatBonuses
        {
            AC = ac, Atk = atk, Str = str, Sta = sta, Agi = agi, Dex = dex, Int = @int, Wis = wis, Cha = cha, Hp = hp,
            HpPerTic = hpTic, ManaPerTic = manaTic, MR = mr, FR = fr, CR = cr, PR = pr, DR = dr, Haste = haste, Slow = slow,
            MovementSpeed = speed, Invisible = invisible, InvisibleToUndead = invisibleToUndead, Rooted = rooted, Mezzed = mezzed,
            Levitating = levitating, DamageShield = damageShield, DamageShieldType = damageShieldType, ReverseDamageShield = reverseDamageShield,
            FrenzyRadius = frenzyRadius, Feared = feared, IllusionRace = illusion,
        };
    }

    /// <summary>SpellsHandler::AproximateSpellAC: the spell file's AC to the server's AC scale.</summary>
    public static int ApproximateSpellAC(float serverAC)
    {
        const double a0 = 1.515301796169375, a1 = 0.17777381516761756, a2 = 0.0024143132498674513,
            a3 = -0.000018370683559159788, a4 = 0.00000004610545751698899;
        double x = serverAC;
        return (int)(a0 + a1 * x + a2 * x * x + a3 * x * x * x + a4 * x * x * x * x + 0.5f);
    }
}

/// <summary>
/// Buff slots after Mob::AddBuff and Mob::CheckStackConflict (Zone/Source/spells.cpp): 15 slots; a
/// new buff blocked by a better one of the same effect does not take hold, a worse one is replaced;
/// a detrimental spell with no free slot pushes out a beneficial one.
/// </summary>
public static class BuffRules
{
    public const int Slots = 15;
    public const int StackingBlock = 148, StackingOverwrite = 149;

    /// <summary>
    /// CalcSpellBuffTics: the spell's duration when it has one, else by formula from the caster's
    /// level (1 and 6: level/2, 2: level/2 + 1, 5: 3, 7: level × 10, 8: level × 10 + 10, 9: level × 2
    /// + 10, 10: level × 3 + 10, 3/4/11 without a duration: 10, 50: permanent).
    /// </summary>
    public static int Tics(Spell spell, int casterLevel)
    {
        if (spell.Duration > 0)
            return spell.Duration;
        return spell.DurationFormula switch
        {
            0 => 0,
            1 or 6 => casterLevel / 2,
            2 => casterLevel / 2 + 1,
            3 or 4 or 11 => 10,
            5 => 3,
            7 => casterLevel * 10,
            8 => casterLevel * 10 + 10,
            9 => casterLevel * 2 + 10,
            10 => casterLevel * 3 + 10,
            50 => 32767,
            _ => spell.Duration,
        };
    }

    /// <summary>
    /// CheckStackConflict: 0 stacks, 1 the new spell replaces the old, −1 the new spell is blocked.
    /// Damage over time from different spells, or from different casters on an NPC, always stacks.
    /// </summary>
    public static int StackConflict(Buff old, Spell spell2, int casterLevel2, int casterId2, bool onNpc)
    {
        var spell1 = old.Spell;
        for (int i = 0; i < Spell.EffectCount; i++)
        {
            if (spell1.Effect[i] != StackingBlock || spell1.Id == spell2.Id)
                continue;
            int slot = spell1.Formula[i] - 201;
            if (slot is >= 0 and < Spell.EffectCount && spell2.Effect[slot] == spell1.Base[i]
                && spell2.Value(slot, casterLevel2) < spell1.Max[i])
                return -1;
        }
        for (int i = 0; i < Spell.EffectCount; i++)
        {
            if (spell2.Effect[i] != StackingOverwrite)
                continue;
            int slot = spell2.Formula[i] - 201;
            if (slot is >= 0 and < Spell.EffectCount && spell1.Effect[slot] == spell2.Base[i]
                && spell1.Value(slot, old.CasterLevel) < spell2.Max[i])
                return 1;
        }
        bool mismatch = spell1.Beneficial != spell2.Beneficial;
        bool overwrite = false;
        for (int i = 0; i < Spell.EffectCount; i++)
        {
            int e1 = spell1.Effect[i], e2 = spell2.Effect[i];
            if (e1 == SpellEffect.Blank || e1 is SpellEffect.CurrentHpOnce or DiseaseCounter or PoisonCounter or CurseCounter || e1 != e2)
                continue;
            bool dot = e1 == SpellEffect.CurrentHp && !spell1.Beneficial && !spell2.Beneficial;
            if (onNpc && dot && old.CasterId != casterId2)
                continue;
            if (e1 == SpellEffect.CompleteHeal)
                return -1;
            if (mismatch)
                continue;
            if (dot && spell1.Id != spell2.Id)
                continue;
            int v1 = spell1.Value(i, old.CasterLevel), v2 = spell2.Value(i, casterLevel2);
            if (e1 == SpellEffect.AttackSpeed)
            {
                v1 -= 100;
                v2 -= 100;
            }
            if (Math.Abs(v2) < Math.Abs(v1))
                return -1;
            overwrite = true;
        }
        return overwrite ? 1 : 0;
    }

    public const int DiseaseCounter = 35, PoisonCounter = 36, CurseCounter = 116;

    /// <summary>
    /// Puts the buff on <paramref name="buffs"/> (at most 15) as AddBuff does; returns the buffs it
    /// replaced (they fade), or null when it did not take hold.
    /// </summary>
    public static List<Buff>? Add(List<Buff> buffs, Buff buff, bool onNpc)
    {
        var replaced = new List<Buff>();
        foreach (var old in buffs)
        {
            int conflict = StackConflict(old, buff.Spell, buff.CasterLevel, buff.CasterId, onNpc);
            if (conflict == -1)
                return null;
            if (conflict == 1)
                replaced.Add(old);
        }
        if (replaced.Count == 0 && buffs.Count >= Slots)
        {
            if (buff.Spell.Beneficial || buffs.FirstOrDefault(b => b.Spell.Beneficial) is not { } pushed)
                return null;
            replaced.Add(pushed);
        }
        foreach (var old in replaced)
            buffs.Remove(old);
        buffs.Add(buff);
        return replaced;
    }
}
