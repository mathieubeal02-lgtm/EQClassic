using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

public class WaypointWalkerTests
{
    // Street at z=0 over EQ x,y in [0,100]; a roof at z=40 over x in [40,60] (Lantern axes: x=eqY, y=up, z=eqX).
    private static readonly ZoneCollisionMesh StreetUnderARoof = ZoneCollisionMesh.ParseLantern(
    [
        "v,0,0,0", "v,100,0,0", "v,100,0,100", "v,0,0,100", "i,0,0,1,2", "i,0,0,2,3",
        "v,0,40,40", "v,100,40,40", "v,100,40,60", "v,0,40,60", "i,0,4,5,6", "i,0,4,6,7",
    ]);

    private static readonly Vec3[] AlongTheStreet = [new(10, 50, 3.75f), new(90, 50, 3.75f)];

    [Fact]
    public void Walks_under_the_roof_on_the_street()
    {
        var walker = new WaypointWalker(AlongTheStreet, StreetUnderARoof);
        var heights = new List<float>();
        while (!walker.Step(2f))
            heights.Add(walker.Position.Z);

        Assert.All(heights, z => Assert.Equal(0f, z));
        Assert.Equal(new Vec3(90, 50, 0), walker.Position);
    }

    [Fact]
    public void Back_and_forth_grids_turn_around_at_the_ends()
    {
        var walker = new WaypointWalker([new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)], mesh: null);
        var order = new List<int>();
        for (int i = 0; i < 6; i++)
        {
            order.Add(walker.TargetIndex);
            while (!walker.Step(100f)) { }
        }
        Assert.Equal([1, 2, 1, 0, 1, 2], order);
    }

    [Fact]
    public void Circular_grids_loop()
    {
        var walker = new WaypointWalker([new(0, 0, 0), new(10, 0, 0), new(20, 0, 0)], mesh: null, GridType.Circular);
        var order = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            order.Add(walker.TargetIndex);
            while (!walker.Step(100f)) { }
        }
        Assert.Equal([1, 2, 0, 1], order);
    }

    [Fact]
    public void Without_a_mesh_the_leg_height_is_interpolated()
    {
        var walker = new WaypointWalker([new(0, 0, 0), new(100, 0, 50)], mesh: null);
        walker.Step(50f);
        Assert.Equal(25f, walker.Position.Z, precision: 3);
    }

    [Fact]
    public void A_ledge_across_the_path_is_climbed_not_popped()
    {
        // Floor at z=0 for EQ x < 50, a ledge at z=12 for x >= 50; the grid walks straight across.
        var mesh = ZoneCollisionMesh.ParseLantern(
        [
            "v,0,0,0", "v,100,0,0", "v,100,0,50", "v,0,0,50", "i,0,0,1,2", "i,0,0,2,3",
            "v,0,12,50", "v,100,12,50", "v,100,12,100", "v,0,12,100", "i,0,4,5,6", "i,0,4,6,7",
        ]);
        var walker = new WaypointWalker([new(10, 50, 3.75f), new(90, 50, 15.75f)], mesh);
        float last = walker.Position.Z, biggest = 0;
        while (!walker.Step(0.32f)) // one 20 Hz tick at the legacy walk speed
        {
            biggest = MathF.Max(biggest, walker.Position.Z - last);
            last = walker.Position.Z;
        }
        Assert.True(biggest <= 0.33f, $"rose {biggest} in one step");
        Assert.Equal(12f, walker.Position.Z, precision: 1);
    }

    [Fact]
    public void A_grid_needs_two_waypoints()
    {
        Assert.Throws<ArgumentException>(() => new WaypointWalker([new(0, 0, 0)], mesh: null));
    }
}
