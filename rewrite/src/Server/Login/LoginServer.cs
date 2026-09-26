using System.Security.Cryptography;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using EQClassic.Shared.Security;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EQClassic.Server.Login;

/// <summary>
/// Login server on LiteNetLib (reliable ordered UDP). Flow:
/// connect (protocol key checked) → <see cref="ServerHello"/> (RSA public key + nonce) →
/// <see cref="SecureLoginRequest"/> → <see cref="LoginResponse"/>; then, once logged in,
/// <see cref="ServerListRequest"/> and <see cref="PlayRequest"/>, whose answer (the world key) is
/// sent <see cref="Sealed"/> with the session keys. Authentication itself is <see cref="LoginService"/>,
/// identical to the legacy server. Single-threaded: call <see cref="PollEvents"/> from one loop.
/// </summary>
public sealed class LoginServer : IDisposable
{
    private sealed class Session
    {
        public byte[] Nonce = LoginCrypto.RandomBytes(LoginCrypto.NonceSize);
        public int? AccountId;
        public string AccountName = "";
        public SessionKeys? Keys;
    }

    private readonly LoginService _login;
    private readonly WorldDirectory _worlds;
    private readonly RSA _key;
    private readonly RSAParameters _publicKey;
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly Dictionary<NetPeer, Session> _sessions = new();
    private readonly NetDataWriter _writer = new();

    /// <summary>Called for each handled message (logging, metrics). Never receives passwords.</summary>
    public Action<string>? Log { get; set; }

    /// <summary>
    /// Accept the unencrypted <see cref="LoginRequest"/> too. Off by default: the password would
    /// cross the network in clear.
    /// </summary>
    public bool AllowPlaintextLogin { get; set; }

    /// <summary>What clients pin (<see cref="Shared.Client.LoginClient.ExpectedServerFingerprint"/>).</summary>
    public string Fingerprint { get; }

    public LoginServer(LoginService login, WorldDirectory worlds, RSA serverKey)
    {
        _login = login;
        _worlds = worlds;
        _key = serverKey;
        _publicKey = serverKey.ExportParameters(includePrivateParameters: false);
        Fingerprint = LoginCrypto.Fingerprint(_publicKey.Modulus!, _publicKey.Exponent!);
        _net = new NetManager(_listener) { AutoRecycle = true };
        _listener.ConnectionRequestEvent += request => request.AcceptIfKey(ProtocolInfo.ConnectionKey);
        _listener.PeerConnectedEvent += OnConnected;
        _listener.PeerDisconnectedEvent += (peer, _) => _sessions.Remove(peer);
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

    private void OnConnected(NetPeer peer)
    {
        var session = new Session();
        _sessions[peer] = session;
        SendHello(peer, session);
    }

    private void SendHello(NetPeer peer, Session session) =>
        Send(peer, new ServerHello(_publicKey.Modulus!, _publicKey.Exponent!, session.Nonce));

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (!_sessions.TryGetValue(peer, out var session))
            return;
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
            case SecureLoginRequest secure:
                HandleSecureLogin(peer, session, secure);
                break;

            case LoginRequest plain when AllowPlaintextLogin:
                Complete(peer, session, _login.Authenticate(plain.Username, plain.Password, out var plainName), plain.Username, plainName, keys: null);
                break;

            case LoginRequest plain:
                Log?.Invoke($"{peer.Address}: plaintext login for '{plain.Username}' refused");
                Send(peer, LoginResponse.Failure(LoginResult.PlaintextRefused));
                break;

            case ServerListRequest when session.AccountId is not null:
                Send(peer, new ServerListResponse(_worlds.List()));
                break;

            case PlayRequest play when session.AccountId is int accountId:
                var response = _worlds.RequestPlay(accountId, play.WorldId, session.AccountName);
                Log?.Invoke($"{peer.Address}: account {accountId} play on world {play.WorldId} -> {(response.Accepted ? "key issued" : response.Message)}");
                // The world key lets whoever holds it enter the world as this account: never in clear
                // when the session has keys.
                Send(peer, session.Keys is { } keys ? Sealed.Of(response, keys) : response);
                break;

            default:
                Log?.Invoke($"{peer.Address}: unexpected {message.Type}, disconnecting");
                peer.Disconnect();
                break;
        }
    }

    private void HandleSecureLogin(NetPeer peer, Session session, SecureLoginRequest request)
    {
        if (!LoginCrypto.TryOpenCredentials(_key, request.SealedCredentials, out var nonce, out var secret, out var user, out var password)
            || !LoginCrypto.FixedTimeEquals(nonce, session.Nonce))
        {
            // Undecryptable, or a request recorded on another connection (replay): same answer as garbage.
            Log?.Invoke($"{peer.Address}: secure login rejected (bad key, layout or nonce)");
            Send(peer, LoginResponse.Failure(LoginResult.Malformed));
            RenewNonce(peer, session);
            return;
        }
        var keys = LoginCrypto.DeriveSessionKeys(secret, nonce);
        Complete(peer, session, _login.Authenticate(user, password, out var accountName), user, accountName, keys);
    }

    private void Complete(NetPeer peer, Session session, LoginResponse response, string user, string accountName, SessionKeys? keys)
    {
        Log?.Invoke($"{peer.Address}: login '{user}' -> {response.Result}");
        if (response.Result == LoginResult.Success)
        {
            session.AccountId = response.AccountId;
            session.AccountName = accountName;
            session.Keys = keys;
            Send(peer, response);
        }
        else
        {
            session.AccountId = null;
            session.Keys = null;
            Send(peer, response);
            RenewNonce(peer, session);
        }
    }

    /// <summary>One nonce per attempt: a failed attempt cannot be replayed, and the client may retry.</summary>
    private void RenewNonce(NetPeer peer, Session session)
    {
        session.Nonce = LoginCrypto.RandomBytes(LoginCrypto.NonceSize);
        SendHello(peer, session);
    }

    private void Send(NetPeer peer, IMessage message)
    {
        _writer.Reset();
        MessageCodec.Write(_writer, message);
        peer.Send(_writer, DeliveryMethod.ReliableOrdered);
    }
}
