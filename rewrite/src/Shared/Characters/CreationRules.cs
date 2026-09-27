using System;
using System.Collections.Generic;
using System.Linq;

namespace EQClassic.Shared.Characters
{
    /// <summary>A combination the server offers at creation (a start_zones row): race, class, deity, city choice, start zone.</summary>
    public sealed record CreationOption(int Race, int Class, int Deity, int Choice, string Zone);

    /// <summary>
    /// The character creation rules the Trilogy client applied before sending its profile (the legacy
    /// World trusts it): base statistics by race, the class additions and the bonus points to spend,
    /// the languages a race starts with. Checked against the creation packet captured from that client
    /// (troll shaman: troll base + shaman additions, 30 points spent). Shared by the Unity client
    /// (creation screen) and the server (validation).
    /// </summary>
    public static class CreationRules
    {
        public static readonly int[] Races = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 128 };
        public static readonly int[] Classes = Enumerable.Range(1, 14).ToArray();

        // STR, STA, CHA, DEX, INT, AGI, WIS (CharacterStats order).
        private static readonly Dictionary<int, int[]> RaceBase = new Dictionary<int, int[]>
        {
            [1] = new[] { 75, 75, 75, 75, 75, 75, 75 },      // Human
            [2] = new[] { 103, 95, 55, 70, 60, 82, 70 },     // Barbarian
            [3] = new[] { 60, 70, 70, 70, 107, 70, 83 },     // Erudite
            [4] = new[] { 65, 65, 75, 80, 75, 95, 80 },      // Wood elf
            [5] = new[] { 55, 65, 80, 70, 92, 85, 95 },      // High elf
            [6] = new[] { 60, 65, 60, 75, 99, 90, 83 },      // Dark elf
            [7] = new[] { 70, 70, 75, 85, 75, 90, 60 },      // Half elf
            [8] = new[] { 90, 90, 45, 90, 60, 70, 83 },      // Dwarf
            [9] = new[] { 108, 109, 40, 75, 52, 83, 60 },    // Troll
            [10] = new[] { 130, 122, 37, 70, 60, 70, 67 },   // Ogre
            [11] = new[] { 70, 75, 50, 90, 67, 95, 80 },     // Halfling
            [12] = new[] { 60, 70, 60, 85, 98, 85, 67 },     // Gnome
            [128] = new[] { 70, 70, 55, 85, 75, 90, 80 },    // Iksar
        };

        // Class additions (same order) and the bonus points left to spend.
        private static readonly Dictionary<int, (int[] Add, int Points)> ClassBonus = new Dictionary<int, (int[], int)>
        {
            [1] = (new[] { 10, 10, 0, 0, 0, 5, 0 }, 25),     // Warrior
            [2] = (new[] { 5, 5, 0, 0, 0, 0, 10 }, 30),      // Cleric
            [3] = (new[] { 10, 5, 10, 0, 0, 0, 5 }, 20),     // Paladin
            [4] = (new[] { 5, 10, 0, 0, 0, 10, 5 }, 20),     // Ranger
            [5] = (new[] { 10, 5, 5, 0, 10, 0, 0 }, 20),     // Shadow knight
            [6] = (new[] { 0, 10, 0, 0, 0, 0, 10 }, 30),     // Druid
            [7] = (new[] { 5, 5, 0, 10, 0, 10, 0 }, 20),     // Monk
            [8] = (new[] { 5, 0, 10, 10, 0, 0, 0 }, 25),     // Bard
            [9] = (new[] { 0, 0, 0, 10, 0, 10, 0 }, 30),     // Rogue
            [10] = (new[] { 0, 5, 5, 0, 0, 0, 10 }, 30),     // Shaman
            [11] = (new[] { 0, 0, 0, 10, 10, 0, 0 }, 30),    // Necromancer
            [12] = (new[] { 0, 10, 0, 0, 10, 0, 0 }, 30),    // Wizard
            [13] = (new[] { 0, 10, 0, 0, 10, 0, 0 }, 30),    // Magician
            [14] = (new[] { 0, 0, 10, 0, 10, 0, 0 }, 30),    // Enchanter
        };

        public static readonly string[] RaceNames =
            { "", "Human", "Barbarian", "Erudite", "Wood Elf", "High Elf", "Dark Elf", "Half Elf", "Dwarf", "Troll", "Ogre", "Halfling", "Gnome" };
        public static readonly string[] ClassNames =
            { "", "Warrior", "Cleric", "Paladin", "Ranger", "Shadow Knight", "Druid", "Monk", "Bard", "Rogue", "Shaman", "Necromancer", "Wizard", "Magician", "Enchanter" };

        public static string RaceName(int race) => race == 128 ? "Iksar" : race > 0 && race < RaceNames.Length ? RaceNames[race] : "Unknown";
        public static string ClassName(int @class) => @class > 0 && @class < ClassNames.Length ? ClassNames[@class] : "Unknown";

        public static string DeityName(int deity) => deity switch
        {
            140 => "Agnostic", 201 => "Bertoxxulous", 202 => "Brell Serilis", 203 => "Cazic-Thule", 204 => "Erollisi Marr",
            205 => "Bristlebane", 206 => "Innoruuk", 207 => "Karana", 208 => "Mithaniel Marr", 209 => "Prexus", 210 => "Quellious",
            211 => "Rallos Zek", 212 => "Rodcet Nife", 213 => "Solusek Ro", 214 => "The Tribunal", 215 => "Tunare", 216 => "Veeshan",
            396 => "Agnostic", _ => "Deity " + deity,
        };

        /// <summary>Statistics before spending the bonus points (race base + class additions), and the points.</summary>
        public static (CharacterStats Stats, int Points) Starting(int race, int @class)
        {
            var b = RaceBase[race];
            var (add, points) = ClassBonus[@class];
            var s = new int[7];
            for (int i = 0; i < 7; i++)
                s[i] = b[i] + add[i];
            return (new CharacterStats(s[0], s[1], s[2], s[3], s[4], s[5], s[6]), points);
        }

        public static int[] ToArray(CharacterStats s) => new[] { s.Str, s.Sta, s.Cha, s.Dex, s.Int, s.Agi, s.Wis };

        /// <summary>Null when the statistics are the starting ones plus exactly the bonus points; otherwise why not.</summary>
        /// <summary>The Trilogy creation screen: at most 25 bonus points in any one statistic (30 to spend: 25 and 5 elsewhere).</summary>
        public const int MaxPointsPerStat = 25;

        public static string? CheckStats(int race, int @class, CharacterStats chosen)
        {
            if (!RaceBase.ContainsKey(race) || !ClassBonus.ContainsKey(@class))
                return "unknown race or class";
            var (start, points) = Starting(race, @class);
            var s = ToArray(start);
            var c = ToArray(chosen);
            int spent = 0;
            for (int i = 0; i < 7; i++)
            {
                if (c[i] < s[i])
                    return "a statistic is below its starting value";
                if (c[i] - s[i] > MaxPointsPerStat)
                    return $"more than {MaxPointsPerStat} bonus points in one statistic";
                spent += c[i] - s[i];
            }
            return spent == points ? null : $"{spent} bonus points spent, {points} expected";
        }

        /// <summary>Languages (language id → skill) a new character of this race speaks: common and the racial tongue.</summary>
        public static IReadOnlyDictionary<int, int> Languages(int race)
        {
            // Trolls and ogres speak the common tongue at 95 (the captured troll profile); racial tongue at 100.
            var languages = new Dictionary<int, int> { [0] = race is 9 or 10 ? 95 : 100 };
            int? own = race switch
            {
                2 => 1, 3 => 2, 4 => 3, 5 => 3, 6 => 4, 7 => 3, 8 => 5, 9 => 6, 10 => 7, 11 => 9, 12 => 8, 128 => 18, _ => (int?)null,
            };
            if (own is int l)
                languages[l] = 100;
            return languages;
        }
    }
}
