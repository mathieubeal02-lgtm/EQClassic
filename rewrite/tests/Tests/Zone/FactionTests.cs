using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Factions after Zone/Source/faction.cpp (CalculateFaction, GetFactionLevel, SetFactionLevel).</summary>
public class FactionTests
{
    private const int Human = 1, Troll = 9, Warrior = 1, Shaman = 10, Bertoxxulous = 201;

    /// <summary>Blackburrow's gnolls (list 567: Sabertooths -30, Guards of Qeynos +10), from the live tables.</summary>
    private static InMemoryFactionData Data()
    {
        var data = new InMemoryFactionData();
        var classes = new int[15];
        data.Factions[279] = new FactionInfo(279, "Sabertooths of Blackburrow", -900, classes, new Dictionary<int, int> { [Troll] = -200 }, new Dictionary<int, int>());
        data.Factions[135] = new FactionInfo(135, "Guards of Qeynos", 0, classes, new Dictionary<int, int> { [Human] = 1500, [Troll] = -350 },
            new Dictionary<int, int> { [Bertoxxulous] = -750 });
        data.Lists[567] = new NpcFactionList(567, 279, [(279, -30), (135, 10)]);
        data.Lists[2078] = new NpcFactionList(2078, 135, []);
        return data;
    }

    private static readonly NpcTemplate Gnoll = new(1, "a_gnoll", 39, 0, 3, 6f, PrimaryFaction: 567) { Combat = new NpcCombatStats(1, 30, 1, 4) };
    private static readonly NpcTemplate Guard = new(2, "Guard_Mezzt", 71, 0, 30, 6f, PrimaryFaction: 2078) { Combat = new NpcCombatStats(1, 800, 10, 40) };

    private static ZoneInstance.Entity Player(ZoneInstance zone, int race, int @class, int deity = FactionRules.AgnosticDeity, Dictionary<int, int>? values = null)
    {
        var fighter = new Combatant(true, 10, @class, 100, 100, 5000, 100, 100, 0, 50, 1f);
        var progress = new ZoneInstance.PlayerProgress(0, "", default, null) { Deity = deity, Factions = values };
        var player = zone.AddPlayer("Qbot", race, 0, 10, new Vec3(0, 0, 0), fighter: fighter, progress: progress);
        zone.DrainEvents();
        return player;
    }

    [Theory]
    [InlineData(1100, FactionStanding.Ally)]
    [InlineData(750, FactionStanding.Warmly)]
    [InlineData(500, FactionStanding.Kindly)]
    [InlineData(101, FactionStanding.Amiable)]
    [InlineData(100, FactionStanding.Indifferent)]
    [InlineData(0, FactionStanding.Indifferent)]
    [InlineData(-1, FactionStanding.Apprehensive)]
    [InlineData(-101, FactionStanding.Dubious)]
    [InlineData(-501, FactionStanding.Threatenly)]
    [InlineData(-751, FactionStanding.Scowls)]
    public void Values_map_to_the_legacy_standings(int value, FactionStanding standing) =>
        Assert.Equal(standing, FactionRules.Standing(value));

    [Fact]
    public void Standing_is_the_value_plus_the_class_race_and_deity_modifiers()
    {
        var factions = new DatabaseFactions(Data());
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>()));
        var human = Player(zone, Human, Warrior);
        Assert.Equal(FactionStanding.Scowls, factions.Standing(human, Gnoll));   // -900
        Assert.Equal(FactionStanding.Ally, factions.Standing(human, Guard));     // +1500

        var troll = Player(zone, Troll, Shaman, Bertoxxulous);
        Assert.Equal(FactionStanding.Dubious, factions.Standing(troll, Guard));  // aggro: agnostic deity, -350
        Assert.Equal(FactionStanding.Scowls, factions.StandingFor(troll, Guard, troll.Deity)); // consider: -350 - 750

        var friend = Player(zone, Troll, Shaman, values: new Dictionary<int, int> { [135] = 600 });
        Assert.Equal(FactionStanding.Amiable, factions.Standing(friend, Guard)); // 600 - 350
    }

    [Theory]
    [InlineData(60, FactionStanding.Scowls)]      // a skeleton: a monster race
    [InlineData(36, FactionStanding.Indifferent)] // a rat
    public void Npcs_without_a_primary_faction_go_by_their_race(int race, FactionStanding standing)
    {
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>()));
        var npc = new NpcTemplate(3, "a_thing", race, 0, 1, 6f);
        Assert.Equal(standing, new DatabaseFactions(Data()).Standing(Player(zone, Human, Warrior), npc));
    }

    [Fact]
    public void A_kill_moves_the_killers_factions_with_the_legacy_messages()
    {
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(5, 0, 0), 0, 0, [(Gnoll, 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data, seed: 1) { Factions = new DatabaseFactions(Data()) };
        var player = Player(zone, Human, Warrior);
        var gnoll = zone.Entities.Single(e => !e.IsPlayer);
        zone.SetTarget(player.Id, gnoll.Id);
        zone.SetAutoAttack(player.Id, true);
        var events = new List<ZoneInstance.ZoneEvent>();
        for (int i = 0; i < 200 && zone.Entities.Any(e => e.Id == gnoll.Id && !e.IsCorpse); i++)
        {
            zone.Tick(0.05f);
            events.AddRange(zone.DrainEvents());
        }
        Assert.Equal(-30, player.FactionValue(279));
        Assert.Equal(0, player.FactionValue(135)); // 10 + 1500 for a human is over the limit: the total stays at 1500
        Assert.Contains(new ZoneInstance.Told(player.Id, "Your faction standing with Sabertooths of Blackburrow has gotten worse!"), events);
        Assert.Contains(new ZoneInstance.Told(player.Id, "Your faction standing with Guards of Qeynos could not possibly get any better!"), events); // 1510 with the human bonus
        Assert.Contains(new ZoneInstance.FactionsChanged(player.Id), events);
    }

    [Fact]
    public void Kos_npcs_aggro_through_the_faction_tables()
    {
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(20, 0, 0), 0, 0, [(Gnoll with { Level = 10 }, 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data) { Factions = new DatabaseFactions(Data()) };
        var player = Player(zone, Human, Warrior);
        for (int i = 0; i < 40; i++)
            zone.Tick(0.05f);
        Assert.Equal(player.Id, zone.Entities.Single(e => !e.IsPlayer).TargetId);
    }
}
