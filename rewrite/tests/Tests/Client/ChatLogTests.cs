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
        Assert.Equal(ChatKind.Other, ChatLog.Kind("You have become better at Offense! (5)"));
    }
}
