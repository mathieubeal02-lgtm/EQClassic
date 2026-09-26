namespace EQClassic.Server.Zone;

/// <summary>Consider colors (legacy NpcAI.h CON_*). Values are the legacy codes.</summary>
public enum Con { White = 0x00, Green = 0x02, Blue = 0x04, Red = 0x0D, Yellow = 0x0F }

/// <summary>Faction standings (legacy faction.h FACTION_VALUE).</summary>
public enum FactionStanding { Ally = 1, Warmly = 2, Kindly = 3, Amiable = 4, Indifferent = 5, Scowls = 6, Threatenly = 7, Apprehensive = 8, Dubious = 9 }

/// <summary>
/// The legacy aggro decision (EntityList::AddHateToCloseMobs, NPC::CheckMyAgroStatus, GetLevelCon),
/// as pure functions. Only KOS players (scowls or threatenly) are aggroed, within a radius that
/// depends on the consider color, sitting and undead. Quirk kept: the size bonus the legacy code
/// computes first is overwritten in every KOS branch, so size never changes the radius.
/// </summary>
public static class AggroRules
{
    /// <summary>BASE_AGRO_RANGE (config.h).</summary>
    public const float BaseRange = 145f;

    /// <summary>How often an NPC scans for players (scanarea_timer, 1250 ms).</summary>
    public const float ScanSeconds = 1.25f;

    public static Con LevelCon(int playerLevel, int npcLevel)
    {
        int d = npcLevel - playerLevel;
        int greenAt = playerLevel <= 12 ? -4 : playerLevel <= 24 ? -6 : playerLevel <= 40 ? -11 : playerLevel <= 49 ? -12 : -14;
        // Legacy quirk for 25-40: -7..-1 falls through to red (the table only lists -10..-8 as blue).
        int blueFrom = playerLevel is >= 25 and <= 40 ? -10 : greenAt + 1;
        int blueTo = playerLevel is >= 25 and <= 40 ? -8 : -1;
        if (d <= greenAt) return Con.Green;
        if (d >= blueFrom && d <= blueTo) return Con.Blue;
        if (d == 0) return Con.White;
        if (d is >= 1 and <= 2) return Con.Yellow;
        return Con.Red;
    }

    /// <summary>Squared aggro radius, or null when this standing never aggroes.</summary>
    public static float? RadiusSquared(FactionStanding standing, int playerLevel, int npcLevel, bool playerSitting = false, bool npcUndead = false, float range = BaseRange)
    {
        if (standing is not (FactionStanding.Scowls or FactionStanding.Threatenly))
            return null;
        bool scowls = standing == FactionStanding.Scowls;
        float r2 = range * range;
        var con = LevelCon(playerLevel, npcLevel);

        if (playerSitting || npcUndead)
        {
            if (npcUndead && !playerSitting && con == Con.Green)
                return r2 / (scowls ? 7.5f : 10f);
            return con is Con.Yellow or Con.Red ? r2 / (scowls ? 4.5f : 6f) : r2 / (scowls ? 5f : 6.5f);
        }
        switch (con)
        {
            case Con.Green:
                int modifier = Math.Clamp(playerLevel / 10, 1, 4);
                return LevelCon(playerLevel, npcLevel + modifier) != Con.Green
                    ? r2 / (scowls ? 7.5f : 10f)
                    : r2 / (scowls ? 500f : 666f);
            case Con.Yellow:
            case Con.Red:
                return r2 / (scowls ? 4.5f : 6f);
            default:
                return r2 / (scowls ? 5f : 6.5f);
        }
    }
}

/// <summary>
/// How a player stands with an NPC's faction. The legacy computation (player faction values, race,
/// class and deity modifiers, npc_faction_entries) is ported in a later milestone; until then the
/// server uses <see cref="IndifferentFactions"/> (no aggro) or a test implementation.
/// </summary>
public interface IFactionStandings
{
    FactionStanding Standing(ZoneInstance.Entity player, NpcTemplate npc);
}

public sealed class IndifferentFactions : IFactionStandings
{
    public FactionStanding Standing(ZoneInstance.Entity player, NpcTemplate npc) => FactionStanding.Indifferent;
}
