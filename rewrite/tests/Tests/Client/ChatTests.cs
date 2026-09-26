using EQClassic.ClientCore;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Tests.Client;

public class ChatTests
{
    [Theory]
    [InlineData("hello there", ChatAction.Send, ChatChannel.Say, "", "hello there")]
    [InlineData("/shout train to zone!", ChatAction.Send, ChatChannel.Shout, "", "train to zone!")]
    [InlineData("/ooc lfg", ChatAction.Send, ChatChannel.Ooc, "", "lfg")]
    [InlineData("/auc WTS rusty sword", ChatAction.Send, ChatChannel.Auction, "", "WTS rusty sword")]
    [InlineData("/t Qbot  hi there ", ChatAction.Send, ChatChannel.Tell, "Qbot", "hi there")]
    [InlineData("/em waves.", ChatAction.Send, ChatChannel.Emote, "", "waves.")]
    [InlineData("/who", ChatAction.Who, ChatChannel.Say, "", "")]
    [InlineData("/LOC", ChatAction.Location, ChatChannel.Say, "", "")]
    [InlineData("/target guard", ChatAction.Target, ChatChannel.Say, "guard", "")]
    [InlineData("/say", ChatAction.None, ChatChannel.Say, "", "")]
    public void Lines_parse_like_the_trilogy_chat_box(string line, ChatAction action, ChatChannel channel, string target, string text)
    {
        var p = Chat.Parse(line);
        Assert.Equal((action, channel, target, text), (p.Action, p.Channel, p.Target, p.Text));
    }

    [Fact]
    public void Unknown_commands_and_bad_tells_explain_themselves()
    {
        Assert.Equal(ChatAction.Unknown, Chat.Parse("/dance").Action);
        Assert.Equal("Usage: /tell <name> <message>", Chat.Parse("/tell Qbot").Text);
    }

    [Fact]
    public void Messages_read_like_the_trilogy_client()
    {
        Assert.Equal("Qbot says, 'hail'", Chat.Format(new ChatMessage(ChatChannel.Say, "Qbot", "", "hail"), "Qtest"));
        Assert.Equal("You say, 'hail'", Chat.Format(new ChatMessage(ChatChannel.Say, "Qtest", "", "hail"), "Qtest"));
        Assert.Equal("Qbot shouts, 'run'", Chat.Format(new ChatMessage(ChatChannel.Shout, "Qbot", "", "run"), "Qtest"));
        Assert.Equal("Qbot says out of character, 'lfg'", Chat.Format(new ChatMessage(ChatChannel.Ooc, "Qbot", "", "lfg"), "Qtest"));
        Assert.Equal("Qbot tells you, 'hi'", Chat.Format(new ChatMessage(ChatChannel.Tell, "Qbot", "Qtest", "hi"), "Qtest"));
        Assert.Equal("You told Qbot, 'hi'", Chat.Format(new ChatMessage(ChatChannel.Tell, "Qtest", "Qbot", "hi"), "Qtest"));
        Assert.Equal("Qbot waves.", Chat.Format(new ChatMessage(ChatChannel.Emote, "Qbot", "", "waves."), "Qtest"));
        Assert.Equal("Your Location is -60.00, 40.00, 3.13", Chat.Location(new Vec3(40, -60, 3.13f)));
    }
}

public class CharacterBuilderTests
{
    private static readonly IReadOnlyList<EQClassic.Shared.Characters.CreationOption> Options =
    [
        new(1, 1, 140, 1, "qeynos"), new(1, 1, 140, 2, "freportn"), new(1, 2, 212, 1, "qeynos"),
        new(9, 10, 203, 5, "grobb"), new(9, 10, 206, 5, "grobb"),
    ];

    [Fact]
    public void Choices_narrow_down_as_the_trilogy_screen_did()
    {
        var b = new CharacterBuilder(Options);
        Assert.Equal([1, 9], b.Races);
        Assert.Equal((1, 1, 140, "qeynos"), (b.Race, b.Class, b.Deity, b.Zone));
        Assert.Equal(["qeynos", "freportn"], b.Zones);
        b.SelectClass(2);
        Assert.Equal((212, "qeynos"), (b.Deity, b.Zone));
        b.SelectRace(9);
        Assert.Equal((10, 203, "grobb"), (b.Class, b.Deity, b.Zone));
        Assert.Equal([203, 206], b.Deities);
    }

    [Fact]
    public void Points_are_spent_one_at_a_time_and_the_request_passes_the_server_rules()
    {
        var b = new CharacterBuilder(Options);
        b.SelectRace(9);
        Assert.Equal((30, 114), (b.PointsLeft, b.Stat(1))); // troll shaman: STA 109 + 5
        Assert.False(b.Complete);
        for (int i = 0; i < 5; i++) b.AddPoint(1);
        for (int i = 0; i < 30; i++) b.AddPoint(6); // only 25 left
        Assert.Equal((0, 119, 95), (b.PointsLeft, b.Stat(1), b.Stat(6)));
        b.RemovePoint(6);
        Assert.Equal(1, b.PointsLeft);
        b.AddPoint(6);
        Assert.True(b.Complete);
        var request = b.Request("Qnew");
        Assert.Null(EQClassic.Shared.Characters.CreationRules.CheckStats(request.Race, request.Class, request.Stats));
        Assert.Equal(new EQClassic.Shared.Characters.CharacterStats(108, 119, 45, 75, 52, 83, 95), request.Stats);
    }
}
