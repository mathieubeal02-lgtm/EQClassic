using System.Security.Cryptography;
using EQClassic.Server.Accounts;
using EQClassic.Server.Characters;
using EQClassic.Server.Login;
using EQClassic.Server.WorldServer;
using EQClassic.Shared.Characters;
using EQClassic.Shared.Client;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;

namespace EQClassic.Tests.Characters;

/// <summary>Login and World in one process, as Program runs them, driven over UDP by the client library.</summary>
public sealed class WorldServerTests : IDisposable
{
    private static readonly RSA Key = RSA.Create(2048);
    private readonly WorldDirectory _directory = new();
    private readonly InMemoryWorldAccountStore _worldAccounts = new(firstId: 24);
    private readonly InMemoryCharacterStore _characters = new();
    private readonly LoginServer _login;
    private readonly WorldServer _world;

    public WorldServerTests()
    {
        var accounts = new InMemoryAccountStore([
            new LoginAccount(1, "test", PasswordHash.Sha1Hex("test")),
            new LoginAccount(16, "bot", PasswordHash.Sha1Hex("bot"))]);
        _world = new WorldServer(1, _directory, _worldAccounts, _characters);
        _world.Start(0);
        _directory.Register(new WorldServerInfo(1, "EverQuest Classic", "127.0.0.1", _world.Port, 0, WorldStatus.Up));
        _login = new LoginServer(new LoginService(accounts), _directory, Key);
        _login.Start(0);

        _worldAccounts.Link(lsAccountId: 16, worldAccountId: 24, "bot");
        _characters.Add(ProfileBuilder.Record(16, 24, "Qbottwo", 9, 10, 1, "grobb", x: 0, y: -100, z: 4));
        _characters.Add(ProfileBuilder.Record(15, 24, "Qbot", 9, 10, 1, "innothule", x: -338, y: -2407, z: -16.4f));
        _characters.Add(ProfileBuilder.Record(1, 1, "Qtest", 9, 10, 59, "permafrost"));
    }

    public void Dispose()
    {
        _login.Dispose();
        _world.Dispose();
    }

    [Fact]
    public void Login_then_world_lists_the_characters_by_name()
    {
        var (key, _) = LogIn("bot", "bot");
        using var world = Connect(_world.Port);

        var list = Assert.IsType<WorldLoginResponse>(Exchange(world, new WorldLoginRequest("LS#16", key)));

        Assert.True(list.Accepted, list.Message);
        Assert.Equal(["Qbot", "Qbottwo"], list.Characters.Select(c => c.Name));
        Assert.Equal(new CharacterSummary("Qbot", 9, 10, 1, 0, "innothule"), list.Characters[0]);
    }

    [Fact]
    public void Entering_the_world_gives_the_saved_zone_and_position()
    {
        var (key, _) = LogIn("bot", "bot");
        using var world = Connect(_world.Port);
        Assert.True(Assert.IsType<WorldLoginResponse>(Exchange(world, new WorldLoginRequest("LS#16", key))).Accepted);

        var enter = Assert.IsType<EnterWorldResponse>(Exchange(world, new EnterWorldRequest("qbot")));

        Assert.True(enter.Accepted);
        Assert.Equal("innothule", enter.Zone);
        Assert.Equal((-338f, -2407f, -16.4f), (enter.X, enter.Y, enter.Z));
    }

    [Fact]
    public void Someone_elses_character_cannot_be_entered()
    {
        var (key, _) = LogIn("bot", "bot");
        using var world = Connect(_world.Port);
        Exchange(world, new WorldLoginRequest("LS#16", key));

        var enter = Assert.IsType<EnterWorldResponse>(Exchange(world, new EnterWorldRequest("Qtest")));

        Assert.False(enter.Accepted);
        Assert.Equal(WorldServer.UnknownCharacter, enter.Message);
    }

    [Fact]
    public void A_world_key_works_once()
    {
        var (key, _) = LogIn("bot", "bot");
        using (var first = Connect(_world.Port))
            Assert.True(Assert.IsType<WorldLoginResponse>(Exchange(first, new WorldLoginRequest("LS#16", key))).Accepted);

        using var second = Connect(_world.Port);
        var again = Assert.IsType<WorldLoginResponse>(Exchange(second, new WorldLoginRequest("LS#16", key)));

        Assert.False(again.Accepted);
        Assert.Equal(WorldServer.BadKey, again.Message);
    }

    [Theory]
    [InlineData("LS#16", "000000000000000")]
    [InlineData("LS#1", null)]   // a key issued to account 16 presented for account 1
    [InlineData("bogus", null)]
    public void Wrong_account_or_key_is_refused(string sessionId, string? key)
    {
        var (issued, _) = LogIn("bot", "bot");
        using var world = Connect(_world.Port);

        var response = Assert.IsType<WorldLoginResponse>(Exchange(world, new WorldLoginRequest(sessionId, key ?? issued)));

        Assert.False(response.Accepted);
    }

    [Fact]
    public void First_visit_creates_the_world_account_like_the_legacy_world()
    {
        var (key, _) = LogIn("test", "test");
        using var world = Connect(_world.Port);

        var list = Assert.IsType<WorldLoginResponse>(Exchange(world, new WorldLoginRequest("LS#1", key)));

        Assert.True(list.Accepted);
        Assert.Empty(list.Characters); // Qtest belongs to world account 1, the new account is 25
        Assert.Equal(25, _worldAccounts.ResolveOrCreate(1, "test"));
    }

    private (string Key, int Port) LogIn(string user, string password)
    {
        using var client = Connect(_login.Port);
        Assert.True(Pump(client, () => client.IsReadyToLogin));
        client.Login(user, password);
        Assert.Equal(LoginResult.Success, Assert.IsType<LoginResponse>(Next(client, m => m is LoginResponse)).Result);
        client.Send(new PlayRequest(1));
        var play = Assert.IsType<PlayResponse>(Next(client, m => m is PlayResponse));
        Assert.True(play.Accepted);
        return (play.SessionKey, play.Port);
    }

    private LoginClient Connect(int port)
    {
        var client = new LoginClient();
        client.Connect("127.0.0.1", port);
        Assert.True(Pump(client, () => client.State == ConnectionState.Connected), "cannot connect");
        return client;
    }

    private IMessage Exchange(LoginClient client, IMessage message)
    {
        client.Send(message);
        return Next(client, m => m.Type != MessageType.ServerHello);
    }

    private readonly Queue<IMessage> _inbox = new();

    private IMessage Next(LoginClient client, Func<IMessage, bool> match)
    {
        IMessage? found = null;
        Assert.True(Pump(client, () =>
        {
            while (_inbox.Count > 0)
            {
                var m = _inbox.Dequeue();
                if (match(m)) { found = m; return true; }
            }
            return false;
        }), "no answer");
        return found!;
    }

    private bool Pump(LoginClient client, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            _login.PollEvents();
            _world.PollEvents();
            foreach (var m in client.Poll())
                _inbox.Enqueue(m);
            if (condition())
                return true;
            Thread.Sleep(5);
        }
        return false;
    }
}
