using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

public class ZoneInstanceTests
{
    private static readonly NpcTemplate Guard = new(2007, "Guard_Hewet", 71, 0, 10, 6f);
    private static readonly NpcTemplate Rat = new(1, "a_rat", 36, 2, 1, 2f);

    // Street at z=0, roof at z=40 over x in [40,60] (Lantern axes: x=eqY, y=up, z=eqX).
    private static readonly ZoneCollisionMesh StreetUnderARoof = ZoneCollisionMesh.ParseLantern(
    [
        "v,0,0,0", "v,100,0,0", "v,100,0,100", "v,0,0,100", "i,0,0,1,2", "i,0,0,2,3",
        "v,0,40,40", "v,100,40,40", "v,100,40,60", "v,0,40,60", "i,0,4,5,6", "i,0,4,6,7",
    ]);

    private static ZoneData Patrol(int pause = 0) => new("qeynos2",
        [new SpawnPoint(1, new Vec3(10, 50, 3.75f), 0, GridId: 7, [(Guard, 100)])],
        new Dictionary<int, Grid> { [7] = new Grid(7, GridType.BackAndForth, [new Waypoint(new Vec3(10, 50, 3.75f), pause), new Waypoint(new Vec3(90, 50, 3.75f), pause)]) });

    [Fact]
    public void Guards_patrol_under_a_roof_without_leaving_the_street()
    {
        var zone = new ZoneInstance(Patrol(), StreetUnderARoof);
        var guard = zone.Entities.Single();
        float maxZ = float.MinValue, minX = float.MaxValue, maxX = float.MinValue;
        for (int i = 0; i < 20 * 60; i++) // one minute at 20 Hz
        {
            zone.Tick(0.05f);
            maxZ = MathF.Max(maxZ, guard.Position.Z);
            minX = MathF.Min(minX, guard.Position.X);
            maxX = MathF.Max(maxX, guard.Position.X);
        }
        Assert.Equal(0f, maxZ);
        Assert.True(minX < 15 && maxX > 85, $"walked {minX}..{maxX}");
    }

    [Fact]
    public void Npcs_walk_at_the_legacy_speed()
    {
        var zone = new ZoneInstance(Patrol());
        var guard = zone.Entities.Single();
        float startX = guard.Position.X;
        zone.Tick(1f);
        Assert.Equal(0.7f * 4 * 2.3f, guard.Position.X - startX, precision: 3);
    }

    [Fact]
    public void Waypoint_pause_holds_the_npc()
    {
        var zone = new ZoneInstance(Patrol(pause: 10));
        var guard = zone.Entities.Single();
        for (int i = 0; i < 20 * 20; i++) zone.Tick(0.05f); // ~6.4 u/s: 80 units take 12.5 s
        var reached = guard.Position;
        zone.Tick(1f);
        Assert.Equal(reached, guard.Position); // still pausing at the far end
    }

    [Fact]
    public void Spawn_groups_pick_by_chance_and_zero_chance_groups_take_the_first()
    {
        var data = new ZoneData("test",
            [new SpawnPoint(1, new Vec3(0, 0, 0), 0, 0, [(Guard, 0), (Rat, 0)]),
             .. Enumerable.Range(2, 200).Select(i => new SpawnPoint(i, new Vec3(i, 0, 0), 0, 0, [(Guard, 75), (Rat, 25)]))],
            new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data, seed: 42);
        Assert.Equal("Guard_Hewet", zone.Entities.First(e => e.Position.X == 0).Name);
        int rats = zone.Entities.Count(e => e.Name == "a_rat");
        Assert.InRange(rats, 30, 70); // ~25% of 200
    }

    [Fact]
    public void Players_move_at_run_speed_but_cannot_teleport()
    {
        var zone = new ZoneInstance(Patrol());
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(0, 0, 0));
        zone.Tick(1f);

        Assert.Null(zone.MovePlayer(player.Id, new Vec3(40, 0, 0), 90));
        zone.Tick(0.05f);
        var refused = zone.MovePlayer(player.Id, new Vec3(1000, 0, 0), 90);

        Assert.NotNull(refused);
        Assert.Equal(new Vec3(40, 0, 0), zone.Get(player.Id)!.Position);
    }

    [Fact]
    public void Tick_reports_what_moved()
    {
        var zone = new ZoneInstance(Patrol());
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(0, 0, 0));
        Assert.Single(zone.Tick(0.05f)); // the guard
        zone.MovePlayer(player.Id, new Vec3(1, 0, 0), 0);
        Assert.Equal(2, zone.Tick(0.05f).Count);
    }
}
