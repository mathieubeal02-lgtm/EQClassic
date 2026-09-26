using EQClassic.Server.Combat;
using EQClassic.Server.Spells;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Tests.Combat;

namespace EQClassic.Tests.Zone;

/// <summary>Buffs after Mob::AddBuff, CheckStackConflict, ApplySpellsBonuses and the tic processing.</summary>
public class BuffTests
{
    private const int SkinLikeWood = 26, Quickness = 39, Clarity = 174, Courage = 202, SpiritOfWolf = 278, ShallowBreath = 286,
        MinorShielding = 288, Shielding = 309, DiseaseCloud = 340;

    private static Spell S(int id) => SpellRulesTests.File()[id];

    [Theory]
    [InlineData(Courage, 1, 270)]       // a duration in the file wins over the formula
    [InlineData(ShallowBreath, 20, 3)]  // formula 5: three tics
    public void Tics_come_from_the_duration_or_the_formula(int spell, int level, int tics) =>
        Assert.Equal(tics, BuffRules.Tics(S(spell), level));

    [Fact]
    public void A_stronger_buff_replaces_a_weaker_one_and_a_weaker_one_does_not_take_hold()
    {
        var buffs = new List<Buff>();
        Assert.Empty(BuffRules.Add(buffs, new Buff(S(MinorShielding), 1, 20, 270), onNpc: false)!);
        var replaced = BuffRules.Add(buffs, new Buff(S(Shielding), 1, 20, 360), onNpc: false);
        Assert.Equal(MinorShielding, Assert.Single(replaced!).Spell.Id);
        Assert.Null(BuffRules.Add(buffs, new Buff(S(MinorShielding), 1, 20, 270), onNpc: false));
        Assert.Equal(Shielding, Assert.Single(buffs).Spell.Id);

        // Unrelated effects stack; the same spell again refreshes.
        Assert.Empty(BuffRules.Add(buffs, new Buff(S(SpiritOfWolf), 1, 20, 360), onNpc: false)!);
        Assert.Single(BuffRules.Add(buffs, new Buff(S(SpiritOfWolf), 1, 20, 360), onNpc: false)!);
        Assert.Equal(2, buffs.Count);
    }

    [Fact]
    public void Damage_over_time_from_two_casters_stacks_on_an_npc()
    {
        var buffs = new List<Buff>();
        BuffRules.Add(buffs, new Buff(S(DiseaseCloud), 1, 5, 60), onNpc: true);
        Assert.Empty(BuffRules.Add(buffs, new Buff(S(DiseaseCloud), 2, 5, 60), onNpc: true)!);
        Assert.Equal(2, buffs.Count);
    }

    [Fact]
    public void Bonuses_add_up_the_effects_at_the_casters_level()
    {
        var b = StatBonuses.From([new Buff(S(Courage), 1, 10, 270), new Buff(S(Quickness), 2, 30, 110), new Buff(S(SpiritOfWolf), 3, 20, 360),
            new Buff(S(Clarity), 4, 40, 270), new Buff(S(DiseaseCloud), 5, 1, 60)]);
        Assert.Equal(StatBonuses.ApproximateSpellAC(15), b.AC); // 10 + 10/2, at most 15
        Assert.Equal(20, b.Hp);                                  // 10 + 10, at most 20
        Assert.Equal(30, b.Haste);                               // 120 + 30/2 = 135, at most 130
        Assert.Equal(40, b.MovementSpeed);                       // 30 + 20/2
        Assert.Equal(6, b.ManaPerTic);                           // 1 + 40/8
        Assert.Equal(-1, b.HpPerTic);
    }

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, ZoneInstance.Entity Npc) Setup(int classId, params int[] gems)
    {
        var rat = new NpcTemplate(1, "a_rat", 36, 2, 5, 2f) { Combat = new NpcCombatStats(1, 100, 1, 2) };
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(30, 0, 0), 0, 0, [(rat, 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data, seed: 2) { Spells = SpellRulesTests.File() };
        var profile = Characters.ProfileBuilder.Record(1, 1, "Qcaster", 1, classId, 20, "qeynos2").Profile with { Sta = 75, Str = 75, Agi = 75 };
        var magic = new ZoneInstance.PlayerMagic(150, 150, Enumerable.Repeat(200, 74).ToArray(), gems, gems);
        var progress = new ZoneInstance.PlayerProgress(0, "", default, (level, b) => Combatant.ForPlayer(profile with { Level = level }, null, b), null, magic);
        var player = zone.AddPlayer("Qcaster", 1, 0, 20, new Vec3(0, 0, 0), progress: progress);
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

    private static List<ZoneInstance.ZoneEvent> CastAndWait(ZoneInstance zone, ZoneInstance.Entity player, int gem)
    {
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

    [Fact]
    public void A_self_buff_raises_armour_and_hit_points_then_fades()
    {
        var (zone, player, _) = Setup(CombatFormulas.Magician, MinorShielding);
        var before = player.Fighter;
        var events = CastAndWait(zone, player, 0);
        var buff = Assert.Single(player.Buffs);
        Assert.Equal(MinorShielding, buff.Spell.Id);
        Assert.Contains(new ZoneInstance.BuffsChanged(player.Id), events);
        Assert.True(player.Fighter.Mitigation > before.Mitigation);
        Assert.Equal(before.MaxHp + 10, player.Fighter.MaxHp); // 5 + 20, at most 10

        buff.TicsLeft = 1;
        events = Run(zone, 6.1f);
        Assert.Empty(player.Buffs);
        Assert.Contains(new ZoneInstance.BuffFaded(player.Id, MinorShielding), events);
        Assert.Equal(before.MaxHp, player.Fighter.MaxHp);
    }

    [Fact]
    public void Damage_over_time_hurts_every_tic_and_the_caster_gets_the_credit()
    {
        const int TaintedBreath = 277;
        var (zone, player, rat) = Setup(CombatFormulas.Shaman, TaintedBreath);
        zone.SetTarget(player.Id, rat.Id);
        CastAndWait(zone, player, 0);
        Assert.Single(rat.Buffs); // poison, level 5 against 20: resisted 3% of the time, not with this seed
        var spell = S(TaintedBreath);                     // poison counter, -10 at once, -8 per tic
        int once = spell.Value(1, 20), perTic = spell.Value(2, 20);
        Assert.Equal(100 + once + perTic, rat.Hp);       // SpellEffect: the damage over time lands at once too
        Assert.Equal(player.Id, rat.TargetId);
        Run(zone, 12.1f);
        Assert.Equal(100 + once + perTic + 2 * (perTic + 1), rat.Hp); // two tics, less the rat's own regeneration (1 per tic)
    }

    [Fact]
    public void Spirit_of_wolf_lets_the_player_move_faster()
    {
        var (zone, player, _) = Setup(CombatFormulas.Shaman, SpiritOfWolf);
        CastAndWait(zone, player, 0);
        Assert.Equal(40, player.Bonuses.MovementSpeed);
        Run(zone, 1f);
        Assert.Null(zone.MovePlayer(player.Id, new Vec3(player.Position.X + 90, 0, 0), 0)); // 90 u/s: over 70, under 70 × 1.4
    }

    [Fact]
    public void Buffs_are_kept_in_the_profile_and_come_back()
    {
        var (zone, player, _) = Setup(CombatFormulas.Magician, MinorShielding);
        CastAndWait(zone, player, 0);
        var saved = ZoneInstance.SaveBuffs(player);
        Assert.Equal(new ZoneInstance.SavedBuff(MinorShielding, 20, 270), Assert.Single(saved));

        var profile = Characters.ProfileBuilder.Build("Qcaster", 1, CombatFormulas.Magician, 20, "qeynos2");
        EQClassic.Server.Characters.ProfileTemplate.SetBuffs(profile, saved.Select(b => (b.SpellId, b.CasterLevel, b.TicsLeft)).ToList());
        var read = EQClassic.Server.Characters.PlayerProfile.Read(profile)!;
        Assert.Equal([(MinorShielding, 20, 270)], read.Buffs);
    }
}
