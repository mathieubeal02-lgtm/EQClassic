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
