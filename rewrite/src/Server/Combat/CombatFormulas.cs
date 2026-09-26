namespace EQClassic.Server.Combat;

/// <summary>
/// Melee combat model for the Classic → Velious era, ported from the C++ server
/// (Zone/Source/CombatFormulas.cpp, tested by tests/combat_test.cpp), itself reconstructed from
/// the TAKP/Quarm project's parses and client decompiles (EQMacEmu is GPLv3: the maths is
/// reproduced, not the code). Same integer arithmetic as the C++ so both servers agree.
///
/// One swing:
///   1. hit or miss:  <see cref="HitChance"/>(<see cref="ToHit"/>(attacker), avoidance(defender));
///   2. damage:       damage bonus + <see cref="MeleeDamage"/>(<see cref="D20"/>(offense, mitigation), base damage)
///                    (players then get <see cref="ClientDamageMultiplier"/>).
/// Class ids are Sony's (warrior 1 … beastlord 15), Iksar is race 128.
/// </summary>
public static class CombatFormulas
{
    public const int Warrior = 1, Cleric = 2, Paladin = 3, Ranger = 4, ShadowKnight = 5, Druid = 6, Monk = 7, Bard = 8,
        Rogue = 9, Shaman = 10, Necromancer = 11, Wizard = 12, Magician = 13, Enchanter = 14, Beastlord = 15;
    public const int RaceIksar = 128;

    public static bool IsMeleeClass(int c) =>
        c is Warrior or Paladin or Ranger or ShadowKnight or Monk or Bard or Rogue or Beastlord;

    private static bool IsPureCaster(int c) => c is Necromancer or Wizard or Magician or Enchanter;

    // ---- attacker ------------------------------------------------------------------------------

    /// <summary>Offense of an NPC (level-based floor + strength), plus its ATK.</summary>
    public static int NpcOffense(int level, int strBonus, int atk)
    {
        if (level > 45 && level < 51)
            level = 45; // NPC weapon skills plateau between 46 and 50
        int baseOffense, str;
        if (level < 6)
        {
            baseOffense = level * 4;
            str = level;
        }
        else
        {
            baseOffense = Math.Min(level * 55 / 10 - 4, 320);
            str = level < 30 ? level / 2 + 1 : level * 2 - 40;
        }
        str = Math.Max(0, str + strBonus * 2 / 3);
        return Math.Max(1, baseOffense + str + atk);
    }

    /// <summary>Offense of a player: weapon skill + ATK + strength above 75.</summary>
    public static int ClientOffense(int weaponSkill, int str, int atkBonus, int playerClass, int level)
    {
        int offense = Math.Max(1, weaponSkill + atkBonus + (str >= 75 ? (2 * str - 150) / 3 : 0));
        if (playerClass == Ranger && level > 54)
            offense += level * 4 - 216;
        return offense;
    }

    public static int ToHit(int offenseSkill, int weaponSkill, int accuracy, bool npc, int npcLevel)
    {
        if (npc && npcLevel < 3)
            accuracy += 2; // the lowest level NPCs parse slightly more accurate
        return 7 + offenseSkill + weaponSkill + accuracy;
    }

    // ---- defender ------------------------------------------------------------------------------

    public static int NpcAvoidance(int level, int agiBonus, int bonusAvoidance)
    {
        int avoidance = Math.Min(level * 9 + 5, level <= 50 ? 400 : 460);
        return Math.Max(1, avoidance + agiBonus * 22 / 100 + bonusAvoidance);
    }

    public static int ClientAvoidance(int defenseSkill, int agi, int level)
    {
        int fromDefense = defenseSkill > 0 ? defenseSkill * 400 / 225 : 0;
        // AGI: -25..0 below 40, nothing from 40 to 59, then a level-dependent curve up to 200 AGI.
        int fromAgi = 0;
        if (agi < 40)
        {
            fromAgi = 25 * (agi - 40) / 40;
        }
        else if (agi >= 60)
        {
            int adj = agi <= 74 ? 28 : level < 7 ? 35 : level < 20 ? 55 : level < 40 ? 70 : 80;
            fromAgi = agi < 200 ? 2 * (adj - (200 - agi) / 5) / 3 : 2 * adj / 3;
        }
        return Math.Max(1, fromDefense + fromAgi);
    }

    /// <summary>Mitigation (AC) of an NPC. Below the 200 cap the DB AC is ignored: parses show uniform values.</summary>
    public static int NpcMitigation(int level, int dbAC, int itemAC, int spellAC)
    {
        int mit = Math.Min(level < 15 ? level * 3 + (level < 3 ? 2 : 0) : level * 41 / 10 - 15, 200);
        if (mit == 200 && dbAC > 200)
            mit = dbAC; // raid targets only
        return Math.Max(1, mit + 4 * itemAC / 3 + spellAC / 4);
    }

    private static (int Hard, int Soft) MonkWeightCaps(int level) => level switch
    {
        < 15 => (30, 14),
        <= 29 => (32, 15),
        <= 44 => (34, 16),
        <= 50 => (36, 17),
        <= 54 => (38, 18),
        <= 59 => (40, 20),
        <= 61 => (45, 24),
        <= 63 => (47, 24),
        64 => (50, 24),
        _ => (53, 24),
    };

    private static int AgiScaledBonus(int levelScaler, int divisor, int agi, int cap)
    {
        int steps = agi < 80 ? 1 : agi < 85 ? 2 : agi < 90 ? 3 : agi < 100 ? 4 : 5;
        return Math.Min(levelScaler * steps / divisor, cap);
    }

    /// <summary>Mitigation (AC) of a player; <paramref name="weight"/> is the carried weight (monks).</summary>
    public static int ClientMitigation(int level, int playerClass, int race, int itemAC, int spellAC, int defenseSkill, int agi, int weight)
    {
        int ac = IsPureCaster(playerClass) ? itemAC : 4 * itemAC / 3;
        if (level < 50 && ac > level * 6 + 25)
            ac = level * 6 + 25; // low levels cannot twink their way to high AC

        if (playerClass == Monk)
        {
            var (hard, soft) = MonkWeightCaps(level);
            double bonus = level + 5.0;
            if (weight <= soft)
            {
                ac += (int)(bonus * 4.0 / 3.0);
            }
            else if (weight > hard + 1)
            {
                double scale = Math.Min(1.0, (weight - (hard - 10)) / 100.0);
                ac -= (int)(scale * 4.0 * bonus / 3.0);
            }
            else
            {
                double reduction = Math.Min(100.0, (weight - soft) * 6.66667);
                bonus = Math.Max(0.0, bonus * (100.0 - reduction) / 100.0);
                ac += (int)(4.0 * bonus / 3.0);
            }
        }
        else if (playerClass == Rogue)
        {
            if (level >= 30 && agi > 75)
                ac += AgiScaledBonus(level - 26, 4, agi, 12);
        }
        else if (playerClass == Beastlord)
        {
            if (level > 10)
                ac += AgiScaledBonus(level - 6, 5, agi, 16);
        }

        if (race == RaceIksar)
            ac += level < 10 ? 10 : Math.Min(level, 35);
        ac = Math.Max(0, ac);
        if (defenseSkill > 0)
            ac += IsPureCaster(playerClass) ? defenseSkill / 2 : defenseSkill / 3;
        ac += spellAC / (IsPureCaster(playerClass) ? 3 : 4);
        if (agi > 70)
            ac += agi / 20;
        ac = Math.Max(0, ac);

        // Hard cap, raised per class above 50 with Velious (returns above it came with Luclin).
        int cap = level <= 50 ? 350 : playerClass switch
        {
            Warrior => 430,
            Paladin or ShadowKnight or Cleric or Bard => 403,
            Ranger or Shaman => 375,
            _ => 350,
        };
        return Math.Min(ac, cap);
    }

    // ---- resolution ----------------------------------------------------------------------------

    /// <summary>Probability (0..1) that a swing with this to-hit lands against this avoidance.</summary>
    public static double HitChance(int toHit, int avoidance)
    {
        double t = (toHit + 10) * 1.21;
        double a = avoidance + 10;
        return t > a ? 1.0 - a / (t * 2.0) : t / (a * 2.0);
    }

    /// <summary>The d20 damage roll (1..20) from an attack roll in [0, offense+5) and a defense roll in [0, mitigation+5).</summary>
    public static int D20(int atkRoll, int defRoll, int offense, int mitigation)
    {
        int avg = Math.Max(1, (offense + mitigation + 10) / 2);
        int index = Math.Max(0, atkRoll - defRoll + avg / 2);
        return Math.Min(index * 20 / avg, 19) + 1;
    }

    public static int RollD20(Random random, int offense, int mitigation) =>
        D20(random.Next(0, offense + 5), random.Next(0, mitigation + 5), offense, mitigation);

    /// <summary>Damage of one hit: roll × base damage / 10, at least minHit and 1.</summary>
    public static int MeleeDamage(int d20, int baseDamage, int minHit) => Math.Max(1, Math.Max((d20 * baseDamage + 5) / 10, minHit));

    /// <summary>(max - min) / 19 in thousandths rounded to a tenth: the DB stores min/max hits, Sony's model a bonus + d20 × interval.</summary>
    private static int IntervalTimes1000(int minDmg, int maxDmg) => ((maxDmg - minDmg) * 1000 / 19 + 50) / 100 * 100;

    /// <summary>NPC DB min/max damage → Sony's base damage (damage interval × 10).</summary>
    public static int NpcBaseDamage(int minDmg, int maxDmg) =>
        maxDmg <= minDmg ? 1 : Math.Max(1, IntervalTimes1000(minDmg, maxDmg) / 100);

    public static int NpcDamageBonus(int minDmg, int maxDmg) =>
        minDmg > maxDmg ? minDmg : (maxDmg * 1000 - IntervalTimes1000(minDmg, maxDmg) * 20) / 1000;

    /// <summary>Player main-hand damage bonus (melee classes from level 28; two-handers get more).</summary>
    public static int ClientDamageBonus(int level, int playerClass, bool twoHanded, int delay)
    {
        if (level < 28 || !IsMeleeClass(playerClass))
            return 0;
        int bonus = 1 + (level - 28) / 3;
        if (!twoHanded)
            return bonus;
        if (delay <= 27)
            return bonus + 1;
        if (level > 29)
        {
            int levelBonus = (level - 30) / 5 + 1;
            if (level > 50)
            {
                levelBonus++;
                int extra = level - 50 + (level > 67 ? 5 : level > 59 ? 4 : level > 58 ? 3 : level > 56 ? 2 : level > 54 ? 1 : 0);
                levelBonus += extra * delay / 40;
            }
            bonus += levelBonus;
        }
        if (delay >= 40)
            bonus += (delay - 40) / 3 + 1 + (delay >= 45 ? 2 : delay >= 43 ? 1 : 0);
        return bonus;
    }

    public static (int RollChance, int MaxExtra, int MinusFactor) ClientMultiplierParams(int level, int playerClass)
    {
        bool monk = playerClass == Monk;
        if (monk && level >= 65) return (83, 300, 50);
        if (level >= 65 || (monk && level >= 63)) return (81, 295, 55);
        if (level >= 63 || (monk && level >= 60)) return (79, 290, 60);
        if (level >= 60 || (monk && level >= 56)) return (77, 285, 65);
        if (level >= 56) return (72, 265, 70);
        if (level >= 51 || monk) return (65, 245, 80);
        return (51, 210, 105);
    }

    /// <summary>With probability RollChance %, damage × (100 + extra) / 100, extra in [0, (offense - minus) / 2], capped.</summary>
    public static int ClientDamageMultiplier(Random random, int damage, int offense, int level, int playerClass)
    {
        var (rollChance, maxExtra, minusFactor) = ClientMultiplierParams(level, playerClass);
        if (random.Next(1, 101) > rollChance)
            return damage;
        int baseBonus = Math.Max(10, (offense - minusFactor) / 2);
        int multiplier = Math.Min(100 + random.Next(0, baseBonus + 1), maxExtra);
        damage = damage * multiplier / 100;
        if (level >= 55 && damage > 1 && IsMeleeClass(playerClass))
            damage++;
        return damage;
    }

    // ---- hit points and bare hands -------------------------------------------------------------

    /// <summary>A player's base hit points (Client::CalcBaseHP): 5 + m·level + m·level·STA/300, m by class and level.</summary>
    public static int ClientBaseHp(int level, int playerClass, int sta)
    {
        int m = playerClass switch
        {
            Warrior => level < 20 ? 22 : level < 30 ? 23 : level < 40 ? 25 : level < 53 ? 27 : level < 57 ? 28 : 30,
            Druid or Cleric or Shaman => 15,
            Monk or Bard or Rogue or Beastlord => level < 51 ? 18 : level < 58 ? 19 : 20,
            Ranger => level < 58 ? 20 : 21,
            Magician or Wizard or Necromancer or Enchanter => 12,
            _ => level < 35 ? 21 : level < 45 ? 22 : level < 51 ? 23 : level < 56 ? 24 : level < 60 ? 25 : 26, // paladin, SK, others
        };
        return 5 + m * level + m * level * sta / 300;
    }

    private static readonly int[] MonkFistDamage =
    [
        99, 4, 4, 4, 4, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 7, 7, 7, 7, 7,
        8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 10, 10, 10, 10, 10, 11, 11, 11, 11, 11,
        12, 12, 12, 12, 12, 13, 13, 13, 13, 13, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14,
        15, 15, 16, 16, 17, 18,
    ];

    private static readonly int[] MonkFistDelayHuman =
    [
        99, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36,
        36, 36, 36, 36, 36, 35, 35, 35, 35, 35, 34, 34, 34, 34, 34, 33, 33, 33, 33, 33,
        32, 32, 32, 32, 32, 31, 31, 31, 31, 31, 30, 30, 30, 29, 29, 29, 28, 28, 28, 27,
        27, 26, 26, 25, 25, 25,
    ];

    private static readonly int[] MonkFistDelayOthers =
    [
        99, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 36,
        36, 36, 36, 36, 36, 36, 36, 36, 36, 36, 35, 35, 35, 35, 35, 34, 34, 34, 34, 34,
        33, 33, 33, 33, 33, 32, 32, 32, 32, 32, 31, 31, 31, 30, 30, 30, 29, 29, 29, 28,
        28, 27, 27, 26, 26, 26,
    ];

    /// <summary>
    /// Bare-hand damage and delay (tenths of a second): 2/36, monks by level; the monk delay table
    /// is the human one for humans, the (Iksar) other one for every other race (Mob::GetMonkHandToHand*).
    /// </summary>
    public static (int Damage, int Delay) Fists(int level, int playerClass, int race)
    {
        if (playerClass != Monk)
            return (2, 36);
        level = Math.Max(level, 1);
        if (level > 65)
            return (19, race == 1 ? 24 : 25);
        return (MonkFistDamage[level], race == 1 ? MonkFistDelayHuman[level] : MonkFistDelayOthers[level]);
    }
}
