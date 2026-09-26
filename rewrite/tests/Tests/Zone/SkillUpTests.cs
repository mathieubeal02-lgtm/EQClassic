using EQClassic.Server.Combat;
using EQClassic.Server.Characters;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Skill caps for every skill and skill-ups (Mob::CheckMaxSkill, Client::CheckAddSkill).</summary>
public class SkillUpTests
{
    private const int Kick = 30, Channeling = 13, Meditate = 31, Bash = 10;

    [Theory]
    [InlineData(Kick, CombatFormulas.Warrior, 20, 105)]      // level × 5 + 5, under 149
    [InlineData(Kick, CombatFormulas.Warrior, 50, 149)]
    [InlineData(Kick, CombatFormulas.Monk, 55, 225)]         // 200 → 250 beyond 50: 5 per level
    [InlineData(Channeling, CombatFormulas.Warrior, 50, 0)]  // warriors never channel
    [InlineData(Meditate, CombatFormulas.Wizard, 60, 252)]
    [InlineData(Bash, CombatFormulas.Paladin, 30, 155)]
    public void Every_skill_has_its_cap(int skill, int classId, int level, int cap) =>
        Assert.Equal(cap, SkillCaps.Cap(skill, classId, level));

    [Theory]
    [InlineData(0, -10, 12)]
    [InlineData(200, 0, 12)]
    [InlineData(250, -10, 1)]
    public void The_chance_falls_as_the_skill_grows(int value, int modifier, int chance) =>
        Assert.Equal(chance, SkillCaps.SkillUpChance(value, modifier));

    [Fact]
    public void Fighting_raises_offense_and_the_weapon_skill_up_to_the_cap()
    {
        var rat = new NpcTemplate(1, "a_rat", 36, 2, 1, 2f) { Combat = new NpcCombatStats(1, 100000, 1, 1) };
        var zone = new ZoneInstance(new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(5, 0, 0), 0, 0, [(rat, 100)], 600, 0)], new Dictionary<int, Grid>()), seed: 4);
        var skills = new int[SkillCaps.SkillCount];
        var profile = Characters.ProfileBuilder.Record(1, 1, "Qbot", 1, CombatFormulas.Warrior, 1, "qeynos2").Profile with { Str = 75, Sta = 75, Agi = 75, Dex = 75 };
        var progress = new ZoneInstance.PlayerProgress(0, "", default, (level, b) => Combatant.ForPlayer(profile with { Level = level, Skills = skills }, null, b)) { Skills = skills };
        var player = zone.AddPlayer("Qbot", 1, 0, 1, new Vec3(0, 0, 0), progress: progress);
        var orc = zone.Entities.Single(e => !e.IsPlayer);
        zone.SetTarget(player.Id, orc.Id);
        zone.SetAutoAttack(player.Id, true);
        var events = new List<ZoneInstance.ZoneEvent>();
        for (int i = 0; i < 20 * 600; i++)
        {
            zone.Tick(0.05f);
            events.AddRange(zone.DrainEvents());
            player.Hp = player.Fighter.MaxHp; // stay alive
        }
        Assert.Equal(10, skills[SkillCaps.Offense]);     // level 1 cap: 1 × 5 + 5
        Assert.Equal(10, skills[SkillCaps.HandToHand]);
        Assert.Contains(new ZoneInstance.Told(player.Id, "You have become better at Offense! (1)"), events);
        Assert.Contains(new ZoneInstance.SkillUp(player.Id, SkillCaps.Defense, 1), events);
    }

    [Fact]
    public void Skills_are_saved_without_losing_the_untrained_markers()
    {
        var raw = Characters.ProfileBuilder.Build("Qbot", 1, 1, 10, "qeynos2");
        raw[2508 + Kick] = 254; // not trained yet
        raw[2508 + Bash] = 254;
        var skills = new int[SkillCaps.SkillCount];
        skills[Kick] = 12;
        ProfileTemplate.SetSkills(raw, skills);
        Assert.Equal(12, raw[2508 + Kick]);
        Assert.Equal(254, raw[2508 + Bash]);
    }
}
