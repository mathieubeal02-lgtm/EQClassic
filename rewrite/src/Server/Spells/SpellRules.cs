namespace EQClassic.Server.Spells;

/// <summary>Mana, fizzles and resists, after the legacy zone (mob.cpp, spells.cpp, SpellsHandler.cpp).</summary>
public static class SpellRules
{
    /// <summary>Distance a caster may move before the spell is interrupted (SpellFinished: 3 units for players).</summary>
    public const float InterruptDistance = 3f;
    /// <summary>Skill ids of the meditate skill and the channeling skill (legacy skills.h).</summary>
    public const int Meditate = 31, Channeling = 13;

    /// <summary>
    /// Mob::CalcMaxMana: (WIS/5 + 2) × level for the WIS casters (cleric, paladin, ranger, druid,
    /// shaman), (INT/5 + 2) × level for the INT casters (shadow knight, bard, necromancer, wizard,
    /// magician, enchanter), 0 for the others. No item or spell bonus yet.
    /// </summary>
    public static int MaxMana(int classId, int level, int wis, int @int) => classId switch
    {
        2 or 3 or 4 or 6 or 10 => (wis / 5 + 2) * level,
        5 or 8 or 11 or 12 or 13 or 14 => (@int / 5 + 2) * level,
        _ => 0,
    };

    /// <summary>
    /// Mob::DoManaRegen, per tic: 2 + level/5 standing; sitting with meditate,
    /// ((meditate/10) + (level − level/4)) / 4 + 4, or the standing amount when that gains nothing
    /// or more than a sixth of the pool.
    /// </summary>
    public static int ManaRegen(int level, bool sitting, int meditate, int maxMana)
    {
        int standing = 2 + level / 5;
        if (!sitting || meditate <= 0)
            return standing;
        int gain = (meditate / 10 + (level - level / 4)) / 4 + 4;
        return gain <= 0 || gain > maxMana / 6 ? standing : gain;
    }

    /// <summary>
    /// Mob::CheckFizzle (from EQEmu 5.0): percent chance to fizzle, 5 to 95. The par skill for the
    /// spell's level (level × 5 − 10, at most 235, plus the level) against the caster's casting
    /// skill plus level; the spell's base difficulty adds, a WIS or INT above 125 helps.
    /// </summary>
    public static int FizzleChance(int spellLevel, int baseDifficulty, int castingSkill, int casterLevel, int wis, int @int)
    {
        int par = Math.Min(spellLevel * 5 - 10, 235) + spellLevel;
        int diff = par + baseDifficulty - (castingSkill + casterLevel);
        int stat = Math.Max(wis, @int);
        diff -= (stat - 125) / 20;
        return Math.Clamp(10 + diff / 5, 5, 95);
    }

    /// <summary>The legacy roll: MakeRandomInt(0, 100) &lt;= chance fizzles.</summary>
    public static bool Fizzles(int chance, Random random) => random.Next(0, 101) <= chance;

    /// <summary>
    /// SpellsHandler::CalcResistValue: percent chance to resist. Unresistable spells (type 0) never
    /// are. The victim's resist, adjusted by 6 per level above the caster (3 per level below) and the
    /// spell's resist modifier, against a base save of level × 3: 20%, × 1.1 or × 0.9 per 3 points of
    /// difference, kept to 3-100 (1-100 for spells with a modifier of −100 or less), plus 5 per
    /// level the victim has over the caster (at most 20).
    /// </summary>
    public static int ResistChance(int resistType, int victimResist, int victimLevel, int casterLevel, int resistModifier = 0)
    {
        if (resistType == 0)
            return 0;
        int adjustment = (victimLevel - casterLevel) * (victimLevel > casterLevel ? 6 : 3);
        int save = victimResist + adjustment + resistModifier;
        int bonus = victimLevel > casterLevel && resistModifier > -100 ? Math.Min(victimLevel - casterLevel, 4) * 5 : 0;
        int difference = save - victimLevel * 3;
        float chance = difference == 0 ? 20f
            : difference > 0 ? 20f * MathF.Pow(1.10f, difference / 3)
            : 20f * MathF.Pow(0.90f, -difference / 3);
        chance = Math.Clamp(chance, resistModifier > -100 ? 3f : 1f, 100f);
        return (int)chance + bonus;
    }

    /// <summary>The legacy roll: rand()%100 + 1 &lt; chance resists.</summary>
    public static bool Resists(int chance, Random random) => random.Next(100) + 1 < chance;

    /// <summary>The resist of an NPC for a spell's resist type (1 magic, 2 fire, 3 cold, 4 poison, 5 disease).</summary>
    public static int NpcResist(int resistType, Zone.NpcCombatStats npc) => resistType switch
    {
        1 => npc.MR, 2 => npc.FR, 3 => npc.CR, 4 => npc.PR, 5 => npc.DR, _ => 0,
    };
}
