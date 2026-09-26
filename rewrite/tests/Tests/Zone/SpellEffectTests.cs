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

    private const int ShieldOfThistles = 256, RuneI = 481, CancelMagic = 48, Lull = 208, Fear = 229, EvacuateNorth = 602, Yaulp = 210;

    /// <summary>Casts until the spell takes hold (NPCs resist some).</summary>
    private static void CastUntil(ZoneInstance zone, ZoneInstance.Entity player, int spellId, Func<bool> landed)
    {
        for (int i = 0; i < 30 && !landed(); i++)
            Cast(zone, player, spellId);
        Assert.True(landed());
    }

    [Fact]
    public void Thorns_hurt_whoever_hits_the_shielded()
    {
        var (zone, player) = Setup(factions: new Kos(), npcs: (new Vec3(5, 0, 0), Orc()));
        var orc = Npc(zone);
        zone.SetTarget(player.Id, player.Id);
        Cast(zone, player, ShieldOfThistles);
        Assert.True(player.Bonuses.DamageShield >= 3); // base 3, growing with the caster's level
        var events = Run(zone, 20f);
        int hits = events.OfType<ZoneInstance.Swung>().Count(s => s.AttackerId == orc.Id && s.Damage > 0);
        Assert.True(hits > 0);
        Assert.True(orc.Hp < orc.Fighter.MaxHp); // the player never swung
        Assert.Contains(new ZoneInstance.Told(player.Id, "an orc is pierced by thorns!"), events);
    }

    [Fact]
    public void A_rune_takes_the_blows_until_it_is_used_up()
    {
        var (zone, player) = Setup(factions: new Kos(), npcs: (new Vec3(5, 0, 0), Orc(level: 20)));
        zone.SetTarget(player.Id, player.Id);
        Cast(zone, player, RuneI);
        Assert.Equal(27, player.Buffs.Single().RuneLeft);
        int hp = player.Hp;
        var events = new List<ZoneInstance.ZoneEvent>();
        while (player.Buffs.Count > 0)
        {
            events.AddRange(Run(zone, 1f));
            if (player.Buffs.Count > 0)
                Assert.True(player.Hp >= hp); // nothing gets through while the rune holds (regeneration may add)
        }
        Assert.Contains(new ZoneInstance.BuffFaded(player.Id, RuneI), events);
    }

    [Fact]
    public void Divine_aura_keeps_every_blow_out()
    {
        var (zone, player) = Setup(factions: new Kos(), npcs: (new Vec3(5, 0, 0), Orc(level: 20)));
        zone.SetTarget(player.Id, player.Id);
        Cast(zone, player, 207); // Divine Aura
        Assert.True(player.Bonuses.Invulnerable);
        int hp = player.Hp;
        var events = Run(zone, 10f);
        Assert.Contains(events, e => e is ZoneInstance.Swung s && s.DefenderId == player.Id);
        Assert.True(player.Hp >= hp);
    }

    [Fact]
    public void Cancel_magic_takes_a_buff_off()
    {
        var (zone, player) = Setup();
        zone.SetTarget(player.Id, player.Id);
        Cast(zone, player, SpiritOfWolf);
        Assert.Single(player.Buffs);
        Cast(zone, player, CancelMagic);
        Assert.Empty(player.Buffs);
    }

    [Fact]
    public void A_lulled_npc_lets_players_pass_and_a_feared_one_runs()
    {
        var (zone, player) = Setup(factions: new Kos(), npcs: [(new Vec3(40, 0, 0), Orc()), (new Vec3(-40, 0, 0), Orc())]);
        var calm = Npc(zone);
        zone.SetTarget(player.Id, calm.Id);
        CastUntil(zone, player, Lull, () => calm.Bonuses.FrenzyRadius == 15);
        calm.TargetId = null; // a resisted cast angered it
        Assert.Null(zone.MovePlayer(player.Id, new Vec3(30, 0, 0), 90));
        Run(zone, 3f);
        Assert.Null(calm.TargetId); // aggro range 145 − 15² ≤ 0

        var scared = Npc(zone, 1);
        zone.SetTarget(player.Id, scared.Id);
        CastUntil(zone, player, Fear, () => scared.Bonuses.Feared);
        var events = Run(zone, 3f);
        Assert.DoesNotContain(events, e => e is ZoneInstance.Swung s && s.AttackerId == scared.Id);
        Assert.True(Math.Abs(scared.Position.X - (-40)) > 1 || Math.Abs(scared.Position.Y) > 1); // on the run
    }

    [Fact]
    public void Evacuate_and_yaulp()
    {
        var (zone, player) = Setup();
        zone.SetTarget(player.Id, player.Id);
        player.Fatigue = 50;
        Cast(zone, player, Yaulp);
        Assert.True(player.Fatigue < 50);
        var events = Cast(zone, player, EvacuateNorth);
        Assert.Contains(events, e => e is ZoneInstance.CrossedZoneLine c && c.Line.TargetZone == "northkarana");
    }

    [Fact]
    public void An_illusion_changes_what_others_see_until_it_is_taken_off()
    {
        var (zone, player) = Setup();
        zone.SetTarget(player.Id, player.Id);
        var events = Cast(zone, player, 287); // Minor Illusion: race 142, rooted in place
        Assert.Contains(new ZoneInstance.IllusionChanged(player.Id), events);
        Assert.Equal(142, player.ToSpawn().Race);
        Assert.Equal(1, player.Race);
        events = Cast(zone, player, CancelMagic);
        Assert.Contains(new ZoneInstance.IllusionChanged(player.Id), events);
        Assert.Equal(1, player.ToSpawn().Race);
    }

    private const int Beguile = 182; // charm up to level 37

    [Fact]
    public void A_charmed_npc_serves_until_let_go_then_turns_on_its_charmer()
    {
        var (zone, player) = Setup(npcs: [(new Vec3(20, 0, 0), Orc()), (new Vec3(-20, 0, 0), Orc())]);
        var orc = Npc(zone);
        zone.SetTarget(player.Id, orc.Id);
        CastUntil(zone, player, Beguile, () => orc.OwnerId == player.Id);
        orc.TargetId = null; // resisted tries angered it; a landed charm forgives
        Assert.Equal(orc.Id, player.PetId);

        var other = Npc(zone, 1);
        zone.SetTarget(player.Id, other.Id);
        zone.CommandPet(player.Id, ZoneInstance.PetOrder.Attack);
        Assert.Equal(other.Id, orc.TargetId);

        zone.CommandPet(player.Id, ZoneInstance.PetOrder.GetLost);
        Assert.Null(orc.OwnerId);
        Assert.Null(player.PetId);
        Assert.NotNull(zone.Get(orc.Id)); // let go, not unmade
        Assert.Equal(player.Id, orc.TargetId);
    }

    [Fact]
    public void Charm_and_mez_have_level_caps()
    {
        var (zone, player) = Setup(npcs: [(new Vec3(20, 0, 0), Orc(level: 40)), (new Vec3(-20, 0, 0), Orc(level: 60))]);
        zone.SetTarget(player.Id, Npc(zone).Id);
        var events = Cast(zone, player, Beguile);
        Assert.Contains(new ZoneInstance.Told(player.Id, "Your target is too high of a level for your charm spell."), events);
        Assert.Null(Npc(zone).OwnerId);
        zone.SetTarget(player.Id, Npc(zone, 1).Id);
        events = Cast(zone, player, Mesmerize);
        Assert.Contains(new ZoneInstance.Told(player.Id, "Your target is too high of a level for your mez spell."), events);
        Assert.Equal(55, ZoneInstance.LevelCap(SpellRulesTests.File()[Mesmerize]));
    }

    [Fact]
    public void Translocate_sends_yourself_and_gift_of_magic_raises_the_mana_pool()
    {
        var (zone, player) = Setup();
        zone.SetTarget(player.Id, player.Id);
        int before = player.MaxMana;
        Cast(zone, player, 1408); // Gift of Magic
        Assert.True(player.MaxMana > before);
        var events = Cast(zone, player, 1336); // Translocate: Fay
        Assert.Contains(events, e => e is ZoneInstance.CrossedZoneLine c && c.Line.TargetZone == "gfaydark");
    }

    [Fact]
    public void Translocating_a_group_member_asks_them_first()
    {
        var (zone, player) = Setup();
        var groups = new GroupRegistry();
        zone.Groups = groups;
        var friend = zone.AddPlayer("Qfriend", 1, 0, 30, new Vec3(5, 0, 0));
        groups.Invite("Qcaster", "Qfriend");
        groups.Accept("Qfriend");
        zone.SetTarget(player.Id, friend.Id);

        var offered = Cast(zone, player, 1336); // Translocate: Fay
        Assert.Contains(new ZoneInstance.TranslocateOffered(friend.Id, "Qcaster", "gfaydark"), offered);
        Assert.DoesNotContain(offered, e => e is ZoneInstance.CrossedZoneLine);
        zone.AnswerTranslocate(friend.Id, false);
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.Told { Text: "Qfriend declines the translocation." } t && t.PlayerId == player.Id);

        Cast(zone, player, 1336);
        zone.AnswerTranslocate(friend.Id, true);
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.CrossedZoneLine c && c.PlayerId == friend.Id && c.Line.TargetZone == "gfaydark");

        Cast(zone, player, 1336);
        zone.Tick((float)ZoneInstance.TranslocateWait + 1);
        zone.DrainEvents();
        zone.AnswerTranslocate(friend.Id, true);
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.Told { Text: "The translocation has faded." });
    }

    [Fact]
    public void Flymode_lifts_the_climbing_limit()
    {
        var (zone, player) = Setup();
        zone.Tick(1);
        Assert.NotNull(zone.MovePlayer(player.Id, new Vec3(0, 0, 80), 0)); // 80 up in a second: refused
        zone.GmFlying(player.Id, true);
        Assert.Contains(new ZoneInstance.FlyingChanged(player.Id, true), zone.DrainEvents());
        zone.Tick(1);
        Assert.Null(zone.MovePlayer(player.Id, new Vec3(0, 0, 80), 0));
    }

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
