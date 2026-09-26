using EQClassic.Shared.World;

namespace EQClassic.Tests.World;

public class ZoneRegionsTests
{
    [Fact]
    public void A_point_goes_left_on_the_positive_side_of_each_plane()
    {
        // Root: the plane z = 0 (Lantern's second coefficient is EverQuest's z); above: normal, below: water.
        // Below it, the plane x = 100 (third coefficient): beyond is lava.
        var regions = ZoneRegions.Parse([
            "# Lantern Extractor - BSP Tree",
            "0,1,0,0,1,2",
            "1,Normal",
            "0,0,1,-100,3,4",
            "2,Lava",
            "3,Water",
        ]);
        Assert.Equal(RegionKind.Normal, regions.At(new Vec3(0, 0, 5)));
        Assert.True(regions.InWater(new Vec3(50, 0, -5)));
        Assert.True(regions.InLava(new Vec3(150, 0, -5)));
    }

    [Fact]
    public void Missing_children_are_normal_regions() =>
        Assert.Equal(RegionKind.Normal, ZoneRegions.Parse(["1,0,0,0,-1,-1"]).At(new Vec3(0, 5, 0)));

    [Fact]
    public void A_north_qeynos_canal_is_water_between_z_minus_15_and_minus_5()
    {
        var path = Path.Combine(Characters.PlayerProfileTests.RepoRoot(), "build", "lantern-work", "Exports", "qeynos2", "Zone", "bsp_tree.txt");
        if (!File.Exists(path))
            return; // the Lantern exports are not in git
        var regions = ZoneRegions.Load(path);
        Assert.True(regions.InWater(new Vec3(-500, -220, -10)));
        Assert.False(regions.InWater(new Vec3(-500, -220, -25))); // the canal floor
        Assert.False(regions.InWater(new Vec3(-500, -220, 5)));
        Assert.Equal(RegionKind.Normal, regions.At(new Vec3(235, 30, 5))); // where Qbot stands
    }
}
