using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace EQClassic.ClientCore
{
    /// <summary>
    /// How the interface is laid out: <see cref="UiMode.Classic"/>, the Trilogy client's fixed frame
    /// around the 3D view (1999), or <see cref="UiMode.Windows"/>, the 3D view on the whole screen with
    /// each module in a window that moves, closes and reopens (the later EverQuest interface).
    /// </summary>
    public enum UiMode { Classic, Windows }

    /// <summary>A module's window in the <see cref="UiMode.Windows"/> layout: where it is, and whether it is open.</summary>
    public sealed class HudWindow
    {
        public HudWindow(string id, float x, float y, float width, float height, bool open)
        {
            Id = id; X = x; Y = y; Width = width; Height = height; Open = open;
        }

        public string Id { get; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public bool Open { get; set; }
    }

    /// <summary>
    /// The modules of the interface and their windows: the layout mode, and for the window layout each
    /// window's place and whether it is open. Kept between sessions as one line (<see cref="Save"/>).
    /// </summary>
    public sealed class HudLayout
    {
        public const string Player = "player", Target = "target", Group = "group", Buffs = "buffs", Gems = "gems",
            Casting = "casting", Hotbar = "hotbar", Chat = "chat", Actions = "actions", Buttons = "buttons",
            Inventory = "inventory", Book = "book", Skills = "skills", Help = "help";

        /// <summary>The modules the button bar and #windows list, in order (the others open by themselves: loot, merchant, trade, bank).</summary>
        public static readonly string[] Modules = { Player, Target, Group, Buffs, Gems, Casting, Hotbar, Chat, Actions, Buttons, Inventory, Book, Skills, Help };

        public static string Title(string id) => id switch
        {
            Player => "Player", Target => "Target", Group => "Group", Buffs => "Effects", Gems => "Spell Gems", Casting => "Casting",
            Hotbar => "Hot Buttons", Chat => "Main Chat", Actions => "Actions", Buttons => "Menu", Inventory => "Inventory",
            Book => "Spell Book", Skills => "Skills", Help => "Help", _ => id,
        };

        private readonly Dictionary<string, HudWindow> _windows = new Dictionary<string, HudWindow>();

        public UiMode Mode { get; set; } = UiMode.Classic;
        public IEnumerable<HudWindow> Windows => Modules.Select(m => _windows[m]);

        public HudWindow this[string id] => _windows[id];

        public bool IsOpen(string id) => _windows.TryGetValue(id, out var w) && w.Open;

        public void Toggle(string id)
        {
            if (_windows.TryGetValue(id, out var w))
                w.Open = !w.Open;
        }

        public void SetOpen(string id, bool open)
        {
            if (_windows.TryGetValue(id, out var w))
                w.Open = open;
        }

        /// <summary>The default windows for a screen (the 3D view in the middle, windows at the edges).</summary>
        public static HudLayout Default(float screenWidth, float screenHeight)
        {
            float w = screenWidth, h = screenHeight;
            var layout = new HudLayout();
            void Add(string id, float x, float y, float width, float height, bool open) =>
                layout._windows[id] = new HudWindow(id, x, y, width, height, open);
            Add(Buttons, w / 2 - 260, 4, 520, 52, true);
            Add(Player, 10, 10, 240, 110, true);
            Add(Target, w / 2 - 150, 62, 300, 64, true);
            Add(Casting, w / 2 - 160, h - 330, 320, 50, true);
            Add(Buffs, w - 270, 10, 260, 300, true);
            Add(Gems, 10, 130, 210, 250, true);
            Add(Group, w - 270, h - 360, 260, 200, true);
            Add(Hotbar, w / 2 - 330, h - 270, 660, 72, true);
            Add(Actions, w - 270, h - 150, 260, 140, true);
            Add(Chat, 10, h - 250, Math.Min(760, w / 2), 240, true);
            Add(Inventory, w - 660, 60, 380, h - 140, false);
            Add(Book, w / 2 - 280, 80, 560, h - 200, false);
            Add(Skills, w - 340, 60, 320, h - 200, false);
            Add(Help, w / 2 - 300, 120, 600, 360, false);
            return layout;
        }

        /// <summary>Keeps every window on the screen (after a resolution change).</summary>
        public void Clamp(float screenWidth, float screenHeight)
        {
            foreach (var win in _windows.Values)
            {
                win.Width = Math.Max(80f, Math.Min(win.Width, screenWidth));
                win.Height = Math.Max(40f, Math.Min(win.Height, screenHeight));
                win.X = Math.Max(0f, Math.Min(win.X, screenWidth - win.Width));
                win.Y = Math.Max(0f, Math.Min(win.Y, screenHeight - win.Height));
            }
        }

        /// <summary>"mode|id:x,y,w,h,open|..." (invariant culture).</summary>
        public string Save()
        {
            var s = new StringBuilder(Mode == UiMode.Windows ? "windows" : "classic");
            foreach (var w in Windows)
                s.Append('|').Append(w.Id).Append(':').Append(string.Join(",",
                    new[] { w.X, w.Y, w.Width, w.Height }.Select(v => v.ToString("0", CultureInfo.InvariantCulture)))).Append(',').Append(w.Open ? 1 : 0);
            return s.ToString();
        }

        /// <summary>A saved layout over the defaults; unknown or broken parts are ignored.</summary>
        public static HudLayout Load(string? saved, float screenWidth, float screenHeight)
        {
            var layout = Default(screenWidth, screenHeight);
            if (string.IsNullOrEmpty(saved))
                return layout;
            var parts = saved!.Split('|');
            layout.Mode = parts[0] == "windows" ? UiMode.Windows : UiMode.Classic;
            foreach (var part in parts.Skip(1))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0 || !layout._windows.TryGetValue(part.Substring(0, colon), out var win))
                    continue;
                var v = part.Substring(colon + 1).Split(',');
                if (v.Length != 5 || !v.Take(4).All(x => float.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
                    continue;
                float F(int i) => float.Parse(v[i], CultureInfo.InvariantCulture);
                (win.X, win.Y, win.Width, win.Height, win.Open) = (F(0), F(1), F(2), F(3), v[4] == "1");
            }
            layout.Clamp(screenWidth, screenHeight);
            return layout;
        }
    }
}
