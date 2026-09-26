using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Tests.Combat;

namespace EQClassic.Tests.Zone;

/// <summary>Groups after Zone/Source/groups.cpp: invitations, leaders, split experience, group spells.</summary>
public class GroupTests
{
    [Fact]
    public void The_leader_invites_up_to_six_and_leaving_hands_over_the_lead()
    {
        var groups = new GroupRegistry();
        Assert.Null(groups.Invite("Ann", "Bob"));
        Assert.Equal(["Ann", "Bob"], groups.Accept("Bob"));
        Assert.Equal("Only the group leader may invite.", groups.Invite("Bob", "Cid"));
        foreach (var name in new[] { "Cid", "Dan", "Eve", "Fay" })
        {
            Assert.Null(groups.Invite("Ann", name));
            Assert.NotNull(groups.Accept(name));
        }
        Assert.Equal("Your group is full.", groups.Invite("Ann", "Gus"));
        Assert.Equal("Bob is already in a group.", groups.Invite("Zed", "Bob"));

        Assert.Equal(5, groups.Leave("Ann").Count);
        Assert.Equal("Bob", groups.LeaderOf("Cid"));
        Assert.Empty(groups.MembersOf("Ann"));
    }

    [Fact]
    public void A_group_of_one_is_disbanded_and_invitations_can_be_declined()
    {
        var groups = new GroupRegistry();
        groups.Invite("Ann", "Bob");
        groups.Accept("Bob");
        Assert.Equal(["Ann"], groups.Leave("Bob"));
        Assert.Empty(groups.MembersOf("Ann"));

        groups.Invite("Ann", "Cid");
        Assert.Equal("Ann", groups.Decline("Cid"));
        Assert.Null(groups.Accept("Cid"));
    }

    private static (ZoneInstance Zone, ZoneInstance.Entity Ann, ZoneInstance.Entity Bob, GroupRegistry Groups) TwoPlayers(NpcTemplate? npc = null)
    {
        var spawns = npc is null ? new List<SpawnPoint>() : [new SpawnPoint(1, new Vec3(5, 0, 0), 0, 0, [(npc, 100)], 600, 0)];
        var groups = new GroupRegistry();
        var zone = new ZoneInstance(new ZoneData("qeynos2", spawns, new Dictionary<int, Grid>()), seed: 2) { Groups = groups, Spells = SpellRulesTests.File() };
        var skills = Enumerable.Repeat(200, SkillCaps.SkillCount).ToArray();
        ZoneInstance.Entity Add(string name, int level, int classId, Vec3 at) =>
            zone.AddPlayer(name, 1, 0, level, at, fighter: new Combatant(true, level, classId, 300, 100, 5000, 100, 100, 0, 200, 1f),
                progress: new ZoneInstance.PlayerProgress(0, "", default, null, null, new ZoneInstance.PlayerMagic(150, 150, skills, [], [])) { Skills = skills });
        var ann = Add("Ann", 20, CombatFormulas.Cleric, new Vec3(0, 0, 0));
        var bob = Add("Bob", 10, CombatFormulas.Warrior, new Vec3(0, 20, 0));
        groups.Invite("Ann", "Bob");
        groups.Accept("Bob");
        zone.DrainEvents();
        return (zone, ann, bob, groups);
    }

    [Fact]
    public void Experience_is_shared_by_level()
    {
        var orc = new NpcTemplate(1, "an_orc", 54, 0, 20, 6f) { Combat = new NpcCombatStats(1, 10, 1, 1) };
        var (zone, ann, bob, _) = TwoPlayers(orc);
        zone.SetTarget(ann.Id, zone.Entities.Single(e => !e.IsPlayer).Id);
        zone.SetAutoAttack(ann.Id, true);
        for (int i = 0; i < 100 && zone.Entities.Any(e => !e.IsPlayer && !e.IsCorpse); i++)
            zone.Tick(0.05f);
        uint kill = Experience.ForKill(20);                     // 20² × 75
        Assert.Equal(kill * 25 / 40, ann.Exp);                   // (20 + 5) / (20 + 10 + 2 × 5)
        Assert.Equal(Experience.Capped(kill * 15 / 40, 10, CombatFormulas.Warrior, 1), bob.Exp);
    }

    [Fact]
    public void Group_spells_land_on_the_members_in_range()
    {
        var (zone, ann, bob, _) = TwoPlayers();
        var heal = SpellRulesTests.File()[135]; // Word of Health: a cleric's group heal
        ann.Book = [heal.Id];
        ann.Gems[0] = heal.Id;
        bob.Hp = 100;
        ann.Hp = 100;
        for (int attempt = 0; attempt < 10 && bob.Hp == 100; attempt++)
        {
            ann.Mana = ann.MaxMana = 1000;
            zone.CastSpell(ann.Id, 0);
            for (int i = 0; i < 200 && ann.Cast is not null; i++)
                zone.Tick(0.05f);
        }
        Assert.True(bob.Hp > 100, $"{heal.Name} did not reach the group");
        Assert.True(ann.Hp > 100);
    }
}
