using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Tests.Zone;

public class ConsiderAndSitTests
{
    private sealed class Hostile : IFactionStandings
    {
        public FactionStanding Standing(ZoneInstance.Entity player, NpcTemplate npc) => FactionStanding.Scowls;
    }

    private static readonly NpcTemplate Orc = new(1, "an_orc_pawn", 54, 0, 3, 6f);
    private static readonly NpcTemplate Merchant = new(2, "Tubal_Weaver", 1, 0, 30, 6f) { Combat = new NpcCombatStats(41, 500, 1, 10) };

    private static (ZoneInstance, ZoneInstance.Entity) Zone(NpcTemplate npc)
    {
        var zone = new ZoneInstance(new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(5, 0, 0), 0, 0, [(npc, 100)])], new Dictionary<int, Grid>()))
        {
            Factions = new Hostile(),
        };
        var player = zone.AddPlayer("Qbot", 9, 0, 3, new Vec3(0, 0, 0));
        zone.DrainEvents();
        return (zone, player);
    }

    [Fact]
    public void Consider_gives_standing_and_level_colour()
    {
        var (zone, player) = Zone(Orc);
        zone.Consider(player.Id, zone.Entities.Single(e => !e.IsPlayer).Id);
        var c = Assert.IsType<ZoneInstance.Considered>(Assert.Single(zone.DrainEvents()));
        Assert.Equal((Standing.Scowls, ConColor.White), (c.Standing, c.Con));
    }

    [Fact]
    public void Merchants_and_bankers_are_never_worse_than_dubious()
    {
        var (zone, player) = Zone(Merchant);
        zone.Consider(player.Id, zone.Entities.Single(e => !e.IsPlayer).Id);
        var c = Assert.IsType<ZoneInstance.Considered>(Assert.Single(zone.DrainEvents()));
        Assert.Equal((Standing.Dubious, ConColor.Red), (c.Standing, c.Con));
    }

    [Fact]
    public void Sitting_is_announced_and_moving_stands_up()
    {
        var (zone, player) = Zone(Orc);
        zone.SetSitting(player.Id, true);
        Assert.True(player.Sitting);
        Assert.Equal([new ZoneInstance.AppearanceChanged(player.Id, true)], zone.DrainEvents());
        zone.Tick(0.5f);
        zone.MovePlayer(player.Id, new Vec3(3, 0, 0), 90);
        Assert.False(player.Sitting);
        Assert.Contains(new ZoneInstance.AppearanceChanged(player.Id, false), zone.DrainEvents());
    }

    [Fact]
    public void Consider_sentences_follow_the_trilogy_client()
    {
        Assert.Equal("a rat regards you indifferently -- looks like an even fight.",
            ConsiderRules.Message("a rat", Standing.Indifferent, ConColor.White));
        Assert.Equal("Guard Liben scowls at you, ready to attack -- what would you like your tombstone to say?",
            ConsiderRules.Message("Guard Liben", Standing.Scowls, ConColor.Red));
        Assert.Equal(ConColor.Green, ConsiderRules.LevelCon(20, 10));
        Assert.Equal(ConColor.Yellow, ConsiderRules.LevelCon(20, 22));
    }
}
