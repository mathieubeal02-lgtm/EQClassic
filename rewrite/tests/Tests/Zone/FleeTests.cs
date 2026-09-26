using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>NPCs fleeing at low health (NPC::Damage's emergency check, CheckMyFleeStatus).</summary>
public class FleeTests
{
    private static (ZoneInstance Zone, ZoneInstance.Entity Player, ZoneInstance.Entity Orc) Setup(bool undead = false, bool ally = false)
    {
        var orc = new NpcTemplate(1, "an_orc", 54, 0, 10, 6f, Undead: undead, PrimaryFaction: 5) { Combat = new NpcCombatStats(1, 100, 1, 2) };
        var spawns = new List<SpawnPoint> { new(1, new Vec3(8, 0, 0), 0, 0, [(orc, 100)], 600, 0) };
        if (ally)
            spawns.Add(new SpawnPoint(2, new Vec3(30, 0, 0), 0, 0, [(orc with { Id = 2, Name = "an_orc_pawn" }, 100)], 600, 0));
        var zone = new ZoneInstance(new ZoneData("qeynos2", spawns, new Dictionary<int, Grid>()), seed: 6);
        var player = zone.AddPlayer("Qbot", 1, 0, 10, new Vec3(0, 0, 0), fighter: new Combatant(true, 10, 1, 500, 1, 1, 1, 1, 0, 1, 60f));
        var npc = zone.Entities.First(e => e.Name == "an_orc");
        npc.TargetId = player.Id;
        npc.Hp = 8; // 8%, the player unhurt
        zone.DrainEvents();
        return (zone, player, npc);
    }

    private static void Run(ZoneInstance zone, float seconds)
    {
        for (float t = 0; t < seconds; t += 0.05f)
            zone.Tick(0.05f);
    }

    [Fact]
    public void A_losing_npc_runs_away_until_its_health_comes_back()
    {
        var (zone, player, orc) = Setup();
        Run(zone, 2f);
        Assert.True(orc.Fleeing);
        Assert.True(Math.Abs(orc.Position.X - 8) + Math.Abs(orc.Position.Y) > 20, "it did not run");

        orc.Hp = 30;
        Run(zone, 0.1f);
        Assert.False(orc.Fleeing);
    }

    [Fact]
    public void Undead_and_npcs_with_allies_nearby_stand_their_ground()
    {
        var (undead, _, skeleton) = Setup(undead: true);
        Run(undead, 2f);
        Assert.False(skeleton.Fleeing);

        var (friends, _, orc) = Setup(ally: true);
        Run(friends, 2f);
        Assert.False(orc.Fleeing);
    }
}
