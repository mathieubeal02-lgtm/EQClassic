using System.Collections.Generic;
using System.Linq;
using EQClassic.ClientCore;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The classic interface drawn with the Trilogy client's own frame (main1.bmp), stretched to the
    /// screen: the 3D view in its hole, and over the art the buttons (HELP, OPTIONS, PERSONA, SPELLS,
    /// VIEW, the page arrows, EFFECTS, MAIN, ABILITIES, SOCIALS, WHO, INVITE, DISBAND, CAMP, SIT, WALK),
    /// the spell gems in their sockets, the six boxes, the gauges, party, current target, and the chat
    /// on the parchment. Places are measured on the 640 × 480 art.
    /// </summary>
    public sealed partial class EQClassicClient
    {
        private ClassicSkin _skin;
        private bool _skinTried;
        private GUIStyle _hit, _small, _smallBold, _boxText, _parchment;
        private Texture2D _highlight;

        /// <summary>What the six boxes under the gems show: the hot buttons (MAIN), combat, abilities or socials.</summary>
        private enum BoxMode { Main, Combat, Abilities, Socials }
        private BoxMode _boxMode = BoxMode.Main;

        private static readonly float[] GemCentres = { 26, 62, 100, 138, 176, 214, 252, 290 };
        private static readonly Rect[] Boxes =
        {
            new Rect(13, 324, 46, 47), new Rect(62, 324, 46, 47),
            new Rect(13, 373, 46, 47), new Rect(62, 373, 46, 47),
            new Rect(13, 421, 46, 47), new Rect(62, 421, 46, 47),
        };

        private ClassicSkin Skin
        {
            get
            {
                if (!_skinTried)
                {
                    _skinTried = true;
                    _skin = ClassicSkin.TryLoad();
                }
                return _skin;
            }
        }

        private void EnsureClassicStyles(float sy)
        {
            if (_hit == null)
            {
                _highlight = new Texture2D(1, 1);
                _highlight.SetPixel(0, 0, new Color(1f, 1f, 0.8f, 0.25f));
                _highlight.Apply();
                _hit = new GUIStyle();
                _hit.hover.background = _highlight;
                _hit.active.background = _highlight;
                _small = new GUIStyle(GUI.skin.label) { wordWrap = false };
                _smallBold = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _boxText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
                _parchment = new GUIStyle(GUI.skin.label);
            }
            int size = Mathf.Clamp(Mathf.RoundToInt(9f * sy), 10, 20);
            _small.fontSize = size;
            _smallBold.fontSize = size;
            _boxText.fontSize = Mathf.Clamp(Mathf.RoundToInt(8f * sy), 9, 18);
            _parchment.fontSize = Mathf.Clamp(Mathf.RoundToInt(8.5f * sy), 10, 20);
            _small.normal.textColor = Color.white;
            _smallBold.normal.textColor = Color.white;
            _boxText.normal.textColor = new Color(0.95f, 0.9f, 0.75f);
        }

        /// <summary>The whole classic interface on the Trilogy art.</summary>
        private void DrawClassicArt(ClassicSkin skin)
        {
            float W = Screen.width, H = Screen.height, sx = W / ClassicSkin.Width, sy = H / ClassicSkin.Height;
            Rect R(float x, float y, float w, float h) => new Rect(x * sx, y * sy, w * sx, h * sy);
            EnsureClassicStyles(sy);
            // The view: the magenta hole of the frame (119, 0) to (518, 319).
            _presenter.Viewport = new Rect(119f / ClassicSkin.Width, 1f - 320f / ClassicSkin.Height, 400f / ClassicSkin.Width, 320f / ClassicSkin.Height);
            GUI.DrawTexture(new Rect(0, 0, W, H), skin.Frame);

            // Left column.
            if (Hit(R(12, 18, 68, 16))) ToggleWindow(HudLayout.Help);
            if (Hit(R(12, 38, 68, 16))) _classicPopup = _classicPopup == "options" ? null : "options";
            if (Hit(R(12, 67, 68, 16))) ToggleWindow(HudLayout.Inventory);
            if (Hit(R(12, 258, 68, 17))) ToggleWindow(HudLayout.Book);
            if (Hit(R(12, 287, 68, 17))) _presenter.CycleView();
            ClassPanel(R(16, 88, 62, 164));
            GemSockets(skin, sx, sy);
            SixBoxes(sx, sy);

            // Right column: name and gauges, effects, party, target, the buttons.
            GUI.Label(R(535, 12, 90, 14), CharacterName, _smallBold);
            GUI.Label(R(535, 25, 90, 14), (_client.AutoAttacking ? "Attacking " : "") + (_client.Sitting ? "Sitting" : ""), _small);
            Gauge(R(538, 44, 85, 6), _client.MaxHp > 0 ? (float)_client.Hp / _client.MaxHp : 0f);
            Gauge(R(538, 53, 85, 6), _client.MaxMana > 0 ? (float)_client.Mana / _client.MaxMana : 0f);
            Gauge(R(538, 62, 85, 7), _client.Stamina is { } st ? (100 - st.Fatigue) / 100f : 1f);
            if (Hit(R(529, 84, 101, 16))) ToggleWindow(HudLayout.Buffs);
            PartyText(R(537, 118, 86, 88));
            TargetBar(R(530, 237, 100, 12));
            if (Hit(R(529, 254, 101, 16))) _boxMode = _boxMode == BoxMode.Main ? BoxMode.Combat : BoxMode.Main;
            if (Hit(R(529, 275, 101, 16))) _boxMode = BoxMode.Abilities;
            if (Hit(R(529, 296, 101, 16))) _boxMode = BoxMode.Socials;
            if (Hit(R(529, 332, 101, 16))) _client.ExecuteChat("/who");
            if (Hit(R(529, 355, 101, 16))) _client.ExecuteChat("/invite");
            if (Hit(R(529, 378, 101, 16))) _client.ExecuteChat("/disband");
            if (Hit(R(529, 401, 101, 16))) _client.ExecuteChat("/camp");
            if (Hit(R(529, 424, 101, 16))) _client.ToggleSit();
            if (Hit(R(529, 447, 101, 16))) _presenter.WalkToggle = !_presenter.WalkToggle;
            if (_client.Sitting) Mark(R(529, 424, 101, 16));
            if (_presenter.WalkToggle) Mark(R(529, 447, 101, 16));

            // The chat on the parchment, its arrows on the frame.
            var chat = R(142, 334, 362, 128);
            ChatPanel(chat, chat, parchment: true);
            if (Hit(R(509, 331, 11, 17))) _chat.ScrollBy(_chatLinesShown - 1, _chatLinesShown);
            if (Hit(R(509, 455, 11, 17))) _chat.ScrollBy(1 - _chatLinesShown, _chatLinesShown);

            // Inside the view: the casting bar, windows, effects, the option panels.
            var view = R(123, 4, 392, 312);
            if (_client.Casting != null)
                CastingPanel(new Rect(view.x + view.width / 2 - 120 * sx / 2f, view.yMax - 20 * sy, 120 * sx, 12));
            int shown = DrawViewWindows(view);
            if (_layout.IsOpen(HudLayout.Buffs) && shown == 0)
                ViewBox(new Rect(view.xMax - 230, view.y + 4, 226, Mathf.Min(260f, 40f + 18f * Mathf.Max(1, _client.Buffs?.Buffs.Count ?? 0))), "Effects", BuffsPanel);
            if (_classicPopup != null)
                ClassicPopup(new Rect(view.xMax - 250, view.y + view.height * 0.3f, 240, 200));
        }

        /// <summary>An invisible button over the art (lit under the mouse).</summary>
        private bool Hit(Rect r) => GUI.Button(r, "", _hit);

        /// <summary>A pressed-in look for a button that is on (SIT, WALK).</summary>
        private static void Mark(Rect r) => Fill(r, new Color(0f, 0f, 0f, 0.35f));

        /// <summary>The art has the gauges full: the missing part is darkened.</summary>
        private static void Gauge(Rect r, float fraction)
        {
            fraction = Mathf.Clamp(fraction, 0f, 1f);
            Fill(new Rect(r.x + r.width * fraction, r.y, r.width * (1f - fraction), r.height), new Color(0.05f, 0.05f, 0.08f, 0.85f));
        }

        /// <summary>The panel under PERSONA: level, experience, hunger and thirst.</summary>
        private void ClassPanel(Rect r)
        {
            if (_client.Experience is not { } xp)
                return;
            GUI.Label(new Rect(r.x, r.y, r.width, 18), $"Level {xp.Level}", _smallBold);
            GUI.Label(new Rect(r.x, r.y + 18, r.width, 18), $"{(int)(xp.Fraction * 100)}% exp", _small);
            DrawBar(new Rect(r.x, r.y + 38, r.width, 5), xp.Fraction, new Color(0.9f, 0.8f, 0.2f), "");
            float y = r.y + 48;
            if (_client.Stamina is { Hunger: 0 }) { GUI.Label(new Rect(r.x, y, r.width, 18), "Hungry", _small); y += 16; }
            if (_client.Stamina is { Thirst: 0 }) GUI.Label(new Rect(r.x, y, r.width, 18), "Thirsty", _small);
        }

        /// <summary>The eight sockets: the memorised spells' gems (click or 1-8 to cast), dimmed while casting or short of mana.</summary>
        private void GemSockets(ClassicSkin skin, float sx, float sy)
        {
            string hovered = null;
            for (int gem = 0; gem < GameClient.GemCount && gem < GemCentres.Length; gem++)
            {
                var r = new Rect(86 * sx, (GemCentres[gem] - 11.5f) * sy, 30 * sx, 23 * sy);
                var spell = _client.GemSpell(gem);
                if (spell == null)
                    continue;
                var colour = GUI.color;
                if (spell.Mana > _client.Mana || _client.Casting != null)
                    GUI.color = new Color(1f, 1f, 1f, 0.45f);
                GUI.DrawTextureWithTexCoords(r, skin.Gems, ClassicSkin.GemUv(spell.Icon));
                GUI.color = colour;
                if (GUI.Button(r, "", _hit))
                    _client.Cast(gem);
                var mouse = Event.current?.mousePosition;
                if (mouse is { } m && r.Contains(m))
                    hovered = $"{gem + 1}: {spell.Name} ({spell.Mana} mana)";
            }
            if (hovered != null)
            {
                var at = new Rect(124 * sx, 6 * sy, 300, 22);
                Fill(at, new Color(0f, 0f, 0f, 0.7f));
                GUI.Label(new Rect(at.x + 4, at.y + 2, at.width, at.height), hovered, _small);
            }
        }

        /// <summary>The six boxes: hot buttons (MAIN, with pages), combat, abilities or socials; right click edits a hot button.</summary>
        private void SixBoxes(float sx, float sy)
        {
            Rect R(Rect b) => new Rect(b.x * sx, b.y * sy, b.width * sx, b.height * sy);
            if (_boxMode == BoxMode.Main)
            {
                if (Hit(new Rect(35 * sx, 310 * sy, 14 * sx, 11 * sy))) _hotbar.Page--;
                if (Hit(new Rect(69 * sx, 310 * sy, 19 * sx, 11 * sy))) _hotbar.Page++;
                GUI.Label(new Rect(50 * sx, 308 * sy, 18 * sx, 14 * sy), (_hotbar.Page + 1).ToString(), _small);
            }
            var entries = BoxEntries();
            for (int i = 0; i < Boxes.Length; i++)
            {
                var r = R(Boxes[i]);
                var e = Event.current;
                if (_boxMode == BoxMode.Main && e != null && e.type == EventType.MouseDown && e.button == 1 && r.Contains(e.mousePosition))
                {
                    var button = _hotbar[i];
                    _editingHotButton = (_hotbar.Page, i);
                    _hotLabel = button?.Label ?? "";
                    _hotCommand = button?.Command ?? "";
                    e.Use();
                }
                if (i >= entries.Count || entries[i].Label == null)
                    continue;
                GUI.Label(new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4), entries[i].Label, _boxText);
                if (Hit(r))
                    entries[i].Run();
            }
        }

        private List<(string Label, System.Action Run)> BoxEntries()
        {
            var list = new List<(string, System.Action)>();
            switch (_boxMode)
            {
                case BoxMode.Main:
                    for (int i = 0; i < Hotbar.Slots; i++)
                    {
                        var b = _hotbar[i];
                        list.Add(b == null ? (null, null) : (b.Label, () => _client.ExecuteChat(b.Command)));
                    }
                    break;
                case BoxMode.Combat:
                    list.Add((_client.AutoAttacking ? "Stop" : "Melee attack", () => _client.ToggleAutoAttack()));
                    list.Add(("Consider", () => _client.Consider()));
                    foreach (int skill in _client.Skills?.Abilities.Where(s => s is 10 or 30 or 73).Take(4) ?? Enumerable.Empty<int>())
                        list.Add((SkillNames[skill], () => _client.UseAbility(skill)));
                    break;
                case BoxMode.Abilities:
                    foreach (int skill in _client.Skills?.Abilities.Where(s => !(s is 10 or 30 or 73)).Take(5) ?? Enumerable.Empty<int>())
                        list.Add((SkillNames[skill], () => _client.UseAbility(skill)));
                    list.Add(("Skills", () => ToggleWindow(HudLayout.Skills)));
                    break;
                case BoxMode.Socials:
                    list.Add(("Hail", () => _client.Hail()));
                    list.Add(("Wave", () => _client.ExecuteChat("/em waves.")));
                    list.Add(("Bow", () => _client.ExecuteChat("/em bows.")));
                    list.Add(("Cheer", () => _client.ExecuteChat("/em cheers!")));
                    list.Add(("Laugh", () => _client.ExecuteChat("/em laughs.")));
                    list.Add(("Thank", () => _client.ExecuteChat("/em thanks everyone.")));
                    break;
            }
            return list;
        }

        /// <summary>The members in the PARTY box, leader in yellow, with their health.</summary>
        private void PartyText(Rect r)
        {
            if (_client.Group is not { } group)
                return;
            float y = r.y;
            foreach (var name in group.Members)
            {
                var here = _client.Zone?.Entities.FirstOrDefault(e => e.Spawn.IsPlayer && e.Spawn.Name == name);
                var colour = GUI.color;
                if (name == group.Leader)
                    GUI.color = Color.yellow;
                GUI.Label(new Rect(r.x, y, r.width, 16), here != null ? $"{name} {here.HpPercent}%" : name, _small);
                GUI.color = colour;
                y += 15;
                if (y > r.yMax - 14)
                    break;
            }
        }

        /// <summary>The current target's bar: its health, its name in its consider colour.</summary>
        private void TargetBar(Rect r)
        {
            if (_client.TargetId is not int target || _client.Zone?.Get(target) is not { } t)
                return;
            Fill(new Rect(r.x, r.y, r.width * t.HpPercent / 100f, r.height), new Color(0.7f, 0.1f, 0.1f, 0.8f));
            var colour = GUI.color;
            GUI.color = ConColour(t.Con);
            GUI.Label(new Rect(r.x + 2, r.y - 3, r.width, r.height + 6), t.DisplayName, _small);
            GUI.color = colour;
        }

        /// <summary>Screens in the middle of the view (two side by side at most); how many were drawn.</summary>
        private int DrawViewWindows(Rect view)
        {
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
            int shown = Mathf.Min(main.Count, 2);
            float each = shown == 0 ? 0 : (view.width - 6 * (shown - 1)) / shown;
            for (int i = 0; i < shown; i++)
                ViewBox(new Rect(view.x + i * (each + 6), view.y, each, view.height - 24), main[i].Title, main[i].Draw);
            return shown;
        }
    }
}
