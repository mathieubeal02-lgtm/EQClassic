using EQClassic.Server.Accounts;
using EQClassic.Server.Login;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EQClassic.Tests.Login;

/// <summary>End to end over UDP on localhost: a LiteNetLib client against <see cref="LoginServer"/>.</summary>
public sealed class LoginServerTests : IDisposable
{
    private readonly LoginServer _server;

    public LoginServerTests()
    {
        var worlds = new WorldDirectory();
        worlds.Register(new WorldServerInfo(1, "EverQuest Classic", "127.0.0.1", 9000, 0, WorldStatus.Up));
        _server = new LoginServer(new LoginService(InMemoryAccountStore.WithTestAccount()), worlds);
        _server.Start(0);
    }

    public void Dispose() => _server.Dispose();

    [Fact]
    public void Test_account_authenticates_over_the_network()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));

        var response = Assert.IsType<LoginResponse>(client.Exchange(new LoginRequest("test", "test")));

        Assert.Equal(LoginResult.Success, response.Result);
        Assert.Equal("LS#1", response.SessionId);
    }

    [Fact]
    public void Bad_password_is_refused_over_the_network()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));

        var response = Assert.IsType<LoginResponse>(client.Exchange(new LoginRequest("test", "wrong")));

        Assert.Equal(LoginResult.BadCredentials, response.Result);
        Assert.Equal(LoginMessages.BadCredentials, response.Message);
    }

    [Fact]
    public void Full_flow_login_server_list_and_world_key()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));
        Assert.Equal(LoginResult.Success, Assert.IsType<LoginResponse>(client.Exchange(new LoginRequest("test", "test"))).Result);

        var list = Assert.IsType<ServerListResponse>(client.Exchange(new ServerListRequest()));
        var world = Assert.Single(list.Worlds);
        Assert.Equal("EverQuest Classic", world.Name);

        var play = Assert.IsType<PlayResponse>(client.Exchange(new PlayRequest(world.Id)));
        Assert.True(play.Accepted);
        Assert.True(SessionKeys.IsWellFormed(play.SessionKey));
    }

    [Fact]
    public void Server_list_before_login_disconnects()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));

        client.Send(new ServerListRequest());

        Assert.True(client.WaitUntil(() => !client.IsConnected), "server should drop a client asking for worlds before logging in");
    }

    [Fact]
    public void Wrong_protocol_version_cannot_connect()
    {
        using var client = new TestClient(_server);
        Assert.False(client.Connect("EQClassic/0"));
    }

    [Fact]
    public void Garbage_gets_a_malformed_answer()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));

        var response = Assert.IsType<LoginResponse>(client.ExchangeRaw([(byte)MessageType.LoginRequest, 0xFF]));

        Assert.Equal(LoginResult.Malformed, response.Result);
    }

    /// <summary>A LiteNetLib client that pumps both itself and the server while waiting.</summary>
    private sealed class TestClient : IDisposable
    {
        private readonly LoginServer _server;
        private readonly EventBasedNetListener _listener = new();
        private readonly NetManager _net;
        private readonly Queue<IMessage> _received = new();
        private NetPeer? _peer;
        private bool _connected;

        public TestClient(LoginServer server)
        {
            _server = server;
            _net = new NetManager(_listener) { AutoRecycle = true };
            _listener.PeerConnectedEvent += _ => _connected = true;
            _listener.PeerDisconnectedEvent += (_, _) => _connected = false;
            _listener.NetworkReceiveEvent += (_, reader, _, _) => _received.Enqueue(MessageCodec.Read(reader));
            _net.Start();
        }

        public bool IsConnected => _connected;

        public bool Connect(string key)
        {
            _peer = _net.Connect("127.0.0.1", _server.Port, key);
            return WaitUntil(() => _connected, TimeSpan.FromSeconds(3));
        }

        public void Send(IMessage message) => SendRaw(MessageCodec.Encode(message));

        public void SendRaw(byte[] data) => _peer!.Send(data, DeliveryMethod.ReliableOrdered);

        public IMessage Exchange(IMessage message) => ExchangeRaw(MessageCodec.Encode(message));

        public IMessage ExchangeRaw(byte[] data)
        {
            SendRaw(data);
            Assert.True(WaitUntil(() => _received.Count > 0), "no answer from the login server");
            return _received.Dequeue();
        }

        public bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                _server.PollEvents();
                _net.PollEvents();
                if (condition())
                    return true;
                Thread.Sleep(5);
            }
            return false;
        }

        public void Dispose() => _net.Stop();
    }
}
