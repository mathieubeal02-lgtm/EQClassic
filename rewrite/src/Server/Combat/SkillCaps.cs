namespace EQClassic.Server.Combat;

/// <summary>
/// Skill caps by class and level (Mob::CheckMaxSkill, Zone/Source/mob.cpp), for the skills melee
/// combat reads. NPCs have exactly these values (NPC::NPC sets every skill to its cap and all
/// melee skills to the best one). Classes 1-15; merchants, bankers and other NPC classes use the
/// warrior column, GM classes (17-31) their base class.
/// </summary>
public static class SkillCaps
{
    public const int OneHandBlunt = 0, OneHandSlashing = 1, TwoHandBlunt = 2, TwoHandSlashing = 3, Defense = 15,
        HandToHand = 28, Offense = 33, Piercing = 36;

    public static readonly int[] MeleeSkills = [OneHandBlunt, OneHandSlashing, TwoHandBlunt, TwoHandSlashing, Piercing, HandToHand];

    //                                        WAR  CLR  PAL  RNG  SHD  DRU  MNK  BRD  ROG  SHM  NEC  WIZ  MAG  ENC  BST
    private static readonly Dictionary<int, int[]> UpTo50 = new()
    {
        [OneHandBlunt] =    [200, 175, 200, 200, 200, 175, 240, 200, 200, 200, 110, 110, 110, 110, 0],
        [OneHandSlashing] = [200, 0, 200, 200, 200, 175, 0, 200, 200, 0, 0, 0, 0, 0, 0],
        [TwoHandBlunt] =    [200, 175, 200, 200, 200, 175, 240, 0, 0, 200, 110, 110, 110, 110, 0],
        [TwoHandSlashing] = [200, 0, 200, 200, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [Defense] =         [210, 200, 210, 200, 210, 200, 230, 200, 200, 200, 145, 145, 145, 145, 0],
        [HandToHand] =      [100, 75, 100, 100, 100, 75, 225, 100, 100, 75, 75, 75, 75, 75, 0],
        [Offense] =         [210, 200, 200, 210, 200, 200, 230, 200, 210, 200, 140, 140, 140, 140, 0],
        [Piercing] =        [200, 0, 200, 200, 200, 0, 0, 200, 210, 200, 110, 110, 110, 110, 0],
    };

    private static readonly Dictionary<int, int[]> Beyond50 = new()
    {
        [OneHandBlunt] =    [250, 175, 225, 250, 225, 175, 252, 225, 250, 200, 110, 110, 110, 110, 0],
        [OneHandSlashing] = [250, 0, 225, 250, 225, 175, 0, 225, 250, 0, 0, 0, 0, 0, 0],
        [TwoHandBlunt] =    [250, 175, 225, 250, 225, 175, 252, 0, 0, 200, 110, 110, 110, 110, 0],
        [TwoHandSlashing] = [250, 0, 225, 250, 225, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        [Defense] =         [252, 200, 252, 200, 252, 200, 252, 252, 252, 200, 145, 145, 145, 145, 0],
        [HandToHand] =      [100, 75, 100, 100, 100, 75, 252, 100, 100, 75, 75, 75, 75, 75, 0],
        [Offense] =         [252, 200, 225, 252, 230, 200, 252, 225, 252, 200, 140, 140, 140, 140, 0],
        [Piercing] =        [240, 0, 225, 240, 225, 0, 0, 225, 250, 200, 110, 110, 110, 110, 0],
    };

    private static int Column(int eqClass) => eqClass switch
    {
        >= 1 and <= 15 => eqClass - 1,
        >= 17 and <= 31 => eqClass - 17, // GM classes
        _ => 0,                           // merchants, bankers...: warrior skills
    };

    /// <summary>The cap of a skill for a class at a level (CheckMaxSkill's general case).</summary>
    public static int Cap(int skill, int eqClass, int level)
    {
        if (!UpTo50.TryGetValue(skill, out var low))
            throw new ArgumentOutOfRangeException(nameof(skill), skill, "not a melee combat skill");
        int column = Column(eqClass);
        int cap50 = low[column], cap60 = Beyond50[skill][column];
        if (level <= 50)
            return Math.Min(level * 5 + 5, cap50);
        if (level <= 60)
        {
            float capAtLevel = cap60 - cap50 > 50
                ? cap50 + (cap60 - cap50) / 10f * (level - 50)
                : cap50 + (level - 50) * 5f;
            return Math.Min((int)capAtLevel, cap60);
        }
        return cap60 > 0 ? cap60 + (level - 60) * 5 : 0;
    }

    /// <summary>An NPC's melee skill: the best of its class's melee skills (NPC::NPC).</summary>
    public static int NpcMelee(int eqClass, int level) => MeleeSkills.Max(s => Cap(s, eqClass, level));
}
