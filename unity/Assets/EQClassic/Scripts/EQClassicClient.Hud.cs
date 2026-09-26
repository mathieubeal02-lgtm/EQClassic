using System.Collections.Generic;
using EQClassic.ClientCore;
using EQClassic.Shared.Zone;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The two layouts of the interface (F12 or /ui switches):
    /// Classic, the Trilogy client's frame (1999): a column on each side of the 3D view (help, options,
    /// persona, the spell gems, view, the hot buttons; the player, effects, party, current target,
    /// abilities / combat / socials, who, invite, disband, camp, sit, walk) and the chat at the bottom
    /// of the view; Windows, the view on the whole screen and each module in a window that moves,
    /// closes and reopens from the menu bar, where it is kept between sessions.
    /// </summary>
    public sealed partial class EQClassicClient
    {
        private HudLayout _layout;
        private bool _layoutDirty;
        private float _layoutSavedAt;

        private static readonly Color FrameColour = new Color(0.36f, 0.38f, 0.42f, 1f);
        private static readonly Color PanelColour = new Color(0.22f, 0.23f, 0.26f, 1f);
        private static readonly Color ViewPanelColour = new Color(0f, 0f, 0f, 0.55f);

        private GUIStyle _bigButton, _heading;

        private void EnsureStyles()
        {
            if (_bigButton != null)
                return;
            _bigButton = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
            _heading = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
        }

        private void ToggleWindow(string id)
        {
            _layout.Toggle(id);
            _layoutDirty = true;
        }

        private void SwitchLayout()
        {
            _layout.Mode = _layout.Mode == UiMode.Classic ? UiMode.Windows : UiMode.Classic;
            _layoutDirty = true;
            AddMessage(_layout.Mode == UiMode.Classic ? "Interface: classic (F12 for windows)." : "Interface: windows (F12 for the classic frame).");
        }

        /// <summary>/ui classic, /ui windows, /ui reset: handled by the interface, not the game.</summary>
        private bool InterfaceCommand(string line)
        {
            var words = line.Trim().Split(' ');
            if (words.Length == 0 || !string.Equals(words[0], "/ui", System.StringComparison.OrdinalIgnoreCase))
                return false;
            string what = words.Length > 1 ? words[1].ToLowerInvariant() : "";
            if (what == "classic" || what == "windows")
            {
                _layout.Mode = what == "classic" ? UiMode.Classic : UiMode.Windows;
                _layoutDirty = true;
            }
            else if (what == "reset")
            {
                var mode = _layout.Mode;
                _layout = HudLayout.Default(Screen.width, Screen.height);
                _layout.Mode = mode;
                _layoutDirty = true;
                AddMessage("Windows back to their default places.");
            }
            else
                AddMessage("Usage: /ui classic | /ui windows | /ui reset (F12 switches)");
            return true;
        }

        private void SaveLayout() => PlayerPrefs.SetString("eqc.layout", _layout.Save());

        private void SaveLayoutWhenChanged()
        {
            if (_layoutDirty && Time.unscaledTime - _layoutSavedAt > 2f)
            {
                SaveLayout();
                _layoutDirty = false;
                _layoutSavedAt = Time.unscaledTime;
            }
        }

        /// <summary>The whole in-zone interface.</summary>
        private void DrawHud()
        {
            EnsureStyles();
            // Windows the game opens by itself bring the inventory with them.
            if (_client.Merchant != null || _client.Trade != null || _client.Bank != null)
                _layout.SetOpen(HudLayout.Inventory, true);
            if (_layout.Mode == UiMode.Classic)
                DrawClassic();
            else
                DrawWindows();
            if (_presenter.MissingZone != null)
                GUI.Box(new Rect(Screen.width / 2 - 300, 80, 600, 44),
                    $"The zone '{_presenter.MissingZone}' is not installed in this client (not imported from Lantern).\nYou are there for the server, but nothing can be drawn.");
            HotButtonEditor();
        }

        // ---- Classic ----

        private string _classicPopup; // abilities, combat, socials, options

        private void DrawClassic()
        {
            float W = Screen.width, H = Screen.height;
            float side = Mathf.Clamp(W * 0.15f, 190f, 300f);
            _presenter.Viewport = new Rect(side / W, 0f, 1f - 2f * side / W, 1f);
            var left = new Rect(0, 0, side, H);
            var right = new Rect(W - side, 0, side, H);
            Fill(left, FrameColour);
            Fill(right, FrameColour);
            float pad = 8, inner = side - 2 * pad;

            // Left column: help, options, persona, the gems around the class, view, the hot buttons.
            float y = pad;
            if (GUI.Button(new Rect(pad, y, inner, 32), "HELP", _bigButton)) ToggleWindow(HudLayout.Help);
            y += 38;
            if (GUI.Button(new Rect(pad, y, inner, 32), "OPTIONS", _bigButton)) _classicPopup = _classicPopup == "options" ? null : "options";
            y += 38;
            if (GUI.Button(new Rect(pad, y, inner, 32), "PERSONA", _bigButton)) ToggleWindow(HudLayout.Inventory);
            y += 44;
            float gemsHeight = Mathf.Min(8 * 30f + 30f, H * 0.4f);
            Fill(new Rect(pad, y, inner, gemsHeight), PanelColour);
            GUI.Label(new Rect(pad, y + 4, inner, 22), "SPELLS", _heading);
            GemsPanel(new Rect(pad + 6, y + 28, inner - 12, gemsHeight - 34));
            y += gemsHeight + 8;
            if (GUI.Button(new Rect(pad, y, inner, 32), "VIEW", _bigButton)) _presenter.CycleView();
            y += 40;
            if (GUI.Button(new Rect(pad, y, inner, 30), "SPELL BOOK", _bigButton)) ToggleWindow(HudLayout.Book);
            y += 38;
            float hotHeight = Mathf.Max(120f, H - y - pad);
            HotbarPanel(new Rect(pad, y, inner, Mathf.Min(hotHeight, 240f)), 2);

            // Right column: the player, effects, party, current target, the action buttons.
            float x = W - side + pad;
            y = pad;
            Fill(new Rect(x, y, inner, 100), PanelColour);
            PlayerPanel(new Rect(x + 6, y + 6, inner - 12, 88));
            y += 106;
            bool effects = _layout.IsOpen(HudLayout.Buffs);
            if (GUI.Button(new Rect(x, y, inner, 28), "EFFECTS", _bigButton)) ToggleWindow(HudLayout.Buffs);
            y += 34;
            float partyHeight = Mathf.Min(180f, H * 0.2f);
            Fill(new Rect(x, y, inner, partyHeight), PanelColour);
            GUI.Label(new Rect(x, y + 2, inner, 22), "PARTY", _heading);
            GroupPanel(new Rect(x + 6, y + 26, inner - 12, partyHeight - 30));
            y += partyHeight + 6;
            GUI.Label(new Rect(x, y, inner, 22), "CURRENT TARGET", _heading);
            y += 24;
            Fill(new Rect(x, y, inner, 44), PanelColour);
            TargetPanel(new Rect(x + 6, y + 4, inner - 12, 38));
            y += 52;
            if (GUI.Button(new Rect(x, y, inner, 28), "ABILITIES", _bigButton)) _classicPopup = _classicPopup == "abilities" ? null : "abilities";
            y += 32;
            if (GUI.Button(new Rect(x, y, inner, 28), "COMBAT", _bigButton)) _classicPopup = _classicPopup == "combat" ? null : "combat";
            y += 32;
            if (GUI.Button(new Rect(x, y, inner, 28), "SOCIALS", _bigButton)) _classicPopup = _classicPopup == "socials" ? null : "socials";
            y += 40;
            (string Label, System.Action Run)[] buttons =
            {
                ("WHO", () => _client.ExecuteChat("/who")),
                ("INVITE", () => _client.ExecuteChat("/invite")),
                ("DISBAND", () => _client.ExecuteChat("/disband")),
                ("CAMP", () => _client.ExecuteChat("/camp")),
                (_client.Sitting ? "STAND" : "SIT", () => _client.ToggleSit()),
                (_presenter.WalkToggle ? "RUN" : "WALK", () => _presenter.WalkToggle = !_presenter.WalkToggle),
            };
            float bh = Mathf.Min(28f, (H - y - pad) / buttons.Length - 4);
            foreach (var (label, run) in buttons)
            {
                if (bh >= 14 && GUI.Button(new Rect(x, y, inner, bh), label, _bigButton))
                    run();
                y += bh + 4;
            }

            // The view: chat at the bottom, casting bar above it, effects at the top right, the rest in the middle.
            float viewX = side + 10, viewW = W - 2 * side - 20;
            float chatH = Mathf.Clamp(H * 0.3f, 150f, 340f);
            var chat = new Rect(viewX, H - chatH - 8, viewW, chatH);
            Fill(chat, ViewPanelColour);
            var chatInner = new Rect(chat.x + 6, chat.y + 4, chat.width - 12, chat.height - 8);
            ChatPanel(chatInner, chatInner);
            if (_client.Casting != null)
                CastingPanel(new Rect(viewX + viewW / 2 - 160, chat.y - 30, 320, 18));

            // Screens that take the middle of the view: the inventory beside a merchant, a bank or a trade, the book...
            var middle = new Rect(viewX + 10, 40, viewW - 20, chat.y - 50);
            var main = new List<(string Title, System.Action<Rect> Draw)>();
            if (_client.Merchant is { } merchant)
                main.Add(($"{_client.Zone?.Get(merchant.NpcId)?.DisplayName ?? "Merchant"} - click to buy", r => MerchantPanel(r, merchant)));
            if (_client.Bank is { } bank)
                main.Add(("Bank", r => BankPanel(r, bank)));
            if (_client.Trade is { } trade)
                main.Add(($"Trading with {trade.Partner}", r => TradePanel(r, trade)));
            if (_client.LootingCorpse is int corpse)
                main.Add((_client.Zone?.Get(corpse)?.DisplayName ?? "Corpse", LootPanel));
            if (_layout.IsOpen(HudLayout.Inventory))
                main.Add(("Persona - inventory", InventoryPanel));
            if (_layout.IsOpen(HudLayout.Book))
                main.Add(("Spell book", BookPanel));
            if (_layout.IsOpen(HudLayout.Skills))
                main.Add(("Skills", SkillsPanel));
            if (_layout.IsOpen(HudLayout.Help))
                main.Add(("Help", HelpPanel));
            int shown = Mathf.Min(main.Count, 2); // two side by side at most, the most recent reasons first
            float each = shown == 0 ? 0 : (middle.width - 10 * (shown - 1)) / shown;
            for (int i = 0; i < shown; i++)
                ViewBox(new Rect(middle.x + i * (each + 10), middle.y, each, middle.height), main[i].Title, main[i].Draw);
            // Effects and the classic panels over the view, when nothing takes the middle of it.
            if (effects && shown == 0)
                ViewBox(new Rect(W - side - 280, 10, 270, Mathf.Min(300f, 40f + 18f * Mathf.Max(1, _client.Buffs?.Buffs.Count ?? 0))), "Effects", BuffsPanel);
            if (_classicPopup != null)
                ClassicPopup(new Rect(W - side - 300, H * 0.35f, 290, 220));
        }

        /// <summary>A dark box in the 3D view with a title, the module inside.</summary>
        private void ViewBox(Rect r, string title, System.Action<Rect> draw)
        {
            Fill(r, new Color(0.1f, 0.1f, 0.12f, 0.85f));
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, 22), title, _heading);
            draw(new Rect(r.x + 8, r.y + 28, r.width - 16, r.height - 34));
        }

        /// <summary>The classic ABILITIES, COMBAT, SOCIALS and OPTIONS panels.</summary>
        private void ClassicPopup(Rect r)
        {
            switch (_classicPopup)
            {
                case "abilities":
                    ViewBox(r, "Abilities", rr =>
                    {
                        AbilitiesPanel(new Rect(rr.x, rr.y, rr.width, rr.height - 30));
                        if (GUI.Button(new Rect(rr.x, rr.yMax - 24, rr.width, 22), "Skills (K)")) ToggleWindow(HudLayout.Skills);
                    });
                    break;
                case "combat":
                    ViewBox(r, "Combat", rr =>
                    {
                        (string Label, System.Action Run)[] b =
                        {
                            (_client.AutoAttacking ? "Stop attacking" : "Attack", () => _client.ToggleAutoAttack()),
                            ("Target nearest", () => _client.TargetNearest()), ("Consider", () => _client.Consider()),
                            ("Face target", () => _client.FaceTarget()), ("Loot", () => _client.Loot()),
                        };
                        for (int i = 0; i < b.Length; i++)
                            if (GUI.Button(new Rect(rr.x, rr.y + i * 28, rr.width, 24), b[i].Label)) b[i].Run();
                    });
                    break;
                case "socials":
                    ViewBox(r, "Socials", rr =>
                    {
                        string[][] b = { new[] { "Hail", "/hail" }, new[] { "Wave", "/em waves." }, new[] { "Bow", "/em bows." },
                            new[] { "Cheer", "/em cheers!" }, new[] { "Laugh", "/em laughs." }, new[] { "Thank", "/em thanks everyone." } };
                        float bw = (rr.width - 4) / 2;
                        for (int i = 0; i < b.Length; i++)
                            if (GUI.Button(new Rect(rr.x + (i % 2) * (bw + 4), rr.y + (i / 2) * 28, bw, 24), b[i][0])) _client.ExecuteChat(b[i][1]);
                    });
                    break;
                case "options":
                    ViewBox(r, "Options", rr =>
                    {
                        if (GUI.Button(new Rect(rr.x, rr.y, rr.width, 24), "Interface: windows (F12)")) SwitchLayout();
                        if (GUI.Button(new Rect(rr.x, rr.y + 28, rr.width, 24), $"Keyboard: {_client.Keys.Layout} (switch)"))
                            _client.ExecuteChat(_client.Keys.Layout == KeyboardLayout.Azerty ? "/keys qwerty" : "/keys azerty");
                        if (GUI.Button(new Rect(rr.x, rr.y + 56, rr.width, 24), "Camera view (F9)")) _presenter.CycleView();
                        if (GUI.Button(new Rect(rr.x, rr.y + 84, rr.width, 24), "Close")) _classicPopup = null;
                    });
                    break;
            }
        }

        // ---- Windows ----

        private readonly Dictionary<int, Rect> _transientWindows = new Dictionary<int, Rect>();
        private const int TransientBase = 100;

        private void DrawWindows()
        {
            _presenter.Viewport = new Rect(0f, 0f, 1f, 1f);
            int index = 0;
            foreach (var win in _layout.Windows)
            {
                int id = index++;
                if (!win.Open)
                    continue;
                var before = new Rect(win.X, win.Y, win.Width, win.Height);
                var after = GUI.Window(id, before, DrawModuleWindow, HudLayout.Title(win.Id));
                if (after.x != before.x || after.y != before.y)
                {
                    (win.X, win.Y) = (after.x, after.y);
                    _layoutDirty = true;
                }
            }
            // Windows the game opens (loot, merchant, bank, trade): movable, not kept.
            Transient(0, _client.LootingCorpse is int corpse ? _client.Zone?.Get(corpse)?.DisplayName ?? "Corpse" : null, 320, 320, LootPanel);
            Transient(1, _client.Merchant is { } m ? _client.Zone?.Get(m.NpcId)?.DisplayName ?? "Merchant" : null, 340, Screen.height - 160,
                r => { if (_client.Merchant is { } mm) MerchantPanel(r, mm); });
            Transient(2, _client.Bank != null ? "Bank" : null, 340, Screen.height - 160, r => { if (_client.Bank is { } b) BankPanel(r, b); });
            Transient(3, _client.Trade is { } t ? $"Trading with {t.Partner}" : null, 540, 360, r => { if (_client.Trade is { } tt) TradePanel(r, tt); });
        }

        private readonly Dictionary<int, System.Action<Rect>> _transientDraw = new Dictionary<int, System.Action<Rect>>();

        private void Transient(int n, string title, float width, float height, System.Action<Rect> draw)
        {
            int id = TransientBase + n;
            if (title == null)
            {
                _transientWindows.Remove(id);
                return;
            }
            if (!_transientWindows.TryGetValue(id, out var rect))
                rect = new Rect(Screen.width - width - 290, 70 + 20 * n, width, height);
            _transientDraw[id] = draw;
            _transientWindows[id] = GUI.Window(id, rect, DrawTransientWindow, title);
        }

        private void DrawTransientWindow(int id)
        {
            var rect = _transientWindows[id];
            if (_transientDraw.TryGetValue(id, out var draw))
                draw(new Rect(6, 22, rect.width - 12, rect.height - 28));
            GUI.DragWindow(new Rect(0, 0, 10000, 20));
        }

        /// <summary>One module's window: its content, a close button, dragged by its title bar.</summary>
        private void DrawModuleWindow(int id)
        {
            var win = System.Linq.Enumerable.ElementAt(_layout.Windows, id);
            var content = new Rect(6, 22, win.Width - 12, win.Height - 28);
            switch (win.Id)
            {
                case HudLayout.Player: PlayerPanel(content); break;
                case HudLayout.Target: TargetPanel(content); break;
                case HudLayout.Group: GroupPanel(content); break;
                case HudLayout.Buffs: BuffsPanel(content); break;
                case HudLayout.Gems: GemsPanel(content); break;
                case HudLayout.Casting: CastingPanel(content); break;
                case HudLayout.Hotbar: HotbarPanel(content, Hotbar.Slots); break;
                case HudLayout.Actions:
                    ActionsPanel(new Rect(content.x, content.y, content.width, content.height));
                    break;
                case HudLayout.Chat:
                    ChatPanel(content, new Rect(win.X + content.x, win.Y + content.y, content.width, content.height));
                    break;
                case HudLayout.Buttons: MenuPanel(content); break;
                case HudLayout.Inventory: InventoryPanel(content); break;
                case HudLayout.Book: BookPanel(content); break;
                case HudLayout.Skills: SkillsPanel(content); break;
                case HudLayout.Help: HelpPanel(content); break;
            }
            if (win.Id != HudLayout.Buttons && GUI.Button(new Rect(win.Width - 20, 2, 18, 16), "x"))
            {
                win.Open = false;
                _layoutDirty = true;
            }
            GUI.DragWindow(new Rect(0, 0, win.Width - 22, 20));
        }

        /// <summary>The menu bar of the window layout: opens and closes the other windows, switches to the classic frame.</summary>
        private void MenuPanel(Rect r)
        {
            string[] ids = { HudLayout.Inventory, HudLayout.Book, HudLayout.Skills, HudLayout.Gems, HudLayout.Hotbar, HudLayout.Actions,
                HudLayout.Group, HudLayout.Buffs, HudLayout.Target, HudLayout.Player, HudLayout.Chat, HudLayout.Help };
            string[] labels = { "Inv", "Book", "Skills", "Gems", "Hot", "Act", "Group", "Buffs", "Target", "Me", "Chat", "?" };
            float bw = (r.width - 60) / ids.Length;
            for (int i = 0; i < ids.Length; i++)
            {
                var colour = GUI.color;
                if (!_layout.IsOpen(ids[i]))
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);
                if (GUI.Button(new Rect(r.x + i * bw, r.y, bw - 2, r.height), labels[i]))
                    ToggleWindow(ids[i]);
                GUI.color = colour;
            }
            if (GUI.Button(new Rect(r.xMax - 56, r.y, 56, r.height), "Classic"))
                SwitchLayout();
        }
    }
}
