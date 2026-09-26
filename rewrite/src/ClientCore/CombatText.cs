using EQClassic.Shared.Zone;

namespace EQClassic.ClientCore
{
    /// <summary>
    /// The chat lines of melee, as the Trilogy client writes them from the server's damage packets
    /// ("You hit a rat for 3 points of damage.", "A rat hits YOU for 2 points of damage.").
    /// </summary>
    public static class CombatText
    {
        /// <summary>"a_rat01" → "a rat", "a_rat01's_corpse" → "a rat's corpse": digits dropped, underscores as spaces.</summary>
        public static string DisplayName(string name) =>
            new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(name, c => c < '0' || c > '9'))).Replace('_', ' ').Trim();

        /// <param name="nameOf">Display name of an entity id (null when unknown).</param>
        public static string Describe(CombatEvent swing, int yourId, System.Func<int, string?> nameOf)
        {
            string attacker = nameOf(swing.AttackerId) ?? "Someone", defender = nameOf(swing.DefenderId) ?? "someone";
            bool hit = swing.Damage > 0;
            if (swing.AttackerId == yourId)
                return hit ? $"You hit {defender} for {swing.Damage} point{Plural(swing.Damage)} of damage." : $"You try to hit {defender}, but miss!";
            if (swing.DefenderId == yourId)
                return hit ? $"{Capital(attacker)} hits YOU for {swing.Damage} point{Plural(swing.Damage)} of damage." : $"{Capital(attacker)} tries to hit YOU, but misses!";
            return hit ? $"{Capital(attacker)} hits {defender} for {swing.Damage} point{Plural(swing.Damage)} of damage." : $"{Capital(attacker)} tries to hit {defender}, but misses!";
        }

        private static string Plural(int n) => n == 1 ? "" : "s";
        private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
