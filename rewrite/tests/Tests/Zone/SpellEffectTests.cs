using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Tests.Combat;

namespace EQClassic.Tests.Zone;

/// <summary>The effects beyond hit points (Zone/Source/SpellEffects.cpp, spells.cpp SpellFinished).</summary>
public class SpellEffectTests
{
    private const int BindAffinity = 35, Gate = 36, Invisibility = 42, SummonFood = 50, Root = 230, WordOfPain = 231,
        Levitate = 261, SpiritOfWolf = 278, Mesmerize = 292, Stun = 216;

    private sealed class Kos : IFactionStandings
    {
        public FactionStanding Standing(ZoneInstance.Entity player, NpcTemplate npc) => FactionStanding.Scowls;
    }

    private static NpcTemplate Orc(int level = 5) => new(1, "an_orc", 54, 0, level, 6f) { Combat = new NpcCombatStats(1, 200, 1, 2) };

    /// <summary>A level 30 caster of every spell above (their class is not checked when casting a gem).</summary>
    private static (ZoneInstance Zone, ZoneInstance.Entity Player) Setup(ZoneRules? rules = null, IFactionStandings? factions = null,
        params (Vec3 At, NpcTemplate Npc)[] npcs)
    {
        var spawns = npcs.Select((n, i) => new SpawnPoint(i + 1, n.At, 0, 0, [(n.Npc, 100)], 600, 0)).ToList();
        var data = new ZoneData("qeynos2", spawns, new Dictionary<int, Grid>()) { Rules = rules ?? ZoneRules.Anything };
        var zone = new ZoneInstance(data, seed: 5) { Spells = SpellRulesTests.File(), Factions = factions ?? new IndifferentFactions() };
        int[] book = [BindAffinity, Gate, Invisibility, SummonFood, Root, WordOfPain, Levitate, SpiritOfWolf, Mesmerize, Stun];
        var magic = new ZoneInstance.PlayerMagic(200, 200, Enumerable.Repeat(250, 74).ToArray(), book, book);
        var fighter = new Combatant(true, 30, CombatFormulas.Enchanter, 300, 100, 100, 100, 100, 0, 5, 3f);
        var progress = new ZoneInstance.PlayerProgress(0, "qeynos2", new Vec3(-100, -100, 0), null, new ZoneInstance.PlayerInventory(), magic);
        var player = zone.AddPlayer("Qcaster", 1, 0, 30, new Vec3(0, 0, 0), fighter: fighter, progress: progress);
        zone.DrainEvents();
        return (zone, player);
    }

    private static List<ZoneInstance.ZoneEvent> Run(ZoneInstance zone, float seconds)
    {
        var events = new List<ZoneInstance.ZoneEvent>();
        for (float t = 0; t < seconds; t += 0.05f)
        {
            zone.Tick(0.05f);
            events.AddRange(zone.DrainEvents());
        }
        return events;
    }

    /// <summary>Casts the spell (again after a fizzle) and waits for it to land.</summary>
    private static List<ZoneInstance.ZoneEvent> Cast(ZoneInstance zone, ZoneInstance.Entity player, int spellId)
    {
        int gem = Array.IndexOf(player.Gems, spellId);
        if (gem < 0 || gem >= ZoneInstance.GemCount)
        {
            player.Gems[7] = spellId;
            gem = 7;
        }
        for (int attempt = 0; attempt < 20; attempt++)
        {
            player.Mana = player.MaxMana;
            zone.CastSpell(player.Id, gem);
            var events = zone.DrainEvents().ToList();
            if (events.Contains(new ZoneInstance.Told(player.Id, ZoneInstance.FizzleMessage)))
                continue;
            events.AddRange(Run(zone, (player.Cast?.Spell.CastTimeMs ?? 0) / 1000f + 0.1f));
            return events;
        }
        throw new InvalidOperationException("fizzled 20 times");
    }

    private static ZoneInstance.Entity Npc(ZoneInstance zone, int index = 0) => zone.Entities.Where(e => !e.IsPlayer).OrderBy(e => e.Id).ElementAt(index);

    [Fact]
    public void A_rooted_npc_does_not_chase()
    {
        var (zone, player) = Setup(npcs: (new Vec3(40, 0, 0), Orc()));
        var orc = Npc(zone);
        zone.SetTarget(player.Id, orc.Id);
        Cast(zone, player, Root);
        Assert.True(orc.Bonuses.Rooted);
        Assert.Equal(player.Id, orc.TargetId);
        var at = orc.Position;
        Run(zone, 2f);
        Assert.Equal(at, orc.Position);
    }

    [Fact]
    public void A_mesmerized_npc_forgets_its_foes_and_wakes_up_when_hurt()
    {
        var (zone, player) = Setup(npcs: (new Vec3(40, 0, 0), Orc()));
        var orc = Npc(zone);
        zone.SetTarget(player.Id, orc.Id);
        var events = Cast(zone, player, Mesmerize);
        Assert.True(orc.Bonuses.Mezzed);
        Assert.Null(orc.TargetId);
        Assert.Contains(events, e => e is ZoneInstance.Told t && t.Text.StartsWith("My mind fogs."));

        Cast(zone, player, WordOfPain); // area damage around the caster: the orc is out of its 20 units
        Assert.True(orc.Bonuses.Mezzed);
        Assert.Null(zone.MovePlayer(player.Id, new Vec3(30, 0, 0), 90));
        Run(zone, 0.5f);
        Cast(zone, player, WordOfPain);
        Assert.False(orc.Bonuses.Mezzed);
        Assert.Equal(player.Id, orc.TargetId);
    }

    [Fact]
    public void A_stunned_npc_neither_moves_nor_fights_for_the_stun_time()
    {
        var (zone, player) = Setup(npcs: (new Vec3(40, 0, 0), Orc()));
        var orc = Npc(zone);
        zone.SetTarget(player.Id, orc.Id);
        Cast(zone, player, Stun);
        var at = orc.Position;
        Run(zone, 3.5f);
        Assert.Equal(at, orc.Position);
        Run(zone, 1f);
        Assert.NotEqual(at, orc.Position); // 4 s later it comes for the caster
    }

    [Fact]
    public void Kos_npcs_do_not_see_an_invisible_player_until_they_attack()
    {
        var (zone, player) = Setup(factions: new Kos(), npcs: (new Vec3(300, 0, 0), Orc(30)));
        Cast(zone, player, Invisibility);
        Assert.True(player.Bonuses.Invisible);
        player.Position = new Vec3(290, 0, 0); // right next to a level 30 orc that hates us
        Run(zone, 3f);
        Assert.Null(Npc(zone).TargetId);

        zone.SetTarget(player.Id, Npc(zone).Id);
        zone.SetAutoAttack(player.Id, true);
        Assert.False(player.Bonuses.Invisible);
        Assert.Contains(new ZoneInstance.BuffFaded(player.Id, Invisibility), zone.DrainEvents());
    }

    [Fact]
    public void Bind_affinity_moves_the_bind_point_and_gate_goes_there()
    {
        var (zone, player) = Setup();
        Run(zone, 1f);
        Assert.Null(zone.MovePlayer(player.Id, new Vec3(50, 20, 0), 0));
        var events = Cast(zone, player, BindAffinity);
        Assert.Equal(("qeynos2", new Vec3(50, 20, 0)), (player.BindZone, player.Bind));
        Assert.Contains(new ZoneInstance.BindChanged(player.Id), events);

        Run(zone, 1f);
        Assert.Null(zone.MovePlayer(player.Id, new Vec3(80, 20, 0), 0));
        events = Cast(zone, player, Gate);
        Assert.Contains(new ZoneInstance.Teleported(player.Id, new Vec3(50, 20, 0), "gate"), events);
        Assert.Equal(new Vec3(50, 20, 0), player.Position);
    }

    [Fact]
    public void The_zone_rules_refuse_binding_levitation_and_outdoor_spells_and_the_mana_is_spent()
    {
        var (zone, player) = Setup(new ZoneRules(0, false, false));
        foreach (var (spell, message) in new[] { (BindAffinity, "You may not bind here."), (Levitate, "You can't levitate in this zone."),
                     (SpiritOfWolf, "You can't cast this spell indoors.") })
        {
            var events = Cast(zone, player, spell);
            Assert.Contains(new ZoneInstance.Told(player.Id, message), events);
            Assert.Empty(player.Buffs);
            Assert.True(player.Mana < player.MaxMana);
        }
    }

    [Fact]
    public void Area_spells_hit_every_npc_around_the_caster()
    {
        var (zone, player) = Setup(npcs: [(new Vec3(10, 0, 0), Orc()), (new Vec3(0, 12, 0), Orc()), (new Vec3(60, 0, 0), Orc())]);
        var events = Cast(zone, player, WordOfPain);
        var hit = events.OfType<ZoneInstance.SpellLanded>().Select(l => l.TargetId).ToHashSet();
        Assert.Contains(Npc(zone, 0).Id, hit);
        Assert.Contains(Npc(zone, 1).Id, hit);
        Assert.DoesNotContain(Npc(zone, 2).Id, hit);
        Assert.DoesNotContain(player.Id, hit);
    }

    [Fact]
    public void Summoning_puts_the_item_in_a_free_general_slot()
    {
        var (zone, player) = Setup();
        Cast(zone, player, SummonFood);
        Assert.Equal(13078, player.Inventory!.Items[ZoneInstance.PlayerInventory.FirstGeneral]);
    }

    [Fact]
    public void Levitation_is_in_the_bonuses()
    {
        var (zone, player) = Setup();
        Cast(zone, player, Levitate);
        Assert.True(player.Bonuses.Levitating);
    }
}
