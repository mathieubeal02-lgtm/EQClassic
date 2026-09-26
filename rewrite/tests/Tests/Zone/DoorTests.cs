using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Doors after Client::ProcessOP_ClickDoor (Zone/Source/client_process.cpp).</summary>
public class DoorTests
{
    private static readonly Door Plain = new(1, "DOOR1", new Vec3(10, 0, 0), 128, 0, TriggerDoor: 2);
    private static readonly Door Linked = new(2, "DOOR1", new Vec3(14, 0, 0), 384, 0);
    private static readonly Door Keyed = new(3, "CELLDOOR", new Vec3(0, 10, 0), 0, 0, KeyItem: 12345);
    private static readonly Door Lift = new(4, "TPORT", new Vec3(0, -10, 0), 0, Door.InvisibleOpenType, DestZone: "qeynos2", Destination: new Vec3(500, 600, 20));
    private static readonly Door ToZone = new(5, "TPORT", new Vec3(-10, 0, 0), 0, Door.InvisibleOpenType, DestZone: "qeynos", Destination: new Vec3(1, 2, 3));
    private static readonly Door Far = new(6, "DOOR1", new Vec3(300, 0, 0), 0, 0);

    private static (ZoneInstance Zone, ZoneInstance.Entity Player) Zone()
    {
        var data = new ZoneData("qeynos2", [], new Dictionary<int, Grid>(), ZoneDoors: [Plain, Linked, Keyed, Lift, ToZone, Far]);
        var zone = new ZoneInstance(data);
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(0, 0, 0));
        zone.DrainEvents();
        return (zone, player);
    }

    private static void Run(ZoneInstance zone, float seconds)
    {
        for (float t = 0; t < seconds; t += 0.05f)
            zone.Tick(0.05f);
    }

    [Fact]
    public void A_click_opens_the_door_and_its_trigger_door_for_everyone()
    {
        var (zone, player) = Zone();
        zone.ClickDoor(player.Id, 1);
        Assert.True(zone.IsDoorOpen(1));
        Assert.True(zone.IsDoorOpen(2));
        Assert.Equal([new ZoneInstance.DoorChanged(1, true), new ZoneInstance.DoorChanged(2, true)], zone.DrainEvents());

        zone.ClickDoor(player.Id, 1);
        Assert.False(zone.IsDoorOpen(1));
        Assert.False(zone.IsDoorOpen(2));
    }

    [Fact]
    public void Doors_close_after_twelve_untouched_seconds()
    {
        var (zone, player) = Zone();
        zone.ClickDoor(player.Id, 1);
        zone.DrainEvents();
        Run(zone, 11.5f);
        Assert.True(zone.IsDoorOpen(1));
        Run(zone, 1f);
        Assert.False(zone.IsDoorOpen(1));
        Assert.Contains(new ZoneInstance.DoorChanged(1, false), zone.DrainEvents());

        // A click after the close opens it again (the legacy "twelve seconds" reset, then the toggle).
        zone.ClickDoor(player.Id, 1);
        Assert.True(zone.IsDoorOpen(1));
    }

    [Fact]
    public void Locked_doors_need_the_key_in_hand()
    {
        var (zone, player) = Zone();
        zone.ClickDoor(player.Id, 3);
        Assert.False(zone.IsDoorOpen(3));
        Assert.Equal([new ZoneInstance.Told(player.Id, ZoneInstance.NoKeyMessage)], zone.DrainEvents());
    }

    [Fact]
    public void Teleport_doors_move_the_player_in_the_zone_or_to_another_zone()
    {
        var (zone, player) = Zone();
        zone.ClickDoor(player.Id, 4);
        Assert.Equal(new Vec3(500, 600, 20), player.Position);
        Assert.Equal([new ZoneInstance.Teleported(player.Id, new Vec3(500, 600, 20))], zone.DrainEvents());

        var (zone2, player2) = Zone();
        zone2.ClickDoor(player2.Id, 5);
        var crossed = Assert.IsType<ZoneInstance.CrossedZoneLine>(Assert.Single(zone2.DrainEvents()));
        Assert.Equal(("qeynos", new Vec3(1, 2, 3)), (crossed.Line.TargetZone, crossed.Destination));
    }

    [Fact]
    public void Doors_out_of_reach_or_unknown_do_nothing()
    {
        var (zone, player) = Zone();
        zone.ClickDoor(player.Id, 6);
        Assert.False(zone.IsDoorOpen(6));
        Assert.IsType<ZoneInstance.Told>(Assert.Single(zone.DrainEvents()));
        zone.ClickDoor(player.Id, 99);
        Assert.Empty(zone.DrainEvents());
    }
}
