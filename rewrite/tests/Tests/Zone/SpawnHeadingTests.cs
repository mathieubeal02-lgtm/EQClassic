using EQClassic.Server.Zone;

namespace EQClassic.Tests.Zone;

/// <summary>spawn2.heading (the client's byte, or EQEmu's 0-512 above 256) in this server's degrees.</summary>
public class SpawnHeadingTests
{
    [Theory]
    [InlineData(0f, 0f)]      // north (+Y)
    [InlineData(64f, 270f)]   // the legacy zone's quarter turn faces -X; Atan2 puts -X at -90
    [InlineData(128f, 180f)]  // south
    [InlineData(192f, 90f)]   // +X
    [InlineData(384f, 90f)]   // 0-512 row (sql/patches/007): 384 is 192 on the byte scale
    [InlineData(256f, 0f)]    // a full turn
    public void Spawn_headings_become_degrees(float raw, float degrees) =>
        Assert.Equal(degrees, SpawnHeading.ToDegrees(raw), precision: 3);
}
