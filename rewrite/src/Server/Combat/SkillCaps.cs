namespace EQClassic.Server.Combat;

/// <summary>
/// Skill caps by class and level (Mob::CheckMaxSkill, Zone/Source/mob.cpp), for every skill (the
/// racial exceptions and the tradeskill rule are not ported). NPCs have exactly these values
/// (NPC::NPC sets every skill to its cap and all melee skills to the best one). Classes 1-15; merchants, bankers and other NPC classes use the
/// warrior column, GM classes (17-31) their base class.
/// </summary>
public static class SkillCaps
{
    public const int OneHandBlunt = 0, OneHandSlashing = 1, TwoHandBlunt = 2, TwoHandSlashing = 3, Defense = 15,
        HandToHand = 28, Offense = 33, Piercing = 36;

    public static readonly int[] MeleeSkills = [OneHandBlunt, OneHandSlashing, TwoHandBlunt, TwoHandSlashing, Piercing, HandToHand];

    // Mob::CheckMaxSkill's tables, rows by skill id (skills.h), columns by class:
    //  WAR  CLR  PAL  RNG  SHD  DRU  MNK  BRD  ROG  SHM  NEC  WIZ  MAG  ENC  BST
    private static readonly int[][] UpTo50 =
    [
        [200, 175, 200, 200, 200, 175, 240, 200, 200, 200, 110, 110, 110, 110, 0], // 0 _1H_BLUNT
        [200, 0, 200, 200, 200, 175, 0, 200, 200, 0, 0, 0, 0, 0, 0], // 1 _1H_SLASHING
        [200, 175, 200, 200, 200, 175, 240, 0, 0, 200, 110, 110, 110, 110, 0], // 2 _2H_BLUNT
        [200, 0, 200, 200, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 3 _2H_SLASHING
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 4 ABJURATION
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 5 ALTERATION
        [0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0], // 6 APPLY_POISON
        [200, 0, 75, 240, 75, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0], // 7 ARCHERY
        [0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0], // 8 BACKSTAB
        [175, 200, 200, 150, 150, 200, 200, 150, 176, 200, 100, 100, 100, 100, 0], // 9 BIND_WOUND
        [220, 0, 180, 0, 175, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 10 BASH
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 11 BLOCK
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 12 BRASS_INSTR
        [0, 200, 200, 200, 200, 200, 0, 0, 0, 200, 200, 200, 200, 200, 0], // 13 CHANNELING
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 14 CONJURATION
        [210, 200, 210, 200, 210, 200, 230, 200, 200, 200, 145, 145, 145, 145, 0], // 15 DEFENSE
        [200, 0, 70, 55, 70, 0, 200, 0, 200, 0, 0, 0, 0, 0, 0], // 16 DISARM
        [0, 0, 0, 0, 0, 0, 0, 100, 200, 0, 0, 0, 0, 0, 0], // 17 DISARM_TRAPS
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 18 DIVINATION
        [140, 75, 125, 137, 125, 75, 200, 125, 150, 75, 75, 75, 75, 75, 0], // 19 DODGE
        [205, 0, 200, 200, 200, 0, 210, 0, 200, 0, 0, 0, 0, 0, 0], // 20 DOUBLE_ATTACK
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 21 DRAGON_PUNCH
        [210, 0, 0, 210, 0, 0, 252, 210, 210, 0, 0, 0, 0, 0, 0], // 22 DUEL_WIELD
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 23 EAGLE_STRIKE
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 24 EVOCATION
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 25 FEIGN_DEATH
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 26 FLYING_KICK
        [0, 0, 0, 200, 0, 200, 50, 55, 0, 0, 0, 0, 0, 0, 0], // 27 FORAGE
        [100, 75, 100, 100, 100, 75, 225, 100, 100, 75, 75, 75, 75, 75, 0], // 28 HAND_TO_HAND
        [0, 0, 0, 75, 75, 0, 0, 40, 200, 0, 0, 0, 0, 0, 0], // 29 HIDE
        [149, 0, 0, 149, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 30 KICK
        [0, 235, 185, 185, 235, 235, 0, 1, 0, 235, 235, 235, 235, 235, 0], // 31 MEDITATE
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 32 MEND
        [210, 200, 200, 210, 200, 200, 230, 200, 210, 200, 140, 140, 140, 140, 0], // 33 OFFENSE
        [200, 0, 175, 185, 175, 0, 0, 75, 200, 0, 0, 0, 0, 0, 0], // 34 PARRY
        [0, 0, 0, 0, 0, 0, 0, 100, 200, 0, 0, 0, 0, 0, 0], // 35 PICK_LOCK
        [200, 0, 200, 200, 200, 0, 0, 200, 210, 200, 110, 110, 110, 110, 0], // 36 PIERCING
        [200, 0, 175, 150, 175, 0, 200, 75, 200, 0, 0, 0, 0, 0, 0], // 37 RIPOSTE
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 38 ROUND_KICK
        [0, 0, 0, 0, 0, 0, 200, 40, 94, 0, 0, 0, 0, 0, 0], // 39 SAFE_FALL
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 40 SENSE_HEADING
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 41 SINGING
        [0, 0, 0, 75, 0, 0, 113, 75, 200, 0, 0, 0, 0, 0, 0], // 42 SNEAK
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 43 SPECIALIZE_ABJ
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 44 SPECIALIZE_ALT
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 45 SPECIALIZE_CON
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 46 SPECIALIZE_DIV
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 47 SPECIALIZE_EVO
        [0, 0, 0, 0, 0, 0, 0, 100, 200, 0, 0, 0, 0, 0, 0], // 48 PICK_POCKETS
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 49 STRINGED_INSTRU
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 50 SWIMMING
        [113, 0, 0, 113, 0, 0, 113, 113, 220, 0, 75, 75, 75, 75, 0], // 51 THROWING
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 52 TIGER_CLAW
        [0, 0, 0, 200, 0, 125, 0, 100, 0, 0, 0, 0, 0, 0, 0], // 53 TRACKING
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 54 WIND_INSTRUMENTS
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 55 SKILL_FISHING
        [0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0], // 56 MAKE_POISON
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 57 TINKERING
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 200, 200, 200, 0], // 58 RESEARCH
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 130, 0, 0, 0, 0, 0], // 59 ALCHEMY
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 60 BAKING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 61 TAILORING
        [0, 0, 0, 0, 0, 0, 0, 75, 200, 0, 0, 0, 0, 0, 0], // 62 SENSE_TRAPS
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 63 BLACKSMITHING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 64 FLETCHING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 65 BREWING
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 66 ALCOHOL_TOL
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 67 BEGGING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 68 JEWELRY_MAKING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 69 POTTERY
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 70 PERCUSSION_INSTR
        [0, 0, 0, 0, 0, 0, 200, 100, 200, 0, 0, 0, 0, 0, 0], // 71 INTIMIDATION
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 72 BERSERKING
        [200, 0, 180, 150, 180, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 73 TAUNT
    ];

    private static readonly int[][] Beyond50 =
    [
        [250, 175, 225, 250, 225, 175, 252, 225, 250, 200, 110, 110, 110, 110, 0], // 0 _1H_BLUNT
        [250, 0, 225, 250, 225, 175, 0, 225, 250, 0, 0, 0, 0, 0, 0], // 1 _1H_SLASHING
        [250, 175, 225, 250, 225, 175, 252, 0, 0, 200, 110, 110, 110, 110, 0], // 2 _2H_BLUNT
        [250, 0, 225, 250, 225, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 3 _2H_SLASHING
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 4 ABJURATION
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 5 ALTERATION
        [0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0], // 6 APPLY_POISON
        [240, 0, 75, 240, 75, 0, 0, 0, 240, 0, 0, 0, 0, 0, 0], // 7 ARCHERY
        [0, 0, 0, 0, 0, 0, 0, 0, 225, 0, 0, 0, 0, 0, 0], // 8 BACKSTAB
        [210, 201, 210, 200, 200, 200, 210, 200, 210, 200, 100, 100, 100, 100, 0], // 9 BIND_WOUND
        [240, 0, 200, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 10 BASH
        [0, 0, 0, 0, 0, 0, 230, 0, 0, 0, 0, 0, 0, 0, 0], // 11 BLOCK
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 12 BRASS_INSTR
        [0, 220, 220, 215, 220, 220, 0, 0, 0, 220, 220, 220, 220, 220, 0], // 13 CHANNELING
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 14 CONJURATION
        [252, 200, 252, 200, 252, 200, 252, 252, 252, 200, 145, 145, 145, 145, 0], // 15 DEFENSE
        [200, 0, 70, 55, 70, 0, 200, 0, 200, 0, 0, 0, 0, 0, 0], // 16 DISARM
        [0, 0, 0, 0, 0, 0, 0, 100, 200, 0, 0, 0, 0, 0, 0], // 17 DISARM_TRAPS
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 18 DIVINATION
        [175, 75, 155, 170, 155, 75, 200, 155, 210, 75, 75, 75, 75, 75, 0], // 19 DODGE
        [245, 0, 235, 245, 235, 0, 250, 0, 240, 0, 0, 0, 0, 0, 0], // 20 DOUBLE_ATTACK
        [0, 0, 0, 0, 0, 0, 225, 0, 0, 0, 0, 0, 0, 0, 0], // 21 DRAGON_PUNCH
        [245, 0, 0, 245, 0, 0, 252, 210, 245, 0, 0, 0, 0, 0, 0], // 22 DUEL_WIELD
        [0, 0, 0, 0, 0, 0, 225, 0, 0, 0, 0, 0, 0, 0, 0], // 23 EAGLE_STRIKE
        [0, 235, 235, 235, 235, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 24 EVOCATION
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 25 FEIGN_DEATH
        [0, 0, 0, 0, 0, 0, 225, 0, 0, 0, 0, 0, 0, 0, 0], // 26 FLYING_KICK
        [0, 0, 0, 200, 0, 200, 50, 55, 0, 0, 0, 0, 0, 0, 0], // 27 FORAGE
        [100, 75, 100, 100, 100, 75, 252, 100, 100, 75, 75, 75, 75, 75, 0], // 28 HAND_TO_HAND
        [0, 0, 0, 75, 75, 0, 0, 40, 200, 0, 0, 0, 0, 0, 0], // 29 HIDE
        [210, 0, 0, 205, 0, 0, 250, 0, 0, 0, 0, 0, 0, 0, 0], // 30 KICK
        [0, 252, 235, 226, 235, 252, 0, 1, 0, 252, 252, 252, 252, 252, 0], // 31 MEDITATE
        [0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0, 0, 0], // 32 MEND
        [252, 200, 225, 252, 230, 200, 252, 225, 252, 200, 140, 140, 140, 140, 0], // 33 OFFENSE
        [230, 0, 205, 220, 205, 0, 0, 75, 230, 0, 0, 0, 0, 0, 0], // 34 PARRY
        [0, 0, 0, 0, 0, 0, 0, 100, 210, 0, 0, 0, 0, 0, 0], // 35 PICK_LOCK
        [240, 0, 225, 240, 225, 0, 0, 225, 250, 200, 110, 110, 110, 110, 0], // 36 PIERCING
        [225, 0, 200, 150, 200, 0, 225, 75, 225, 0, 0, 0, 0, 0, 0], // 37 RIPOSTE
        [0, 0, 0, 0, 0, 0, 225, 0, 0, 0, 0, 0, 0, 0, 0], // 38 ROUND_KICK
        [0, 0, 0, 0, 0, 0, 200, 40, 94, 0, 0, 0, 0, 0, 0], // 39 SAFE_FALL
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 40 SENSE_HEADING
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 41 SINGING
        [0, 0, 0, 75, 0, 0, 113, 75, 200, 0, 0, 0, 0, 0, 0], // 42 SNEAK
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 43 SPECIALIZE_ABJ
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 44 SPECIALIZE_ALT
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 45 SPECIALIZE_CON
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 46 SPECIALIZE_DIV
        [0, 235, 0, 0, 0, 235, 0, 0, 0, 235, 235, 235, 235, 235, 0], // 47 SPECIALIZE_EVO
        [0, 0, 0, 0, 0, 0, 0, 100, 200, 0, 0, 0, 0, 0, 0], // 48 PICK_POCKETS
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 49 STRINGED_INSTRU
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 50 SWIMMING
        [200, 0, 0, 113, 0, 0, 200, 113, 250, 0, 75, 75, 75, 75, 0], // 51 THROWING
        [0, 0, 0, 0, 0, 0, 252, 0, 0, 0, 0, 0, 0, 0, 0], // 52 TIGER_CLAW
        [0, 0, 0, 200, 0, 125, 0, 100, 0, 0, 0, 0, 0, 0, 0], // 53 TRACKING
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 54 WIND_INSTRUMENTS
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 55 SKILL_FISHING
        [0, 0, 0, 0, 0, 0, 0, 0, 200, 0, 0, 0, 0, 0, 0], // 56 MAKE_POISON
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 57 TINKERING
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 200, 200, 200, 200, 0], // 58 RESEARCH
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 180, 0, 0, 0, 0, 0], // 59 ALCHEMY
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 60 BAKING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 61 TAILORING
        [0, 0, 0, 0, 0, 0, 0, 75, 200, 0, 0, 0, 0, 0, 0], // 62 SENSE_TRAPS
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 63 BLACKSMITHING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 64 FLETCHING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 65 BREWING
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 66 ALCOHOL_TOL
        [200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 200, 0], // 67 BEGGING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 68 JEWELRY_MAKING
        [250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 250, 0], // 69 POTTERY
        [0, 0, 0, 0, 0, 0, 0, 235, 0, 0, 0, 0, 0, 0, 0], // 70 PERCUSSION_INSTR
        [0, 0, 0, 0, 0, 0, 200, 100, 200, 0, 0, 0, 0, 0, 0], // 71 INTIMIDATION
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 72 BERSERKING
        [200, 0, 180, 150, 180, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], // 73 TAUNT
    ];

    public const int SkillCount = 74;

    /// <summary>Skill names as the client writes them ("You have become better at Offense!").</summary>
    public static readonly string[] Names =
    [
        "1H Blunt", "1H Slashing", "2H Blunt", "2H Slashing", "Abjuration", "Alteration", "Apply Poison", "Archery", "Backstab", "Bind Wound", "Bash", "Block", "Brass Instruments", "Channeling", "Conjuration", "Defense", "Disarm", "Disarm Traps", "Divination", "Dodge", "Double Attack", "Dragon Punch", "Dual Wield", "Eagle Strike", "Evocation", "Feign Death", "Flying Kick", "Forage", "Hand to Hand", "Hide", "Kick", "Meditate", "Mend", "Offense", "Parry", "Pick Lock", "Piercing", "Riposte", "Round Kick", "Safe Fall", "Sense Heading", "Singing", "Sneak", "Specialize Abjure", "Specialize Alteration", "Specialize Conjuration", "Specialize Divination", "Specialize Evocation", "Pick Pockets", "Stringed Instruments", "Swimming", "Throwing", "Tiger Claw", "Tracking", "Wind Instruments", "Fishing", "Make Poison", "Tinkering", "Research", "Alchemy", "Baking", "Tailoring", "Sense Traps", "Blacksmithing", "Fletching", "Brewing", "Alcohol Tolerance", "Begging", "Jewelry Making", "Pottery", "Percussion Instruments", "Intimidation", "Berserking", "Taunt",
    ];

    /// <summary>
    /// Client::CheckAddSkill: below the cap, a skill goes up by one with a chance of
    /// 10 + <paramref name="modifier"/> + (252 − skill) / 20 percent (at least 1).
    /// </summary>
    public static int SkillUpChance(int value, int modifier) => Math.Max(1, 10 + modifier + (252 - value) / 20);

    private static int Column(int eqClass) => eqClass switch
    {
        >= 1 and <= 15 => eqClass - 1,
        >= 17 and <= 31 => eqClass - 17, // GM classes
        _ => 0,                           // merchants, bankers...: warrior skills
    };

    /// <summary>The cap of a skill for a class at a level (CheckMaxSkill's general case); 0: the class never learns it.</summary>
    public static int Cap(int skill, int eqClass, int level)
    {
        if (skill < 0 || skill >= SkillCount)
            throw new ArgumentOutOfRangeException(nameof(skill), skill, "no such skill");
        int column = Column(eqClass);
        int cap50 = UpTo50[skill][column], cap60 = Beyond50[skill][column];
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
