using EQClassic.Shared.World;

namespace EQClassic.Tests.World;

public class ZoneCollisionMeshTests
{
    // Two quads in Lantern's Unity axes (x, y up, z): a floor at height 0 and a roof at height 40,
    // both covering EverQuest x = lanternZ in [0, 100], y = lanternX in [0, 100].
    private static readonly string[] FloorAndRoof =
    [
        "# Lantern Extractor 0.1.7 - Mesh Intermediate Format",
        "v,0,0,0", "v,100,0,0", "v,100,0,100", "v,0,0,100",
        "v,0,40,0", "v,100,40,0", "v,100,40,100", "v,0,40,100",
        "i,0,0,1,2", "i,0,0,2,3",
        "i,0,4,5,6", "i,0,4,6,7",
    ];

    [Fact]
    public void Parses_lantern_intermediate_format()
    {
        var mesh = ZoneCollisionMesh.ParseLantern(FloorAndRoof);
        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(4, mesh.TriangleCount);
    }

    [Fact]
    public void Lists_every_surface_under_a_point()
    {
        Assert.Equal([0f, 40f], ZoneCollisionMesh.ParseLantern(FloorAndRoof).SurfacesAt(50, 50));
    }

    [Fact]
    public void Ground_is_the_floor_the_actor_stands_on_not_the_roof()
    {
        var mesh = ZoneCollisionMesh.ParseLantern(FloorAndRoof);
        Assert.Equal(0f, mesh.GroundZ(50, 50, fromZ: 3));
        Assert.Equal(40f, mesh.GroundZ(50, 50, fromZ: 45));
    }

    [Fact]
    public void Nothing_outside_the_mesh_or_under_the_world()
    {
        var mesh = ZoneCollisionMesh.ParseLantern(FloorAndRoof);
        Assert.Null(mesh.GroundZ(500, 500, 0));
        Assert.Null(mesh.GroundZ(50, 50, fromZ: -100));
    }

    [Fact]
    public void Axes_follow_the_everquest_convention()
    {
        // A floor only over EverQuest x in [0,100] and y in [200,300]: Lantern (x=200..300, z=0..100).
        var mesh = ZoneCollisionMesh.ParseLantern(["v,200,5,0", "v,300,5,0", "v,300,5,100", "v,200,5,100", "i,0,0,1,2", "i,0,0,2,3"]);
        Assert.Equal(5f, mesh.GroundZ(50, 250, 5));
        Assert.Null(mesh.GroundZ(250, 50, 5));
    }

    [Fact]
    public void Localized_decimal_commas_are_reported()
    {
        // A French-locale export writes "285,875" and shifts every field.
        var e = Assert.ThrowsAny<Exception>(() => ZoneCollisionMesh.ParseLantern(["v,285,875,14,223,90625", "i,0,0,0,x"]));
        Assert.Contains("line 2", e.Message);
    }

    // An object: a wall in Lantern axes, x -5..5, height 0..20, at z = 10 (in front of its origin).
    private static readonly string[] WallModel =
        ["v,-5,0,10", "v,5,0,10", "v,5,20,10", "v,-5,20,10", "i,0,0,1,2", "i,0,0,2,3"];

    private static ZoneCollisionMesh ZoneWith(params string[] instances) =>
        ZoneCollisionMesh.ParseLanternZone(["v,-100,0,-100", "v,100,0,-100", "v,100,0,100", "v,-100,0,100", "i,0,0,1,2", "i,0,0,2,3"],
            instances, model => model switch
            {
                "wall" => WallModel,
                "lamp" => ["i,0,0,0,0", "i,0,0,0,0"], // how LanternExtractor writes most object collisions
                _ => null,
            });

    [Fact]
    public void Placed_objects_are_solid()
    {
        var mesh = ZoneWith("wall,0,0,0,0,0,0,1,1,1,-1");
        // Unrotated, the wall stands at Lantern z = 10, that is EverQuest x = 10.
        Assert.False(mesh.LineOfSight(new Vec3(0, 0, 5), new Vec3(20, 0, 5)));
        Assert.True(mesh.LineOfSight(new Vec3(0, 0, 5), new Vec3(0, 20, 5)));
    }

    [Fact]
    public void Objects_follow_unity_rotation_scale_and_position()
    {
        // Quaternion.Euler(0, 90, 0) turns +z into +x: the wall moves to Lantern x = 10 (EverQuest y).
        var rotated = ZoneWith("wall,0,0,0,0,90,0,1,1,1,-1");
        Assert.True(rotated.LineOfSight(new Vec3(0, 0, 5), new Vec3(20, 0, 5)));
        Assert.False(rotated.LineOfSight(new Vec3(0, 0, 5), new Vec3(0, 20, 5)));

        // Scale 2 then moved by Lantern (0, 0, 30): the wall ends at EverQuest x = 50, 40 high.
        var moved = ZoneWith("wall,0,0,30,0,0,0,2,2,2,-1");
        Assert.True(moved.LineOfSight(new Vec3(0, 0, 5), new Vec3(45, 0, 5)));
        Assert.False(moved.LineOfSight(new Vec3(45, 0, 35), new Vec3(55, 0, 35)));
    }

    [Fact]
    public void Objects_without_collision_or_under_the_world_are_ignored()
    {
        var mesh = ZoneWith("crate,0,0,0,0,0,0,1,1,1,-1", "lamp,0,0,0,0,0,0,1,1,1,-1", "wall,0,-40000,0,0,0,0,1,1,1,-1");
        Assert.Equal(2, mesh.TriangleCount);
    }

    [Fact]
    public void A_written_mesh_reads_back_the_same()
    {
        var mesh = ZoneWith("wall,3,0,7,0,33,0,1.5,1,1,-1");
        var text = new StringWriter();
        mesh.WriteLantern(text);
        var back = ZoneCollisionMesh.ParseLantern(text.ToString().Split('\n'));
        Assert.Equal(mesh.TriangleCount, back.TriangleCount);
        foreach (var (a, b) in new[] { (new Vec3(0, 0, 5), new Vec3(20, 0, 5)), (new Vec3(0, 0, 5), new Vec3(0, 20, 5)) })
            Assert.Equal(mesh.LineOfSight(a, b), back.LineOfSight(a, b));
    }

    /// <summary>
    /// With a real export (tools/lantern/extract.sh permafrost), Permafrost's entrance matches the
    /// EQEmu .map the zone server uses: floors at 0 and 40 at (167, -60). Skipped silently when the
    /// client-derived export is absent (it is not in git).
    /// </summary>
    [Fact]
    public void Permafrost_export_matches_the_zone_server_map_when_present()
    {
        var path = Path.Combine(RepoRoot(), "build", "lantern-work", "Exports", "permafrost", "Zone", "Meshes", "permafrost_collision.txt");
        if (!File.Exists(path))
            return;
        var mesh = ZoneCollisionMesh.LoadLantern(path);
        Assert.Equal([0f, 40f], mesh.SurfacesAt(167, -60).Select(z => MathF.Round(z)).ToArray());
        Assert.Equal(0f, mesh.GroundZ(167, -60, fromZ: 3.75f)!.Value, precision: 1);
    }

    /// <summary>With a real Qeynos export: the trees add triangles.</summary>
    [Fact]
    public void Qeynos_objects_add_collision_when_present()
    {
        var exports = Path.Combine(RepoRoot(), "build", "lantern-work", "Exports");
        if (!File.Exists(Path.Combine(exports, "qeynos2", "Zone", "object_instances.txt")))
            return;
        var zoneOnly = ZoneCollisionMesh.LoadLantern(Path.Combine(exports, "qeynos2", "Zone", "Meshes", "qeynos2_collision.txt"));
        var withObjects = ZoneCollisionMesh.LoadLanternZone(exports, "qeynos2");
        Assert.True(withObjects.TriangleCount > zoneOnly.TriangleCount);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "rewrite")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
