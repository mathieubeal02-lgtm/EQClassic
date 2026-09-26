using EQClassic.ClientCore;
using EQClassic.Shared.Characters;
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
        private readonly System.Collections.Generic.List<string> _messages = new System.Collections.Generic.List<string>();

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
                if (Input.GetKeyDown(KeyCode.U) && _client.UseNearestDoor() == null)
                    _messages.Add("There is nothing here to use.");
                _presenter.Present(_client, Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            _client?.Dispose();
        }

        private void OnGUI()
        {
            var state = _client?.State ?? GameState.Disconnected;
            if (state == GameState.InZone)
            {
                GUI.Label(new Rect(10, 10, 700, 20), $"{_client.Zone?.Zone}  -  {_client.Zone?.Count} entities  -  WASD/arrows to move, Q/E to turn, U to use a door");
                for (int i = 0; i < _messages.Count; i++)
                    GUI.Label(new Rect(10, Screen.height - 20 * (_messages.Count - i) - 10, 700, 20), _messages[i]);
                return;
            }

            GUILayout.BeginArea(new Rect(20, 20, 420, 520), GUI.skin.box);
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
                    GUILayout.Label("New troll shaman (Grobb)");
                    _newName = GUILayout.TextField(_newName);
                    if (GUILayout.Button("Create"))
                        _client.CreateCharacter(new CreateCharacterRequest(_newName, 9, 10, 0, 203, 1, "grobb", new CharacterStats(108, 119, 45, 75, 52, 83, 95)));
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
            _client.MessageReceived += text =>
            {
                _messages.Add(text);
                if (_messages.Count > 6)
                    _messages.RemoveAt(0);
            };
            _client.Connect(_host, int.TryParse(_port, out var p) ? p : 5999);
        }
    }
}
