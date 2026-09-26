using EQClassic.ClientCore;

namespace EQClassic.Tests.Client;

public class HudTests
{
    [Fact]
    public void A_saved_layout_comes_back_and_stays_on_screen()
    {
        var layout = HudLayout.Default(1920, 1080);
        Assert.Equal(UiMode.Classic, layout.Mode);
        layout.Mode = UiMode.Windows;
        layout[HudLayout.Inventory].Open = true;
        layout[HudLayout.Chat].X = 300;
        layout[HudLayout.Player].X = 1800; // off a smaller screen later
        var back = HudLayout.Load(layout.Save(), 1280, 720);
        Assert.Equal(UiMode.Windows, back.Mode);
        Assert.True(back.IsOpen(HudLayout.Inventory));
        Assert.Equal(300, back[HudLayout.Chat].X);
        Assert.Equal(1280 - back[HudLayout.Player].Width, back[HudLayout.Player].X);
        Assert.Equal(UiMode.Classic, HudLayout.Load("rubbish|chat:x,1,2,3,1", 800, 600).Mode);
    }

    [Fact]
    public void Windows_toggle()
    {
        var layout = HudLayout.Default(1024, 768);
        Assert.False(layout.IsOpen(HudLayout.Book));
        layout.Toggle(HudLayout.Book);
        Assert.True(layout.IsOpen(HudLayout.Book));
        Assert.False(layout.IsOpen("nothing"));
    }

    [Fact]
    public void Hot_buttons_have_pages_and_are_kept()
    {
        var bar = Hotbar.Default();
        Assert.Equal("/attack", bar[0]!.Command);
        bar.Set(3, 5, new HotButton("Heal me", "/cast 1"));
        bar.Set(0, 1, null);
        bar.Page = -1;
        Assert.Equal(Hotbar.Pages - 1, bar.Page);
        var back = Hotbar.Load(bar.Save());
        Assert.Equal("/cast 1", back.Get(3, 5)!.Command);
        Assert.Equal("Heal me", back.Get(3, 5)!.Label);
        Assert.Null(back.Get(0, 1));
        Assert.Equal("Hail", back.Get(0, 2)!.Label);
    }

    [Fact]
    public void Attack_and_loot_are_commands_for_hot_buttons()
    {
        Assert.Equal(ChatAction.Attack, Chat.Parse("/attack").Action);
        Assert.Equal(ChatAction.Loot, Chat.Parse("/loot").Action);
    }
}
