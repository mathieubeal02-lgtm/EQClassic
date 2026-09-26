using EQClassic.ClientCore;

namespace EQClassic.Tests.Client;

/// <summary>The Trilogy client's interface art, when a client install is there (EQC_CLIENT or ~/eq-client).</summary>
public class ClassicArtTests
{
    private static string? ClientFile(string name)
    {
        var dir = Environment.GetEnvironmentVariable("EQC_CLIENT") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "eq-client");
        var path = Path.Combine(dir, name);
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void The_interface_frame_comes_out_of_bmpwad_with_its_view_hole_transparent()
    {
        if (ClientFile("bmpwad.s3d") is not { } archive)
            return;
        var files = PfsArchive.Read(File.ReadAllBytes(archive));
        Assert.Contains("main1.bmp", files.Keys);
        Assert.Contains("spelgems.bmp", files.Keys);
        var (w, h, rgba) = ClassicBitmap.Decode(files["main1.bmp"]);
        Assert.Equal((640, 480), (w, h));
        byte Alpha(int x, int yFromTop) => rgba[((h - 1 - yFromTop) * w + x) * 4 + 3];
        Assert.Equal(0, Alpha(300, 150));    // the 3D view (magenta)
        Assert.Equal(255, Alpha(40, 25));    // the HELP button
        var (gw, gh, _) = ClassicBitmap.Decode(files["spelgems.bmp"]); // 8-bit with a palette
        Assert.Equal((640, 480), (gw, gh));
    }

    [Fact]
    public void Scale2x_doubles_and_keeps_a_diagonal_sharp()
    {
        // 2 × 2: white on the diagonal, black elsewhere.
        byte[] W = [255, 255, 255, 255], K = [0, 0, 0, 255];
        byte[] image = [.. W, .. K, .. K, .. W];
        var (w, h, rgba) = ClassicBitmap.Scale2x(2, 2, image);
        Assert.Equal((4, 4), (w, h));
        Assert.Equal(64, rgba.Length);
        Assert.Equal(255, rgba[0]);              // the corner stays white
        Assert.Equal(0, rgba[(0 * 4 + 3) * 4]); // the opposite corner of the row stays black
    }
}
