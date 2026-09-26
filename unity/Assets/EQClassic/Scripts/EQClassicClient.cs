using System.Linq;
using EQClassic.ClientCore;
using EQClassic.Shared.Characters;
using EQClassic.Shared.Zone;
using UnityEngine;

namespace EQClassic.Unity
{
    /// <summary>
    /// Thin Unity layer over <see cref="GameClient"/> (EQClassic.ClientCore.dll): drives it every
    /// frame and draws the out-of-zone screens with IMGUI (login, server select, character select).
    /// In zone, <see cref="ZonePresenter"/> draws the world.
    /// </summary>
    public sealed class EQClassicClient : MonoBehaviour
    {
        private GameClient _client;
        private ZonePresenter _presenter;
        private string _host = "127.0.0.1";
        private string _port = "5999";
        private string _fingerprint = "";
        private string _user = "";
        private string _password = "";
        private string _newName = "";
        private CharacterBuilder _builder;
        private bool _creating;
        private readonly System.Collections.Generic.List<string> _messages = new System.Collections.Generic.List<string>();
        private bool _chatOpen;
        private bool _inventoryOpen;
        private bool _bookOpen;
        private bool _skillsOpen;
        private Vector2 _skillsScroll;
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
        private Vector2 _bookScroll;

        private static readonly string[] SlotNames =
        {
            "Charm", "Ear", "Head", "Face", "Ear", "Neck", "Shoulders", "Arms", "Back", "Wrist", "Wrist", "Range", "Hands",
            "Primary", "Secondary", "Finger", "Finger", "Chest", "Legs", "Feet", "Waist", "Ammo",
        };
        private string _chatLine = "";
        private int _chatClosedFrame = -1;
        private const int ChatLines = 12;

        private void Awake()
        {
            _host = PlayerPrefs.GetString("eqc.host", _host);
            _port = PlayerPrefs.GetString("eqc.port", _port);
            _fingerprint = PlayerPrefs.GetString("eqc.fingerprint", "");
            _user = PlayerPrefs.GetString("eqc.user", "");
            ApplyCommandLine(System.Environment.GetCommandLineArgs());
            _presenter = gameObject.AddComponent<ZonePresenter>();
        }

        /// <summary>
        /// -host, -port, -fingerprint and -user prefill the screens, for a shortcut or a script
        /// (EQClassic.x86_64 -host 192.168.1.2 -port 5999 -user bob). The password is never taken
        /// from the command line: other local users can read it there.
        /// </summary>
        private void ApplyCommandLine(string[] args)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-host": _host = args[++i]; break;
                    case "-port": _port = args[++i]; break;
                    case "-fingerprint": _fingerprint = args[++i]; break;
                    case "-user": _user = args[++i]; break;
                }
            }
        }

        private void Update()
        {
            if (_client == null)
                return;
            _client.Update(Time.deltaTime);
            if (_client.State == GameState.InZone)
            {
                _presenter.InputEnabled = !_chatOpen;
                if (!_chatOpen && Time.frameCount != _chatClosedFrame
                    && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Slash)))
                {
                    _chatOpen = true;
                    _chatLine = Input.GetKeyDown(KeyCode.Slash) ? "/" : "";
                }
                if (_chatOpen)
                {
                    _presenter.Present(_client, Time.deltaTime);
                    return; // typing: no game keys
                }
                if (Input.GetKeyDown(KeyCode.U) && _client.Use() == null)
                    AddMessage("There is nothing here to use.");
                if (Input.GetKeyDown(KeyCode.Tab) && _client.TargetNearest() == null)
                    AddMessage("There is no one near to target.");
                if (Input.GetKeyDown(KeyCode.F))
                    _client.ToggleAutoAttack();
                if (Input.GetKeyDown(KeyCode.T))
                    _client.FaceTarget();
                if (Input.GetKeyDown(KeyCode.C))
                    _client.Consider();
                if (Input.GetKeyDown(KeyCode.X))
                    _client.ToggleSit();
                if (Input.GetKeyDown(KeyCode.L))
                    _client.Loot();
                if (Input.GetKeyDown(KeyCode.I))
                    _inventoryOpen = !_inventoryOpen;
                if (Input.GetKeyDown(KeyCode.B))
                    _bookOpen = !_bookOpen;
                if (Input.GetKeyDown(KeyCode.K))
                    _skillsOpen = !_skillsOpen;
                for (int gem = 0; gem < GameClient.GemCount; gem++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + gem))
                        _client.Cast(gem);
                if (Input.GetKeyDown(KeyCode.Escape))
                    _client.SetTarget(null);
                _presenter.Present(_client, Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            _client?.Dispose();
        }

        private void AddMessage(string text)
        {
            _messages.Add(text);
            if (_messages.Count > 100)
                _messages.RemoveAt(0);
        }

        /// <summary>The creation screen: race, gender, class, deity, city among the server's combinations, points, name.</summary>
        private void DrawCreation()
        {
            var b = _builder;
            GUILayout.Label("Race");
            var races = new System.Collections.Generic.List<int>(b.Races);
            int race = GUILayout.SelectionGrid(races.IndexOf(b.Race), races.ConvertAll(CreationRules.RaceName).ToArray(), 3);
            if (race >= 0 && races[race] != b.Race)
                b.SelectRace(races[race]);
            b.Gender = GUILayout.Toolbar(b.Gender, new[] { "Male", "Female" });
            GUILayout.Label("Class");
            var classes = new System.Collections.Generic.List<int>(b.Classes);
            int cls = GUILayout.SelectionGrid(classes.IndexOf(b.Class), classes.ConvertAll(CreationRules.ClassName).ToArray(), 3);
            if (cls >= 0 && classes[cls] != b.Class)
                b.SelectClass(classes[cls]);
            GUILayout.Label("Deity");
            var deities = new System.Collections.Generic.List<int>(b.Deities);
            int deity = GUILayout.SelectionGrid(deities.IndexOf(b.Deity), deities.ConvertAll(CreationRules.DeityName).ToArray(), 3);
            if (deity >= 0 && deities[deity] != b.Deity)
                b.SelectDeity(deities[deity]);
            GUILayout.Label("City");
            var zones = new System.Collections.Generic.List<string>(b.Zones);
            int zone = GUILayout.SelectionGrid(zones.IndexOf(b.Zone), zones.ToArray(), 3);
            if (zone >= 0 && zones[zone] != b.Zone)
                b.SelectZone(zones[zone]);
            GUILayout.Label($"Statistics ({b.PointsLeft} points left)");
            for (int i = 0; i < 7; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{CharacterBuilder.StatNames[i]}  {b.Stat(i)}");
                if (GUILayout.Button("-"))
                    b.RemovePoint(i);
                if (GUILayout.Button("+"))
                    b.AddPoint(i);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Name");
            _newName = GUILayout.TextField(_newName);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(b.Complete ? "Create" : "Create (spend every point first)") && b.Complete)
            {
                _client.CreateCharacter(b.Request(_newName));
                _builder = null;
                _creating = false;
            }
            if (GUILayout.Button("Cancel"))
            {
                _builder = null;
                _creating = false;
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>The loot window: what is left on the corpse, one Take button each, and Done.</summary>
        private void DrawLoot(int corpse)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 320, 90, 300, 320), GUI.skin.box);
            GUILayout.Label(_client.Zone?.Get(corpse)?.DisplayName ?? "Corpse");
            var items = _client.LootItems;
            if (items.Count == 0)
                GUILayout.Label("(nothing left)");
            for (int i = 0; i < items.Count; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(items[i].Charges > 1 ? $"{items[i].Name} ({items[i].Charges})" : items[i].Name);
                if (GUILayout.Button("Take"))
                    _client.TakeLoot(i);
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Done"))
                _client.EndLoot();
            GUILayout.EndArea();
        }

        private int? _heldSlot;

        /// <summary>
        /// The inventory window (I): every worn slot and the eight general slots, money. Click an item to
        /// pick it up, then the slot to put it in (the server checks it fits, and swaps).
        /// </summary>
        private void DrawInventory()
        {
            var inventory = _client.Inventory;
            GUILayout.BeginArea(new Rect(Screen.width - 640, 60, 300, Screen.height - 120), GUI.skin.box);
            GUILayout.Label(_heldSlot is int held && inventory != null ? $"Inventory - holding {inventory.Slots[held].Name}" : "Inventory");
            if (inventory == null)
            {
                GUILayout.Label("(not received yet)");
                GUILayout.EndArea();
                return;
            }
            for (int slot = 0; slot < inventory.Slots.Count; slot++)
            {
                var item = inventory.Slots[slot];
                string where = slot < SlotNames.Length ? SlotNames[slot] : $"General {slot - SlotNames.Length + 1}";
                string what = item.ItemId == 0 ? "-" : item.Charges > 1 ? $"{item.Name} ({item.Charges})" : item.Name;
                GUILayout.BeginHorizontal();
                if (item.Name.StartsWith("Spell: ") && GUILayout.Button("Scribe", GUILayout.Width(60)))
                    _client.Scribe(slot);
                if (_client.Merchant != null && item.ItemId != 0
                    && GUILayout.Button("Sell " + MerchantRules.Coins(MerchantRules.SellPrice(item.Price)), GUILayout.Width(90)))
                    _client.Sell(slot);
                bool clicked = GUILayout.Button($"{where}: {what}");
                GUILayout.EndHorizontal();
                if (clicked)
                {
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
            }
            GUILayout.Label($"{inventory.Platinum} pp  {inventory.Gold} gp  {inventory.Silver} sp  {inventory.Copper} cp");
            GUILayout.EndArea();
        }

        /// <summary>
        /// The spell gems (keys 1-8, or click): the memorised spells with their mana cost, dimmed while
        /// casting or short of mana.
        /// </summary>
        private void DrawGems()
        {
            if (_client.SpellBook == null || _client.MaxMana <= 0 && _client.SpellBook.Spells.Count == 0)
                return;
            for (int gem = 0; gem < GameClient.GemCount; gem++)
            {
                var spell = _client.GemSpell(gem);
                var colour = GUI.color;
                if (spell != null && (spell.Mana > _client.Mana || _client.Casting != null))
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                if (GUI.Button(new Rect(10, 100 + 26 * gem, 190, 22), spell == null ? $"{gem + 1}  -" : $"{gem + 1}  {spell.Name} ({spell.Mana})") && spell != null)
                    _client.Cast(gem);
                GUI.color = colour;
            }
        }

        /// <summary>The group window: the members (leader first, in yellow), their health when they are in this zone.</summary>
        private void DrawGroup()
        {
            if (_client.Group is not { } group)
                return;
            float y = 100 + 26 * GameClient.GemCount + 10;
            GUI.Label(new Rect(10, y, 200, 20), "Group");
            foreach (var name in group.Members)
            {
                y += 18;
                var here = _client.Zone?.Entities.FirstOrDefault(e => e.Spawn.IsPlayer && e.Spawn.Name == name);
                var colour = GUI.color;
                if (name == group.Leader)
                    GUI.color = Color.yellow;
                GUI.Label(new Rect(10, y, 200, 20), here != null ? $"{name}  {here.HpPercent}%" : name);
                GUI.color = colour;
            }
        }

        /// <summary>The ability buttons (kick, bash, taunt...), above the chat.</summary>
        private void DrawAbilities()
        {
            if (_client.Skills == null)
                return;
            float x = 10;
            foreach (int skill in _client.Skills.Abilities)
            {
                if (GUI.Button(new Rect(x, Screen.height - 300, 80, 22), SkillNames[skill]))
                    _client.UseAbility(skill);
                x += 84;
            }
        }

        /// <summary>The skills window (K): every skill the character has, with its value.</summary>
        private void DrawSkills()
        {
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 160, 80, 320, Screen.height - 200), GUI.skin.box);
            GUILayout.Label("Skills");
            _skillsScroll = GUILayout.BeginScrollView(_skillsScroll);
            var values = _client.Skills?.Values;
            for (int i = 0; values != null && i < values.Count && i < SkillNames.Length; i++)
                if (values[i] > 0)
                    GUILayout.Label($"{SkillNames[i]}: {values[i]}");
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close"))
                _skillsOpen = false;
            GUILayout.EndArea();
        }

        /// <summary>The buff window: each spell lasting on you with the time left (a tic is 6 s), detrimental ones in red.</summary>
        private void DrawBuffs()
        {
            if (_client.Buffs == null || _client.Buffs.Buffs.Count == 0)
                return;
            float y = 30;
            foreach (var buff in _client.Buffs.Buffs)
            {
                int seconds = buff.TicsLeft * 6;
                string left = buff.TicsLeft >= 32767 ? "" : $"  {seconds / 60}:{seconds % 60:00}";
                var colour = GUI.color;
                GUI.color = buff.Beneficial ? Color.white : new Color(1f, 0.5f, 0.5f);
                GUI.Label(new Rect(Screen.width - 260, y, 250, 20), buff.Name + left);
                GUI.color = colour;
                y += 18;
            }
        }

        /// <summary>A merchant's window (U on a merchant): the goods at 2.5 times their value; the inventory shows Sell buttons.</summary>
        private void DrawMerchant(MerchantGoods merchant)
        {
            GUILayout.BeginArea(new Rect(Screen.width - 330, 60, 320, Screen.height - 120), GUI.skin.box);
            string name = _client.Zone?.Get(merchant.NpcId)?.DisplayName ?? "Merchant";
            GUILayout.Label($"{name} - click to buy (sell from the inventory, I)");
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
                _inventoryOpen = false;
            }
            GUILayout.EndArea();
            _inventoryOpen = true;
        }

        private Vector2 _merchantScroll;

        /// <summary>The spell book (B): every scribed spell by level; a gem button memorises it there.</summary>
        private void DrawBook()
        {
            GUILayout.BeginArea(new Rect(Screen.width / 2 - 260, 80, 520, Screen.height - 200), GUI.skin.box);
            GUILayout.Label("Spell book - click a gem number to memorize");
            var book = _client.SpellBook;
            if (book == null || book.Spells.Count == 0)
                GUILayout.Label(book == null ? "(not received yet)" : "No spells scribed. Scribe a scroll from the inventory (I).");
            else
            {
                _bookScroll = GUILayout.BeginScrollView(_bookScroll);
                foreach (var spell in book.Spells)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{spell.Level,2}  {spell.Name}  - {spell.Mana} mana, {spell.CastMs / 1000f:0.#} s", GUILayout.Width(300));
                    for (int gem = 0; gem < GameClient.GemCount; gem++)
                        if (GUILayout.Button((gem + 1).ToString(), GUILayout.Width(22)))
                            _client.Memorize(gem, spell.SpellId);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }
            if (GUILayout.Button("Close"))
                _bookOpen = false;
            GUILayout.EndArea();
        }

        private static Texture2D _white;

        /// <summary>A filled bar (hit points) with a label over it.</summary>
        private static void DrawBar(Rect area, float fraction, Color fill, string label)
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            var colour = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(area, _white);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(area.x, area.y, area.width * Mathf.Clamp(fraction, 0f, 1f), area.height), _white);
            GUI.color = colour;
            GUI.Label(new Rect(area.x + 4, area.y - 3, area.width, area.height + 6), label);
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

        /// <summary>The chat window: the latest lines, and the input line while it is open.</summary>
        private void DrawChat()
        {
            int shown = System.Math.Min(ChatLines, _messages.Count);
            float bottom = Screen.height - 36;
            for (int i = 0; i < shown; i++)
                GUI.Label(new Rect(10, bottom - 20 * (shown - i), 800, 20), _messages[_messages.Count - shown + i]);
            if (!_chatOpen)
                return;
            GUI.SetNextControlName("chat");
            _chatLine = GUI.TextField(new Rect(10, Screen.height - 30, 700, 22), _chatLine);
            GUI.FocusControl("chat");
            var e = Event.current;
            if (e.type != EventType.KeyDown)
                return;
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                _client.ExecuteChat(_chatLine);
                CloseChat(e);
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                CloseChat(e);
            }
        }

        private void CloseChat(Event e)
        {
            _chatOpen = false;
            _chatLine = "";
            _chatClosedFrame = Time.frameCount;
            e.Use();
        }

        private void OnGUI()
        {
            var state = _client?.State ?? GameState.Disconnected;
            if (state == GameState.InZone)
            {
                GUI.Label(new Rect(10, 10, 1200, 20), $"{_client.Zone?.Zone}  -  {_client.Zone?.Count} entities  -  WASD/arrows move, Q/E turn, R autorun, Shift walk, Space jump, X sit, right mouse look, wheel zoom, F9 view, U door/merchant, Tab target, T face, C consider, F attack, L loot, I inventory, B spell book, K skills, 1-8 cast");
                if (_presenter.MissingZone != null)
                    GUI.Box(new Rect(Screen.width / 2 - 300, 80, 600, 44),
                        $"The zone '{_presenter.MissingZone}' is not installed in this client (not imported from Lantern).\nYou are there for the server, but nothing can be drawn.");
                DrawBar(new Rect(10, 34, 220, 16), _client.MaxHp > 0 ? (float)_client.Hp / _client.MaxHp : 0f, new Color(0.8f, 0.1f, 0.1f),
                    $"{_client.Hp} / {_client.MaxHp}" + (_client.AutoAttacking ? "  attacking" : "") + (_client.Sitting ? "  sitting" : ""));
                if (_client.MaxMana > 0)
                    DrawBar(new Rect(10, 52, 220, 12), (float)_client.Mana / _client.MaxMana, new Color(0.2f, 0.3f, 0.9f), $"{_client.Mana} / {_client.MaxMana}");
                if (_client.Experience is { } xp)
                    DrawBar(new Rect(10, 66, 220, 8), xp.Fraction, new Color(0.9f, 0.8f, 0.2f), "");
                if (_client.Experience is { } lvl)
                    GUI.Label(new Rect(10, 74, 220, 20), $"Level {lvl.Level}  {(int)(lvl.Fraction * 100)}%");
                if (_client.Casting is { } casting)
                    DrawBar(new Rect(Screen.width / 2 - 150, Screen.height - 320, 300, 16), _client.CastProgress, new Color(0.7f, 0.3f, 0.9f), casting.SpellName);
                DrawGems();
                DrawBuffs();
                DrawAbilities();
                DrawGroup();
                if (_skillsOpen)
                    DrawSkills();
                if (_client.TargetId is int target && _client.Zone?.Get(target) is { } t)
                {
                    var colour = GUI.color;
                    GUI.color = ConColour(t.Con);
                    GUI.Label(new Rect(250, 30, 300, 20), t.DisplayName);
                    GUI.color = colour;
                    DrawBar(new Rect(250, 50, 220, 12), t.HpPercent / 100f, new Color(0.8f, 0.1f, 0.1f), t.HpPercent + "%");
                }
                DrawChat();
                if (_client.LootingCorpse is int corpse)
                    DrawLoot(corpse);
                if (_inventoryOpen)
                    DrawInventory();
                if (_bookOpen)
                    DrawBook();
                if (_client.Merchant is { } merchant)
                    DrawMerchant(merchant);
                return;
            }

            GUILayout.BeginArea(new Rect(20, 20, 460, Screen.height - 40), GUI.skin.box);
            GUILayout.Label("EQClassic");
            if (_client?.LastError != null)
                GUILayout.Label("Error: " + _client.LastError);

            switch (state)
            {
                case GameState.Disconnected:
                    GUILayout.Label("Login server");
                    _host = GUILayout.TextField(_host);
                    _port = GUILayout.TextField(_port);
                    GUILayout.Label("Server key fingerprint (optional, printed by the server)");
                    _fingerprint = GUILayout.TextField(_fingerprint);
                    if (GUILayout.Button("Connect"))
                        Connect();
                    break;

                case GameState.Login:
                    GUILayout.Label("Account");
                    _user = GUILayout.TextField(_user);
                    _password = GUILayout.PasswordField(_password, '*');
                    if (GUILayout.Button("Log in"))
                    {
                        PlayerPrefs.SetString("eqc.user", _user);
                        _client.Login(_user, _password);
                    }
                    break;

                case GameState.ServerSelect:
                    GUILayout.Label("Servers");
                    foreach (var world in _client.Worlds)
                        if (GUILayout.Button($"{world.Name}  ({world.PlayersOnline} online, {world.Status})"))
                            _client.SelectWorld(world.Id);
                    break;

                case GameState.CharacterSelect:
                    GUILayout.Label("Characters");
                    foreach (var c in _client.Characters)
                        if (GUILayout.Button($"{c.Name}  level {c.Level}  -  {c.Zone}"))
                            _client.EnterWorld(c.Name);
                    GUILayout.Space(10);
                    if (_builder == null)
                    {
                        if (GUILayout.Button("New character"))
                        {
                            _client.RequestCreationOptions();
                            _creating = true;
                        }
                        if (_creating && _client.CreationOptions is { Count: > 0 } options)
                            _builder = new CharacterBuilder(options);
                    }
                    else
                    {
                        DrawCreation();
                    }
                    break;

                default:
                    GUILayout.Label(state + "...");
                    break;
            }
            GUILayout.EndArea();
        }

        private void Connect()
        {
            PlayerPrefs.SetString("eqc.host", _host);
            PlayerPrefs.SetString("eqc.port", _port);
            PlayerPrefs.SetString("eqc.fingerprint", _fingerprint);
            _client?.Dispose();
            _client = new GameClient(string.IsNullOrWhiteSpace(_fingerprint) ? null : _fingerprint.Trim());
            _client.ZoneEntered += zone => _presenter.Enter(zone);
            _client.CombatReceived += _presenter.OnCombat;
            _client.SpellCastReceived += _presenter.OnSpellCast;
            _client.ZoneInfoReceived += _presenter.ApplyZoneInfo;
            _client.MessageReceived += AddMessage;
            _client.Connect(_host, int.TryParse(_port, out var p) ? p : 5999);
        }
    }
}
