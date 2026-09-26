namespace EQClassic.Server.Combat;

/// <summary>
/// Experience as the C++ zone computes it (Zone/Source/client.cpp GetEXPForLevel, GetLevelForEXP,
/// AddEXP, ExpLost; attack.cpp NPC::Death), in the same single-precision arithmetic so both servers
/// give the same numbers.
/// </summary>
public static class Experience
{
    public const int MaxLevel = 60;

    // Indexed by race - 1 (Iksar 13, Vah Shir 14) and class - 1; beastlords (15) have no table in the legacy code.
    private static readonly float[] RaceModifiers = [100f, 105f, 100f, 100f, 100f, 100f, 100f, 100f, 120f, 115f, 95f, 100f, 120f, 120f];
    private static readonly float[] ClassModifiers = [9f, 10f, 14f, 14f, 14f, 10f, 12f, 14f, 9.05f, 10f, 11f, 11f, 11f, 11f];

    /// <summary>Total experience at the start of a level: (level-1)³ × class × race modifiers, × a level factor from 31 on.</summary>
    public static uint ForLevel(int level, int playerClass, int race)
    {
        int r = race == 128 ? 13 : race == 130 ? 14 : race;
        r -= 1;
        if (r < 0 || r >= 14 || playerClass < 1 || playerClass > 14)
            return uint.MaxValue;
        int l1 = level - 1;
        uint calc = (uint)((float)(l1 * l1 * l1) * ClassModifiers[playerClass - 1] * RaceModifiers[r]);
        if (level < 31)
            return calc;
        float modifier = level switch
        {
            < 36 => 1.1f, < 41 => 1.2f, < 46 => 1.3f, < 52 => 1.4f, < 53 => 1.5f, < 54 => 1.6f, < 55 => 1.7f,
            < 56 => 1.9f, < 57 => 2.1f, < 58 => 2.3f, < 59 => 2.5f, < 60 => 2.7f, < 61 => 3.0f, _ => 3.1f,
        };
        return (uint)(calc * modifier);
    }

    public static int LevelFor(uint exp, int playerClass, int race)
    {
        for (int level = MaxLevel; level > 0; level--)
            if ((int)ForLevel(level, playerClass, race) <= (long)exp) // exp_needed is int32 in the legacy code
                return level;
        return 1;
    }

    /// <summary>Experience from one level to the next (GetTotalLevelExp); 2,140,000,000 at 60.</summary>
    public static uint LevelBand(int level, int playerClass, int race) =>
        level >= MaxLevel ? 2_140_000_000u : ForLevel(level + 1, playerClass, race) - ForLevel(level, playerClass, race);

    /// <summary>A kill: NPC level² × 75 (the default zone modifier), nothing for NPCs that con green.</summary>
    public static uint ForKill(int npcLevel) => (uint)(npcLevel * npcLevel * 75);

    /// <summary>One gain is capped at a tenth of the level's band (AddEXP).</summary>
    public static uint Capped(uint gain, int level, int playerClass, int race) =>
        Math.Min(gain, (uint)(LevelBand(level, playerClass, race) / 10.00f));

    /// <summary>Lost on death from level 6: level × level/18 × 12000, never below one point left (ExpLost).</summary>
    public static uint DeathLoss(int level, uint exp)
    {
        if (level <= 5)
            return 0;
        int loss = (int)(level * (level / 18.0) * 12000);
        if (loss <= 0)
            return 0;
        return loss > exp ? (exp > 0 ? exp - 1 : 0) : (uint)loss;
    }
}
