using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Experience from kills, level ups, and deaths at the bind point.</summary>
public class ExperienceZoneTests
{
    private const int Troll = 9;
    private static NpcTemplate Npc(int level, int hp = 1000) => new(1, "an_orc", 54, 0, level, 6f) { Combat = new NpcCombatStats(1, hp, 1, 2) };

    // Always hits for 50, never misses; the level only matters for experience.
    private static Combatant Striker(int level) => new(true, level, CombatFormulas.Shaman, 100 + level, 300, 5000, 400, 300, 0, 500, 1f);

    private static (ZoneInstance, ZoneInstance.Entity) Zone(NpcTemplate npc, int level, uint exp, string bindZone = "qeynos2", Vec3 bind = default)
    {
        var zone = new ZoneInstance(new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(5, 0, 0), 0, 0, [(npc, 100)], 600, 0)], new Dictionary<int, Grid>()), seed: 1);
        var progress = new ZoneInstance.PlayerProgress(exp, bindZone, bind, (l, _) => Striker(l));
        var player = zone.AddPlayer("Qbot", Troll, 0, level, new Vec3(0, 0, 0), progress: progress);
        zone.DrainEvents();
        return (zone, player);
    }

    private static void Fight(ZoneInstance zone, ZoneInstance.Entity player)
    {
        zone.SetTarget(player.Id, zone.Entities.Single(e => !e.IsPlayer).Id);
        zone.SetAutoAttack(player.Id, true);
        for (int i = 0; i < 100 && zone.Entities.Any(e => !e.IsPlayer); i++)
            zone.Tick(0.05f);
    }

    [Fact]
    public void A_kill_gives_level_squared_times_75_capped_at_a_tenth_of_the_level()
    {
        var (zone, player) = Zone(Npc(10), level: 10, exp: Experience.ForLevel(10, CombatFormulas.Shaman, Troll));
        uint before = player.Exp;
        Fight(zone, player);
        Assert.Equal(before + 7500u, player.Exp); // 10² × 75, under the cap
        var events = zone.DrainEvents();
        Assert.Contains(new ZoneInstance.Told(player.Id, "You gain experience!!"), events);
        Assert.Contains(new ZoneInstance.ExperienceChanged(player.Id, player.Exp, 10), events);
    }

    [Fact]
    public void Green_npcs_give_nothing()
    {
        var (zone, player) = Zone(Npc(1), level: 20, exp: Experience.ForLevel(20, CombatFormulas.Shaman, Troll));
        uint before = player.Exp;
        Fight(zone, player);
        Assert.Equal(before, player.Exp);
    }

    [Fact]
    public void Enough_experience_levels_up_and_rebuilds_the_fighter()
    {
        uint almost = Experience.ForLevel(2, CombatFormulas.Shaman, Troll) - 10; // 10 points from level 2
        var (zone, player) = Zone(Npc(1), level: 1, exp: almost);
        Fight(zone, player);
        Assert.Equal(2, player.Level);
        Assert.Equal(102, player.Fighter.MaxHp); // Striker(2)
        Assert.Contains(new ZoneInstance.Told(player.Id, "You have gained a level! Welcome to level 2!"), zone.DrainEvents());
    }

    [Fact]
    public void Death_costs_experience_from_level_six_and_returns_to_the_bind_point()
    {
        var brute = new NpcTemplate(2, "a_brute", 54, 0, 40, 6f) { Combat = new NpcCombatStats(1, 100_000, 200, 400) };
        var bind = new Vec3(100, 200, 3);
        uint exp = Experience.ForLevel(10, CombatFormulas.Shaman, Troll) + 100_000;
        var (zone, player) = Zone(brute, level: 10, exp: exp, bind: bind);
        zone.SetTarget(player.Id, zone.Entities.Single(e => !e.IsPlayer).Id);
        zone.SetAutoAttack(player.Id, true);
        for (int i = 0; i < 200 && player.Position != bind; i++)
            zone.Tick(0.05f);
        Assert.Equal(bind, player.Position);
        Assert.Equal(exp - Experience.DeathLoss(10, exp), player.Exp);
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.Teleported t && t.Destination == bind);
    }

    [Fact]
    public void A_bind_point_in_another_zone_hands_the_player_over()
    {
        var brute = new NpcTemplate(2, "a_brute", 54, 0, 40, 6f) { Combat = new NpcCombatStats(1, 100_000, 200, 400) };
        var (zone, player) = Zone(brute, level: 1, exp: 0, bindZone: "innothule", bind: new Vec3(-339, -2408, -14));
        zone.SetTarget(player.Id, zone.Entities.Single(e => !e.IsPlayer).Id);
        zone.SetAutoAttack(player.Id, true);
        ZoneInstance.CrossedZoneLine? crossed = null;
        for (int i = 0; i < 200 && crossed is null; i++)
        {
            zone.Tick(0.05f);
            crossed = zone.DrainEvents().OfType<ZoneInstance.CrossedZoneLine>().FirstOrDefault();
        }
        Assert.NotNull(crossed);
        Assert.Equal(("innothule", new Vec3(-339, -2408, -14)), (crossed!.Line.TargetZone, crossed.Destination));
    }
}
