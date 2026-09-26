using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

public class AggroRulesTests
{
    [Theory]
    [InlineData(10, 10, Con.White)]
    [InlineData(10, 12, Con.Yellow)]
    [InlineData(10, 13, Con.Red)]
    [InlineData(10, 9, Con.Blue)]
    [InlineData(10, 6, Con.Green)]
    [InlineData(20, 14, Con.Green)]
    [InlineData(20, 15, Con.Blue)]
    [InlineData(30, 20, Con.Blue)]
    [InlineData(30, 25, Con.Red)]   // legacy table gap for 25-40: -7..-1 is "red"
    [InlineData(30, 19, Con.Green)]
    [InlineData(55, 41, Con.Green)]
    [InlineData(55, 42, Con.Blue)]
    public void Consider_colors_follow_the_legacy_table(int player, int npc, Con expected) =>
        Assert.Equal(expected, AggroRules.LevelCon(player, npc));

    [Theory]
    [InlineData(FactionStanding.Indifferent)]
    [InlineData(FactionStanding.Dubious)]
    [InlineData(FactionStanding.Apprehensive)]
    [InlineData(FactionStanding.Ally)]
    public void Only_kos_standings_aggro(FactionStanding standing) =>
        Assert.Null(AggroRules.RadiusSquared(standing, 10, 10));

    [Fact]
    public void Radius_depends_on_con_and_standing()
    {
        float r2 = 145f * 145f;
        Assert.Equal(r2 / 5f, AggroRules.RadiusSquared(FactionStanding.Scowls, 10, 10));      // white
        Assert.Equal(r2 / 6.5f, AggroRules.RadiusSquared(FactionStanding.Threatenly, 10, 10));
        Assert.Equal(r2 / 4.5f, AggroRules.RadiusSquared(FactionStanding.Scowls, 10, 15));    // red
        Assert.Equal(r2 / 666f, AggroRules.RadiusSquared(FactionStanding.Threatenly, 50, 10)); // deep green: barely noticed
        Assert.Equal(r2 / 7.5f, AggroRules.RadiusSquared(FactionStanding.Scowls, 10, 6));     // green, but blue one level up
        Assert.Equal(r2 / 500f, AggroRules.RadiusSquared(FactionStanding.Scowls, 10, 5));     // still green one level up
    }

    [Fact]
    public void Sitting_players_and_undead_npcs_keep_the_standard_radius_when_green()
    {
        float r2 = 145f * 145f;
        Assert.Equal(r2 / 5f, AggroRules.RadiusSquared(FactionStanding.Scowls, 50, 10, playerSitting: true));
        Assert.Equal(r2 / 7.5f, AggroRules.RadiusSquared(FactionStanding.Scowls, 50, 10, npcUndead: true));
    }

    [Fact]
    public void A_wall_blocks_line_of_sight()
    {
        // Floor, plus a wall at EQ y = 50 (Lantern x = 50) from z 0 to 30.
        var mesh = ZoneCollisionMesh.ParseLantern(
        [
            "v,0,0,0", "v,100,0,0", "v,100,0,100", "v,0,0,100", "i,0,0,1,2", "i,0,0,2,3",
            "v,50,0,0", "v,50,0,100", "v,50,30,100", "v,50,30,0", "i,0,4,5,6", "i,0,4,6,7",
        ]);
        Assert.False(mesh.LineOfSight(new Vec3(50, 20, 5), new Vec3(50, 80, 5)));
        Assert.True(mesh.LineOfSight(new Vec3(50, 20, 40), new Vec3(50, 80, 40)));  // over the wall
        Assert.True(mesh.LineOfSight(new Vec3(20, 20, 5), new Vec3(30, 40, 5)));    // same side
    }
}
