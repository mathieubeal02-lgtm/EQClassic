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
                    float lx = Parse(parts[1], lineNo), ly = Parse(parts[2], lineNo), lz = Parse(parts[3], lineNo);
                    vertices.Add(new Vec3(lz, lx, ly));
                    break;
                case "i" when parts.Length >= 5:
                    triangles.Add(ParseInt(parts[2], lineNo));
                    triangles.Add(ParseInt(parts[3], lineNo));
                    triangles.Add(ParseInt(parts[4], lineNo));
                    break;
                default:
                    break; // material lists, UVs, colors...: not needed for collision
            }
        }
        return new ZoneCollisionMesh(vertices, triangles);
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
