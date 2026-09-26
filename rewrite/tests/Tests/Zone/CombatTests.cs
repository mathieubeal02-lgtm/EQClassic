using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Melee in the zone: targets, auto-attack, fighting back, deaths, regeneration.</summary>
public class CombatTests
{
    private static readonly NpcTemplate Rat = new(1, "a_rat", 36, 2, 1, 2f) { Combat = new NpcCombatStats(1, 16, 1, 4, AC: 5) };
    private static readonly NpcTemplate Brute = new(2, "a_brute", 54, 0, 30, 6f) { Combat = new NpcCombatStats(1, 3000, 60, 120) };

    // Always hits hard (to-hit far above any avoidance), or never kills anything.
    private static Combatant Hero => new(true, 50, CombatFormulas.Warrior, 1000, 300, 5000, 400, 300, 0, 200, 1f);
    private static Combatant Weakling => new(true, 1, CombatFormulas.Wizard, 20, 1, 1, 1, 1, 0, 1, 3.6f);

    private static ZoneInstance ZoneWith(NpcTemplate npc, Vec3 at) =>
        new(new ZoneData("qeynos2", [new SpawnPoint(1, at, 0, 0, [(npc, 100)], 600, 0)], new Dictionary<int, Grid>()), seed: 3);

    private static void Run(ZoneInstance zone, float seconds)
    {
        for (float t = 0; t < seconds; t += 0.05f)
            zone.Tick(0.05f);
    }

    [Fact]
    public void Auto_attack_needs_a_target()
    {
        var zone = ZoneWith(Rat, new Vec3(5, 0, 0));
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(0, 0, 0));
        zone.SetAutoAttack(player.Id, true);
        Assert.False(player.AutoAttack);
        Assert.Contains(new ZoneInstance.Told(player.Id, "You must first select a target for this command!"), zone.DrainEvents());
    }

    [Fact]
    public void A_player_kills_a_rat_which_respawns_later()
    {
        var zone = ZoneWith(Rat, new Vec3(5, 0, 0));
        var rat = zone.Entities.Single();
        var player = zone.AddPlayer("Qbot", 9, 0, 50, new Vec3(0, 0, 0), fighter: Hero);
        zone.SetTarget(player.Id, rat.Id);
        zone.SetAutoAttack(player.Id, true);
        Run(zone, 0.1f);
        var events = zone.DrainEvents();
        Assert.Contains(events, e => e is ZoneInstance.Swung s && s.AttackerId == player.Id && s.Damage > 0);
        Assert.Contains(new ZoneInstance.Slain(rat.Id, "a_rat", player.Id, "Qbot"), events);
        Assert.Contains(new ZoneInstance.Removed(rat.Id), events);
        Assert.DoesNotContain(zone.Entities, e => e.Id == rat.Id);

        Run(zone, 0.1f); // the target is gone: auto-attack stops
        Assert.False(player.AutoAttack);
        Run(zone, 601);
        Assert.Single(zone.Entities, e => !e.IsPlayer); // respawned
    }

    [Fact]
    public void An_npc_fights_back_and_swings_at_its_own_delay()
    {
        var zone = ZoneWith(Rat, new Vec3(5, 0, 0));
        var rat = zone.Entities.Single();
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(0, 0, 0), fighter: Weakling);
        zone.SetTarget(player.Id, rat.Id);
        zone.SetAutoAttack(player.Id, true);
        Run(zone, 10f);
        var swings = zone.DrainEvents().OfType<ZoneInstance.Swung>().ToList();
        Assert.Equal(player.Id, rat.TargetId);
        int ratSwings = swings.Count(s => s.AttackerId == rat.Id), playerSwings = swings.Count(s => s.AttackerId == player.Id);
        Assert.InRange(ratSwings, 4, 6);      // every 2 s
        Assert.InRange(playerSwings, 2, 3);   // every 3.6 s
    }

    [Fact]
    public void Out_of_reach_the_player_is_told_to_get_closer()
    {
        var zone = ZoneWith(Rat, new Vec3(50, 0, 0));
        var rat = zone.Entities.Single();
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(0, 0, 0));
        zone.SetTarget(player.Id, rat.Id);
        zone.SetAutoAttack(player.Id, true);
        Run(zone, 0.2f);
        var events = zone.DrainEvents();
        Assert.Contains(new ZoneInstance.Told(player.Id, ZoneInstance.TooFarMessage), events);
        Assert.DoesNotContain(events, e => e is ZoneInstance.Swung);
    }

    [Fact]
    public void A_slain_player_comes_back_at_full_health_where_they_entered()
    {
        var zone = ZoneWith(Brute, new Vec3(5, 0, 0));
        var brute = zone.Entities.Single();
        var entry = new Vec3(0, 0, 0);
        var player = zone.AddPlayer("Qbot", 9, 0, 1, entry, fighter: Weakling);
        zone.SetTarget(player.Id, brute.Id);
        zone.SetAutoAttack(player.Id, true);
        Run(zone, 30f);
        var events = zone.DrainEvents();
        Assert.Contains(new ZoneInstance.Slain(player.Id, "Qbot", brute.Id, "a_brute"), events);
        Assert.Contains(new ZoneInstance.Teleported(player.Id, entry, ZoneInstance.DeathReason), events);
        Assert.Equal(player.Fighter.MaxHp, player.Hp);
        Assert.Null(brute.TargetId);
        Assert.False(player.AutoAttack);
    }

    [Fact]
    public void Players_regenerate_out_of_combat_faster_sitting()
    {
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>()));
        var player = zone.AddPlayer("Qbot", 9, 0, 20, new Vec3(0, 0, 0), hp: 10);
        Run(zone, 6.1f);
        Assert.Equal(12, player.Hp); // level 20: 2 per tic
        player.Sitting = true;
        Run(zone, 6f);
        Assert.Equal(16, player.Hp);
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.HealthChanged h && h.Hp == 16);
    }
}
