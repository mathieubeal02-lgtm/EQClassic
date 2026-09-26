using System.Security.Cryptography;
using EQClassic.ClientCore;
using EQClassic.Server.Accounts;
using EQClassic.Server.Characters;
using EQClassic.Server.Login;
using EQClassic.Server.WorldServer;
using EQClassic.Server.Zone;
using EQClassic.Shared.Characters;
using EQClassic.Shared.Login;
using EQClassic.Shared.World;
using EQClassic.Tests.Characters;

namespace EQClassic.Tests.Client;

/// <summary>The engine-free client core against the three servers, as the Unity client will drive it.</summary>
public sealed class GameClientTests : IDisposable
{
    private static readonly RSA Key = RSA.Create(2048);
    private readonly LoginServer _login;
    private readonly WorldServer _world;
    private readonly ZoneServer _zones;
    private readonly InMemoryCharacterStore _characters = new();
    private readonly List<GameClient> _clients = new();

    public GameClientTests()
    {
        var directory = new WorldDirectory();
        var zoneData = new InMemoryZoneDataSource();
        zoneData.Zones["grobb"] = new ZoneData("grobb",
            [new SpawnPoint(1, new Vec3(0, 0, 4), 0, 3, [(new NpcTemplate(52001, "a_troll_guard", 9, 0, 20, 8f), 100)])],
            new Dictionary<int, Grid> { [3] = new Grid(3, GridType.BackAndForth, [new Waypoint(new Vec3(0, 0, 4), 0), new Waypoint(new Vec3(0, 200, 4), 0)]) },
            [new ZoneLine(396, new Vec3(50.34f, -129.93f, 3.13f), 20, "innothule", new Vec3(-612.29f, -2789.26f, -31.44f))]);
        zoneData.Zones["innothule"] = new ZoneData("innothule", [], new Dictionary<int, Grid>());
        var keys = new ZoneKeys();
        _zones = new ZoneServer(keys, n => zoneData.Load(n) is { } d ? new ZoneInstance(d) : null) { Characters = _characters };
        _zones.Start(0);
        var creation = new InMemoryCreationData();
        creation.StartPositions[("grobb", 9, 10)] = (40, -60, 3.13f); // 70 units from the zone line
        var worldAccounts = new InMemoryWorldAccountStore();
        worldAccounts.Link(16, 24, "bot");
        _world = new WorldServer(1, directory, worldAccounts, _characters, creation) { Zones = new ZoneHandoff(keys, "127.0.0.1", _zones.Port) };
        _world.Start(0);
        directory.Register(new WorldServerInfo(1, "EverQuest Classic", "127.0.0.1", _world.Port, 0, WorldStatus.Up));
        _characters.Add(ProfileBuilder.Record(15, 24, "Qbot", 9, 10, 1, "grobb", x: 10, y: 10, z: 4));
        _login = new LoginServer(new LoginService(new InMemoryAccountStore([new LoginAccount(16, "bot", PasswordHash.Sha1Hex("bot"))])), directory, Key);
        _login.Start(0);
    }

    public void Dispose()
    {
        foreach (var c in _clients) c.Dispose();
        _login.Dispose();
        _world.Dispose();
        _zones.Dispose();
    }

    [Fact]
    public void Plays_from_login_to_zone()
    {
        var client = InZone("Qbot");

        Assert.Equal("grobb", client.Zone!.Zone);
        Assert.Contains(client.Zone.Entities, e => e.Spawn.Name == "a_troll_guard" && e.ModelCode == "trm");
        Assert.Equal(new Vec3(10, 10, 4), client.Player!.Position);
    }

    [Fact]
    public void Wrong_password_stays_on_the_login_screen_and_can_retry()
    {
        var client = New();
        client.Connect("127.0.0.1", _login.Port);
        Run(() => client.State == GameState.Login);
        client.Login("bot", "nope");
        Run(() => client.LastError is not null);
        Assert.Equal(LoginMessages.BadCredentials, client.LastError);
        Run(() => client.State == GameState.Login);
        client.Login("bot", "bot");
        Assert.True(Run(() => client.State == GameState.ServerSelect));
    }

    [Fact]
    public void Creates_a_character_then_plays_it()
    {
        var client = AtCharacterSelect();
        client.CreateCharacter(new CreateCharacterRequest("Qfresh", 9, 10, 0, 203, 1, "grobb", new CharacterStats(108, 119, 45, 75, 52, 83, 95)));
        Assert.True(Run(() => client.Characters.Any(c => c.Name == "Qfresh")));

        client.EnterWorld("Qfresh");
        Assert.True(Run(() => client.State == GameState.InZone));
        Assert.Equal(new Vec3(40, -60, 3.13f), client.Player!.Position);
    }

    [Fact]
    public void Other_players_are_seen_moving_smoothly()
    {
        var watcher = InZone("Qbot");
        var walker = AtCharacterSelect();
        walker.CreateCharacter(new CreateCharacterRequest("Qwalk", 9, 10, 0, 203, 1, "grobb", new CharacterStats(75, 75, 75, 75, 75, 75, 75)));
        Run(() => walker.Characters.Any(c => c.Name == "Qwalk"));
        walker.EnterWorld("Qwalk");
        Run(() => walker.State == GameState.InZone);
        int walkerId = walker.Player!.EntityId;
        Assert.True(Run(() => watcher.Zone!.Get(walkerId) is not null), "the watcher never saw the walker arrive");

        for (int i = 0; i < 30; i++) // walk north for ~1.5 s
        {
            walker.Player.Move(forward: 1, strafe: 0, turn: 0, seconds: 0.05f);
            Run(() => false, 50);
        }
        Assert.True(Run(() => watcher.Zone!.Get(walkerId)?.Latest.Y > -50));
        var drawn = watcher.Zone!.Interpolated(walkerId, watcher.Now);
        Assert.InRange(drawn.Position.Y, -61f, walker.Player.Position.Y);
    }

    [Fact]
    public void Follows_a_zone_line_into_the_next_zone()
    {
        var client = InZone("Qbot");
        var entered = new List<string>();
        client.ZoneEntered += z => entered.Add(z.Zone);
        // Walk towards Grobb's zone line at (50, -130), ~150 units away at 45 u/s.
        for (int i = 0; i < 100 && client.Zone?.Zone != "innothule"; i++)
        {
            if (client.Player is { } p)
            {
                float dx = 50 - p.Position.X, dy = -128 - p.Position.Y;
                SetHeading(p, MathF.Atan2(dx, dy) * 180f / MathF.PI);
                p.Move(1, 0, 0, 0.05f);
            }
            Run(() => false, 50);
        }
        Assert.True(Run(() => client.State == GameState.InZone && client.Zone?.Zone == "innothule"));
        Assert.Equal(["innothule"], entered);
        Assert.Equal(-612.29f, client.Player!.Position.X, precision: 2);
    }

    private static void SetHeading(LocalPlayer p, float heading)
    {
        float turn = ((heading - p.Heading) % 360f + 540f) % 360f - 180f;
        p.Move(0, 0, turn / (LocalPlayer.TurnDegreesPerSecond * 1f), 1f);
    }

    private GameClient InZone(string character)
    {
        var client = AtCharacterSelect();
        client.EnterWorld(character);
        Assert.True(Run(() => client.State == GameState.InZone), $"stuck in {client.State}: {client.LastError}");
        return client;
    }

    private GameClient AtCharacterSelect()
    {
        var client = New();
        client.Connect("127.0.0.1", _login.Port);
        Assert.True(Run(() => client.State == GameState.Login));
        client.Login("bot", "bot");
        Assert.True(Run(() => client.State == GameState.ServerSelect), $"stuck in {client.State}: {client.LastError}");
        client.SelectWorld(client.Worlds[0].Id);
        Assert.True(Run(() => client.State == GameState.CharacterSelect), $"stuck in {client.State}: {client.LastError}");
        return client;
    }

    private GameClient New()
    {
        var c = new GameClient();
        _clients.Add(c);
        return c;
    }

    private bool Run(Func<bool> condition, int ms = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < deadline)
        {
            _login.PollEvents();
            _world.PollEvents();
            _zones.PollEvents();
            _zones.Tick(0.01f);
            foreach (var c in _clients) c.Update(0.01f);
            if (condition())
                return true;
            Thread.Sleep(5);
        }
        return false;
    }
}
