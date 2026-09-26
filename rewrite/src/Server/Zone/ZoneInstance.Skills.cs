using EQClassic.Server.Combat;

namespace EQClassic.Server.Zone;

/// <summary>Skill-ups after Client::CheckAddSkill (Zone/Source/client.cpp) and its callers.</summary>
public sealed partial class ZoneInstance
{
    /// <summary>A player's skill went up; the server saves the skills when they leave.</summary>
    public sealed record SkillUp(int PlayerId, int Skill, int Value) : ZoneEvent;
    public sealed record SkillsChanged(int PlayerId) : ZoneEvent;

    private static int SkillOf(Entity e, int skill) => skill >= 0 && skill < e.Skills.Length ? e.Skills[skill] : 0;

    /// <summary>
    /// CheckAddSkill: below the class's cap for the level, one more point with the legacy chance, and
    /// the client's "You have become better at ..." line. Training at a guildmaster is not modelled:
    /// an untrained skill can go up from 0.
    /// </summary>
    private void CheckAddSkill(Entity player, int skill, int modifier = 0)
    {
        if (!player.IsPlayer || skill < 0 || skill >= SkillCaps.SkillCount || skill >= player.Skills.Length)
            return;
        int value = player.Skills[skill];
        if (value >= SkillCaps.Cap(skill, player.Fighter.Class, player.Level))
            return;
        if (_random.Next(100) >= SkillCaps.SkillUpChance(value, modifier))
            return;
        player.Skills[skill] = value + 1;
        _events.Add(new Told(player.Id, $"You have become better at {SkillCaps.Names[skill]}! ({value + 1})"));
        _events.Add(new SkillUp(player.Id, skill, value + 1));
        if (skill is SkillCaps.Offense or SkillCaps.Defense || skill == player.Fighter.WeaponSkill)
            RebuildFighter(player);
        _events.Add(new SkillsChanged(player.Id));
    }
}
