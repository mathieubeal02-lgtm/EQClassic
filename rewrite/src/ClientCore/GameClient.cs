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
        /// <summary>A line from the server for the chat window.</summary>
        public event Action<string>? MessageReceived;

        /// <summary>Reach of the Use key: the server accepts clicks within 40 units (ZoneInstance.DoorReach).</summary>
        public const float DoorUseReach = 30f;
        /// <summary>Tab targets the nearest NPC within this distance.</summary>
        public const float TargetReach = 100f;
        /// <summary>Reason of the server's move after the player was slain (ZoneInstance.DeathReason).</summary>
        public const string DeathReason = "death";

        public int? TargetId { get; private set; }
        public bool AutoAttacking { get; private set; }
        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        /// <summary>A melee swing near the player (for animations; the text goes to <see cref="MessageReceived"/>).</summary>
        public event Action<CombatEvent>? CombatReceived;

        /// <summary>
        /// Tab: targets the nearest NPC, or the next one by distance when the current target is
        /// near (repeated Tabs cycle). Clears the target when none is near. Returns the new target.
        /// </summary>
        public ZoneView.EntityView? TargetNearest()
        {
            if (_state != GameState.InZone || Zone == null || Player == null)
                return null;
            var target = Zone.NextNpc(Player.Position, TargetReach, TargetId);
            SetTarget(target?.Id);
            return target;
        }

        /// <summary>Turns the player toward the target, if any (T).</summary>
        public void FaceTarget()
        {
            if (TargetId is int id && Zone?.Get(id) is { } target)
                Player?.Face(target.Latest);
        }

        public void SetTarget(int? entityId)
        {
            TargetId = entityId;
            if (entityId == null)
                AutoAttacking = false;
            _connection?.Send(new EQClassic.Shared.Zone.SetTarget(entityId ?? 0));
        }

        /// <summary>Auto-attack on or off (the server refuses without a target and says so).</summary>
        public void ToggleAutoAttack()
        {
            if (_state != GameState.InZone)
                return;
            bool on = !AutoAttacking;
            AutoAttacking = on && TargetId != null;
            _connection?.Send(new AutoAttack(on)); // without a target the server answers "You must first select a target..."
        }

        /// <summary>
        /// Uses the nearest door (the Use key, U in EverQuest). Returns it, or null when none is in
        /// reach. The server answers with the door's new state, a teleport or a message.
        /// </summary>
        public DoorInfo? UseNearestDoor()
        {
            if (_state != GameState.InZone || Zone == null || Player == null)
                return null;
            var door = Zone.NearestDoor(Player.Position, DoorUseReach);
            if (door != null)
                _connection?.Send(new ClickDoor(door.Id));
            return door;
        }

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
                    if (removed.Id == TargetId)
                    {
                        TargetId = null;
                        AutoAttacking = false;
                    }
                    break;
                case EntityPositions positions:
                    Zone?.Apply(positions, Now);
                    break;
                case MoveCorrection correction:
                    Player?.Apply(correction);
                    if (correction.Reason == DeathReason)
                    {
                        TargetId = null;
                        AutoAttacking = false;
                    }
                    break;
                case ZoneDoors doors:
                    Zone?.Apply(doors);
                    break;
                case DoorState door:
                    Zone?.Apply(door);
                    break;
                case ZoneMessage text:
                    if (text.Text == "Auto attack is off.")
                        AutoAttacking = false;
                    MessageReceived?.Invoke(text.Text);
                    break;
                case CombatEvent swing when Zone != null:
                    Zone.Apply(swing);
                    CombatReceived?.Invoke(swing);
                    MessageReceived?.Invoke(CombatText.Describe(swing, Zone.YourEntityId, id => Zone.Get(id)?.DisplayName));
                    break;
                case PlayerHealth health:
                    Hp = health.Hp;
                    MaxHp = health.MaxHp;
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
