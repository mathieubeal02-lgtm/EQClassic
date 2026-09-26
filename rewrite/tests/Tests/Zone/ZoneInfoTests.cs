using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Tests.Zone;

public class ZoneInfoTests
{
    private static string Cfg(string zone) => Path.Combine(EQClassic.Tests.Characters.PlayerProfileTests.RepoRoot(), "runtime", "cfg", zone + ".cfg");

    [Fact]
    public void Reads_the_legacy_zone_header()
    {
        var info = ZoneInfo.FromLegacyCfg(File.ReadAllBytes(Cfg("qeynos2")))!;
        Assert.Equal("North Qeynos", info.LongName);
        Assert.Equal((200, 200, 220), (info.FogRed, info.FogGreen, info.FogBlue));
        Assert.Equal((10f, 450f, 450f), (info.FogMin, info.FogMax, info.MaxClip));
        Assert.Equal((428f, -74f, -404f), (MathF.Round(info.SafeX), MathF.Round(info.SafeY), MathF.Round(info.Underworld)));
        Assert.Equal(1, info.Sky);

        var permafrost = ZoneInfo.FromLegacyCfg(File.ReadAllBytes(Cfg("permafrost")))!;
        Assert.Equal((25, 35, 45, 180f), (permafrost.FogRed, permafrost.FogGreen, permafrost.FogBlue, permafrost.FogMax)); // dark blue dungeon fog
        Assert.Null(ZoneInfo.FromLegacyCfg(new byte[100]));
    }

    [Fact]
    public void Falling_below_the_underworld_returns_to_the_safe_point()
    {
        var info = ZoneInfo.FromLegacyCfg(File.ReadAllBytes(Cfg("qeynos2")))!;
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>())) { Info = info };
        var player = zone.AddPlayer("Qbot", 9, 0, 1, new Vec3(428, -74, -400));
        zone.Tick(0.2f);
        Assert.Null(zone.MovePlayer(player.Id, new Vec3(428, -74, -402), 0)); // still above -404
        zone.Tick(0.2f);
        Assert.Equal(ZoneInstance.UnderworldReason, zone.MovePlayer(player.Id, new Vec3(428, -74, -410), 0));
        Assert.Equal(new Vec3(info.SafeX, info.SafeY, info.SafeZ), player.Position);
    }
}
