using System.Collections.Generic;

namespace EQClassic.ClientCore
{
    /// <summary>
    /// Race id → the three-letter model code LanternExtractor uses for character exports
    /// (Exports/characters/.../&lt;code&gt;.txt), which LanternUnityTools turns into prefabs. Playable races
    /// have a male and a female model; most other races one code. Unknown races fall back to
    /// <see cref="Fallback"/>; the Unity client logs them so the table can be completed.
    /// </summary>
    public static class ModelCodes
    {
        private static readonly Dictionary<int, (string Male, string Female)> Playable = new Dictionary<int, (string, string)>
        {
            [1] = ("hum", "huf"),   // Human
            [2] = ("bam", "baf"),   // Barbarian
            [3] = ("erm", "erf"),   // Erudite
            [4] = ("elm", "elf"),   // Wood elf
            [5] = ("him", "hif"),   // High elf
            [6] = ("dam", "daf"),   // Dark elf
            [7] = ("ham", "haf"),   // Half elf
            [8] = ("dwm", "dwf"),   // Dwarf
            [9] = ("trm", "trf"),   // Troll
            [10] = ("ogm", "ogf"),  // Ogre
            [11] = ("hom", "hof"),  // Halfling
            [12] = ("gnm", "gnf"),  // Gnome
            [128] = ("ikm", "ikf"), // Iksar
        };

        // Gendered non-playable races (Qeynos citizens: qcm/qcf in the Qeynos export).
        private static readonly Dictionary<int, (string Male, string Female)> GenderedOthers = new Dictionary<int, (string, string)>
        {
            [71] = ("qcm", "qcf"),
            // City citizens and guards, and ghosts of playable races, drawn with their race's models.
            [44] = ("fpm", "fpm"),  // Freeport guards (their own model)
            [55] = ("hum", "huf"),  // beggars
            [67] = ("hum", "huf"),  // Highpass citizens
            [77] = ("dam", "daf"),  // Neriak citizens
            [78] = ("erm", "erf"),  // Erudin citizens
            [81] = ("hom", "hof"),  // Rivervale citizens
            [90] = ("bam", "baf"),  // Halas citizens
            [92] = ("trm", "trf"),  // Grobb citizens
            [93] = ("ogm", "ogf"),  // Oggok citizens
            [94] = ("dwm", "dwf"),  // Kaladim citizens
            [106] = ("him", "hif"), // Felwithe guards
            [112] = ("elm", "elf"), // Kelethin guards
            [117] = ("dwm", "dwf"), // dwarf ghosts
            [118] = ("erm", "erf"), // Erudite ghosts
            [139] = ("ikm", "ikf"), // Iksar citizens
            [183] = ("clm", "clf"), // Coldain
        };

        // Codes present in this project's Lantern exports (Qeynos, Permafrost and the global_chr
        // archives), matched with the races the database spawns.
        // Complete it from build/lantern-work/Exports/characters/Skeletons when more zones are exported.
        private static readonly Dictionary<int, string> Others = new Dictionary<int, string>
        {
            [14] = "wer",  // werewolves
            [18] = "gia",  // giants (Permafrost)
            [21] = "eye",  // evil eyes
            [22] = "bet",  // beetles
            [24] = "fis",  // fish (a_Koalindl)
            [34] = "bat",
            [36] = "rat",
            [37] = "sna",
            [38] = "spi",
            [39] = "gnn",  // gnolls
            [40] = "gob",
            [42] = "wol",
            [43] = "bea",
            [46] = "imp",
            [49] = "dra",  // Lady Vox
            [54] = "orc",
            [60] = "ske",
            [68] = "ten",  // icy terrors (Tentacle)
            [70] = "zom",
            [75] = "ele",  // elementals
            [15] = "bri",  // brownies
            [48] = "kob",  // kobolds
            [26] = "frg",  // frogloks
            [27] = "frg",
            [28] = "fun",  // fungusmen
            [33] = "zom",  // ghouls
            [58] = "sol",  // Solusek Ro
            [62] = "tun",  // Tunare
            [66] = "ral",  // Rallos Zek
            [72] = "boat",
            [82] = "sca",  // scarecrows
            [85] = "spe",  // spectres
            [88] = "gnm",  // clockwork gnomes
            [95] = "caz",  // Cazic Thule
            [120] = "woe", // wolf elementals
            [123] = "inn", // Innoruuk
            [126] = "dji", // djinn
            [127] = "ivm", // invisible men
            [137] = "gob", // Kunark goblins
            [140] = "gia", // forest giants
            [141] = "boat",
            [151] = "tri", // the Tribunal
            [155] = "ske", // sarnak skeletons
            [161] = "ske", // Iksar skeletons
            [188] = "fgi", // frost giants
            [189] = "gia", // storm giants
            // The zones' own character archives (tools/lantern/extract.sh ... *_chr.s3d, import-zones.sh --characters).
            [13] = "avi", [16] = "cen", [17] = "gol", [19] = "trk", [29] = "gar", [31] = "cub", [41] = "gor", [45] = "dml",
            [47] = "gri", [50] = "lim", [51] = "liz", [52] = "mim", [53] = "min", [56] = "pif", [61] = "sha", [63] = "tig",
            [64] = "tre", [69] = "wil", [74] = "pir", [76] = "pum", [79] = "bix", [83] = "sku", [86] = "sph", [87] = "arm",
            [89] = "drk", [91] = "all", [96] = "coc", [99] = "den", [100] = "der", [101] = "efr", [103] = "ked", [104] = "lee",
            [105] = "swo", [107] = "mam", [109] = "was", [110] = "mer", [111] = "har", [113] = "dri", [116] = "sea",
            [124] = "uni", [125] = "peg", [133] = "lyc", [134] = "mos", [135] = "rhi", [136] = "xal", [138] = "yet",
            [145] = "goo", [149] = "isc", [154] = "fdr", [157] = "wyv", [158] = "wur", [159] = "dev", [160] = "ikg",
            [162] = "mep", [163] = "rap", [181] = "yak", [185] = "hag", [187] = "sir", [190] = "otm", [191] = "wal",
        };

        public static string For(int race, int gender)
        {
            if (Playable.TryGetValue(race, out var codes) || GenderedOthers.TryGetValue(race, out codes))
                return gender == 1 ? codes.Female : codes.Male;
            return Others.TryGetValue(race, out var code) ? code : Fallback;
        }

        /// <summary>Model used for races missing from the table.</summary>
        public const string Fallback = "hum";

        // Default sizes of the playable races (the size a spawn of that race has when nothing changes it).
        private static readonly Dictionary<int, float> DefaultSizes = new Dictionary<int, float>
        {
            [1] = 6f, [2] = 7f, [3] = 6f, [4] = 5f, [5] = 6f, [6] = 5f, [7] = 5f, [8] = 4f, [9] = 8f, [10] = 9f,
            [11] = 3.5f, [12] = 3f, [128] = 6f, [71] = 6f,
        };

        /// <summary>
        /// How much to scale a model for a spawn's size: size / the race's default size, for the races
        /// whose default size is known (1 otherwise, or for sizes 0 and below).
        /// </summary>
        public static float Scale(int race, float size) =>
            size > 0 && DefaultSizes.TryGetValue(race, out var normal) ? size / normal : 1f;

        public static bool IsKnown(int race) => Playable.ContainsKey(race) || GenderedOthers.ContainsKey(race) || Others.ContainsKey(race);
    }
}
