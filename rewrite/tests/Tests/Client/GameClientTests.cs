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
using EQClassic.Shared.Zone;
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

    /// <summary>The troll shaman of the Trilogy creation packet: base + class additions + 30 points (STA 5, WIS 25).</summary>
    private static readonly CharacterStats TrollShaman = new(108, 119, 45, 75, 52, 83, 95);

    public GameClientTests()
    {
        var directory = new WorldDirectory();
        var zoneData = new InMemoryZoneDataSource();
        zoneData.Zones["grobb"] = new ZoneData("grobb",
            [new SpawnPoint(1, new Vec3(0, 0, 4), 0, 3, [(new NpcTemplate(52001, "a_troll_guard", 9, 0, 20, 8f), 100)]),
             new SpawnPoint(2, new Vec3(14, 12, 4), 0, 0, [(new NpcTemplate(1, "a_rat01", 36, 2, 1, 2f) { Combat = new NpcCombatStats(1, 16, 1, 4, AC: 5), LoottableId = 137 }, 100)])],
            new Dictionary<int, Grid> { [3] = new Grid(3, GridType.BackAndForth, [new Waypoint(new Vec3(0, 0, 4), 0), new Waypoint(new Vec3(0, 200, 4), 0)]) },
            [new ZoneLine(396, new Vec3(50.34f, -129.93f, 3.13f), 20, "innothule", new Vec3(-612.29f, -2789.26f, -31.44f))],
            [new Door(1, "DOOR1", new Vec3(15, 10, 4), 128, 0), new Door(2, "CELLDOOR", new Vec3(10, 34, 4), 0, 0, KeyItem: 1)]);
        zoneData.Zones["innothule"] = new ZoneData("innothule", [], new Dictionary<int, Grid>());
        var keys = new ZoneKeys();
        var loot = new InMemoryLootSource();
        loot.Tables[137] = new LootTable(137, 25, 25, 0, [(1, 1, 100)]); // 2 silver 5 copper and rat whiskers
        loot.Drops[1] = [(13071, 1, 1)];
        var items = new EQClassic.Server.Combat.InMemoryItemSource();
        items.Items[13071] = new EQClassic.Server.Combat.ItemStats(13071, "Rat Whiskers", 0, 0, 11, 0);
        items.Items[9993] = new EQClassic.Server.Combat.ItemStats(9993, "Spell: Minor Healing*", 0, 0, EQClassic.Server.Combat.ItemStats.SpellScroll, 0) { ScrollSpell = 200 };
        var spells = EQClassic.Tests.Combat.SpellRulesTests.File();
        _zones = new ZoneServer(keys, n => zoneData.Load(n) is { } d ? new ZoneInstance(d) { Loot = loot, Items = items, Spells = spells } : null)
        {
            Characters = _characters, Items = items, AccountStatus = _ => _gmStatus,
        };
        _zones.Start(0);
        var creation = new InMemoryCreationData();
        creation.StartPositions[("grobb", 9, 10)] = (40, -60, 3.13f); // 70 units from the zone line
        creation.OptionList.AddRange([new CreationOption(9, 10, 203, 1, "grobb"), new CreationOption(1, 1, 140, 1, "qeynos")]);
        var worldAccounts = new InMemoryWorldAccountStore();
        worldAccounts.Link(16, 24, "bot");
        _world = new WorldServer(1, directory, worldAccounts, _characters, creation) { Zones = new ZoneHandoff(keys, "127.0.0.1", _zones.Port) };
        _world.Start(0);
        directory.Register(new WorldServerInfo(1, "EverQuest Classic", "127.0.0.1", _world.Port, 0, WorldStatus.Up));
        _characters.Add(ProfileBuilder.Record(15, 24, "Qbot", 9, 10, 1, "grobb", x: 10, y: 10, z: 4));
        // A troll shaman with its starting scroll (Minor Healing) in the first general slot, WIS 95.
        var caster = ProfileBuilder.Build("Qcaster", 9, 10, 1, "grobb", x: 10, y: 10, z: 4);
        ProfileTemplate.SetStats(caster, 108, 119, 45, 75, 52, 83, 95);
        ProfileTemplate.SetItem(caster, 22, 9993, 1);
        ProfileTemplate.SetMana(caster, 21);
        _characters.Add(new CharacterRecord(16, 24, PlayerProfile.Read(caster)!));
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
        walker.CreateCharacter(new CreateCharacterRequest("Qwalk", 9, 10, 0, 203, 1, "grobb", TrollShaman));
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
    public void A_door_one_player_opens_is_seen_open_by_the_others()
    {
        var watcher = InZone("Qbot");
        var user = AtCharacterSelect();
        user.CreateCharacter(new CreateCharacterRequest("Qdoor", 9, 10, 0, 203, 1, "grobb", TrollShaman));
        Run(() => user.Characters.Any(c => c.Name == "Qdoor"));
        user.EnterWorld("Qdoor");
        Run(() => user.State == GameState.InZone);
        Assert.True(Run(() => watcher.Zone!.Doors.Count == 2 && user.Zone!.Doors.Count == 2), "door list not received");

        // Qdoor starts at (40, -60): too far from any door. Qbot, at (10, 10), uses DOOR1.
        Assert.Null(user.UseNearestDoor());
        var opened = new List<int>();
        user.Zone!.DoorChanged += d => { if (d.Open) opened.Add(d.Id); };
        Assert.Equal(1, watcher.UseNearestDoor()?.Id);
        Assert.True(Run(() => opened.Contains(1)), "the other player never saw the door open");
        Assert.True(watcher.Zone!.Doors.Single(d => d.Id == 1).Open);
    }

    [Fact]
    public void A_locked_door_answers_with_a_message()
    {
        var client = InZone("Qbot");
        var messages = new List<string>();
        client.MessageReceived += messages.Add;
        Run(() => client.Zone!.Doors.Count == 2);
        var player = client.Player!;
        player.Move(0, 0, turn: -1, seconds: player.Heading / LocalPlayer.TurnDegreesPerSecond); // face north (heading 0)
        for (int i = 0; i < 10; i++)                                                           // 22.5 units in 0.5 s, next to the cell door
        {
            player.Move(forward: 1, strafe: 0, turn: 0, seconds: 0.05f);
            Run(() => false, 50);
        }
        Run(() => false, 200);                                                                 // the last move reaches the server
        Assert.Equal(2, client.UseNearestDoor()?.Id);
        Assert.True(Run(() => messages.Count > 0));
        Assert.Equal(ZoneInstance.NoKeyMessage, messages[0]);
    }

    [Fact]
    public void Targets_the_nearest_npc_and_trades_blows_with_it()
    {
        var client = InZone("Qbot"); // at (10, 10), the rat at (14, 12)
        var lines = new List<string>();
        client.MessageReceived += lines.Add;
        Assert.True(Run(() => client.MaxHp > 0), "no hit points received");

        Assert.Equal("a rat", client.TargetNearest()?.DisplayName);
        client.ToggleAutoAttack();
        Assert.True(client.AutoAttacking);
        Assert.True(Run(() => lines.Any(l => l.StartsWith("You hit a rat") || l.StartsWith("You try to hit a rat")), 8000), string.Join(" | ", lines));
        Assert.True(Run(() => lines.Any(l => l.StartsWith("A rat hits YOU") || l.StartsWith("A rat tries to hit YOU")), 8000), string.Join(" | ", lines));
        Assert.Contains("Auto attack is on.", lines);
        Assert.InRange(client.Hp, 1, client.MaxHp);

        client.ToggleAutoAttack();
        Assert.True(Run(() => lines.Contains("Auto attack is off.")));
        Assert.False(client.AutoAttacking);
    }

    [Fact]
    public void Considers_and_sits_through_the_server()
    {
        var client = InZone("Qbot");
        var lines = new List<string>();
        client.MessageReceived += lines.Add;
        client.TargetNearest(); // the level 1 rat, for the level 1 Qbot
        client.Consider();
        Assert.True(Run(() => lines.Count > 0));
        Assert.Equal("a rat regards you indifferently -- looks like an even fight.", lines[0]);

        client.ToggleSit();
        Assert.True(Run(() => client.Sitting), "never saw itself sit");
        client.ToggleSit();
        Assert.True(Run(() => !client.Sitting), "never stood up");
    }

    [Fact]
    public void Players_say_tell_and_who()
    {
        var qbot = InZone("Qbot");
        var other = AtCharacterSelect();
        other.CreateCharacter(new CreateCharacterRequest("Qchat", 9, 10, 0, 203, 1, "grobb", TrollShaman));
        Run(() => other.Characters.Any(c => c.Name == "Qchat"));
        other.EnterWorld("Qchat");
        Run(() => other.State == GameState.InZone);
        var heard = new List<string>();
        var said = new List<string>();
        qbot.MessageReceived += heard.Add;
        other.MessageReceived += said.Add;

        other.ExecuteChat("hail, Qbot");                // Qchat at (40, -60), Qbot at (10, 10): 76 units, within say range
        Assert.True(Run(() => heard.Contains("Qchat says, 'hail, Qbot'")), string.Join(" | ", heard));
        Assert.True(Run(() => said.Contains("You say, 'hail, Qbot'")));

        other.ExecuteChat("/tell qbot meet me at the gate");
        Assert.True(Run(() => heard.Contains("Qchat tells you, 'meet me at the gate'")));
        Assert.True(Run(() => said.Contains("You told Qbot, 'meet me at the gate'")));
        other.ExecuteChat("/tell Nobody hello");
        Assert.True(Run(() => said.Contains("Nobody is not online at this time.")));

        qbot.ExecuteChat("/who");
        Assert.True(Run(() => heard.Contains("There are 2 players in grobb.")), string.Join(" | ", heard));
        Assert.Contains("[1 Shaman] Qchat (Troll)", heard);
        qbot.ExecuteChat("/loc");
        Assert.Contains("Your Location is 10.00, 10.00, 4.00", heard);
    }

    private int _gmStatus;

    [Fact]
    public void Gm_commands_need_a_gm_account_and_nobody_hears_them()
    {
        var qbot = InZone("Qbot");
        var heard = new List<string>();
        qbot.MessageReceived += heard.Add;
        qbot.ExecuteChat("#level 10");
        Assert.True(Run(() => heard.Contains("Your access level is not high enough to use this command.")), string.Join(" | ", heard));
        Assert.DoesNotContain(heard, h => h.StartsWith("You say"));
        qbot.ExecuteChat("#loc");
        Assert.True(Run(() => heard.Contains("Your Location is 10.00, 10.00, 4.00")), string.Join(" | ", heard));

        _gmStatus = 255; // made a GM while playing
        var gm = qbot;
        var told = heard;
        gm.ExecuteChat("#level 10");
        Assert.True(Run(() => gm.Experience?.Level == 10), string.Join(" | ", told));
        gm.ExecuteChat("#nothing");
        Assert.True(Run(() => told.Contains("Unknown command '#nothing'. #help lists yours.")));
        gm.ExecuteChat("#goto 30 40 4");
        Assert.True(Run(() => gm.Player is { } p && Math.Abs(p.Position.X - 30) < 0.1f && Math.Abs(p.Position.Y - 40) < 0.1f), gm.Player?.Position.ToString());
    }

    [Fact]
    public void Creation_offers_the_start_zones_combinations()
    {
        var client = AtCharacterSelect();
        client.RequestCreationOptions();
        Assert.True(Run(() => client.CreationOptions is not null));
        Assert.Equal(2, client.CreationOptions!.Count);
        Assert.Contains(new CreationOption(1, 1, 140, 1, "qeynos"), client.CreationOptions);
    }

    [Fact]
    public void Loots_a_corpse_into_the_inventory()
    {
        var client = InZone("Qbot");
        var lines = new List<string>();
        client.MessageReceived += lines.Add;
        Assert.True(Run(() => client.Inventory is not null));
        var zone = _zones.Instance("grobb")!;
        var rat = zone.Entities.Single(e => e.Name == "a_rat01");
        zone.Kill(rat.Id, client.Player!.EntityId);            // as if Qbot had killed it; the server broadcasts on its next tick
        Run(() => client.Zone!.Entities.Any(e => e.Spawn.IsCorpse));

        client.Loot();
        Assert.True(Run(() => client.LootingCorpse is not null), string.Join(" | ", lines));
        Assert.Equal("Rat Whiskers", Assert.Single(client.LootItems).Name);
        Assert.True(Run(() => client.Inventory!.Silver == 2 && client.Inventory.Copper == 5));
        client.TakeLoot(0);
        Assert.True(Run(() => client.Inventory!.Slots[22].Name == "Rat Whiskers"), string.Join(" | ", lines));
        Assert.Contains("You have looted a Rat Whiskers.", lines);
        client.EndLoot();
        Assert.True(Run(() => !client.Zone!.Entities.Any(e => e.Spawn.IsCorpse)), "the empty corpse stayed");
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

    [Fact]
    public void Scribes_memorises_and_casts_a_spell()
    {
        var client = InZone("Qcaster");
        var lines = new List<string>();
        client.MessageReceived += lines.Add;
        Assert.True(Run(() => client.SpellBook is not null && client.MaxMana > 0));
        Assert.Equal((95 / 5 + 2) * 1, client.MaxMana);
        Assert.Empty(client.SpellBook!.Spells);

        client.Scribe(22);
        Assert.True(Run(() => client.SpellBook!.Spells.Any(s => s.Name == "Minor Healing")), string.Join(" | ", lines));
        Assert.Contains("You have finished scribing Minor Healing.", lines);
        Assert.True(Run(() => client.Inventory!.Slots[22].ItemId == 0));

        client.Memorize(0, 200);
        Assert.True(Run(() => client.GemSpell(0)?.Name == "Minor Healing"));
        client.Cast(0);
        // Begins (then lands after 1 s), or fizzles: either way the mana goes.
        Assert.True(Run(() => client.Mana == client.MaxMana - 10), string.Join(" | ", lines));
        Assert.True(lines.Contains("You begin casting Minor Healing.") || lines.Contains("Your spell fizzles!"), string.Join(" | ", lines));
    }

    [Fact]
    public void Two_players_group_and_talk_in_group_chat()
    {
        var bot = InZone("Qbot");
        var caster = InZone("Qcaster");
        var casterLines = new List<string>();
        var botLines = new List<string>();
        caster.MessageReceived += casterLines.Add;
        bot.MessageReceived += botLines.Add;

        bot.ExecuteChat("/invite Qcaster");
        Assert.True(Run(() => casterLines.Any(l => l.StartsWith("Qbot invites you to join a group."))), string.Join(" | ", casterLines));
        caster.ExecuteChat("/follow");
        Assert.True(Run(() => bot.Group?.Members.Count == 2 && caster.Group?.Leader == "Qbot"));

        caster.ExecuteChat("/g hello group");
        Assert.True(Run(() => botLines.Contains("Qcaster tells the group, 'hello group'")), string.Join(" | ", botLines));

        caster.ExecuteChat("/disband");
        Assert.True(Run(() => bot.Group is null && caster.Group is null));
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
