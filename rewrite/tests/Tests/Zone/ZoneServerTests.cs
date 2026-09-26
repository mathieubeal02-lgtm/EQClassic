using System.Security.Cryptography;
using EQClassic.Server.Accounts;
using EQClassic.Server.Characters;
using EQClassic.Server.Login;
using EQClassic.Server.WorldServer;
using EQClassic.Server.Zone;
using EQClassic.Shared.Characters;
using EQClassic.Shared.Client;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;
using EQClassic.Tests.Characters;

namespace EQClassic.Tests.Zone;

/// <summary>Login → World → zone over UDP, the whole chain of Program in one test.</summary>
public sealed class ZoneServerTests : IDisposable
{
    private static readonly RSA Key = RSA.Create(2048);
    private readonly WorldDirectory _directory = new();
    private readonly LoginServer _login;
    private readonly WorldServer _world;
    private readonly ZoneServer _zones;
    private readonly List<(LoginClient Client, Queue<IMessage> Inbox)> _clients = new();
    private readonly InMemoryCharacterStore _characters = new();

    public ZoneServerTests()
    {
        var zoneData = new InMemoryZoneDataSource();
        zoneData.Zones["grobb"] = new ZoneData("grobb",
            [new SpawnPoint(1, new Vec3(0, 0, 4), 0, 3, [(new NpcTemplate(52001, "a_troll_guard", 9, 0, 20, 8f), 100)])],
            new Dictionary<int, Grid> { [3] = new Grid(3, GridType.BackAndForth, [new Waypoint(new Vec3(0, 0, 4), 0), new Waypoint(new Vec3(0, 200, 4), 0)]) },
            [new ZoneLine(396, new Vec3(50.34f, -129.93f, 3.13f), 20, "innothule", new Vec3(-612.29f, -2789.26f, -31.44f))]);
        zoneData.Zones["innothule"] = new ZoneData("innothule", [], new Dictionary<int, Grid>());
        // A busy zone: 300 patrolling NPCs, all near the entrance, plus one far away.
        var patrol = new Grid(9, GridType.BackAndForth, [new Waypoint(new Vec3(0, 0, 0), 0), new Waypoint(new Vec3(50, 0, 0), 0)]);
        var far = new Grid(10, GridType.BackAndForth, [new Waypoint(new Vec3(5000, 5000, 0), 0), new Waypoint(new Vec3(5050, 5000, 0), 0)]);
        zoneData.Zones["qeynos2"] = new ZoneData("qeynos2",
            [.. Enumerable.Range(1, 300).Select(i => new SpawnPoint(i, new Vec3(0, 0, 0), 0, 9, [(new NpcTemplate(i, "npc" + i, 1, 0, 1, 6f), 100)])),
             new SpawnPoint(999, new Vec3(5000, 5000, 0), 0, 10, [(new NpcTemplate(999, "far_away", 1, 0, 1, 6f), 100)])],
            new Dictionary<int, Grid> { [9] = patrol, [10] = far });
        var keys = new ZoneKeys();
        _zones = new ZoneServer(keys, name => zoneData.Load(name) is { } d ? new ZoneInstance(d) : null) { Characters = _characters };
        _zones.Start(0);

        var characters = _characters;
        characters.Add(ProfileBuilder.Record(15, 24, "Qbot", 9, 10, 1, "grobb", x: 10, y: 10, z: 4));
        characters.Add(ProfileBuilder.Record(16, 24, "Qbottwo", 9, 10, 1, "grobb", x: 20, y: 20, z: 4));
        characters.Add(ProfileBuilder.Record(17, 24, "Qlost", 9, 10, 1, "nowhere"));
        characters.Add(ProfileBuilder.Record(18, 24, "Qbusy", 9, 10, 1, "qeynos2", x: 10, y: 0, z: 0));
        var worldAccounts = new InMemoryWorldAccountStore();
        worldAccounts.Link(16, 24, "bot");
        _world = new WorldServer(1, _directory, worldAccounts, characters);
        _world.Start(0);
        _world.Zones = new ZoneHandoff(keys, "127.0.0.1", _zones.Port);
        _directory.Register(new WorldServerInfo(1, "EverQuest Classic", "127.0.0.1", _world.Port, 0, WorldStatus.Up));
        _login = new LoginServer(new LoginService(new InMemoryAccountStore([new LoginAccount(16, "bot", PasswordHash.Sha1Hex("bot"))])), _directory, Key);
        _login.Start(0);
    }

    public void Dispose()
    {
        foreach (var (c, _) in _clients) c.Dispose();
        _login.Dispose();
        _world.Dispose();
        _zones.Dispose();
    }

    [Fact]
    public void A_character_enters_its_zone_and_sees_the_npcs_move()
    {
        var (zone, entered) = EnterZone("Qbot");

        Assert.True(entered.Accepted, entered.Message);
        Assert.Equal("grobb", entered.Zone);
        var guard = Assert.Single(entered.Entities, e => !e.IsPlayer);
        Assert.Equal("a_troll_guard", guard.Name);
        Assert.Contains(entered.Entities, e => e.IsPlayer && e.Name == "Qbot" && e.Id == entered.YourEntityId);

        var update = Assert.IsType<EntityPositions>(Next(zone, m => m is EntityPositions, tick: true));
        var moved = Assert.Single(update.Positions);
        Assert.Equal(guard.Id, moved.Id);
        Assert.True(moved.Y > 0);
    }

    [Fact]
    public void Players_see_each_other_arrive_move_and_leave()
    {
        var (first, firstEntered) = EnterZone("Qbot");
        var (second, secondEntered) = EnterZone("Qbottwo");

        var spawned = Assert.IsType<EntitySpawned>(Next(first, m => m is EntitySpawned));
        Assert.Equal(("Qbottwo", secondEntered.YourEntityId), (spawned.Entity.Name, spawned.Entity.Id));
        Assert.Contains(secondEntered.Entities, e => e.Id == firstEntered.YourEntityId);

        Pump(second.Client, () => false, ms: 100);
        second.Client.Send(new PlayerMove(25, 25, 4, 45));
        var seen = Assert.IsType<EntityPositions>(Next(first, m => m is EntityPositions p && p.Positions.Any(x => x.Id == secondEntered.YourEntityId), tick: true));
        Assert.Equal(25f, seen.Positions.Single(x => x.Id == secondEntered.YourEntityId).X);

        second.Client.Disconnect();
        Assert.Equal(secondEntered.YourEntityId, Assert.IsType<EntityRemoved>(Next(first, m => m is EntityRemoved)).Id);
    }

    [Fact]
    public void A_busy_zone_streams_every_nearby_npc_and_nothing_far_away()
    {
        var (zone, entered) = EnterZone("Qbusy");
        Assert.Equal(301, entered.Entities.Count(e => !e.IsPlayer));
        int farId = entered.Entities.Single(e => e.Name == "far_away").Id;

        var seen = new HashSet<int>();
        uint? tick = null;
        Assert.True(Pump(zone.Client, () =>
        {
            while (zone.Inbox.Count > 0)
                if (zone.Inbox.Dequeue() is EntityPositions p)
                {
                    tick ??= p.Tick;
                    if (p.Tick == tick)
                        foreach (var e in p.Positions) seen.Add(e.Id);
                }
            return seen.Count >= 300;
        }, tick: true), $"saw {seen.Count} of 300 nearby NPCs in one tick");
        Assert.DoesNotContain(farId, seen);
    }

    [Fact]
    public void A_teleport_is_corrected()
    {
        var (zone, _) = EnterZone("Qbot");
        Pump(zone.Client, () => false, ms: 100, tick: true);

        zone.Client.Send(new PlayerMove(5000, 5000, 4, 0));

        var correction = Assert.IsType<MoveCorrection>(Next(zone, m => m is MoveCorrection));
        Assert.Equal((10f, 10f), (correction.X, correction.Y));
    }

    [Fact]
    public void Walking_onto_a_zone_line_moves_the_character_to_the_next_zone_and_saves_it()
    {
        var (zone, _) = EnterZone("Qbot");
        Pump(zone.Client, () => false, ms: 700, tick: true); // let time pass: the zone line is ~150 units away
        zone.Client.Send(new PlayerMove(50, -128, 3.13f, 180));

        var change = Assert.IsType<ZoneChange>(Next(zone, m => m is ZoneChange, tick: true));
        Assert.Equal(("innothule", _zones.Port), (change.Zone, change.Port));
        Assert.Equal((-612.29f, -2789.26f), (change.X, change.Y));
        var saved = _characters.ListForAccount(24).Single(c => c.Profile.Name == "Qbot").Profile;
        Assert.Equal(("innothule", -612.29f, -2789.26f), (saved.Zone, saved.X, saved.Y));

        var next = Connect(change.Port);
        var entered = Assert.IsType<ZoneEnterResponse>(Exchange(next, new ZoneEnterRequest("Qbot", change.ZoneKey)));
        Assert.True(entered.Accepted, entered.Message);
        Assert.Equal("innothule", entered.Zone);
        Assert.Contains(entered.Entities, e => e.Name == "Qbot" && e.X == -612.29f);
    }

    [Fact]
    public void Leaving_saves_the_last_position()
    {
        var (zone, _) = EnterZone("Qbottwo");
        Pump(zone.Client, () => false, ms: 300, tick: true);
        zone.Client.Send(new PlayerMove(22, 23, 4, 0));
        Pump(zone.Client, () => false, ms: 200, tick: true);
        zone.Client.Disconnect();
        Assert.True(Pump(zone.Client, () => _characters.ListForAccount(24).Single(c => c.Profile.Name == "Qbottwo").Profile.X == 22f, tick: true));
    }

    [Fact]
    public void Zone_key_works_once()
    {
        var (key, port) = WorldEnter("Qbot");
        var first = Connect(port);
        Assert.True(Assert.IsType<ZoneEnterResponse>(Exchange(first, new ZoneEnterRequest("Qbot", key))).Accepted);

        var second = Connect(port);
        var again = Assert.IsType<ZoneEnterResponse>(Exchange(second, new ZoneEnterRequest("Qbot", key)));
        Assert.False(again.Accepted);
        Assert.Equal(ZoneServer.BadKey, again.Message);
    }

    [Fact]
    public void Unknown_zone_is_unavailable()
    {
        var (key, port) = WorldEnter("Qlost");
        var zone = Connect(port);
        var response = Assert.IsType<ZoneEnterResponse>(Exchange(zone, new ZoneEnterRequest("Qlost", key)));
        Assert.False(response.Accepted);
        Assert.Equal(ZoneServer.UnknownZone, response.Message);
    }

    private ((LoginClient Client, Queue<IMessage> Inbox) Zone, ZoneEnterResponse Entered) EnterZone(string character)
    {
        var (key, port) = WorldEnter(character);
        var zone = Connect(port);
        var entered = Assert.IsType<ZoneEnterResponse>(Exchange(zone, new ZoneEnterRequest(character, key)));
        return (zone, entered);
    }

    private (string Key, int Port) WorldEnter(string character)
    {
        var login = Connect(_login.Port);
        Assert.True(Pump(login.Client, () => login.Client.IsReadyToLogin));
        login.Client.Login("bot", "bot");
        Assert.Equal(LoginResult.Success, Assert.IsType<LoginResponse>(Next(login, m => m is LoginResponse)).Result);
        var play = Assert.IsType<PlayResponse>(Exchange(login, new PlayRequest(1)));

        var world = Connect(play.Port);
        Assert.True(Assert.IsType<WorldLoginResponse>(Exchange(world, new WorldLoginRequest("LS#16", play.SessionKey))).Accepted);
        var enter = Assert.IsType<EnterWorldResponse>(Exchange(world, new EnterWorldRequest(character)));
        Assert.True(enter.Accepted, enter.Message);
        return (enter.ZoneKey, enter.ZonePort);
    }

    private (LoginClient Client, Queue<IMessage> Inbox) Connect(int port)
    {
        var client = new LoginClient();
        var entry = (client, new Queue<IMessage>());
        _clients.Add(entry);
        client.Connect("127.0.0.1", port);
        Assert.True(Pump(client, () => client.State == ConnectionState.Connected), "cannot connect");
        return entry;
    }

    private IMessage Exchange((LoginClient Client, Queue<IMessage> Inbox) c, IMessage message)
    {
        c.Client.Send(message);
        return Next(c, m => m.Type != MessageType.ServerHello && m is not EntityPositions && m is not EntitySpawned);
    }

    private IMessage Next((LoginClient Client, Queue<IMessage> Inbox) c, Func<IMessage, bool> match, bool tick = false)
    {
        IMessage? found = null;
        Assert.True(Pump(c.Client, () =>
        {
            while (c.Inbox.Count > 0)
            {
                var m = c.Inbox.Dequeue();
                if (match(m)) { found = m; return true; }
            }
            return false;
        }, tick: tick), "no matching message");
        return found!;
    }

    /// <summary>Pumps every server and every client (so other players' inboxes fill too).</summary>
    private bool Pump(LoginClient waiting, Func<bool> condition, int ms = 5000, bool tick = false)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < deadline)
        {
            _login.PollEvents();
            _world.PollEvents();
            _zones.PollEvents();
            if (tick)
                _zones.Tick(0.05f);
            foreach (var (client, inbox) in _clients)
                foreach (var m in client.Poll())
                    inbox.Enqueue(m);
            if (condition())
                return true;
            Thread.Sleep(5);
        }
        return false;
    }
}
