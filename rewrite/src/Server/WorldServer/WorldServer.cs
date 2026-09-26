using EQClassic.Server.Accounts;
using EQClassic.Server.Characters;
using EQClassic.Server.Login;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Shared.Characters;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EQClassic.Server.WorldServer;

/// <summary>
/// World: accepts players sent by the login server, lists their characters and says where each
/// one enters the world. Mirrors the legacy World (World/Source/client_process.cpp):
/// "LS#&lt;id&gt;" + key checked against what the login server issued, world account created on the
/// first visit, characters by name. Runs in the same process as <see cref="LoginServer"/> and shares
/// its <see cref="WorldDirectory"/> (keys), which replaces the legacy login-to-world TCP link.
/// </summary>
public sealed class WorldServer : IDisposable
{
    public const int DefaultPort = 9100; // not 9000: the legacy World may run on the same host

    public const string BadKey = "Your session has expired or is invalid. Please log in again.";
    public const string AccountUnavailable = "Unable to create your account on this server. Please contact an administrator.";
    public const string UnknownCharacter = "That character does not exist on your account.";

    private sealed class Session
    {
        public int WorldAccountId;
    }

    private readonly int _worldId;
    private readonly WorldDirectory _directory;
    private readonly IWorldAccountStore _accounts;
    private readonly ICharacterStore _characters;
    private readonly CharacterCreation _creation;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<NetPeer, Session> _sessions = new();
    private readonly NetDataWriter _writer = new();

    public Action<string>? Log { get; set; }

    /// <summary>Where characters are sent after entering the world; without it EnterWorld only reports the saved zone.</summary>
    public ZoneHandoff? Zones { get; set; }

    public WorldServer(int worldId, WorldDirectory directory, IWorldAccountStore accounts, ICharacterStore characters, ICreationData? creationData = null)
    {
        _creation = new CharacterCreation(characters, creationData ?? new InMemoryCreationData());
        _worldId = worldId;
        _directory = directory;
        _accounts = accounts;
        _characters = characters;
        _net = new NetManager(_listener) { AutoRecycle = true };
        _listener.ConnectionRequestEvent += request => request.AcceptIfKey(ProtocolInfo.ConnectionKey);
        _listener.PeerDisconnectedEvent += (peer, _) => _sessions.Remove(peer);
        _listener.NetworkReceiveEvent += OnReceive;
    }

    public void Start(int port = DefaultPort)
    {
        if (!_net.Start(port))
            throw new InvalidOperationException($"cannot listen on UDP port {port}");
    }

    public int Port => _net.LocalPort;

    public void PollEvents() => _net.PollEvents();

    public void Dispose() => _net.Stop();

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        IMessage message;
        try
        {
            message = MessageCodec.Read(reader);
        }
        catch (MessageFormatException)
        {
            peer.Disconnect();
            return;
        }

        switch (message)
        {
            case WorldLoginRequest login when !_sessions.ContainsKey(peer):
                Send(peer, HandleLogin(peer, login));
                break;

            case EnterWorldRequest enter when _sessions.TryGetValue(peer, out var session):
                Send(peer, HandleEnter(session, enter));
                break;

            case CreationOptionsRequest when _sessions.ContainsKey(peer):
                Send(peer, new CreationOptionsResponse(_creation.Options()));
                break;

            case CreateCharacterRequest create when _sessions.TryGetValue(peer, out var creator):
                var error = _creation.Create(creator.WorldAccountId, create);
                Log?.Invoke($"{peer.Address}: account {creator.WorldAccountId} create '{create.Name}' -> {error ?? "created"}");
                Send(peer, new CreateCharacterResponse(error is null, error ?? "", Summaries(creator.WorldAccountId)));
                break;

            default:
                Log?.Invoke($"{peer.Address}: unexpected {message.Type}, disconnecting");
                peer.Disconnect();
                break;
        }
    }

    private WorldLoginResponse HandleLogin(NetPeer peer, WorldLoginRequest login)
    {
        if (!SessionIds.TryParse(login.SessionId, out int lsAccountId)
            || !_directory.TryRedeem(lsAccountId, _worldId, login.WorldKey, out var accountName))
        {
            Log?.Invoke($"{peer.Address}: bad or expired key for {login.SessionId}");
            return WorldLoginResponse.Refused(BadKey);
        }
        int worldAccountId = _accounts.ResolveOrCreate(lsAccountId, accountName);
        if (worldAccountId == 0)
        {
            Log?.Invoke($"{peer.Address}: cannot create world account for LS#{lsAccountId} '{accountName}'");
            return WorldLoginResponse.Refused(AccountUnavailable);
        }
        _sessions[peer] = new Session { WorldAccountId = worldAccountId };
        var characters = Summaries(worldAccountId);
        Log?.Invoke($"{peer.Address}: LS#{lsAccountId} '{accountName}' in world as account {worldAccountId}, {characters.Count} character(s)");
        return new WorldLoginResponse(true, "", characters);
    }

    private List<CharacterSummary> Summaries(int worldAccountId) =>
        _characters.ListForAccount(worldAccountId)
            .Select(c => new CharacterSummary(c.Profile.Name, c.Profile.Race, c.Profile.Class, c.Profile.Level, c.Profile.Gender, c.Profile.Zone))
            .ToList();

    private EnterWorldResponse HandleEnter(Session session, EnterWorldRequest enter)
    {
        var character = _characters.Find(session.WorldAccountId, enter.CharacterName);
        if (character is null)
            return EnterWorldResponse.Refused(UnknownCharacter);
        var p = character.Profile;
        if (Zones is null)
            return new EnterWorldResponse(true, "", p.Zone, p.X, p.Y, p.Z);
        var key = Zones.Keys.Issue(new ZoneTicket(p.Name, p.Zone, session.WorldAccountId, p.Race, p.Gender, p.Level, new Vec3(p.X, p.Y, p.Z), p));
        return new EnterWorldResponse(true, "", p.Zone, p.X, p.Y, p.Z, Zones.Address, Zones.Port, key);
    }

    private void Send(NetPeer peer, IMessage message)
    {
        _writer.Reset();
        MessageCodec.Write(_writer, message);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
}

/// <summary>The zone server World hands characters to.</summary>
public sealed record ZoneHandoff(ZoneKeys Keys, string Address, int Port);
