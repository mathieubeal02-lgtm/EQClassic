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
