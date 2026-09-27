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
    public sealed partial class EQClassicClient : MonoBehaviour
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
        private readonly ChatLog _chat = new ChatLog();
        private bool _chatOpen;
        private Vector2 _skillsScroll;
        private string _chatLine = "";
        private int _chatClosedFrame = -1;
        private int _chatOpenedFrame = -1;

        private void Awake()
        {
            _host = PlayerPrefs.GetString("eqc.host", _host);
            _port = PlayerPrefs.GetString("eqc.port", _port);
            _fingerprint = PlayerPrefs.GetString("eqc.fingerprint", "");
            _user = PlayerPrefs.GetString("eqc.user", "");
            ApplyCommandLine(System.Environment.GetCommandLineArgs());
            _presenter = gameObject.AddComponent<ZonePresenter>();
            _layout = HudLayout.Load(PlayerPrefs.GetString("eqc.layout", ""), Screen.width, Screen.height);
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
                var pointer = Input.mousePosition; // from the bottom left; the chat's rectangle is from the top left
                bool overChat = _chatScreenRect.Contains(new Vector2(pointer.x, Screen.height - pointer.y));
                _presenter.ZoomEnabled = !overChat;
                if (overChat && Input.mouseScrollDelta.y != 0f)
                    _chat.ScrollBy(Input.mouseScrollDelta.y > 0f ? 3 : -3, _chatLinesShown);
                SaveLayoutWhenChanged();
                if (!_chatOpen && Time.frameCount != _chatClosedFrame
                    && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Slash)))
                {
                    _chatOpen = true;
                    _chatOpenedFrame = Time.frameCount;
                    _chatLine = Input.GetKeyDown(KeyCode.Slash) ? "/" : "";
                }
                if (_chatOpen)
                {
                    // Enter sends, Escape closes. Read here rather than from the text field's events: on
                    // Windows the focused IMGUI text field swallows Return and Escape, so the chat never sent.
                    if (Time.frameCount != _chatOpenedFrame)
                    {
                        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                            SubmitChat();
                        else if (Input.GetKeyDown(KeyCode.Escape))
                            CloseChat();
                    }
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
                if (Input.GetKeyDown(KeyCode.H))
                    _client.Hail();
                if (Input.GetKeyDown(KeyCode.X))
                    _client.ToggleSit();
                if (Input.GetKeyDown(KeyCode.L))
                    _client.Loot();
                if (Input.GetKeyDown(KeyCode.I))
                    ToggleWindow(HudLayout.Inventory);
                if (Input.GetKeyDown(KeyCode.B))
                    ToggleWindow(HudLayout.Book);
                if (Input.GetKeyDown(KeyCode.K))
                    ToggleWindow(HudLayout.Skills);
                if (Input.GetKeyDown(KeyCode.F12))
                    SwitchLayout();
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
            SaveLayout();
            _client?.Dispose();
        }

        private void AddMessage(string text)
        {
            _chat.Add(text);
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
            GUILayout.Label($"Statistics ({b.PointsLeft} points left, at most {CreationRules.MaxPointsPerStat} in one)");
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

        private void OnGUI()
        {
            var state = _client?.State ?? GameState.Disconnected;
            if (state == GameState.InZone)
            {
                DrawHud();
                return;
            }

            if (state == GameState.EnteringZone)
            {
                // The Trilogy client's loading screen between zones: black, a short line in the middle.
                DrawBar(new Rect(0, 0, Screen.width, Screen.height), 1f, Color.black, "");
                GUI.Label(new Rect(Screen.width / 2 - 100, Screen.height / 2 - 10, 200, 20), "Loading, please wait...");
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
            _client.Keys = KeyBindings.Parse(PlayerPrefs.GetString("eqc.keys", "azerty")) ?? KeyBindings.Azerty;
            _client.KeysChanged += keys => PlayerPrefs.SetString("eqc.keys", keys.Layout.ToString().ToLowerInvariant());
            _client.ZoneEntered += zone =>
            {
                _presenter.Enter(zone);
                LoadHotbar();
            };
            _client.CombatReceived += _presenter.OnCombat;
            _client.SpellCastReceived += _presenter.OnSpellCast;
            _client.ZoneInfoReceived += _presenter.ApplyZoneInfo;
            _client.MessageReceived += AddMessage;
            _client.Connect(_host, int.TryParse(_port, out var p) ? p : 5999);
        }
    }
}
