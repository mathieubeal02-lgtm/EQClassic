using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Mob::GetDefaultSize: players, and NPCs whose npc_types size is 0, have their race's usual size.</summary>
public class RaceSizeTests
{
    [Theory]
    [InlineData(8, 4f)]   // dwarf
    [InlineData(12, 3f)]  // gnome
    [InlineData(10, 9f)]  // ogre
    [InlineData(1, 6f)]   // human
    public void Players_have_their_race_size(int race, float size)
    {
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>()));
        Assert.Equal(size, zone.AddPlayer("Ann", race, 0, 1, new Vec3(0, 0, 0)).ToSpawn().Size);
    }

    [Fact]
    public void Races_without_a_table_size_are_human_sized() => Assert.Equal(6f, RaceSizes.Default(71));
}
