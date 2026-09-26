using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Kick, bash, taunt, mend, hide, sneak, forage (client_process.cpp, Mend.cpp, forage.cpp).</summary>
public class AbilityTests
{
    private sealed class Kos : IFactionStandings
    {
        public FactionStanding Standing(ZoneInstance.Entity player, NpcTemplate npc) => FactionStanding.Scowls;
    }

    private static NpcTemplate Orc(int level) => new(1, "an_orc", 54, 0, level, 6f) { Combat = new NpcCombatStats(1, 5000, 1, 2) };

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, ZoneInstance.Entity Npc) Setup(int classId, int skillValue = 100,
        float npcAt = 8, IFactionStandings? factions = null, int npcLevel = 20)
    {
        var items = new InMemoryItemSource();
        foreach (int food in new[] { 13046, 13045, 13419, 13048, 13047, 13044, 13106 })
            items.Items[food] = new ItemStats(food, "Food " + food, 0, 0, 14, 0);
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(npcAt, 0, 0), 0, 0, [(Orc(npcLevel), 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data, seed: 9) { Items = items, Factions = factions ?? new IndifferentFactions() };
        var skills = Enumerable.Repeat(skillValue, SkillCaps.SkillCount).ToArray();
        var fighter = new Combatant(true, 30, classId, 400, 100, 100, 100, 100, 0, 5, 3f);
        var progress = new ZoneInstance.PlayerProgress(0, "", default, null, new ZoneInstance.PlayerInventory()) { Skills = skills, Str = 100 };
        var player = zone.AddPlayer("Qbot", 1, 0, 30, new Vec3(0, 0, 0), fighter: fighter, progress: progress);
        zone.DrainEvents();
        return (zone, player, zone.Entities.Single(e => !e.IsPlayer));
    }

    [Fact]
    public void Only_classes_that_learn_a_skill_may_use_it()
    {
        Assert.True(ZoneInstance.CanUse(ZoneInstance.KickSkill, CombatFormulas.Warrior));
        Assert.False(ZoneInstance.CanUse(ZoneInstance.KickSkill, CombatFormulas.Wizard));
        Assert.True(ZoneInstance.CanUse(ZoneInstance.MendSkill, CombatFormulas.Monk));
        Assert.True(ZoneInstance.CanUse(ZoneInstance.ForageSkill, CombatFormulas.Druid));
    }

    [Fact]
    public void A_kick_hurts_the_target_angers_it_and_waits_for_its_reuse_time()
    {
        var (zone, player, orc) = Setup(CombatFormulas.Warrior);
        zone.SetTarget(player.Id, orc.Id);
        zone.UseAbility(player.Id, ZoneInstance.KickSkill);
        var events = zone.DrainEvents();
        var swing = Assert.Single(events.OfType<ZoneInstance.Swung>());
        Assert.InRange(swing.Damage, 0, (100 + 100) / 25 * 4 + 30); // ((kick + STR) / 25 × 4 + level) × a fraction
        Assert.Equal(player.Id, orc.TargetId);

        zone.UseAbility(player.Id, ZoneInstance.KickSkill);
        Assert.Contains(new ZoneInstance.Told(player.Id, "You can't use that ability again yet."), zone.DrainEvents());
        for (int i = 0; i < 165; i++)
            zone.Tick(0.05f);
        zone.DrainEvents();
        zone.UseAbility(player.Id, ZoneInstance.KickSkill);
        Assert.Single(zone.DrainEvents().OfType<ZoneInstance.Swung>());
    }

    [Fact]
    public void A_bash_does_damage_from_level_one()
    {
        var (zone, player, orc) = Setup(CombatFormulas.Warrior);
        player.Level = 5;
        zone.SetTarget(player.Id, orc.Id);
        zone.UseAbility(player.Id, ZoneInstance.BashSkill);
        var swing = Assert.Single(zone.DrainEvents().OfType<ZoneInstance.Swung>());
        Assert.Equal((int)(5 / 10f * 3 * (100 + 100 + 5) / (700 - 100)), swing.Damage);
    }

    [Fact]
    public void Taunting_turns_the_npc_on_the_taunter()
    {
        var (zone, player, orc) = Setup(CombatFormulas.Warrior);
        orc.TargetId = 12345;
        zone.SetTarget(player.Id, orc.Id);
        zone.UseAbility(player.Id, ZoneInstance.TauntSkill);
        Assert.Equal(player.Id, orc.TargetId);
    }

    [Fact]
    public void Mending_heals_a_quarter_of_the_hit_points()
    {
        var (zone, player, _) = Setup(CombatFormulas.Monk, skillValue: 200);
        player.Hp = 100;
        zone.UseAbility(player.Id, ZoneInstance.MendSkill);
        Assert.Equal(200, player.Hp);
        Assert.Contains(new ZoneInstance.Told(player.Id, "You mend your wounds and heal some damage"), zone.DrainEvents());
    }

    [Fact]
    public void Hidden_players_are_not_seen_until_they_move()
    {
        var (zone, player, orc) = Setup(CombatFormulas.Rogue, skillValue: 200, npcAt: 30, factions: new Kos(), npcLevel: 30);
        zone.UseAbility(player.Id, ZoneInstance.HideSkill);
        Assert.True(player.Hidden); // 200/300 + 25%: this seed hides
        for (int i = 0; i < 40; i++)
            zone.Tick(0.05f);
        Assert.Null(orc.TargetId);
        zone.MovePlayer(player.Id, new Vec3(5, 0, 0), 0);
        Assert.False(player.Hidden);
        for (int i = 0; i < 40; i++)
            zone.Tick(0.05f);
        Assert.Equal(player.Id, orc.TargetId);
    }

    [Fact]
    public void Foraging_finds_common_food_with_skill()
    {
        var (zone, player, _) = Setup(CombatFormulas.Druid, skillValue: 239);
        zone.UseAbility(player.Id, ZoneInstance.ForageSkill);
        var found = player.Inventory!.Items[ZoneInstance.PlayerInventory.FirstGeneral];
        Assert.Contains(found, new[] { 13046, 13045, 13419, 13048, 13047, 13044, 13106 });
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.Told t && t.Text.StartsWith("You forage a Food"));
    }
}
