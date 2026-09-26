using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Tests.Combat;

namespace EQClassic.Tests.Zone;

/// <summary>Caster NPCs after NPC::CheckMyOffenseCastStatus and the low-health rescue.</summary>
public class NpcCastingTests
{
    private const int BurstOfFlame = 93, MinorHealing = 200;

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, ZoneInstance.Entity Npc) Setup(int npcHp = 500)
    {
        var shaman = new NpcTemplate(1, "a_goblin_shaman", 50, 0, 12, 6f) { Combat = new NpcCombatStats(10, npcHp, 1, 3) };
        var spells = new InMemoryNpcSpellSource();
        spells.Rows.Add(new NpcSpellSet(10, 0, -1, -1, MinorHealing, -1, [BurstOfFlame, -1, -1, -1]));
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(8, 0, 0), 0, 0, [(shaman, 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data, seed: 3) { Spells = SpellRulesTests.File(), NpcSpells = spells };
        var player = zone.AddPlayer("Qbot", 1, 0, 12, new Vec3(0, 0, 0), fighter: new Combatant(true, 12, 1, 5000, 1, 1, 1, 1, 0, 1, 30f));
        zone.DrainEvents();
        var npc = zone.Entities.Single(e => !e.IsPlayer);
        npc.TargetId = player.Id; // engaged
        return (zone, player, npc);
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
    public void An_engaged_caster_nukes_its_target_now_and_then()
    {
        var (zone, player, npc) = Setup();
        var events = Run(zone, 60f);
        Assert.Contains(new ZoneInstance.CastStarted(npc.Id, BurstOfFlame, "Burst of Flame", 1500), events);
        Assert.Contains(events, e => e is ZoneInstance.SpellLanded l && l.CasterId == npc.Id && l.TargetId == player.Id && l.Amount < 0);
    }

    [Fact]
    public void A_losing_caster_heals_itself()
    {
        var (zone, player, npc) = Setup();
        npc.Hp = 20; // 4%: far below its flee ratio, the player unhurt
        var events = Run(zone, 5f);
        Assert.Contains(events, e => e is ZoneInstance.SpellLanded l && l.CasterId == npc.Id && l.TargetId == npc.Id && l.SpellId == MinorHealing);
        Assert.True(npc.Hp > 20);
    }

    [Fact]
    public void Classes_without_spells_never_cast()
    {
        var (zone, _, npc) = Setup();
        Assert.NotNull(zone.NpcSpells!.For(10, 12));
        Assert.Null(zone.NpcSpells.For(1, 12)); // warriors
    }
}
