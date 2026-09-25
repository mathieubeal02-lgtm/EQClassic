using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EQClassic.Server.Login;

/// <summary>
/// Minimal login server on LiteNetLib (reliable ordered UDP). Flow, like the legacy one:
/// connect (protocol key checked) → <see cref="LoginRequest"/> → <see cref="LoginResponse"/>;
/// then, once logged in, <see cref="ServerListRequest"/> and <see cref="PlayRequest"/>.
/// Single-threaded: call <see cref="PollEvents"/> from one loop (Program) or test.
/// </summary>
public sealed class LoginServer : IDisposable
{
    private readonly LoginService _login;
    private readonly WorldDirectory _worlds;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<NetPeer, int> _loggedIn = new();
    private readonly NetDataWriter _writer = new();

    /// <summary>Called for each handled message (logging, metrics). Never receives passwords.</summary>
    public Action<string>? Log { get; set; }

    public LoginServer(LoginService login, WorldDirectory worlds)
    {
        _login = login;
        _worlds = worlds;
        _net = new NetManager(_listener) { AutoRecycle = true };
        _listener.ConnectionRequestEvent += request => request.AcceptIfKey(ProtocolInfo.ConnectionKey);
        _listener.PeerDisconnectedEvent += (peer, _) => _loggedIn.Remove(peer);
        _listener.NetworkReceiveEvent += OnReceive;
    }

    /// <summary>Starts listening; port 0 picks a free port (see <see cref="Port"/>).</summary>
    public void Start(int port = ProtocolInfo.DefaultLoginPort)
    {
        if (!_net.Start(port))
            throw new InvalidOperationException($"cannot listen on UDP port {port}");
    }

    public int Port => _net.LocalPort;
    public int ConnectedClients => _net.ConnectedPeersCount;

    public void PollEvents() => _net.PollEvents();

    public void Dispose() => _net.Stop();

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        IMessage message;
        try
        {
            message = MessageCodec.Read(reader);
        }
        catch (MessageFormatException e)
        {
            Log?.Invoke($"{peer.Address}: malformed message ({e.Message})");
            Send(peer, LoginResponse.Failure(LoginResult.Malformed));
            return;
        }

        switch (message)
        {
            case LoginRequest request:
                var response = _login.Authenticate(request.Username, request.Password);
                if (response.Result == LoginResult.Success)
                    _loggedIn[peer] = response.AccountId;
                else
                    _loggedIn.Remove(peer);
                Log?.Invoke($"{peer.Address}: login '{request.Username}' -> {response.Result}");
                Send(peer, response);
                break;

            case ServerListRequest when _loggedIn.ContainsKey(peer):
                Send(peer, new ServerListResponse(_worlds.List()));
                break;

            case PlayRequest play when _loggedIn.TryGetValue(peer, out int accountId):
                var playResponse = _worlds.RequestPlay(accountId, play.WorldId);
                Log?.Invoke($"{peer.Address}: account {accountId} play on world {play.WorldId} -> {(playResponse.Accepted ? "key issued" : playResponse.Message)}");
                Send(peer, playResponse);
                break;

            default:
                // Anything else before login (or a server-to-client message) is a protocol violation.
                Log?.Invoke($"{peer.Address}: unexpected {message.Type}, disconnecting");
                peer.Disconnect();
                break;
        }
    }

    private void Send(NetPeer peer, IMessage message)
    {
        _writer.Reset();
        MessageCodec.Write(_writer, message);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
}
