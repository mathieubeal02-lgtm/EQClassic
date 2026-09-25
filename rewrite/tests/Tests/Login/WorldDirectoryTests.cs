using EQClassic.Server.Login;
using EQClassic.Shared.Login;

namespace EQClassic.Tests.Login;

public class WorldDirectoryTests
{
    private static WorldDirectory With(WorldStatus status, Func<DateTime>? now = null)
    {
        var d = new WorldDirectory(TimeSpan.FromMinutes(1), now);
        d.Register(new WorldServerInfo(1, "EverQuest Classic", "10.0.0.5", 9000, 3, status));
        return d;
    }

    [Theory]
    [InlineData(WorldStatus.Down, "World is down.")]
    [InlineData(WorldStatus.Locked, "World is locked.")]
    public void Unavailable_worlds_are_refused_with_the_legacy_messages(WorldStatus status, string message)
    {
        var response = With(status).RequestPlay(1, 1);
        Assert.False(response.Accepted);
        Assert.Equal(message, response.Message);
    }

    [Fact]
    public void Unknown_world_is_refused()
    {
        Assert.Equal("Worldserver not found.", With(WorldStatus.Up).RequestPlay(1, 42).Message);
    }

    [Fact]
    public void Key_is_issued_for_an_up_world_and_redeemed_once()
    {
        var directory = With(WorldStatus.Up);
        var response = directory.RequestPlay(16, 1);

        Assert.True(response.Accepted);
        Assert.Equal(("10.0.0.5", 9000), (response.Address, response.Port));
        Assert.True(SessionKeys.IsWellFormed(response.SessionKey));
        Assert.False(directory.TryRedeem(16, 1, "wrong-key-00000"));
        Assert.False(directory.TryRedeem(17, 1, response.SessionKey));
        Assert.True(directory.TryRedeem(16, 1, response.SessionKey));
        Assert.False(directory.TryRedeem(16, 1, response.SessionKey));
    }

    [Fact]
    public void Keys_expire()
    {
        var now = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);
        var directory = With(WorldStatus.Up, () => now);
        var key = directory.RequestPlay(16, 1).SessionKey;
        now = now.AddMinutes(2);
        Assert.False(directory.TryRedeem(16, 1, key));
    }
}
