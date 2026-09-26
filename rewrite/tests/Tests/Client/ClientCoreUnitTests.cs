using EQClassic.ClientCore;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Tests.Client;

public class ClientCoreUnitTests
{
    private static ZoneView ViewWith(params EntitySpawn[] entities) =>
        new(new ZoneEnterResponse(true, "", "grobb", 1, entities), now: 0);

    private static readonly EntitySpawn Guard = new(2, "a_troll_guard", false, 9, 0, 20, 8f, 0, 0, 0, 0);

    [Fact]
    public void An_illusion_redraws_the_entity_where_it_stands()
    {
        var view = ViewWith(Guard);
        view.Apply(new EntityPositions(1, [new EntityPosition(2, 5, 10, 0, 90)]), now: 1.0);
        var events = new List<string>();
        view.Removed += id => events.Add($"removed {id}");
        view.Added += e => events.Add($"added {e.Id} race {e.Spawn.Race} at {e.Latest.X},{e.Latest.Y}");
        view.Apply(new EntityIllusion(2, 142, 0), now: 1.1);
        Assert.Equal(["removed 2", "added 2 race 142 at 5,10"], events);
        view.Apply(new EntityIllusion(2, 142, 0), now: 1.2); // no change, nothing to redraw
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void Positions_are_interpolated_between_updates()
    {
        var view = ViewWith(Guard);
        view.Apply(new EntityPositions(1, [new EntityPosition(2, 0, 10, 0, 0)]), now: 1.0);
        view.Apply(new EntityPositions(2, [new EntityPosition(2, 0, 20, 0, 0)]), now: 1.05);

        var (p, _) = view.Interpolated(2, now: 1.05 + ZoneView.InterpolationDelay - 0.025);
        Assert.Equal(15f, p.Y, precision: 3);
    }

    [Fact]
    public void Late_updates_are_ignored()
    {
        var view = ViewWith(Guard);
        view.Apply(new EntityPositions(5, [new EntityPosition(2, 0, 50, 0, 0)]), now: 1);
        view.Apply(new EntityPositions(4, [new EntityPosition(2, 0, 40, 0, 0)]), now: 1.1);
        Assert.Equal(50f, view.Get(2)!.Latest.Y);
    }

    [Fact]
    public void Spawns_and_removals_are_reported()
    {
        var view = ViewWith();
        var added = new List<int>();
        var removed = new List<int>();
        view.Added += e => added.Add(e.Id);
        view.Removed += removed.Add;
        view.Apply(new EntitySpawned(Guard), 0);
        view.Apply(new EntityRemoved(2));
        Assert.Equal([2], added);
        Assert.Equal([2], removed);
        Assert.Equal(0, view.Count);
    }

    [Fact]
    public void Local_movement_follows_the_heading_and_is_sent_at_10_hz()
    {
        var player = new LocalPlayer(1, new Vec3(0, 0, 0), heading: 90); // facing +X (east)
        player.Move(forward: 1, strafe: 0, turn: 0, seconds: 1);
        Assert.Equal(LocalPlayer.RunSpeed, player.Position.X, precision: 3);
        Assert.Equal(0f, player.Position.Y, precision: 3);

        Assert.NotNull(player.Due(0.01f));
        player.Move(1, 0, 0, 0.01f);
        Assert.Null(player.Due(0.01f)); // too soon
        Assert.NotNull(player.Due(0.1f));
    }

    // Lantern meshes are (x, y up, z) = EverQuest (y, z, x). Quads: EverQuest x0..x1, y 0..100, height z.
    private static string[] Floor(float x0, float x1, float z, int first) =>
    [
        $"v,0,{z},{x0}", $"v,100,{z},{x0}", $"v,100,{z},{x1}", $"v,0,{z},{x1}",
        $"i,0,{first},{first + 1},{first + 2}", $"i,0,{first},{first + 2},{first + 3}",
    ];

    [Fact]
    public void Movement_on_the_mesh_climbs_steps()
    {
        var mesh = ZoneCollisionMesh.ParseLantern([.. Floor(0, 50, 0, 0), .. Floor(50, 200, 4, 4)]);
        var player = new LocalPlayer(1, new Vec3(40, 50, 0), heading: 90);
        Assert.True(player.Move(1, 0, 0, 1, mesh));
        Assert.Equal(40 + LocalPlayer.RunSpeed, player.Position.X, precision: 3);
        Assert.Equal(4f, player.Position.Z);
    }

    [Fact]
    public void Walking_off_a_ledge_falls_with_gravity_and_lands()
    {
        // Upper floor at 20 for x 0..50, lower floor at 0 from 50 on.
        var mesh = ZoneCollisionMesh.ParseLantern([.. Floor(0, 50, 20, 0), .. Floor(50, 200, 0, 4)]);
        var player = new LocalPlayer(1, new Vec3(45, 50, 20), heading: 90);
        player.Move(1, 0, 0, 0.2f, mesh); // 9 units east: over the drop
        Assert.True(player.Airborne);
        Assert.InRange(player.Position.Z, 15f, 20f);   // not snapped down
        for (int i = 0; i < 40 && player.Airborne; i++)
            player.Move(0, 0, 0, 0.05f, mesh);
        Assert.False(player.Airborne);
        Assert.Equal(0f, player.Position.Z);
    }

    [Fact]
    public void Flying_the_player_stays_up_climbs_and_dives_to_the_ground()
    {
        var mesh = ZoneCollisionMesh.ParseLantern(Floor(0, 200, 0, 0));
        var player = new LocalPlayer(1, new Vec3(40, 50, 0), heading: 90) { Flying = true };
        player.SwimInput = 1;
        player.Move(0, 0, 0, 1f, mesh);
        Assert.Equal(LocalPlayer.FlyVerticalSpeed, player.Position.Z);
        player.SwimInput = 0;
        player.Move(1, 0, 0, 1f, mesh);
        Assert.Equal(LocalPlayer.FlyVerticalSpeed, player.Position.Z); // no gravity
        Assert.False(player.Airborne);
        player.Jump();
        Assert.Equal(0f, player.VerticalSpeed);
        player.SwimInput = -1;
        for (int i = 0; i < 40; i++)
            player.Move(0, 0, 0, 0.1f, mesh);
        Assert.Equal(0f, player.Position.Z);
    }

    [Fact]
    public void In_water_the_player_floats_swims_up_to_the_surface_and_down_to_the_floor()
    {
        var mesh = ZoneCollisionMesh.ParseLantern(Floor(0, 200, -30, 0));
        // Water below z = 0 everywhere (the plane on EverQuest z: Lantern's second coefficient).
        var regions = ZoneRegions.Parse(["0,1,0,0,1,2", "1,Normal", "2,Water"]);
        var player = new LocalPlayer(1, new Vec3(40, 50, -20), heading: 90) { Regions = regions };
        player.Move(0, 0, 0, 0.5f, mesh);
        Assert.True(player.Swimming);
        Assert.Equal(-20f, player.Position.Z); // no gravity

        player.SwimInput = 1;
        for (int i = 0; i < 40; i++)
            player.Move(0, 0, 0, 0.05f, mesh);
        Assert.InRange(player.Position.Z, -LocalPlayer.ChestHeight - 1.5f, -LocalPlayer.ChestHeight); // the chest stays under the surface

        player.SwimInput = -1;
        for (int i = 0; i < 60; i++)
            player.Move(0, 0, 0, 0.05f, mesh);
        Assert.Equal(-30f, player.Position.Z);

        player.SwimInput = 0;
        player.Move(1, 0, 0, 1f, mesh);
        Assert.Equal(40 + LocalPlayer.RunSpeed * LocalPlayer.SwimSpeedFactor, player.Position.X, precision: 3);
    }

    [Fact]
    public void A_jump_goes_up_about_six_units_and_comes_back_down()
    {
        var mesh = ZoneCollisionMesh.ParseLantern(Floor(0, 200, 0, 0));
        var player = new LocalPlayer(1, new Vec3(40, 50, 0), heading: 90);
        player.Jump();
        float top = 0;
        for (int i = 0; i < 40 && player.Airborne; i++)
        {
            player.Move(0, 0, 0, 0.02f, mesh);
            top = Math.Max(top, player.Position.Z);
        }
        Assert.InRange(top, 5f, 6.5f); // 32² / (2 × 90) = 5.7
        Assert.False(player.Airborne);
        Assert.Equal(0f, player.Position.Z);
        player.Jump();
        player.Jump(); // no double jump
        Assert.Equal(LocalPlayer.JumpSpeed, player.VerticalSpeed);
    }

    [Fact]
    public void Walls_and_the_edge_of_the_zone_stop_the_player()
    {
        string[] wallAt60 = ["v,0,0,60", "v,100,0,60", "v,100,50,60", "v,0,50,60", "i,0,4,5,6", "i,0,4,6,7"];
        var walled = ZoneCollisionMesh.ParseLantern([.. Floor(0, 200, 0, 0), .. wallAt60]);
        var player = new LocalPlayer(1, new Vec3(40, 50, 0), heading: 90);
        Assert.False(player.Move(1, 0, 0, 1, walled));
        Assert.Equal(new Vec3(40, 50, 0), player.Position);

        var edge = ZoneCollisionMesh.ParseLantern(Floor(0, 50, 0, 0));
        Assert.False(player.Move(1, 0, 0, 1, edge));
        Assert.Equal(new Vec3(40, 50, 0), player.Position);
        Assert.True(player.Move(-0.5f, 0, 0, 1, edge)); // back into the zone
    }

    [Fact]
    public void Sidestepping_right_goes_to_the_screen_right()
    {
        var player = new LocalPlayer(1, new Vec3(0, 0, 0), heading: 0); // facing +Y
        player.Move(forward: 0, strafe: 1, turn: 0, seconds: 1);
        Assert.True(player.Position.X < 0, $"{player.Position}"); // +X is west, on the left of a player facing north
    }

    [Fact]
    public void The_body_keeps_its_radius_from_walls()
    {
        string[] wallAt60 = ["v,0,0,60", "v,100,0,60", "v,100,50,60", "v,0,50,60", "i,0,4,5,6", "i,0,4,6,7"];
        var walled = ZoneCollisionMesh.ParseLantern([.. Floor(0, 200, 0, 0), .. wallAt60]);
        var player = new LocalPlayer(1, new Vec3(56, 50, 0), heading: 90);
        for (int i = 0; i < 50; i++)
            player.Move(1, 0, 0, 0.01f, walled); // small steps up to the wall
        Assert.InRange(player.Position.X, 57f, 60f - LocalPlayer.BodyRadius + 0.01f); // it walked, and stopped short
    }

    [Fact]
    public void Tab_targets_the_nearest_npc_then_cycles_by_distance()
    {
        var view = ViewWith(
            new EntitySpawn(1, "Qbot", true, 9, 0, 1, 6f, 0, 0, 0, 0),
            new EntitySpawn(2, "a_rat", false, 36, 0, 1, 2f, 10, 0, 0, 0),
            new EntitySpawn(3, "a_bat", false, 34, 0, 1, 2f, 20, 0, 0, 0),
            new EntitySpawn(4, "a_far_rat", false, 36, 0, 1, 2f, 500, 0, 0, 0),
            new EntitySpawn(5, "Qother", true, 9, 0, 1, 6f, 5, 0, 0, 0));
        var here = new Vec3(0, 0, 0);
        Assert.Equal(2, view.NextNpc(here, 100)!.Id);
        Assert.Equal(3, view.NextNpc(here, 100, current: 2)!.Id);
        Assert.Equal(2, view.NextNpc(here, 100, current: 3)!.Id); // wraps; players and far NPCs are skipped
        Assert.Null(view.NextNpc(new Vec3(1000, 1000, 0), 100));
    }

    [Fact]
    public void Facing_a_point_sets_the_heading()
    {
        var player = new LocalPlayer(1, new Vec3(0, 0, 0), heading: 0);
        player.Face(new Vec3(10, 0, 0));
        Assert.Equal(90f, player.Heading, precision: 3);   // east
        player.Face(new Vec3(0, -10, 0));
        Assert.Equal(180f, player.Heading, precision: 3);  // south
        player.Face(new Vec3(-10, 10, 0));
        Assert.Equal(315f, player.Heading, precision: 3);  // north-west
        Assert.NotNull(player.Due(1));                     // the turn is sent
    }

    [Fact]
    public void Mouse_turns_and_walking_slows_down()
    {
        var player = new LocalPlayer(1, new Vec3(0, 0, 0), heading: 10);
        player.Turn(20);                                   // to the right on screen: the heading goes down (mirrored world)
        Assert.Equal(350f, player.Heading, precision: 3);
        player.Turn(-30);
        Assert.Equal(20f, player.Heading, precision: 3);

        player.Turn(20); // face north
        player.Walking = true;
        player.Move(forward: 1, strafe: 0, turn: 0, seconds: 1);
        Assert.Equal(LocalPlayer.WalkSpeed, player.Position.Y, precision: 3);
    }

    [Fact]
    public void Models_scale_with_the_spawn_size_for_known_races()
    {
        Assert.Equal(1f, ModelCodes.Scale(9, 8f));    // a troll of troll size
        Assert.Equal(1.5f, ModelCodes.Scale(1, 9f));  // a big human
        Assert.Equal(1f, ModelCodes.Scale(36, 2f));   // rats: default size unknown, left alone
        Assert.Equal(1f, ModelCodes.Scale(1, 0f));
    }

    [Fact]
    public void Combat_lines_read_like_the_trilogy_client()
    {
        string? Name(int id) => id switch { 1 => "Qbot", 2 => "a rat", 3 => "Guard Liben", _ => null };
        Assert.Equal("You hit a rat for 3 points of damage.", CombatText.Describe(new CombatEvent(1, 2, 3, 80), 1, Name));
        Assert.Equal("You try to hit a rat, but miss!", CombatText.Describe(new CombatEvent(1, 2, 0, 80), 1, Name));
        Assert.Equal("A rat hits YOU for 1 point of damage.", CombatText.Describe(new CombatEvent(2, 1, 1, 90), 1, Name));
        Assert.Equal("A rat tries to hit YOU, but misses!", CombatText.Describe(new CombatEvent(2, 1, 0, 90), 1, Name));
        Assert.Equal("Guard Liben hits a rat for 12 points of damage.", CombatText.Describe(new CombatEvent(3, 2, 12, 0), 1, Name));
        Assert.Equal("a rat", CombatText.DisplayName("a_rat01"));
        Assert.Equal("a rat's corpse", CombatText.DisplayName("a_rat01's_corpse"));
    }

    [Fact]
    public void Server_correction_wins()
    {
        var player = new LocalPlayer(1, new Vec3(0, 0, 0), 0);
        player.Move(1, 0, 0, 1);
        player.Apply(new MoveCorrection(5, 5, 0, "moved too fast"));
        Assert.Equal(new Vec3(5, 5, 0), player.Position);
        Assert.Null(player.Due(1));
    }

    [Fact]
    public void Coordinates_round_trip_between_everquest_and_unity()
    {
        var eq = new Vec3(167, -60, 3.75f);
        var (x, y, z) = Coordinates.ToUnity(eq);
        Assert.Equal((-60f, 3.75f, 167f), (x, y, z));
        Assert.Equal(eq, Coordinates.FromUnity(x, y, z));
        Assert.Equal(0f, Coordinates.HeadingToUnityYaw(Coordinates.UnityYawToHeading(0)));
    }

    [Theory]
    [InlineData(9, 0, "trm")]
    [InlineData(5, 1, "hif")]
    [InlineData(71, 1, "qcf")]
    [InlineData(40, 0, "gob")]
    [InlineData(999, 0, ModelCodes.Fallback)]
    public void Races_map_to_lantern_model_codes(int race, int gender, string code) =>
        Assert.Equal(code, ModelCodes.For(race, gender));
}
