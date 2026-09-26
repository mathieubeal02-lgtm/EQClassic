namespace EQClassic.Shared.Zone
{
    /// <summary>Consider colours (legacy GetLevelCon values).</summary>
    public enum ConColor { White = 0x00, Green = 0x02, Blue = 0x04, Red = 0x0D, Yellow = 0x0F }

    /// <summary>Faction standings (legacy faction.h FACTION_VALUE).</summary>
    public enum Standing { Ally = 1, Warmly = 2, Kindly = 3, Amiable = 4, Indifferent = 5, Scowls = 6, Threatenly = 7, Apprehensive = 8, Dubious = 9 }

    /// <summary>
    /// What "consider" says: the colour by level difference (legacy GetLevelCon, shared by the
    /// server's aggro rules and the client) and the Trilogy client's sentence for a standing and a colour.
    /// </summary>
    public static class ConsiderRules
    {
        public static ConColor LevelCon(int playerLevel, int npcLevel)
        {
            int d = npcLevel - playerLevel;
            int greenAt = playerLevel <= 12 ? -4 : playerLevel <= 24 ? -6 : playerLevel <= 40 ? -11 : playerLevel <= 49 ? -12 : -14;
            // Legacy quirk for 25-40: -7..-1 falls through to red (the table only lists -10..-8 as blue).
            bool quirk = playerLevel >= 25 && playerLevel <= 40;
            int blueFrom = quirk ? -10 : greenAt + 1;
            int blueTo = quirk ? -8 : -1;
            if (d <= greenAt) return ConColor.Green;
            if (d >= blueFrom && d <= blueTo) return ConColor.Blue;
            if (d == 0) return ConColor.White;
            if (d >= 1 && d <= 2) return ConColor.Yellow;
            return ConColor.Red;
        }

        /// <summary>"a rat regards you indifferently -- looks like an even fight."</summary>
        public static string Message(string name, Standing standing, ConColor con)
        {
            string attitude = standing switch
            {
                Standing.Ally => "regards you as an ally",
                Standing.Warmly => "looks upon you warmly",
                Standing.Kindly => "kindly considers you",
                Standing.Amiable => "judges you amiably",
                Standing.Apprehensive => "looks your way apprehensively",
                Standing.Dubious => "glowers at you dubiously",
                Standing.Threatenly => "glares at you threateningly",
                Standing.Scowls => "scowls at you, ready to attack",
                _ => "regards you indifferently",
            };
            string odds = con switch
            {
                ConColor.Green => "looks like a reasonably safe opponent.",
                ConColor.Blue => "looks like you would have the upper hand.",
                ConColor.Yellow => "looks like quite a gamble.",
                ConColor.Red => "what would you like your tombstone to say?",
                _ => "looks like an even fight.",
            };
            return $"{name} {attitude} -- {odds}";
        }
    }
}
