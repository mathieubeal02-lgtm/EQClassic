using System;
using System.Linq;
using System.Text;

namespace EQClassic.ClientCore
{
    /// <summary>A hot button: its label and the command line it runs (/attack, /cast 1, /kick, a say...).</summary>
    public sealed class HotButton
    {
        public HotButton(string label, string command)
        {
            Label = label;
            Command = command;
        }

        public string Label { get; }
        public string Command { get; }
    }

    /// <summary>
    /// The hot buttons: <see cref="Pages"/> pages of <see cref="Slots"/> buttons, as in the Trilogy
    /// client (the arrows under the six boxes turn the pages). A button runs its command line.
    /// </summary>
    public sealed class Hotbar
    {
        public const int Pages = 10, Slots = 6;

        private readonly HotButton?[,] _buttons = new HotButton?[Pages, Slots];
        private int _page;

        public int Page
        {
            get => _page;
            set => _page = ((value % Pages) + Pages) % Pages;
        }

        public HotButton? Get(int page, int slot) => page >= 0 && page < Pages && slot >= 0 && slot < Slots ? _buttons[page, slot] : null;
        public HotButton? this[int slot] => Get(_page, slot);

        public void Set(int page, int slot, HotButton? button)
        {
            if (page >= 0 && page < Pages && slot >= 0 && slot < Slots)
                _buttons[page, slot] = button is null || string.IsNullOrWhiteSpace(button.Command) ? null : button;
        }

        /// <summary>The first page a new character finds: fight, look, rest.</summary>
        public static Hotbar Default()
        {
            var bar = new Hotbar();
            string[][] first = { new[] { "Attack", "/attack" }, new[] { "Consider", "/con" }, new[] { "Hail", "/hail" },
                new[] { "Loot", "/loot" }, new[] { "Sit", "/sit" }, new[] { "Camp", "/camp" } };
            for (int i = 0; i < first.Length; i++)
                bar.Set(0, i, new HotButton(first[i][0], first[i][1]));
            return bar;
        }

        /// <summary>"page,slot,label,command" lines (label and command with \t and \n removed, commas kept in the command).</summary>
        public string Save()
        {
            var s = new StringBuilder();
            for (int p = 0; p < Pages; p++)
                for (int i = 0; i < Slots; i++)
                    if (_buttons[p, i] is { } b)
                        s.Append(p).Append(',').Append(i).Append(',').Append(Clean(b.Label).Replace(',', ' ')).Append(',').Append(Clean(b.Command)).Append('\n');
            return s.ToString();
        }

        public static Hotbar Load(string? saved)
        {
            if (string.IsNullOrWhiteSpace(saved))
                return Default();
            var bar = new Hotbar();
            foreach (var line in saved!.Split('\n'))
            {
                var f = line.Split(new[] { ',' }, 4);
                if (f.Length == 4 && int.TryParse(f[0], out int p) && int.TryParse(f[1], out int i))
                    bar.Set(p, i, new HotButton(f[2], f[3]));
            }
            return bar;
        }

        private static string Clean(string s) => new string(s.Where(c => c != '\n' && c != '\r' && c != '\t').ToArray()).Trim();
    }
}
