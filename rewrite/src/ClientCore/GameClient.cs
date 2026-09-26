using System;
using System.Collections.Generic;
using System.Diagnostics;
using EQClassic.Shared.Characters;
using EQClassic.Shared.Client;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.ClientCore
{
    public enum GameState { Disconnected, Connecting, Login, LoggingIn, ServerSelect, EnteringWorld, CharacterSelect, EnteringZone, InZone }

    /// <summary>
    /// The whole client flow without any engine: login server → world → zone, reconnecting at each
    /// hop and on zone changes. Call <see cref="Update"/> every frame; read <see cref="State"/>,
    /// <see cref="Worlds"/>, <see cref="Characters"/>, <see cref="Zone"/> and <see cref="Player"/>.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private LoginClient? _connection;
        private IMessage? _sendOnConnect;
        private string _sessionId = "";
        private string _characterName = "";
        private GameState _state = GameState.Disconnected;

        public GameClient(string? expectedServerFingerprint = null) => ExpectedServerFingerprint = expectedServerFingerprint;

        public string? ExpectedServerFingerprint { get; }
        public GameState State => _state;
        public string? LastError { get; private set; }
        public IReadOnlyList<WorldServerInfo> Worlds { get; private set; } = Array.Empty<WorldServerInfo>();
        public IReadOnlyList<CharacterSummary> Characters { get; private set; } = Array.Empty<CharacterSummary>();
        public ZoneView? Zone { get; private set; }
        public LocalPlayer? Player { get; private set; }
        public double Now => _clock.Elapsed.TotalSeconds;

        public event Action<GameState>? StateChanged;
        /// <summary>A new zone view (first entry or after a zone change): rebuild the scene.</summary>
        public event Action<ZoneView>? ZoneEntered;

        public void Connect(string host, int port = ProtocolInfo.DefaultLoginPort)
        {
            LastError = null;
            Open(host, port, null, GameState.Connecting);
        }

        public void Login(string user, string password)
        {
            Require(GameState.Login);
            _connection!.Login(user, password);
            SetState(GameState.LoggingIn);
        }

        public void SelectWorld(int worldId)
        {
            Require(GameState.ServerSelect);
            _connection!.Send(new PlayRequest(worldId));
            SetState(GameState.EnteringWorld);
        }

        public void CreateCharacter(CreateCharacterRequest request)
        {
            Require(GameState.CharacterSelect);
            _connection!.Send(request);
        }

        public void EnterWorld(string characterName)
        {
            Require(GameState.CharacterSelect);
            _characterName = characterName;
            _connection!.Send(new EnterWorldRequest(characterName));
            SetState(GameState.EnteringZone);
        }

        /// <summary>Network, then the local player's report to the zone.</summary>
        public void Update(float seconds)
        {
            if (_connection == null)
                return;
            foreach (var message in _connection.Poll())
                Handle(message);
            if (_connection != null && _connection.State == ConnectionState.Connected && _sendOnConnect != null)
            {
                _connection.Send(_sendOnConnect);
                _sendOnConnect = null;
            }
            if (_connection != null && _connection.State == ConnectionState.Disconnected && _state != GameState.Disconnected)
                Fail(_connection.DisconnectReason ?? "connection lost");
            if (_state == GameState.InZone && Player?.Due(seconds) is PlayerMove move)
                _connection?.Send(move);
        }

        private void Handle(IMessage message)
        {
            switch (message)
            {
                case EQClassic.Shared.Security.ServerHello _ when _state == GameState.Connecting:
                    SetState(GameState.Login);
                    break;
                case LoginResponse login:
                    if (login.Result != LoginResult.Success)
                    {
                        LastError = login.Message;
                        SetState(GameState.Login); // a new ServerHello follows: the player may retry
                        break;
                    }
                    _sessionId = login.SessionId;
                    _connection!.Send(new ServerListRequest());
                    break;
                case ServerListResponse list:
                    Worlds = list.Worlds;
                    SetState(GameState.ServerSelect);
                    break;
                case PlayResponse play:
                    if (!play.Accepted) { LastError = play.Message; SetState(GameState.ServerSelect); break; }
                    Open(play.Address, play.Port, new WorldLoginRequest(_sessionId, play.SessionKey), GameState.EnteringWorld);
                    break;
                case WorldLoginResponse world:
                    if (!world.Accepted) { Fail(world.Message); break; }
                    Characters = world.Characters;
                    SetState(GameState.CharacterSelect);
                    break;
                case CreateCharacterResponse created:
                    if (!created.Accepted) LastError = created.Message;
                    else Characters = created.Characters;
                    StateChanged?.Invoke(_state);
                    break;
                case EnterWorldResponse enter:
                    if (!enter.Accepted || enter.ZonePort == 0) { LastError = enter.Accepted ? "no zone server" : enter.Message; SetState(GameState.CharacterSelect); break; }
                    Open(enter.ZoneAddress, enter.ZonePort, new ZoneEnterRequest(_characterName, enter.ZoneKey), GameState.EnteringZone);
                    break;
                case ZoneEnterResponse entered:
                    if (!entered.Accepted) { Fail(entered.Message); break; }
                    Zone = new ZoneView(entered, Now);
                    var me = Zone.Get(entered.YourEntityId)!.Spawn;
                    Player = new LocalPlayer(entered.YourEntityId, new Vec3(me.X, me.Y, me.Z), me.Heading);
                    SetState(GameState.InZone);
                    ZoneEntered?.Invoke(Zone);
                    break;
                case EntitySpawned spawned:
                    Zone?.Apply(spawned, Now);
                    break;
                case EntityRemoved removed:
                    Zone?.Apply(removed);
                    break;
                case EntityPositions positions:
                    Zone?.Apply(positions, Now);
                    break;
                case MoveCorrection correction:
                    Player?.Apply(correction);
                    break;
                case ZoneChange change:
                    Zone = null;
                    Player = null;
                    Open(change.Address, change.Port, new ZoneEnterRequest(_characterName, change.ZoneKey), GameState.EnteringZone);
                    break;
            }
        }

        private void Open(string host, int port, IMessage? firstMessage, GameState state)
        {
            _connection?.Dispose();
            _connection = new LoginClient { ExpectedServerFingerprint = firstMessage == null ? ExpectedServerFingerprint : null };
            _sendOnConnect = firstMessage;
            SetState(state);
            _connection.Connect(host, port);
        }

        private void Fail(string reason)
        {
            LastError = reason;
            _connection?.Dispose();
            _connection = null;
            Zone = null;
            Player = null;
            SetState(GameState.Disconnected);
        }

        private void Require(GameState expected)
        {
            if (_state != expected)
                throw new InvalidOperationException($"{expected} expected, client is {_state}");
        }

        private void SetState(GameState state)
        {
            if (_state == state)
                return;
            _state = state;
            StateChanged?.Invoke(state);
        }

        public void Dispose()
        {
            _connection?.Dispose();
            _connection = null;
        }
    }
}
