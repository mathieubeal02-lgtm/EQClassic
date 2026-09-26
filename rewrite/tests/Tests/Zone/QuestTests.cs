using EQClassic.Server.Combat;
using EQClassic.Server.Quests;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using Inventory = EQClassic.Server.Zone.ZoneInstance.PlayerInventory;

namespace EQClassic.Tests.Zone;

/// <summary>The legacy Perl quests: the script lookup, the host running an event, the zone applying it.</summary>
public class QuestTests
{
    private const int Whiskers = 13071, Sword = 5013;

    private static readonly NpcTemplate Guard = new(1, "Guard_Tom01", 1, 0, 20, 6f) { Combat = new NpcCombatStats(1, 500, 1, 2) };

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, ZoneInstance.Entity Npc) Setup(IFactionStandings? factions = null)
    {
        var items = new InMemoryItemSource();
        items.Items[Whiskers] = new ItemStats(Whiskers, "Rat Whiskers", 0, 0, 11, 0);
        items.Items[Sword] = new ItemStats(Sword, "Rusty Short Sword", 5, 28, 0, 0) { Size = 2 };
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(5, 0, 0), 0, 0, [(Guard, 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data) { Items = items };
        if (factions is not null)
            zone.Factions = factions;
        var player = zone.AddPlayer("Ann", 1, 0, 10, new Vec3(0, 0, 0),
            progress: new ZoneInstance.PlayerProgress(0, "", default, null, new Inventory { Coins = new Coins(1, 0, 0, 0) }));
        player.Inventory!.Items[22] = Whiskers;
        var npc = zone.Entities.Single(e => !e.IsPlayer);
        zone.DrainEvents();
        return (zone, player, npc);
    }

    [Fact]
    public void Giving_to_an_npc_takes_the_items_and_money_and_raises_its_event()
    {
        var (zone, ann, npc) = Setup();
        zone.RequestTrade(ann.Id, npc.Id);
        zone.OfferItem(ann.Id, 22);
        zone.OfferCoins(ann.Id, new Coins(0, 2, 0, 0));
        zone.AcceptTrade(ann.Id); // the NPC does not need to accept

        Assert.Equal(0, ann.Inventory!.Items[22]);
        Assert.Equal(800, ann.Inventory.Coins.TotalCopper);
        Assert.Null(ann.Trade);
        var handedIn = Assert.Single(zone.DrainEvents().OfType<ZoneInstance.HandedIn>());
        Assert.Equal([Whiskers], handedIn.Items);
        Assert.Equal(200, handedIn.Coins.TotalCopper);
        Assert.Equal(npc.Id, handedIn.NpcId);
    }

    [Fact]
    public void Quest_actions_speak_give_and_depop()
    {
        var (zone, ann, npc) = Setup();
        zone.ApplyQuest(npc.Id, ann.Id, [
            new QuestAction("say", ["Hail, Ann."]),
            new QuestAction("summonitem", [Sword.ToString()]),
            new QuestAction("givecash", ["5", "0", "0", "0"]),
            new QuestAction("exp", ["100"]),
            new QuestAction("depop", []),
        ]);
        var events = zone.DrainEvents();
        Assert.Contains(new ZoneInstance.NpcSpoke(npc.Id, "Guard Tom", npc.Position, "say", "Hail, Ann."), events);
        Assert.Contains(Sword, ann.Inventory!.Items);
        Assert.Equal(1005, ann.Inventory.Coins.TotalCopper);
        Assert.Equal(100u, ann.Exp);
        Assert.Null(zone.Get(npc.Id));
        Assert.Contains(new ZoneInstance.Removed(npc.Id), events);
    }

    [Fact]
    public void Timers_signals_and_death_raise_script_events()
    {
        var (zone, ann, npc) = Setup();
        zone.ApplyQuest(npc.Id, ann.Id, [new QuestAction("settimer", ["patrol", "10"])]);
        zone.Tick(5);
        Assert.DoesNotContain(zone.DrainEvents(), e => e is ZoneInstance.QuestTriggered);
        zone.Tick(6);
        var timer = Assert.Single(zone.DrainEvents().OfType<ZoneInstance.QuestTriggered>());
        Assert.Equal(("EVENT_TIMER", "patrol", "Guard Tom"), (timer.Event, timer.Variables["timer"], timer.Variables["mname"]));
        zone.ApplyQuest(npc.Id, ann.Id, [new QuestAction("stoptimer", ["patrol"])]);
        zone.Tick(11);
        Assert.DoesNotContain(zone.DrainEvents(), e => e is ZoneInstance.QuestTriggered);

        zone.ApplyQuest(npc.Id, ann.Id, [new QuestAction("signalwith", ["1", "7"])]);
        var signal = Assert.Single(zone.DrainEvents().OfType<ZoneInstance.QuestTriggered>());
        Assert.Equal(("EVENT_SIGNAL", "7", npc.Id), (signal.Event, signal.Variables["signal"], signal.NpcId));

        zone.Kill(npc.Id, ann.Id);
        var death = Assert.Single(zone.DrainEvents().OfType<ZoneInstance.QuestTriggered>());
        Assert.Equal(("EVENT_DEATH", "Ann", Guard), (death.Event, death.Variables["name"], death.Template));
        // A dead NPC's last words still come from where it fell.
        zone.ApplyQuest(npc.Id, ann.Id, [new QuestAction("say", ["Argh!"])], speaker: ("Guard Tom", npc.Position));
        Assert.Contains(new ZoneInstance.NpcSpoke(npc.Id, "Guard Tom", npc.Position, "say", "Argh!"), zone.DrainEvents());
    }

    [Fact]
    public void Scripts_spawn_npcs_once_when_unique_and_depop_them_by_type()
    {
        var (zone, ann, npc) = Setup();
        var rat = new NpcTemplate(99, "a_large_rat", 29, 0, 2, 4f) { Combat = new NpcCombatStats(1, 20, 1, 2) };
        zone.NpcTypes = id => id == 99 ? rat : null;
        zone.ApplyQuest(npc.Id, ann.Id, [new QuestAction("spawn", ["99", "0", "0", "10.5", "20", "0"])]);
        zone.ApplyQuest(npc.Id, ann.Id, [new QuestAction("unique_spawn", ["99", "0", "0", "30", "20", "0"])]);
        var rats = zone.Entities.Where(e => e.Name == "a_large_rat").ToList();
        Assert.Equal(new Vec3(10.5f, 20, 0), Assert.Single(rats).Position);
        zone.ApplyQuest(npc.Id, ann.Id, [new QuestAction("depop", ["99"])]);
        Assert.DoesNotContain(zone.Entities, e => e.Name == "a_large_rat");
        Assert.NotNull(zone.Get(npc.Id)); // the speaker stays
    }

    [Fact]
    public void Walkers_raise_a_waypoint_event_and_npcs_turn_to_who_speaks()
    {
        var walker = new NpcTemplate(3, "Guard_Walker", 1, 0, 20, 6f) { Combat = new NpcCombatStats(1, 500, 1, 2) };
        var grid = new Grid(1, GridType.Circular, [new Waypoint(new Vec3(100, 0, 0), 0), new Waypoint(new Vec3(110, 0, 0), 0)]);
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(100, 0, 0), 0, 1, [(walker, 100)], 600, 0)], new Dictionary<int, Grid> { [1] = grid });
        var zone = new ZoneInstance(data);
        var ann = zone.AddPlayer("Ann", 1, 0, 10, new Vec3(100, 50, 0));
        var npc = zone.Entities.Single(e => !e.IsPlayer);
        var events = new List<ZoneInstance.ZoneEvent>();
        for (int i = 0; i < 100; i++)
        {
            zone.Tick(0.1f);
            events.AddRange(zone.DrainEvents());
        }
        Assert.Contains(events, e => e is ZoneInstance.QuestTriggered { Event: "EVENT_WAYPOINT" } t && t.Variables["wp"] is "0" or "1");

        float before = npc.Heading;
        zone.FaceTowards(npc.Id, ann.Id);
        Assert.NotEqual(before, npc.Heading);
    }

    [Fact]
    public void Quest_faction_moves_the_standing_with_the_message()
    {
        var data = new InMemoryFactionData();
        data.Factions[135] = new FactionInfo(135, "Guards of Qeynos", 0, new int[16], new Dictionary<int, int>(), new Dictionary<int, int>());
        var (zone, ann, npc) = Setup(new DatabaseFactions(data));
        zone.ApplyQuest(npc.Id, ann.Id, [new QuestAction("faction", ["135", "10"])]);
        Assert.Equal(10, ann.FactionValue(135));
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.Told { Text: var t } && t.Contains("Guards of Qeynos"));
    }

    [Fact]
    public void Scripts_are_found_by_npc_type_then_name_then_template()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "qeynos2"));
            Directory.CreateDirectory(Path.Combine(dir, "templates"));
            File.WriteAllText(Path.Combine(dir, "templates", "Guard_Tom.pl"), "");
            QuestEngine Engine() => new(dir, "quest-host.pl"); // lookups are kept a minute: a fresh engine sees new files
            Assert.EndsWith(Path.Combine("templates", "Guard_Tom.pl"), Engine().ScriptFor("qeynos2", 1, "Guard_Tom01"));
            File.WriteAllText(Path.Combine(dir, "qeynos2", "Guard_Tom.pl"), "");
            Assert.EndsWith(Path.Combine("qeynos2", "Guard_Tom.pl"), Engine().ScriptFor("qeynos2", 1, "Guard_Tom01"));
            File.WriteAllText(Path.Combine(dir, "qeynos2", "1.pl"), "");
            var engine = Engine();
            Assert.EndsWith(Path.Combine("qeynos2", "1.pl"), engine.ScriptFor("qeynos2", 1, "Guard_Tom01"));
            File.Delete(Path.Combine(dir, "qeynos2", "1.pl"));
            Assert.EndsWith(Path.Combine("qeynos2", "1.pl"), engine.ScriptFor("qeynos2", 1, "Guard_Tom01")); // kept
            Assert.Null(engine.ScriptFor("qeynos2", 2, "Nobody"));
            Assert.Equal("Tom-s_Guard", QuestEngine.CleanName("Tom`s_Guard012"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private static string Host => Path.Combine(AppContext.BaseDirectory, "Quests", "quest-host.pl");

    private static bool HasPerl()
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("perl", "-v") { RedirectStandardOutput = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [Fact]
    public async Task A_legacy_script_runs_in_perl_and_its_calls_come_back()
    {
        if (!HasPerl())
            return; // Windows runners without Perl
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "qeynos2"));
            Directory.CreateDirectory(Path.Combine(dir, "plugins"));
            var script = Path.Combine(dir, "qeynos2", "Guard_Tom.pl");
            File.WriteAllText(script, """
                sub EVENT_SAY {
                  if ($text =~ /hail/i) { quest::say("Hail, $name! Bring me rat [whiskers]."); }
                  if ($text =~ /whiskers/i) { quest::emote("scratches his chin."); }
                }
                sub EVENT_ITEM {
                  if (plugin::check_handin(\%itemcount, 13071 => 1)) {
                    quest::say("Thank you.");
                    quest::summonitem(5013);
                    quest::faction(135, 10);
                    quest::exp(100);
                  }
                  plugin::return_items(\%itemcount);
                }
                """);
            var engine = new QuestEngine(dir, Host);
            Assert.True(engine.HasEvent(script, "EVENT_ITEM"));
            Assert.False(engine.HasEvent(script, "EVENT_DEATH"));
            var said = await engine.RunAsync(script, "EVENT_SAY", new Dictionary<string, string> { ["name"] = "Ann", ["text"] = "Hail, Guard Tom" });
            Assert.Equal([new QuestAction("say", ["Hail, Ann! Bring me rat [whiskers]."])], said.Select(a => a with { Args = a.Args.ToArray() }), new ActionComparer());

            var given = await engine.RunAsync(script, "EVENT_ITEM", new Dictionary<string, string> { ["name"] = "Ann", ["item1"] = "13071" },
                new Dictionary<int, int> { [13071] = 1, [Sword] = 1 });
            Assert.Equal(["say", "summonitem", "faction", "exp", "return_item"], given.Select(a => a.Function));
            Assert.Equal(Sword, given[^1].Int(0)); // the extra item comes back

            var broken = Path.Combine(dir, "qeynos2", "Broken.pl");
            File.WriteAllText(broken, "sub EVENT_SAY { quest::say( }");
            Assert.Equal("error", Assert.Single(await engine.RunAsync(broken, "EVENT_SAY", new Dictionary<string, string>())).Function);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    private sealed class ActionComparer : IEqualityComparer<QuestAction>
    {
        public bool Equals(QuestAction? x, QuestAction? y) => x!.Function == y!.Function && x.Args.SequenceEqual(y.Args);
        public int GetHashCode(QuestAction a) => a.Function.GetHashCode();
    }
}
