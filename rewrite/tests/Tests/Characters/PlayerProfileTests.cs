using System.Globalization;
using EQClassic.Server.Characters;

namespace EQClassic.Tests.Characters;

public class PlayerProfileTests
{
    [Fact]
    public void Reads_the_fields_world_needs()
    {
        var p = PlayerProfile.Read(ProfileBuilder.Build("Qtest", race: 9, @class: 10, level: 59, zone: "permafrost", x: 167, y: -60, z: 3.75f))!;
        Assert.Equal(("Qtest", 9, 10, 59, "permafrost"), (p.Name, p.Race, p.Class, p.Level, p.Zone));
        Assert.Equal((167f, -60f, 3.75f), (p.X, p.Y, p.Z));
    }

    [Fact]
    public void Missing_or_truncated_profiles_are_null()
    {
        Assert.Null(PlayerProfile.Read(ReadOnlySpan<byte>.Empty));
        Assert.Null(PlayerProfile.Read(new byte[2000]));
    }

    [Fact]
    public void Full_length_zone_name_without_terminator_is_read()
    {
        Assert.Equal("abcdefghijklmno", PlayerProfile.Read(ProfileBuilder.Build("A", 1, 1, 1, "abcdefghijklmno"))!.Zone);
    }

    /// <summary>
    /// The creation packet captured from the real Trilogy client (tools/eqbot/charcreate_template.inc,
    /// a troll shaman, name zeroed) decodes with the same offsets: race 9, class 10.
    /// </summary>
    [Fact]
    public void Decodes_the_packet_captured_from_the_trilogy_client()
    {
        var payload = ReadIncludeBytes(Path.Combine(RepoRoot(), "tools", "eqbot", "charcreate_template.inc"));
        Assert.Equal(8100, payload.Length);

        var p = PlayerProfile.ReadWithoutChecksum(payload)!;

        Assert.Equal(9, p.Race);   // troll
        Assert.Equal(10, p.Class); // shaman
        Assert.Equal("", p.Name);
        // Combat fields: a level 1 troll shaman with 20 HP, troll stats, nothing equipped yet.
        Assert.Equal((20, 108, 119, 75, 83), (p.CurHp, p.Str, p.Sta, p.Dex, p.Agi));
        Assert.Equal(30, p.Inventory.Count);
        Assert.All(p.Inventory, id => Assert.Equal(0, id)); // 0xFFFF slots read as empty
        Assert.Equal(74, p.Skills.Count);
    }

    private static byte[] ReadIncludeBytes(string path) =>
        File.ReadLines(path)
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .SelectMany(l => l.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(h => byte.Parse(h.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "rewrite")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repository root");
    }
}
