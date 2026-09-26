using EQClassic.Server.Accounts;
using EQClassic.Server.Login;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using System.Security.Cryptography;
using EQClassic.Shared.Client;
using EQClassic.Shared.Security;

namespace EQClassic.Tests.Login;

/// <summary>End to end over UDP on localhost: a LiteNetLib client against <see cref="LoginServer"/>.</summary>
public sealed class LoginServerTests : IDisposable
{
    private readonly LoginServer _server;

    public LoginServerTests()
    {
        var worlds = new WorldDirectory();
        worlds.Register(new WorldServerInfo(1, "EverQuest Classic", "127.0.0.1", 9000, 0, WorldStatus.Up));
        _server = new LoginServer(new LoginService(InMemoryAccountStore.WithTestAccount()), worlds, ServerKey);
        _server.Start(0);
    }

    // One key for the whole class: RSA generation is the slow part.
    private static readonly RSA ServerKey = RSA.Create(2048);

    public void Dispose() => _server.Dispose();

    [Fact]
    public void Test_account_authenticates_over_the_network()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));

        var response = Assert.IsType<LoginResponse>(client.Login("test", "test"));

        Assert.Equal(LoginResult.Success, response.Result);
        Assert.Equal("LS#1", response.SessionId);
    }

    [Fact]
    public void Bad_password_is_refused_over_the_network()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));

        var response = Assert.IsType<LoginResponse>(client.Login("test", "wrong"));

        Assert.Equal(LoginResult.BadCredentials, response.Result);
        Assert.Equal(LoginMessages.BadCredentials, response.Message);
    }

    [Fact]
    public void Full_flow_login_server_list_and_world_key()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));
        Assert.Equal(LoginResult.Success, Assert.IsType<LoginResponse>(client.Login("test", "test")).Result);

        var list = Assert.IsType<ServerListResponse>(client.Exchange(new ServerListRequest()));
        var world = Assert.Single(list.Worlds);
        Assert.Equal("EverQuest Classic", world.Name);

        var play = Assert.IsType<PlayResponse>(client.Exchange(new PlayRequest(world.Id)));
        Assert.True(play.Accepted);
        Assert.True(WorldKeys.IsWellFormed(play.SessionKey));
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

    [Fact]
    public void Plaintext_login_is_refused_by_default()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));
        client.WaitHello();

        var response = Assert.IsType<LoginResponse>(client.Exchange(new LoginRequest("test", "test")));

        Assert.Equal(LoginResult.PlaintextRefused, response.Result);
    }

    [Fact]
    public void Plaintext_login_works_when_explicitly_allowed()
    {
        _server.AllowPlaintextLogin = true;
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));
        client.WaitHello();

        Assert.Equal("LS#1", Assert.IsType<LoginResponse>(client.Exchange(new LoginRequest("test", "test"))).SessionId);
    }

    [Fact]
    public void A_recorded_login_cannot_be_replayed_on_another_connection()
    {
        using var victim = new TestClient(_server);
        Assert.True(victim.Connect(ProtocolInfo.ConnectionKey));
        var hello = victim.WaitHello();
        // What an eavesdropper would capture from the victim's connection:
        var recorded = new SecureLoginRequest(LoginCrypto.SealCredentials(hello.Modulus, hello.Exponent, hello.Nonce,
            LoginCrypto.RandomBytes(LoginCrypto.SecretSize), "test", "test"));

        using var attacker = new TestClient(_server);
        Assert.True(attacker.Connect(ProtocolInfo.ConnectionKey));
        attacker.WaitHello();
        var response = Assert.IsType<LoginResponse>(attacker.Exchange(recorded));

        Assert.Equal(LoginResult.Malformed, response.Result);
    }

    [Fact]
    public void Retry_after_a_wrong_password_uses_a_new_nonce_and_succeeds()
    {
        using var client = new TestClient(_server);
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));

        Assert.Equal(LoginResult.BadCredentials, Assert.IsType<LoginResponse>(client.Login("test", "nope")).Result);
        Assert.Equal(LoginResult.Success, Assert.IsType<LoginResponse>(client.Login("test", "test")).Result);
    }

    [Fact]
    public void Pinned_fingerprint_mismatch_disconnects_before_sending_credentials()
    {
        using var client = new TestClient(_server);
        client.Raw.ExpectedServerFingerprint = new string('0', 64);
        client.Connect(ProtocolInfo.ConnectionKey);

        Assert.True(client.WaitUntil(() => client.Raw.DisconnectReason is not null && !client.IsConnected));
        Assert.Contains("fingerprint", client.Raw.DisconnectReason);
        Assert.False(client.Raw.IsReadyToLogin);
    }

    [Fact]
    public void Pinned_fingerprint_match_logs_in()
    {
        using var client = new TestClient(_server);
        client.Raw.ExpectedServerFingerprint = _server.Fingerprint.ToUpperInvariant();
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));
        Assert.Equal(LoginResult.Success, Assert.IsType<LoginResponse>(client.Login("test", "test")).Result);
    }

    [Fact]
    public void World_key_travels_sealed()
    {
        using var client = new TestClient(_server);
        var wire = new List<MessageType>();
        client.Raw.RawMessageReceived += wire.Add;
        Assert.True(client.Connect(ProtocolInfo.ConnectionKey));
        Assert.Equal(LoginResult.Success, Assert.IsType<LoginResponse>(client.Login("test", "test")).Result);

        var play = Assert.IsType<PlayResponse>(client.Exchange(new PlayRequest(1)));

        Assert.True(play.Accepted);
        Assert.Contains(MessageType.Sealed, wire);
        Assert.DoesNotContain(MessageType.PlayResponse, wire);
    }

    /// <summary>Drives the real client library (EQClassic.Shared.Client.LoginClient) and pumps the server while waiting.</summary>
    private sealed class TestClient : IDisposable
    {
        private readonly LoginServer _server;
        private readonly LoginClient _client = new();
        private readonly Queue<IMessage> _received = new();

        public TestClient(LoginServer server) => _server = server;

        public bool IsConnected => _client.State == ConnectionState.Connected;

        public bool Connect(string key)
        {
            _client.Connect("127.0.0.1", _server.Port, key);
            return WaitUntil(() => _client.State != ConnectionState.Connecting, TimeSpan.FromSeconds(3)) && IsConnected;
        }

        public LoginClient Raw => _client;

        public void Send(IMessage message) => _client.Send(message);

        public IMessage Login(string user, string password)
        {
            Assert.True(WaitUntil(() => _client.IsReadyToLogin), "no ServerHello");
            DrainHellos();
            _client.Login(user, password);
            return NextNonHello();
        }

        public ServerHello WaitHello()
        {
            Assert.True(WaitUntil(() => _client.IsReadyToLogin), "no ServerHello");
            return Assert.IsType<ServerHello>(_received.Dequeue());
        }

        private void DrainHellos()
        {
            while (_received.Count > 0 && _received.Peek() is ServerHello)
                _received.Dequeue();
        }

        public IMessage NextNonHello()
        {
            while (true)
            {
                var m = Next();
                if (m is not ServerHello)
                    return m;
            }
        }

        public IMessage Exchange(IMessage message)
        {
            DrainHellos();
            _client.Send(message);
            return NextNonHello();
        }

        public IMessage ExchangeRaw(byte[] data)
        {
            DrainHellos();
            _client.SendRaw(data);
            return NextNonHello();
        }

        private IMessage Next()
        {
            Assert.True(WaitUntil(() => _received.Count > 0), "no answer from the login server");
            return _received.Dequeue();
        }

        public bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
            while (DateTime.UtcNow < deadline)
            {
                _server.PollEvents();
                foreach (var m in _client.Poll())
                    _received.Enqueue(m);
                if (condition())
                    return true;
                Thread.Sleep(5);
            }
            return false;
        }

        public void Dispose() => _client.Dispose();
    }
}
