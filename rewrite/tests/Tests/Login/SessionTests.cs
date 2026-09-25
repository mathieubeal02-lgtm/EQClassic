using EQClassic.Server.Login;
using EQClassic.Shared.Login;

namespace EQClassic.Tests.Login;

public class SessionTests
{
    [Fact]
    public void Session_id_round_trips()
    {
        Assert.Equal("LS#16", SessionIds.ForAccount(16));
        Assert.True(SessionIds.TryParse("LS#16", out var id));
        Assert.Equal(16, id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("LS#")]
    [InlineData("LS#0")]
    [InlineData("LS#-3")]
    [InlineData("ls#3")]
    [InlineData("LS#3x")]
    public void Malformed_session_ids_are_rejected(string sessionId) => Assert.False(SessionIds.TryParse(sessionId, out _));

    [Fact]
    public void Session_keys_have_the_legacy_shape_and_differ()
    {
        var keys = Enumerable.Range(0, 200).Select(_ => SessionKeys.New()).ToList();
        Assert.All(keys, k => Assert.True(SessionKeys.IsWellFormed(k), k));
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
