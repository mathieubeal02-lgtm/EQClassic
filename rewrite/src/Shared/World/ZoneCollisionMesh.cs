using System.IO;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;

namespace EQClassic.Shared.World;

/// <summary>
/// A zone's collision triangles, loaded from LanternExtractor's intermediate export
/// (Exports/&lt;zone&gt;/Zone/Meshes/&lt;zone&gt;_collision.txt), with the ground queries the server
/// needs to place and move NPCs.
///
/// Lantern writes Unity coordinates (Y up). Server coordinates are EverQuest's (Z up):
/// eqX = lanternZ, eqY = lanternX, eqZ = lanternY. Checked against the EQEmu .map of Permafrost:
/// at (167, -60) both give floors at z = 0 and z = 40.
/// </summary>
public sealed class ZoneCollisionMesh
{
    private const float CellSize = 64f;

    private readonly Vec3[] _vertices;
    private readonly int[] _triangles; // 3 vertex indices per triangle
    private readonly Dictionary<(int, int), List<int>> _cells = new();

    public int VertexCount => _vertices.Length;
    public int TriangleCount => _triangles.Length / 3;

    public ZoneCollisionMesh(IReadOnlyList<Vec3> vertices, IReadOnlyList<int> triangleIndices)
    {
        if (triangleIndices.Count % 3 != 0)
            throw new ArgumentException("triangle index count must be a multiple of 3", nameof(triangleIndices));
        _vertices = vertices.ToArray();
        _triangles = triangleIndices.ToArray();
        foreach (int index in _triangles)
            if ((uint)index >= (uint)_vertices.Length)
                throw new ArgumentException($"vertex index {index} out of range", nameof(triangleIndices));
        BuildIndex();
    }

    /// <summary>Reads a Lantern intermediate mesh ("v,x,y,z" and "i,material,a,b,c" lines).</summary>
    public static ZoneCollisionMesh LoadLantern(string path) => ParseLantern(File.ReadLines(path));

    public static ZoneCollisionMesh ParseLantern(IEnumerable<string> lines)
    {
        var vertices = new List<Vec3>();
        var triangles = new List<int>();
        ReadLantern(lines, vertices, triangles, static v => v);
        return new ZoneCollisionMesh(vertices, triangles);
    }

    /// <summary>
    /// A zone as the client collides with it: the zone mesh (Zone/Meshes/&lt;zone&gt;_collision.txt)
    /// plus every placed object (Zone/object_instances.txt) whose model has a collision mesh
    /// (Objects/Meshes/&lt;model&gt;_collision.txt). Objects without one (crates, barrels) or with an
    /// empty one are not solid; in Qeynos only the trees are.
    /// </summary>
    public static ZoneCollisionMesh LoadLanternZone(string exportDir, string zone)
    {
        var instances = Path.Combine(exportDir, zone, "Zone", "object_instances.txt");
        return ParseLanternZone(
            File.ReadLines(Path.Combine(exportDir, zone, "Zone", "Meshes", zone + "_collision.txt")),
            File.Exists(instances) ? File.ReadLines(instances) : Array.Empty<string>(),
            model =>
            {
                var path = Path.Combine(exportDir, zone, "Objects", "Meshes", model + "_collision.txt");
                return File.Exists(path) ? File.ReadLines(path) : null;
            });
    }

    /// <param name="objectMesh">The collision mesh lines of an object model, or null when it has none.</param>
    public static ZoneCollisionMesh ParseLanternZone(IEnumerable<string> zoneMesh, IEnumerable<string> objectInstances,
        Func<string, IEnumerable<string>?> objectMesh)
    {
        var vertices = new List<Vec3>();
        var triangles = new List<int>();
        ReadLantern(zoneMesh, vertices, triangles, static v => v);
        var models = new Dictionary<string, List<string>?>();
        int lineNo = 0;
        foreach (var raw in objectInstances)
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            // ModelName, PosX, PosY, PosZ, RotX, RotY, RotZ, ScaleX, ScaleY, ScaleZ, ColorIndex (Lantern axes)
            var f = line.Split(',');
            if (f.Length < 10)
                throw new FormatException($"object instance line {lineNo}: expected 10 fields or more");
            var pos = (X: Parse(f[1], lineNo), Y: Parse(f[2], lineNo), Z: Parse(f[3], lineNo));
            if (pos.Y < -30000f)
                continue; // fallen to the bottom of the world (LanternUnityTools skips them too)
            if (!models.TryGetValue(f[0], out var mesh))
            {
                mesh = objectMesh(f[0])?.ToList();
                // LanternExtractor writes most object collision files with no vertices at all, only
                // "i,0,0,0,0" lines (Qeynos: everything but the trees): nothing solid there.
                if (mesh is not null && !mesh.Any(l => l.StartsWith("v,", StringComparison.Ordinal)))
                    mesh = null;
                models[f[0]] = mesh;
            }
            if (mesh is null)
                continue;
            var place = Placement(pos, Parse(f[4], lineNo), Parse(f[5], lineNo), Parse(f[6], lineNo),
                (Parse(f[7], lineNo), Parse(f[8], lineNo), Parse(f[9], lineNo)));
            ReadLantern(mesh, vertices, triangles, place);
        }
        return new ZoneCollisionMesh(vertices, triangles);
    }

    /// <summary>
    /// Lantern (Unity) axes: scale, then Unity's Quaternion.Euler(x, y, z) rotation (z first, then
    /// x, then y; left-handed), then the position.
    /// </summary>
    private static Func<(float X, float Y, float Z), (float X, float Y, float Z)> Placement(
        (float X, float Y, float Z) pos, float rx, float ry, float rz, (float X, float Y, float Z) scale)
    {
        const float rad = MathF.PI / 180f;
        float cx = MathF.Cos(rx * rad), sx = MathF.Sin(rx * rad);
        float cy = MathF.Cos(ry * rad), sy = MathF.Sin(ry * rad);
        float cz = MathF.Cos(rz * rad), sz = MathF.Sin(rz * rad);
        return v =>
        {
            float x = v.X * scale.X, y = v.Y * scale.Y, z = v.Z * scale.Z;
            (x, y) = (x * cz - y * sz, x * sz + y * cz);
            (y, z) = (y * cx - z * sx, y * sx + z * cx);
            (x, z) = (x * cy + z * sy, -x * sy + z * cy);
            return (x + pos.X, y + pos.Y, z + pos.Z);
        };
    }

    /// <summary>Writes the mesh back in Lantern's intermediate format (Lantern axes).</summary>
    public void WriteLantern(TextWriter writer)
    {
        writer.WriteLine("# EQClassic collision mesh (Lantern intermediate format)");
        foreach (var v in _vertices)
            writer.WriteLine(FormattableString.Invariant($"v,{v.Y:R},{v.Z:R},{v.X:R}"));
        for (int t = 0; t < TriangleCount; t++)
            writer.WriteLine(FormattableString.Invariant($"i,0,{_triangles[3 * t]},{_triangles[3 * t + 1]},{_triangles[3 * t + 2]}"));
    }

    private static void ReadLantern(IEnumerable<string> lines, List<Vec3> vertices, List<int> triangles,
        Func<(float X, float Y, float Z), (float X, float Y, float Z)> place)
    {
        int first = vertices.Count;
        int lineNo = 0;
        foreach (var raw in lines)
        {
            lineNo++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var parts = line.Split(',');
            switch (parts[0])
            {
                case "v" when parts.Length >= 4:
                    var (lx, ly, lz) = place((Parse(parts[1], lineNo), Parse(parts[2], lineNo), Parse(parts[3], lineNo)));
                    vertices.Add(new Vec3(lz, lx, ly));
                    break;
                case "i" when parts.Length >= 5:
                    triangles.Add(first + ParseInt(parts[2], lineNo));
                    triangles.Add(first + ParseInt(parts[3], lineNo));
                    triangles.Add(first + ParseInt(parts[4], lineNo));
                    break;
                default:
                    break; // material lists, UVs, colors...: not needed for collision
            }
        }
    }

    /// <summary>
    /// Heights of every surface under (x, y), lowest first. A point on an edge shared by two
    /// triangles of the same surface is reported once (heights within 0.01 are merged).
    /// </summary>
    public IReadOnlyList<float> SurfacesAt(float x, float y)
    {
        var result = new List<float>();
        if (!_cells.TryGetValue(CellOf(x, y), out var candidates))
            return result;
        foreach (int t in candidates)
            if (HeightInTriangle(t, x, y) is float z)
                result.Add(z);
        result.Sort();
        for (int i = result.Count - 1; i > 0; i--)
            if (result[i] - result[i - 1] < 0.01f)
                result.RemoveAt(i);
        return result;
    }

    /// <summary>
    /// The ground an actor at (x, y) standing around <paramref name="fromZ"/> is on: the highest
    /// surface no more than <paramref name="stepUp"/> above fromZ. Null when there is nothing
    /// below. Searching from far above picks up roofs and bridges (the legacy zone's "guards fall
    /// from the sky" bug came from a 150-unit search), so keep stepUp small.
    /// </summary>
    public float? GroundZ(float x, float y, float fromZ, float stepUp = 10f)
    {
        float? best = null;
        foreach (float z in SurfacesAt(x, y))
            if (z <= fromZ + stepUp)
                best = z;
        return best;
    }

    /// <summary>
    /// True when nothing of the mesh crosses the segment a→b (legacy CheckCoordLos): used for aggro.
    /// Möller–Trumbore over the triangles of the cells the segment passes through.
    /// </summary>
    public bool LineOfSight(Vec3 a, Vec3 b)
    {
        float dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        float length = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length < 1e-4f)
            return true;
        int steps = (int)(MathF.Sqrt(dx * dx + dy * dy) / (CellSize / 2)) + 1;
        var seen = new HashSet<int>();
        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            if (!_cells.TryGetValue(CellOf(a.X + dx * t, a.Y + dy * t), out var candidates))
                continue;
            foreach (int tri in candidates)
                if (seen.Add(tri) && SegmentHits(tri, a, dx, dy, dz))
                    return false;
        }
        return true;
    }

    private bool SegmentHits(int t, Vec3 p, float dx, float dy, float dz)
    {
        var v0 = _vertices[_triangles[3 * t]];
        var v1 = _vertices[_triangles[3 * t + 1]];
        var v2 = _vertices[_triangles[3 * t + 2]];
        float e1x = v1.X - v0.X, e1y = v1.Y - v0.Y, e1z = v1.Z - v0.Z;
        float e2x = v2.X - v0.X, e2y = v2.Y - v0.Y, e2z = v2.Z - v0.Z;
        float hx = dy * e2z - dz * e2y, hy = dz * e2x - dx * e2z, hz = dx * e2y - dy * e2x;
        float det = e1x * hx + e1y * hy + e1z * hz;
        if (MathF.Abs(det) < 1e-9f)
            return false;
        float f = 1f / det;
        float sx = p.X - v0.X, sy = p.Y - v0.Y, sz = p.Z - v0.Z;
        float u = f * (sx * hx + sy * hy + sz * hz);
        if (u < 0f || u > 1f)
            return false;
        float qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
        float v = f * (dx * qx + dy * qy + dz * qz);
        if (v < 0f || u + v > 1f)
            return false;
        float hit = f * (e2x * qx + e2y * qy + e2z * qz);
        return hit > 0.01f && hit < 0.99f; // end points (standing on a floor) do not count
    }

    private float? HeightInTriangle(int t, float x, float y)
    {
        var a = _vertices[_triangles[3 * t]];
        var b = _vertices[_triangles[3 * t + 1]];
        var c = _vertices[_triangles[3 * t + 2]];
        float d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(d) < 1e-9f)
            return null; // vertical (wall) or degenerate: no ground here
        float l1 = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d;
        float l2 = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d;
        float l3 = 1f - l1 - l2;
        const float eps = -1e-5f;
        if (l1 < eps || l2 < eps || l3 < eps)
            return null;
        return l1 * a.Z + l2 * b.Z + l3 * c.Z;
    }

    private void BuildIndex()
    {
        for (int t = 0; t < TriangleCount; t++)
        {
            var a = _vertices[_triangles[3 * t]];
            var b = _vertices[_triangles[3 * t + 1]];
            var c = _vertices[_triangles[3 * t + 2]];
            var (minX, maxX) = (MathF.Min(a.X, MathF.Min(b.X, c.X)), MathF.Max(a.X, MathF.Max(b.X, c.X)));
            var (minY, maxY) = (MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), MathF.Max(a.Y, MathF.Max(b.Y, c.Y)));
            var (cx0, cy0) = CellOf(minX, minY);
            var (cx1, cy1) = CellOf(maxX, maxY);
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cy = cy0; cy <= cy1; cy++)
                {
                    if (!_cells.TryGetValue((cx, cy), out var list))
                        _cells[(cx, cy)] = list = new List<int>();
                    list.Add(t);
                }
        }
    }

    private static (int, int) CellOf(float x, float y) => ((int)MathF.Floor(x / CellSize), (int)MathF.Floor(y / CellSize));

    private static float Parse(string s, int lineNo) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new FormatException($"line {lineNo}: '{s}' is not a number (export with an invariant culture, see tools/lantern/extract.sh)");

    private static int ParseInt(string s, int lineNo) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new FormatException($"line {lineNo}: '{s}' is not an index");
}
