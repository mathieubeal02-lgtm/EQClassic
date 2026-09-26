using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Aggro, death and respawn, zone lines.</summary>
public class ZoneBehaviourTests
{
    private sealed class KosTo : IFactionStandings
    {
        private readonly FactionStanding _standing;
        public KosTo(FactionStanding standing) => _standing = standing;
        public FactionStanding Standing(ZoneInstance.Entity player, NpcTemplate npc) => _standing;
    }

    private static readonly NpcTemplate Orc = new(1, "an_orc_pawn", 54, 0, 10, 6f);

    private static ZoneData OneNpc(Vec3 at, NpcTemplate? npc = null, int respawn = 600, int variance = 0, params ZoneLine[] lines) =>
        new("crushbone", [new SpawnPoint(1, at, 0, 0, [(npc ?? Orc, 100)], respawn, variance)], new Dictionary<int, Grid>(), lines);

    private static void Run(ZoneInstance zone, float seconds)
    {
        for (float t = 0; t < seconds; t += 0.05f)
            zone.Tick(0.05f);
    }

    [Fact]
    public void Kos_npc_aggroes_a_player_in_range_and_runs_to_melee_range()
    {
        var zone = new ZoneInstance(OneNpc(new Vec3(0, 0, 0))) { Factions = new KosTo(FactionStanding.Scowls) };
        var orc = zone.Entities.Single();
        var player = zone.AddPlayer("Qbot", 9, 0, 10, new Vec3(50, 0, 0)); // white con: radius 145/sqrt(5) = 64.8

        Run(zone, 1.3f);
        Assert.Equal(player.Id, orc.TargetId);
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.Engaged en && en.NpcId == orc.Id);

        Run(zone, 3f);
        float d = orc.Position.X - player.Position.X;
        Assert.InRange(MathF.Abs(d), ZoneInstance.MeleeRange - 0.01f, ZoneInstance.MeleeRange + 0.5f);
    }

    [Fact]
    public void Out_of_range_indifferent_or_hidden_players_are_ignored()
    {
        var far = new ZoneInstance(OneNpc(new Vec3(0, 0, 0))) { Factions = new KosTo(FactionStanding.Scowls) };
        far.AddPlayer("Qfar", 9, 0, 10, new Vec3(80, 0, 0));
        var friendly = new ZoneInstance(OneNpc(new Vec3(0, 0, 0)));
        friendly.AddPlayer("Qnear", 9, 0, 10, new Vec3(20, 0, 0));
        var wall = ZoneCollisionMesh.ParseLantern(["v,-100,0,40", "v,100,0,40", "v,100,50,40", "v,-100,50,40", "i,0,0,1,2", "i,0,0,2,3"]); // wall at EQ x=40
        var hidden = new ZoneInstance(OneNpc(new Vec3(0, 0, 0)), wall) { Factions = new KosTo(FactionStanding.Scowls) };
        hidden.AddPlayer("Qhidden", 9, 0, 10, new Vec3(50, 0, 0));

        foreach (var zone in new[] { far, friendly, hidden })
        {
            Run(zone, 3f);
            Assert.Null(zone.Entities.Single(e => !e.IsPlayer).TargetId);
        }
    }

    [Fact]
    public void Npc_goes_back_to_its_grid_when_the_target_leaves()
    {
        var grid = new Grid(5, GridType.BackAndForth, [new Waypoint(new Vec3(0, 0, 0), 0), new Waypoint(new Vec3(0, 100, 0), 0)]);
        var data = new ZoneData("crushbone", [new SpawnPoint(1, new Vec3(0, 0, 0), 0, 5, [(Orc, 100)])], new Dictionary<int, Grid> { [5] = grid });
        var zone = new ZoneInstance(data) { Factions = new KosTo(FactionStanding.Threatenly) };
        var orc = zone.Entities.Single();
        var player = zone.AddPlayer("Qbot", 9, 0, 10, new Vec3(30, 0, 0));
        Run(zone, 1.3f);
        Assert.NotNull(orc.TargetId);

        zone.RemovePlayer(player.Id);
        zone.Tick(0.05f);
        Assert.Null(orc.TargetId);
        var before = orc.Position;
        Run(zone, 2f);
        Assert.NotEqual(before, orc.Position); // walking its grid again
    }

    [Fact]
    public void Killed_npcs_respawn_after_their_respawn_time()
    {
        var zone = new ZoneInstance(OneNpc(new Vec3(5, 5, 0), respawn: 10));
        var orc = zone.Entities.Single();
        Assert.True(zone.Kill(orc.Id));
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.Removed r && r.EntityId == orc.Id);
        Assert.Equal("an_orc_pawn's_corpse", Assert.Single(zone.Entities).Name); // its corpse stays

        Run(zone, 9.5f);
        Assert.DoesNotContain(zone.Entities, e => !e.IsCorpse);
        Run(zone, 0.6f);
        var back = Assert.Single(zone.Entities, e => !e.IsCorpse);
        Assert.NotEqual(orc.Id, back.Id);
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.Spawned s && s.Entity.Id == back.Id);
    }

    [Fact]
    public void Respawn_variance_only_ever_shortens_the_delay_like_the_legacy_zone()
    {
        var zone = new ZoneInstance(OneNpc(new Vec3(0, 0, 0)));
        var spawn = new SpawnPoint(1, new Vec3(0, 0, 0), 0, 0, [(Orc, 100)], 1000, 20);
        var delays = Enumerable.Range(0, 500).Select(_ => zone.RespawnDelay(spawn)).ToList();
        Assert.All(delays, d => Assert.InRange(d, 800.0, 1000.0));
        Assert.True(delays.Min() < 850);
    }

    [Fact]
    public void Players_cannot_kill_other_players_this_way() =>
        Assert.False(new ZoneInstance(OneNpc(new Vec3(0, 0, 0))).Kill(999));

    [Fact]
    public void Walking_onto_a_zone_line_queues_the_crossing_with_its_destination()
    {
        var toQeynos = new ZoneLine(977, new Vec3(2.66f, -148.38f, 2.13f), 8, "qeynos", new Vec3(-410.68f, 456.42f, 2.13f));
        var zone = new ZoneInstance(OneNpc(new Vec3(500, 500, 0), lines: toQeynos));
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(2, -130, 2));
        zone.Tick(1f);

        Assert.Null(zone.MovePlayer(player.Id, new Vec3(2, -145, 2), 180));

        var crossed = Assert.IsType<ZoneInstance.CrossedZoneLine>(Assert.Single(zone.DrainEvents()));
        Assert.Equal("qeynos", crossed.Line.TargetZone);
        Assert.Equal(new Vec3(-410.68f, 456.42f, 2.13f), crossed.Destination);
    }

    [Fact]
    public void Keep_x_zone_lines_keep_the_players_x()
    {
        var toQrg = new ZoneLine(7, new Vec3(73, 1350, 2.5f), 5, "qeytoqrg", new Vec3(95, -380, 0), KeepX: true);
        var zone = new ZoneInstance(OneNpc(new Vec3(500, 500, 0), lines: toQrg));
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(71, 1340, 2.5f));
        zone.Tick(1f);
        zone.MovePlayer(player.Id, new Vec3(71, 1349, 2.5f), 0);
        var crossed = Assert.IsType<ZoneInstance.CrossedZoneLine>(Assert.Single(zone.DrainEvents()));
        Assert.Equal(new Vec3(71, -380, 0), crossed.Destination);
    }

    [Fact]
    public void Swimming_up_is_not_limited_like_climbing()
    {
        var regions = ZoneRegions.Parse(["0,1,0,0,1,2", "1,Normal", "2,Water"]); // water below z = 0
        var zone = new ZoneInstance(OneNpc(new Vec3(500, 500, 0))) { Regions = regions };
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(0, 0, -60));
        zone.Tick(0.1f);
        Assert.Null(zone.MovePlayer(player.Id, new Vec3(0, 0, -30), 0)); // 30 units up in 0.1 s: too fast for climbing
        var dry = new ZoneInstance(OneNpc(new Vec3(500, 500, 0)));
        var walker = dry.AddPlayer("Qbot", 9, 0, 1, new Vec3(0, 0, -60));
        dry.Tick(0.1f);
        Assert.NotNull(dry.MovePlayer(walker.Id, new Vec3(0, 0, -30), 0));
    }
}
