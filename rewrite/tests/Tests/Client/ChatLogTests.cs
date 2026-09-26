using EQClassic.ClientCore;
using EQClassic.Shared.Zone;

namespace EQClassic.Tests.Client;

public class ChatLogTests
{
    [Fact]
    public void Scrolling_back_keeps_the_view_while_lines_arrive()
    {
        var log = new ChatLog();
        for (int i = 0; i < 30; i++)
            log.Add($"line {i}");
        Assert.Equal(["line 28", "line 29"], log.Visible(2));
        log.ScrollBy(5, 2);
        Assert.Equal(["line 23", "line 24"], log.Visible(2));
        log.Add("line 30");
        Assert.Equal(["line 23", "line 24"], log.Visible(2));
        log.ScrollBy(100, 2);
        Assert.Equal(["line 0", "line 1"], log.Visible(2));
        log.ScrollToBottom();
        Assert.Equal(["line 29", "line 30"], log.Visible(2));
    }

    [Fact]
    public void Only_the_last_lines_are_kept()
    {
        var log = new ChatLog();
        for (int i = 0; i < ChatLog.Capacity + 10; i++)
            log.Add($"line {i}");
        Assert.Equal(ChatLog.Capacity, log.Count);
        log.ScrollBy(ChatLog.Capacity, 1);
        Assert.Equal(["line 10"], log.Visible(1));
    }

    [Fact]
    public void Arrows_recall_what_was_typed()
    {
        var log = new ChatLog();
        log.Typed("/hail");
        log.Typed("hello");
        log.Typed("hello");
        Assert.Equal("hello", log.Previous());
        Assert.Equal("/hail", log.Previous());
        Assert.Equal("/hail", log.Previous());
        Assert.Equal("hello", log.Next());
        Assert.Equal("", log.Next());
    }

    [Theory]
    [InlineData(ChatChannel.Say, ChatKind.Say)]
    [InlineData(ChatChannel.Tell, ChatKind.Tell)]
    [InlineData(ChatChannel.Group, ChatKind.Group)]
    [InlineData(ChatChannel.Shout, ChatKind.Shout)]
    [InlineData(ChatChannel.Ooc, ChatKind.OutOfCharacter)]
    [InlineData(ChatChannel.Auction, ChatKind.Auction)]
    public void Lines_are_coloured_by_channel(ChatChannel channel, ChatKind kind)
    {
        Assert.Equal(kind, ChatLog.Kind(Chat.Format(new ChatMessage(channel, "Bob", "Ann", "hi"), "Ann")));
        Assert.Equal(kind, ChatLog.Kind(Chat.Format(new ChatMessage(channel, "Ann", "Bob", "hi"), "Ann")));
        Assert.Equal(ChatKind.SkillUp, ChatLog.Kind("You have become better at Offense! (5)"));
        Assert.Equal(ChatKind.Experience, ChatLog.Kind("You gain experience!!"));
        Assert.Equal(ChatKind.Experience, ChatLog.Kind("You have gained a level! Welcome to level 2!"));
        Assert.Equal(ChatKind.Hurt, ChatLog.Kind("A rat hits YOU for 2 points of damage."));
        Assert.Equal(ChatKind.Hurt, ChatLog.Kind("Auto attack is on."));
        Assert.Equal(ChatKind.Other, ChatLog.Kind("You hit a rat for 3 points of damage."));
    }
}

public class KeyBindingsTests
{
    [Fact]
    public void Azerty_by_default_and_qwerty_on_request()
    {
        var client = new GameClient();
        Assert.Equal(KeyboardLayout.Azerty, client.Keys.Layout);
        Assert.Equal(('z', 'q', 'd', 'a'), (client.Keys.Forward, client.Keys.StrafeLeft, client.Keys.StrafeRight, client.Keys.TurnLeft));
        var parsed = Chat.Parse("/keys qwerty");
        Assert.Equal((ChatAction.Keys, "qwerty"), (parsed.Action, parsed.Target));
        var qwerty = KeyBindings.Parse(parsed.Target)!;
        Assert.Equal(('w', 'a', 'd', 'q'), (qwerty.Forward, qwerty.StrafeLeft, qwerty.StrafeRight, qwerty.TurnLeft));
        Assert.Same(KeyBindings.Azerty, KeyBindings.Parse(" AZERTY "));
        Assert.Null(KeyBindings.Parse("dvorak"));
    }
}
