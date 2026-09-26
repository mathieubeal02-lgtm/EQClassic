using System.Linq;
using EQClassic.ClientCore;
using EQClassic.Shared.Zone;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// The interface's modules, each drawn inside the rectangle it is given: the classic frame gives
    /// them fixed places, the window layout gives them windows (<see cref="EQClassicClient"/>.Hud).
    /// </summary>
    public sealed partial class EQClassicClient
    {
        private static Texture2D _white;

        /// <summary>A filled bar (hit points) with a label over it.</summary>
        private static void DrawBar(Rect area, float fraction, Color fill, string label)
        {
            Fill(area, new Color(0f, 0f, 0f, 0.6f));
            Fill(new Rect(area.x, area.y, area.width * Mathf.Clamp(fraction, 0f, 1f), area.height), fill);
            if (label.Length > 0)
                GUI.Label(new Rect(area.x + 4, area.y - 3, area.width, area.height + 6), label);
        }

        /// <summary>A plain coloured rectangle.</summary>
        private static void Fill(Rect area, Color colour)
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            var before = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(area, _white);
            GUI.color = before;
        }

        /// <summary>Consider colours as the Trilogy client shows them (white until considered).</summary>
        public static Color ConColour(ConColor? con) => con switch
        {
            ConColor.Green => Color.green,
            ConColor.Blue => new Color(0.4f, 0.6f, 1f),
            ConColor.Yellow => Color.yellow,
            ConColor.Red => Color.red,
            _ => Color.white,
        };

        /// <summary>The Trilogy client's chat colours.</summary>
        private static Color ChatColour(ChatKind kind)
        {
            switch (kind)
            {
                case ChatKind.Tell: return new Color(0.9f, 0.4f, 0.9f);
                case ChatKind.Group: return new Color(0.4f, 0.8f, 1f);
                case ChatKind.Shout: return new Color(1f, 0.35f, 0.35f);
                case ChatKind.OutOfCharacter:
                case ChatKind.Auction: return new Color(0.4f, 1f, 0.4f);
                default: return Color.white;
            }
        }

        /// <summary>The chat's colours on the parchment of the classic frame: dark inks.</summary>
        private static Color InkColour(ChatKind kind)
        {
            switch (kind)
            {
                case ChatKind.Tell: return new Color(0.45f, 0f, 0.45f);
                case ChatKind.Group: return new Color(0f, 0.2f, 0.55f);
                case ChatKind.Shout: return new Color(0.6f, 0f, 0f);
                case ChatKind.OutOfCharacter:
                case ChatKind.Auction: return new Color(0f, 0.38f, 0f);
                default: return new Color(0.12f, 0.08f, 0.04f);
            }
        }

        private static int Number(string text) => int.TryParse(text, out int n) && n > 0 ? n : 0;

        private static readonly string[] SlotNames =
        {
            "Charm", "Ear", "Head", "Face", "Ear", "Neck", "Shoulders", "Arms", "Back", "Wrist", "Wrist", "Range", "Hands",
            "Primary", "Secondary", "Finger", "Finger", "Chest", "Legs", "Feet", "Waist", "Ammo",
        };

        private string CharacterName => _client.Zone?.Get(_client.Zone.YourEntityId)?.Spawn.Name ?? "";

        // ---- Player, target, group, effects, casting ----

        /// <summary>Name, level, and the hit point, mana, stamina and experience bars.</summary>
        private void PlayerPanel(Rect r)
        {
            float w = r.width;
            string status = (_client.AutoAttacking ? "  attacking" : "") + (_client.Sitting ? "  sitting" : "")
                + (_client.Stamina is { Hunger: 0 } ? "  hungry" : "") + (_client.Stamina is { Thirst: 0 } ? "  thirsty" : "");
            GUI.Label(new Rect(r.x, r.y, w, 20), CharacterName + (_client.Experience is { } lvl ? $"  (level {lvl.Level})" : "") + status);
            float y = r.y + 22;
            DrawBar(new Rect(r.x, y, w, 14), _client.MaxHp > 0 ? (float)_client.Hp / _client.MaxHp : 0f, new Color(0.8f, 0.1f, 0.1f), $"{_client.Hp} / {_client.MaxHp}");
            y += 17;
            if (_client.MaxMana > 0)
            {
                DrawBar(new Rect(r.x, y, w, 12), (float)_client.Mana / _client.MaxMana, new Color(0.2f, 0.3f, 0.9f), $"{_client.Mana} / {_client.MaxMana}");
                y += 15;
            }
            if (_client.Stamina is { } stamina)
            {
                DrawBar(new Rect(r.x, y, w, 8), (100 - stamina.Fatigue) / 100f, new Color(0.9f, 0.8f, 0.1f), "");
                y += 11;
            }
            if (_client.Experience is { } xp)
                DrawBar(new Rect(r.x, y, w, 6), xp.Fraction, new Color(0.7f, 0.7f, 0.7f), "");
        }

        /// <summary>The current target: its name in its consider colour and its health.</summary>
        private void TargetPanel(Rect r)
        {
            if (_client.TargetId is not int target || _client.Zone?.Get(target) is not { } t)
            {
                GUI.Label(new Rect(r.x, r.y, r.width, 20), "(no target)");
                return;
            }
            var colour = GUI.color;
            GUI.color = ConColour(t.Con);
            GUI.Label(new Rect(r.x, r.y, r.width, 20), t.DisplayName);
            GUI.color = colour;
            DrawBar(new Rect(r.x, r.y + 22, r.width, 14), t.HpPercent / 100f, new Color(0.8f, 0.1f, 0.1f), t.HpPercent + "%");
        }

        /// <summary>The group: the members (leader in yellow) and their health when they are in this zone.</summary>
        private void GroupPanel(Rect r)
        {
            if (_client.Group is not { } group || group.Members.Count == 0)
            {
                GUI.Label(new Rect(r.x, r.y, r.width, 20), "(no group)");
                return;
            }
            float y = r.y;
            foreach (var name in group.Members)
            {
                var here = _client.Zone?.Entities.FirstOrDefault(e => e.Spawn.IsPlayer && e.Spawn.Name == name);
                var colour = GUI.color;
                if (name == group.Leader)
                    GUI.color = Color.yellow;
                GUI.Label(new Rect(r.x, y, r.width, 18), name);
                GUI.color = colour;
                DrawBar(new Rect(r.x, y + 18, r.width, 8), (here?.HpPercent ?? 0) / 100f, new Color(0.8f, 0.1f, 0.1f), "");
                y += 30;
                if (y > r.yMax - 26)
                    break;
            }
        }

        /// <summary>Spells lasting on you with the time left (a tic is 6 s), detrimental ones in red.</summary>
        private void BuffsPanel(Rect r)
        {
            if (_client.Buffs == null || _client.Buffs.Buffs.Count == 0)
            {
                GUI.Label(new Rect(r.x, r.y, r.width, 20), "(no effects)");
                return;
            }
            float y = r.y;
            foreach (var buff in _client.Buffs.Buffs)
            {
                int seconds = buff.TicsLeft * 6;
                string left = buff.TicsLeft >= 32767 ? "" : $"  {seconds / 60}:{seconds % 60:00}";
                var colour = GUI.color;
                GUI.color = buff.Beneficial ? Color.white : new Color(1f, 0.5f, 0.5f);
                GUI.Label(new Rect(r.x, y, r.width, 20), buff.Name + left);
                GUI.color = colour;
                y += 18;
                if (y > r.yMax - 18)
                    break;
            }
        }

        /// <summary>The casting bar.</summary>
        private void CastingPanel(Rect r)
        {
            if (_client.Casting is { } casting)
                DrawBar(new Rect(r.x, r.y, r.width, Mathf.Min(18f, r.height)), _client.CastProgress, new Color(0.7f, 0.3f, 0.9f), casting.SpellName);
        }

        // ---- Spells, hot buttons, actions ----

        /// <summary>The spell gems (keys 1-8, or click): memorised spells and their mana, dimmed while casting or short of mana.</summary>
        private void GemsPanel(Rect r)
        {
            float h = Mathf.Min(26f, r.height / GameClient.GemCount);
            for (int gem = 0; gem < GameClient.GemCount; gem++)
            {
                var spell = _client.GemSpell(gem);
                var colour = GUI.color;
                if (spell != null && (spell.Mana > _client.Mana || _client.Casting != null))
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                if (GUI.Button(new Rect(r.x, r.y + h * gem, r.width, h - 3), spell == null ? $"{gem + 1}  -" : $"{gem + 1}  {spell.Name} ({spell.Mana})") && spell != null)
                    _client.Cast(gem);
                GUI.color = colour;
            }
        }

        private Hotbar _hotbar = Hotbar.Default();
        private (int Page, int Slot)? _editingHotButton;
        private string _hotLabel = "", _hotCommand = "";

        private void LoadHotbar() => _hotbar = Hotbar.Load(PlayerPrefs.GetString("eqc.hotbar." + CharacterName, ""));

        private void SaveHotbar() => PlayerPrefs.SetString("eqc.hotbar." + CharacterName, _hotbar.Save());

        /// <summary>
        /// The hot buttons: six per page, the arrows turn the pages. A click runs the button's command
        /// line; a right click edits it (label and command: /attack, /cast 3, /kick, a say...).
        /// </summary>
        private void HotbarPanel(Rect r, int columns)
        {
            int rows = (Hotbar.Slots + columns - 1) / columns;
            float arrows = 22f;
            float bw = (r.width - 4 * (columns - 1)) / columns, bh = (r.height - arrows - 4 * rows) / rows;
            for (int i = 0; i < Hotbar.Slots; i++)
            {
                var cell = new Rect(r.x + (i % columns) * (bw + 4), r.y + (i / columns) * (bh + 4), bw, bh);
                var button = _hotbar[i];
                var e = Event.current;
                if (e != null && e.type == EventType.MouseDown && e.button == 1 && cell.Contains(e.mousePosition))
                {
                    _editingHotButton = (_hotbar.Page, i);
                    _hotLabel = button?.Label ?? "";
                    _hotCommand = button?.Command ?? "";
                    e.Use();
                }
                if (GUI.Button(cell, button?.Label ?? "") && button != null)
                    _client.ExecuteChat(button.Command);
            }
            float ay = r.yMax - arrows;
            if (GUI.Button(new Rect(r.x, ay, 30, arrows), "<"))
                _hotbar.Page--;
            GUI.Label(new Rect(r.x + r.width / 2 - 10, ay, 30, arrows), (_hotbar.Page + 1).ToString());
            if (GUI.Button(new Rect(r.xMax - 30, ay, 30, arrows), ">"))
                _hotbar.Page++;
        }

        /// <summary>The hot button editor (right click on a button).</summary>
        private void HotButtonEditor()
        {
            if (_editingHotButton is not { } editing)
                return;
            var r = new Rect(Screen.width / 2 - 200, Screen.height / 2 - 80, 400, 150);
            GUI.Box(r, $"Hot button {editing.Slot + 1}, page {editing.Page + 1}");
            GUILayout.BeginArea(new Rect(r.x + 10, r.y + 24, r.width - 20, r.height - 30));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Label", GUILayout.Width(70));
            _hotLabel = GUILayout.TextField(_hotLabel);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Command", GUILayout.Width(70));
            _hotCommand = GUILayout.TextField(_hotCommand);
            GUILayout.EndHorizontal();
            GUILayout.Label("e.g. /attack, /cast 1, /kick, /sit, /hail, or a line to say");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
            {
                string label = _hotLabel.Trim().Length > 0 ? _hotLabel.Trim() : _hotCommand.Trim();
                _hotbar.Set(editing.Page, editing.Slot, new HotButton(label, _hotCommand.Trim()));
                SaveHotbar();
                _editingHotButton = null;
            }
            if (GUILayout.Button("Clear"))
            {
                _hotbar.Set(editing.Page, editing.Slot, null);
                SaveHotbar();
                _editingHotButton = null;
            }
            if (GUILayout.Button("Cancel"))
                _editingHotButton = null;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static readonly string[] SkillNames =
        {
            "1H Blunt", "1H Slashing", "2H Blunt", "2H Slashing", "Abjuration", "Alteration", "Apply Poison", "Archery", "Backstab", "Bind Wound",
            "Bash", "Block", "Brass Instruments", "Channeling", "Conjuration", "Defense", "Disarm", "Disarm Traps", "Divination", "Dodge",
            "Double Attack", "Dragon Punch", "Dual Wield", "Eagle Strike", "Evocation", "Feign Death", "Flying Kick", "Forage", "Hand to Hand", "Hide",
            "Kick", "Meditate", "Mend", "Offense", "Parry", "Pick Lock", "Piercing", "Riposte", "Round Kick", "Safe Fall",
            "Sense Heading", "Singing", "Sneak", "Specialize Abjure", "Specialize Alteration", "Specialize Conjuration", "Specialize Divination", "Specialize Evocation", "Pick Pockets", "Stringed Instruments",
            "Swimming", "Throwing", "Tiger Claw", "Tracking", "Wind Instruments", "Fishing", "Make Poison", "Tinkering", "Research", "Alchemy",
            "Baking", "Tailoring", "Sense Traps", "Blacksmithing", "Fletching", "Brewing", "Alcohol Tolerance", "Begging", "Jewelry Making", "Pottery",
            "Percussion Instruments", "Intimidation", "Berserking", "Taunt",
        };

        /// <summary>The class's abilities (kick, bash, taunt...), one button each.</summary>
        private void AbilitiesPanel(Rect r)
        {
            if (_client.Skills == null || _client.Skills.Abilities.Count == 0)
            {
                GUI.Label(new Rect(r.x, r.y, r.width, 20), "(no abilities)");
                return;
            }
            int i = 0;
            float bw = (r.width - 4) / 2;
            foreach (int skill in _client.Skills.Abilities)
            {
                if (GUI.Button(new Rect(r.x + (i % 2) * (bw + 4), r.y + (i / 2) * 26, bw, 22), SkillNames[skill]))
                    _client.UseAbility(skill);
                i++;
            }
        }

        /// <summary>The Trilogy client's action buttons: who, invite, disband, camp, sit, walk (and attack, loot, hail).</summary>
        private void ActionsPanel(Rect r)
        {
            (string Label, System.Action Run)[] actions =
            {
                (_client.AutoAttacking ? "Stop attack" : "Attack", () => _client.ToggleAutoAttack()),
                ("Consider", () => _client.Consider()),
                ("Hail", () => _client.Hail()),
                ("Loot", () => _client.Loot()),
                ("Who", () => _client.ExecuteChat("/who")),
                ("Invite", () => _client.ExecuteChat("/invite")),
                ("Disband", () => _client.ExecuteChat("/disband")),
                ("Camp", () => _client.ExecuteChat("/camp")),
                (_client.Sitting ? "Stand" : "Sit", () => _client.ToggleSit()),
                (_presenter.WalkToggle ? "Run" : "Walk", () => _presenter.WalkToggle = !_presenter.WalkToggle),
            };
            float bw = (r.width - 4) / 2, bh = Mathf.Min(24f, (r.height - 4 * 4) / 5);
            for (int i = 0; i < actions.Length; i++)
                if (GUI.Button(new Rect(r.x + (i % 2) * (bw + 4), r.y + (i / 2) * (bh + 4), bw, bh), actions[i].Label))
                    actions[i].Run();
        }

        /// <summary>The keys and commands.</summary>
        private void HelpPanel(Rect r)
        {
            GUILayout.BeginArea(r);
            GUILayout.Label($"Keys: {_client.Keys.Help}, R autorun, Shift walk, Space jump, X sit, right mouse look, wheel zoom, F9 view.");
            GUILayout.Label("U door / merchant / banker, Tab target, T face, C consider, H hail, F attack, L loot, I inventory, B spell book, K skills, 1-8 cast.");
            GUILayout.Label("F12 switches the interface: classic frame / windows. Right click a hot button to change it.");
            foreach (var line in Chat.HelpLines)
                GUILayout.Label(line);
            GUILayout.EndArea();
        }

        // ---- Chat ----

        private Rect _chatScreenRect;
        private int _chatLinesShown = 12;

        /// <summary>
        /// The chat: the latest lines in their channel's colour (wheel over it, the arrows, or Page Up/Down
        /// while typing scroll back), and the input line while it is open (arrows recall what was typed).
        /// <paramref name="screen"/> is where it is on the screen, for the wheel.
        /// </summary>
        private void ChatPanel(Rect r, Rect screen, bool parchment = false)
        {
            _chatScreenRect = screen;
            float input = _chatOpen ? 26f : 0f;
            _chatLinesShown = Mathf.Max(1, (int)((r.height - input) / 18f));
            var lines = _chat.Visible(_chatLinesShown);
            float bottom = r.yMax - input;
            float textWidth = parchment ? r.width : r.width - 26;
            var colour = GUI.color;
            for (int i = 0; i < lines.Count; i++)
            {
                var kind = ChatLog.Kind(lines[i]);
                GUI.color = parchment ? InkColour(kind) : ChatColour(kind);
                var at = new Rect(r.x, bottom - 18 * (lines.Count - i), textWidth, 20);
                if (parchment)
                    GUI.Label(at, lines[i], _parchment);
                else
                    GUI.Label(at, lines[i]);
            }
            GUI.color = colour;
            if (!parchment && GUI.Button(new Rect(r.xMax - 22, r.y, 22, 22), "^"))
                _chat.ScrollBy(_chatLinesShown - 1, _chatLinesShown);
            if (!parchment && GUI.Button(new Rect(r.xMax - 22, bottom - 22, 22, 22), "v"))
                _chat.ScrollBy(1 - _chatLinesShown, _chatLinesShown);
            if (_chat.Scroll > 0)
                GUI.Label(new Rect(r.xMax - 250, r.y, 220, 20), $"(back {_chat.Scroll} lines)");
            if (!_chatOpen)
                return;
            // Enter and Escape are taken before the text field is drawn: a focused IMGUI text field keeps
            // them to itself (on Windows the event is used; under Linux Input.GetKeyDown stays false).
            var key = Event.current;
            if (key != null && key.type == EventType.KeyDown && Time.frameCount != _chatOpenedFrame)
            {
                if (key.keyCode == KeyCode.Return || key.keyCode == KeyCode.KeypadEnter)
                {
                    key.Use();
                    SubmitChat();
                    return;
                }
                if (key.keyCode == KeyCode.Escape)
                {
                    key.Use();
                    CloseChat();
                    return;
                }
            }
            GUI.SetNextControlName("chat");
            _chatLine = GUI.TextField(new Rect(r.x, r.yMax - 24, r.width, 22), _chatLine);
            GUI.FocusControl("chat");
            var e = Event.current;
            if (e.type != EventType.KeyDown)
                return;
            if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.DownArrow)
            {
                _chatLine = e.keyCode == KeyCode.UpArrow ? _chat.Previous() : _chat.Next();
                e.Use();
            }
            else if (e.keyCode == KeyCode.PageUp || e.keyCode == KeyCode.PageDown)
            {
                _chat.ScrollBy(e.keyCode == KeyCode.PageUp ? _chatLinesShown - 1 : 1 - _chatLinesShown, _chatLinesShown);
                e.Use();
            }
        }

        /// <summary>Enter: the line goes to the game (or the interface: /ui), the chat closes.</summary>
        private void SubmitChat()
        {
            string line = _chatLine;
            CloseChat();
            if (line.Trim().Length == 0)
                return;
            _chat.Typed(line);
            _chat.ScrollToBottom();
            if (!InterfaceCommand(line))
                _client.ExecuteChat(line);
        }

        private void CloseChat()
        {
            _chatOpen = false;
            _chatLine = "";
            _chatClosedFrame = Time.frameCount;
        }

        // ---- Inventory, bank, loot, merchant, trade, spell book, skills ----

        private int? _heldSlot;
        private Vector2 _inventoryScroll;

        /// <summary>
        /// The inventory (I): every worn slot and the eight general slots with their bags, money. Click an
        /// item to pick it up, then the slot to put it in (the server checks it fits, and swaps).
        /// </summary>
        private void InventoryPanel(Rect r)
        {
            var inventory = _client.Inventory;
            GUILayout.BeginArea(r);
            GUILayout.Label(_heldSlot is int held && inventory != null ? $"Holding {HeldName(inventory, held)}" : "Click an item, then where it goes");
            if (inventory == null)
            {
                GUILayout.Label("(not received yet)");
                GUILayout.EndArea();
                return;
            }
            _inventoryScroll = GUILayout.BeginScrollView(_inventoryScroll);
            for (int slot = 0; slot < inventory.Slots.Count; slot++)
            {
                var item = inventory.Slots[slot];
                string where = slot < SlotNames.Length ? SlotNames[slot] : $"General {slot - SlotNames.Length + 1}";
                DrawInventoryRow(slot, where, item);
                // A bag in a general slot: its cells below it (slots 250 + bag × 10 + cell).
                for (int cell = 0; slot >= SlotNames.Length && cell < item.BagSlots && cell < PlayerInventory.BagCells; cell++)
                {
                    int bagSlot = PlayerInventory.BagSlotBase + (slot - SlotNames.Length) * PlayerInventory.BagCells + cell;
                    DrawInventoryRow(bagSlot, $"    {cell + 1}", ItemIn(inventory, bagSlot));
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Label($"{inventory.Platinum} pp  {inventory.Gold} gp  {inventory.Silver} sp  {inventory.Copper} cp");
            GUILayout.EndArea();
        }

        private Vector2 _bankScroll;
        private string _bankP = "0", _bankG = "0", _bankS = "0", _bankC = "0";

        /// <summary>The bank (U on a banker): its 8 slots and their bags, moved like the inventory's, and money in or out.</summary>
        private void BankPanel(Rect r, BankContents bank)
        {
            GUILayout.BeginArea(r);
            _bankScroll = GUILayout.BeginScrollView(_bankScroll);
            for (int i = 0; i < bank.Slots.Count; i++)
            {
                var item = bank.Slots[i];
                DrawInventoryRow(BankContents.BankBase + i, $"Bank {i + 1}", item);
                for (int cell = 0; cell < item.BagSlots && cell < PlayerInventory.BagCells; cell++)
                {
                    int index = i * PlayerInventory.BagCells + cell;
                    DrawInventoryRow(BankContents.BankBagBase + index, $"    {cell + 1}", index < bank.Bags.Count ? bank.Bags[index] : new ItemView(0, "", 0));
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Label($"In the bank: {bank.Platinum}p {bank.Gold}g {bank.Silver}s {bank.Copper}c");
            GUILayout.BeginHorizontal();
            _bankP = GUILayout.TextField(_bankP);
            _bankG = GUILayout.TextField(_bankG);
            _bankS = GUILayout.TextField(_bankS);
            _bankC = GUILayout.TextField(_bankC);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Deposit"))
                _client.BankMoney(Number(_bankP), Number(_bankG), Number(_bankS), Number(_bankC), 0, 0, 0, 0);
            if (GUILayout.Button("Withdraw"))
                _client.BankMoney(0, 0, 0, 0, Number(_bankP), Number(_bankG), Number(_bankS), Number(_bankC));
            if (GUILayout.Button("Done"))
            {
                _client.CloseBank();
                _layout.SetOpen(HudLayout.Inventory, false);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>The name of the item picked up, which may be in the bank.</summary>
        private string HeldName(PlayerInventory inventory, int slot)
        {
            if (_client.Bank is { } bank && slot >= BankContents.BankBase)
            {
                if (slot < BankContents.BankBase + bank.Slots.Count)
                    return bank.Slots[slot - BankContents.BankBase].Name;
                int cell = slot - BankContents.BankBagBase;
                return cell >= 0 && cell < bank.Bags.Count ? bank.Bags[cell].Name : "";
            }
            return ItemIn(inventory, slot).Name;
        }

        private static ItemView ItemIn(PlayerInventory inventory, int slot)
        {
            if (slot < inventory.Slots.Count)
                return inventory.Slots[slot];
            int cell = slot - PlayerInventory.BagSlotBase;
            return cell >= 0 && cell < inventory.BagCellsOrEmpty.Count ? inventory.BagCellsOrEmpty[cell] : new ItemView(0, "", 0);
        }

        /// <summary>One inventory line: Scribe, Eat/Drink, Sell and Offer when they apply; a click picks the item up, another puts it down.</summary>
        private void DrawInventoryRow(int slot, string where, ItemView item)
        {
            string what = item.ItemId == 0 ? "-" : item.Charges > 1 ? $"{item.Name} ({item.Charges})" : item.Name;
            GUILayout.BeginHorizontal();
            if (item.Name.StartsWith("Spell: ") && GUILayout.Button("Scribe", GUILayout.Width(60)))
                _client.Scribe(slot);
            if ((item.ItemType == 14 || item.ItemType == 15) && GUILayout.Button(item.ItemType == 14 ? "Eat" : "Drink", GUILayout.Width(60)))
                _client.Consume(slot);
            if (_client.Merchant != null && item.ItemId != 0
                && GUILayout.Button("Sell " + MerchantRules.Coins(MerchantRules.SellPrice(item.Price)), GUILayout.Width(90)))
                _client.Sell(slot);
            if (_client.Trade is { } trading && item.ItemId != 0
                && GUILayout.Button(trading.Mine.Slots.Contains(slot) ? "Take back" : "Offer", GUILayout.Width(80)))
                _client.OfferItem(slot);
            bool clicked = GUILayout.Button($"{where}: {what}");
            GUILayout.EndHorizontal();
            if (!clicked)
                return;
            if (_heldSlot is int from)
            {
                if (from != slot)
                    _client.MoveItem(from, slot);
                _heldSlot = null;
            }
            else if (item.ItemId != 0)
            {
                _heldSlot = slot;
            }
        }

        /// <summary>What is left on the corpse, one Take button each, and Done.</summary>
        private void LootPanel(Rect r)
        {
            GUILayout.BeginArea(r);
            var items = _client.LootItems;
            if (items.Count == 0)
                GUILayout.Label("(nothing left)");
            for (int i = 0; i < items.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(items[i].Charges > 1 ? $"{items[i].Name} ({items[i].Charges})" : items[i].Name);
                if (GUILayout.Button("Take", GUILayout.Width(60)))
                    _client.TakeLoot(i);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Done"))
                _client.EndLoot();
            GUILayout.EndArea();
        }

        private Vector2 _merchantScroll;

        /// <summary>A merchant's goods at 2.5 times their value (click to buy); the inventory shows Sell buttons.</summary>
        private void MerchantPanel(Rect r, MerchantGoods merchant)
        {
            GUILayout.BeginArea(r);
            _merchantScroll = GUILayout.BeginScrollView(_merchantScroll);
            for (int i = 0; i < merchant.Items.Count; i++)
            {
                var item = merchant.Items[i];
                if (GUILayout.Button($"{item.Name}  {MerchantRules.Coins(MerchantRules.BuyPrice(item.Price))}"))
                    _client.Buy(i);
            }
            GUILayout.EndScrollView();
            if (_client.Inventory is { } money)
                GUILayout.Label($"You have {money.Platinum}p {money.Gold}g {money.Silver}s {money.Copper}c");
            if (GUILayout.Button("Done"))
            {
                _client.CloseMerchant();
                _layout.SetOpen(HudLayout.Inventory, false);
            }
            GUILayout.EndArea();
        }

        private string _tradePlatinum = "0", _tradeGold = "0", _tradeSilver = "0", _tradeCopper = "0";

        /// <summary>Both offers, money, Accept and Cancel; items are offered from the inventory.</summary>
        private void TradePanel(Rect r, TradeWindow trade)
        {
            GUILayout.BeginArea(r);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label(trade.Mine.Accepted ? "You (accepted)" : "You");
            foreach (var item in trade.Mine.Items)
                GUILayout.Label(item.Name);
            GUILayout.Label(MerchantRules.Coins(trade.Mine.Copper));
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            GUILayout.Label(trade.Theirs.Accepted ? $"{trade.Partner} (accepted)" : trade.Partner);
            foreach (var item in trade.Theirs.Items)
                GUILayout.Label(item.Name);
            GUILayout.Label(MerchantRules.Coins(trade.Theirs.Copper));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("p/g/s/c", GUILayout.Width(50));
            _tradePlatinum = GUILayout.TextField(_tradePlatinum);
            _tradeGold = GUILayout.TextField(_tradeGold);
            _tradeSilver = GUILayout.TextField(_tradeSilver);
            _tradeCopper = GUILayout.TextField(_tradeCopper);
            if (GUILayout.Button("Offer money", GUILayout.Width(100)))
                _client.OfferCoins(Number(_tradePlatinum), Number(_tradeGold), Number(_tradeSilver), Number(_tradeCopper));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Accept"))
                _client.AcceptTrade();
            if (GUILayout.Button("Cancel"))
                _client.CancelTrade();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private Vector2 _bookScroll;

        /// <summary>The spell book (B): every scribed spell by level; a gem button memorises it there.</summary>
        private void BookPanel(Rect r)
        {
            GUILayout.BeginArea(r);
            var book = _client.SpellBook;
            if (book == null || book.Spells.Count == 0)
                GUILayout.Label(book == null ? "(not received yet)" : "No spells scribed. Scribe a scroll from the inventory (I).");
            else
            {
                GUILayout.Label("Click a gem number to memorize the spell there.");
                _bookScroll = GUILayout.BeginScrollView(_bookScroll);
                foreach (var spell in book.Spells)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{spell.Level,2}  {spell.Name}  - {spell.Mana} mana, {spell.CastMs / 1000f:0.#} s", GUILayout.Width(Mathf.Max(200f, r.width - 8 * 26 - 30)));
                    for (int gem = 0; gem < GameClient.GemCount; gem++)
                        if (GUILayout.Button((gem + 1).ToString(), GUILayout.Width(22)))
                            _client.Memorize(gem, spell.SpellId);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        /// <summary>The skills (K): every skill the character has, with its value.</summary>
        private void SkillsPanel(Rect r)
        {
            GUILayout.BeginArea(r);
            _skillsScroll = GUILayout.BeginScrollView(_skillsScroll);
            var values = _client.Skills?.Values;
            for (int i = 0; values != null && i < values.Count && i < SkillNames.Length; i++)
                if (values[i] > 0)
                    GUILayout.Label($"{SkillNames[i]}: {values[i]}");
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
