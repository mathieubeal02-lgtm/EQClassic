using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Tests.Combat;

namespace EQClassic.Tests.Zone;

/// <summary>Casting in the zone after Mob::CastSpell / SpellFinished (Zone/Source/spells.cpp).</summary>
public class SpellCastingTests
{
    private const int BurstOfFlame = 93, MinorHealing = 200, Lull = 208, LightHealing = 17;

    private static NpcTemplate Rat(int hp, int fireResist = 0) =>
        new(1, "a_rat", 36, 2, 1, 2f) { Combat = new NpcCombatStats(1, hp, 1, 2) { FR = fireResist } };

    private static readonly int[] Skilled = Enumerable.Repeat(200, 74).ToArray();

    private static ZoneInstance.PlayerMagic Magic(int? mana = null, params int[] gems) =>
        new(Wis: 75, Int: 150, Skilled, Book: [BurstOfFlame, MinorHealing, Lull, LightHealing], Gems: gems, Mana: mana);

    /// <summary>A zone with one rat and a level 20 caster of <paramref name="classId"/> (magician by default).</summary>
    private static (ZoneInstance Zone, ZoneInstance.Entity Player, ZoneInstance.Entity Rat) Setup(
        NpcTemplate? rat = null, int classId = CombatFormulas.Magician, int? mana = null, int seed = 1, int ratAt = 50,
        ZoneInstance.PlayerInventory? inventory = null)
    {
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(ratAt, 0, 0), 0, 0, [(rat ?? Rat(100), 100)], 600, 0)], new Dictionary<int, Grid>());
        var items = new InMemoryItemSource();
        items.Items[9994] = new ItemStats(9994, "Spell: Burst of Flame*", 0, 0, ItemStats.SpellScroll, 0) { ScrollSpell = BurstOfFlame };
        items.Items[9993] = new ItemStats(9993, "Spell: Minor Healing*", 0, 0, ItemStats.SpellScroll, 0) { ScrollSpell = MinorHealing };
        var zone = new ZoneInstance(data, seed: seed) { Spells = SpellRulesTests.File(), Items = items };
        var fighter = new Combatant(true, 20, classId, 200, 100, 100, 100, 100, 0, 5, 3f);
        var progress = new ZoneInstance.PlayerProgress(0, "", default, null, inventory, Magic(mana, BurstOfFlame, MinorHealing, Lull));
        var player = zone.AddPlayer("Qcaster", 1, 0, 20, new Vec3(0, 0, 0), fighter: fighter, progress: progress);
        zone.DrainEvents();
        return (zone, player, zone.Entities.Single(e => !e.IsPlayer));
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

    [Fact]
    public void Casters_get_their_mana_pool_from_class_level_and_stat()
    {
        var (_, mage, _) = Setup();
        Assert.Equal((30 + 2) * 20, mage.MaxMana);
        Assert.Equal(mage.MaxMana, mage.Mana);
        Assert.Equal([BurstOfFlame, MinorHealing, Lull, -1, -1, -1, -1, -1], mage.Gems);
    }

    [Fact]
    public void A_nuke_lands_after_its_cast_time_costs_mana_and_angers_the_target()
    {
        var (zone, player, rat) = Setup();
        zone.SetTarget(player.Id, rat.Id);
        zone.CastSpell(player.Id, 0);
        Assert.Contains(new ZoneInstance.CastStarted(player.Id, BurstOfFlame, "Burst of Flame", 1500), zone.DrainEvents());
        Assert.NotNull(player.Cast);

        var events = Run(zone, 1.4f);
        Assert.DoesNotContain(events, e => e is ZoneInstance.SpellLanded);
        events = Run(zone, 0.2f);

        Assert.Contains(new ZoneInstance.SpellLanded(player.Id, rat.Id, BurstOfFlame, -5), events);
        Assert.Contains(new ZoneInstance.CastEnded(player.Id, BurstOfFlame, ZoneInstance.CastOutcome.Finished), events);
        Assert.Equal(95, rat.Hp);
        Assert.Equal(player.MaxMana - 7, player.Mana);
        Assert.Equal(player.Id, rat.TargetId);
        Assert.Null(player.Cast);
    }

    [Fact]
    public void A_killing_spell_gives_the_kill_and_its_experience()
    {
        var (zone, player, rat) = Setup(Rat(4));
        zone.SetTarget(player.Id, rat.Id);
        zone.CastSpell(player.Id, 0);
        var events = Run(zone, 2f);
        Assert.Contains(new ZoneInstance.Slain(rat.Id, "a_rat", player.Id, "Qcaster"), events);
        Assert.Contains(zone.Entities, e => e.IsCorpse);
    }

    [Fact]
    public void Moving_while_casting_interrupts_the_spell_without_spending_mana()
    {
        var (zone, player, rat) = Setup();
        zone.SetTarget(player.Id, rat.Id);
        zone.CastSpell(player.Id, 0);
        Run(zone, 0.5f);
        zone.MovePlayer(player.Id, new Vec3(5, 0, 0), 0);
        var events = Run(zone, 0.1f);
        Assert.Contains(new ZoneInstance.Told(player.Id, ZoneInstance.InterruptedMessage), events);
        Assert.Contains(new ZoneInstance.CastEnded(player.Id, BurstOfFlame, ZoneInstance.CastOutcome.Interrupted), events);
        Assert.Equal(player.MaxMana, player.Mana);
        Assert.Equal(100, rat.Hp);
    }

    [Fact]
    public void A_heal_without_a_target_heals_the_caster()
    {
        var (zone, player, _) = Setup(classId: CombatFormulas.Cleric);
        player.Hp = 150;
        zone.CastSpell(player.Id, 1);
        var events = Run(zone, 1.1f);
        Assert.Contains(new ZoneInstance.SpellLanded(player.Id, player.Id, MinorHealing, 10), events);
        Assert.Contains(new ZoneInstance.HealthChanged(player.Id, 160, 200), events);
    }

    [Fact]
    public void Hostile_spells_need_a_target_in_range_and_enough_mana()
    {
        var (zone, player, rat) = Setup(ratAt: 500);
        zone.CastSpell(player.Id, 0);
        Assert.Contains(new ZoneInstance.Told(player.Id, ZoneInstance.NeedTargetMessage), zone.DrainEvents());

        zone.SetTarget(player.Id, rat.Id);
        zone.CastSpell(player.Id, 0);
        Assert.Contains(new ZoneInstance.Told(player.Id, ZoneInstance.OutOfRangeMessage), zone.DrainEvents());

        var (poor, broke, rat2) = Setup(mana: 3);
        poor.SetTarget(broke.Id, rat2.Id);
        poor.CastSpell(broke.Id, 0);
        Assert.Contains(new ZoneInstance.Told(broke.Id, ZoneInstance.NoManaMessage), poor.DrainEvents());
        Assert.Null(broke.Cast);
    }

    [Fact]
    public void Spells_with_effects_not_written_yet_are_refused_before_any_mana_is_spent()
    {
        var (zone, player, rat) = Setup();
        zone.SetTarget(player.Id, rat.Id);
        player.Gems[2] = 500; // Bind Sight: not written yet
        zone.CastSpell(player.Id, 2);
        Assert.Contains(new ZoneInstance.Told(player.Id, ZoneInstance.NotYetMessage), zone.DrainEvents());
        Assert.Equal(player.MaxMana, player.Mana);
    }

    [Fact]
    public void Some_casts_fizzle_and_cost_the_full_mana()
    {
        // Level 20 magician, skill 200: the minimum 5% chance. Cast until one fizzles.
        var (zone, player, rat) = Setup(Rat(30000));
        zone.SetTarget(player.Id, rat.Id);
        for (int i = 0; i < 200; i++)
        {
            int before = player.Mana;
            zone.CastSpell(player.Id, 0);
            if (zone.DrainEvents().Contains(new ZoneInstance.Told(player.Id, ZoneInstance.FizzleMessage)))
            {
                Assert.Equal(before - 7, player.Mana);
                Assert.Null(player.Cast);
                return;
            }
            Run(zone, 1.6f);
            player.Mana = player.MaxMana;
        }
        Assert.Fail("no fizzle in 200 casts");
    }

    [Fact]
    public void Memorising_needs_the_spell_in_the_book_and_the_level()
    {
        var (zone, player, _) = Setup();
        zone.MemorizeSpell(player.Id, 3, 12345);
        Assert.Equal(-1, player.Gems[3]);
        zone.MemorizeSpell(player.Id, 3, LightHealing); // a cleric spell, not a magician's
        Assert.Equal(-1, player.Gems[3]);
        zone.MemorizeSpell(player.Id, 0, -1);
        Assert.Equal(-1, player.Gems[0]);
        Assert.Contains(new ZoneInstance.GemsChanged(player.Id), zone.DrainEvents());
    }

    [Fact]
    public void Mana_comes_back_every_tic()
    {
        var (zone, player, _) = Setup(mana: 10);
        var events = Run(zone, 6.1f);
        Assert.Contains(new ZoneInstance.ManaChanged(player.Id, 16, player.MaxMana), events); // 2 + 20/5
    }

    [Fact]
    public void Scribing_a_scroll_puts_its_spell_in_the_book_and_uses_the_scroll_up()
    {
        var inventory = new ZoneInstance.PlayerInventory();
        inventory.Items[22] = 9994;
        inventory.Items[23] = 9994;
        var (zone, player, _) = Setup(inventory: inventory);
        var fresh = new ZoneInstance.PlayerMagic(75, 150, Skilled, Book: [], Gems: []);
        // A new magician: empty book.
        var zone2 = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>())) { Spells = SpellRulesTests.File(), Items = zone.Items };
        var inv2 = new ZoneInstance.PlayerInventory();
        inv2.Items[22] = 9994;
        inv2.Items[23] = 9993; // Minor Healing: not a magician spell
        var mage = zone2.AddPlayer("Qmage", 1, 0, 1, new Vec3(0, 0, 0), fighter: new Combatant(true, 1, CombatFormulas.Magician, 20, 1, 1, 1, 1, 0, 1, 3f),
            progress: new ZoneInstance.PlayerProgress(0, "", default, null, inv2, fresh));

        zone2.ScribeScroll(mage.Id, 22);
        Assert.Equal(BurstOfFlame, mage.Book[0]);
        Assert.Equal(0, inv2.Items[22]);
        Assert.Contains(new ZoneInstance.Told(mage.Id, "You have finished scribing Burst of Flame."), zone2.DrainEvents());

        zone2.ScribeScroll(mage.Id, 23);
        Assert.Equal(9993, inv2.Items[23]);
        Assert.Contains(new ZoneInstance.Told(mage.Id, "Your class cannot learn this spell."), zone2.DrainEvents());

        zone.ScribeScroll(player.Id, 22); // already in the book
        Assert.Equal(9994, inventory.Items[22]);
        Assert.Contains(new ZoneInstance.Told(player.Id, "You already have this spell scribed."), zone.DrainEvents());
    }
}
