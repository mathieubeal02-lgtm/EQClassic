using EQClassic.Server.Combat;

namespace EQClassic.Tests.Combat;

public class ExperienceTests
{
    private const int Troll = 9, HighElf = 5, Shaman = 10, Paladin = 3;

    [Fact]
    public void Level_thresholds_match_the_live_characters()
    {
        // Qtest, level 59 troll shaman with 736,964,417 experience; Lanlaan, level 50 high elf paladin with 241,318,833.
        Assert.Equal(59, Experience.LevelFor(736_964_417, Shaman, Troll));
        Assert.Equal(50, Experience.LevelFor(241_318_833, Paladin, HighElf));
        Assert.Equal(1, Experience.LevelFor(150, Shaman, Troll)); // Qbot
    }

    [Fact]
    public void Thresholds_follow_the_cube_and_the_level_factors()
    {
        Assert.Equal(0u, Experience.ForLevel(1, Shaman, Troll));
        Assert.Equal(1200u, Experience.ForLevel(2, Shaman, Troll));             // 1³ × 10 × 120
        Assert.Equal(35_640_000u, Experience.ForLevel(31, Shaman, Troll));     // 30³ × 1200 × 1.1
        Assert.Equal(uint.MaxValue, Experience.ForLevel(10, 15, Troll));       // beastlords: no table in the legacy code
    }

    [Fact]
    public void Kills_and_deaths()
    {
        Assert.Equal(75u, Experience.ForKill(1));
        Assert.Equal(7500u, Experience.ForKill(10));
        // A tenth of the level 1 band (1200) caps a gain at 120.
        Assert.Equal(120u, Experience.Capped(7500, 1, Shaman, Troll));
        Assert.Equal(0u, Experience.DeathLoss(5, 100_000));
        Assert.Equal(66_666u, Experience.DeathLoss(10, 1_000_000));             // 10 × 10/18 × 12000
        Assert.Equal(99u, Experience.DeathLoss(10, 100));                       // never below one point left
    }
}
