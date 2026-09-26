using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace EQClassic.Shared.World;

/// <summary>What a place of a zone is, from its BSP tree: water, lava, a zone line, PvP, ice.</summary>
[Flags]
public enum RegionKind
{
    Normal = 0,
    Water = 1,
    Lava = 2,
    Pvp = 4,
    Zoneline = 8,
    WaterBlockLos = 16,
    FreezingWater = 32,
    Slippery = 64,
}

/// <summary>
/// The zone's BSP tree (LanternExtractor's Exports/&lt;zone&gt;/Zone/bsp_tree.txt), which the
/// client used to know where water, lava and zone lines are. Lantern writes each plane's normal in
/// its own axis order (WLD x, z, y), that is EverQuest (y, z, x); a point on the positive side goes
/// to the left child, a missing child is a normal region (as EQEmu's water maps read the same tree).
/// Checked on North Qeynos: the canals' surface is flat at z = −10, the zone lines are Zoneline leaves.
/// </summary>
public sealed class ZoneRegions
{
    private readonly struct Node
    {
        public readonly float A, B, C, D; // A·eqY + B·eqZ + C·eqX + D
        public readonly int Left, Right;
        public readonly RegionKind Kind;
        public readonly bool Leaf;

        public Node(float a, float b, float c, float d, int left, int right)
        {
            A = a; B = b; C = c; D = d; Left = left; Right = right; Kind = RegionKind.Normal; Leaf = false;
        }

        public Node(RegionKind kind)
        {
            A = B = C = D = 0; Left = Right = -1; Kind = kind; Leaf = true;
        }
    }

    private readonly Node[] _nodes;

    private ZoneRegions(Node[] nodes) => _nodes = nodes;

    public int NodeCount => _nodes.Length;

    public static ZoneRegions Load(string path) => Parse(File.ReadLines(path));

    /// <summary>Normal nodes: "nx, nz, ny, split, left, right"; leaves: "regionId, Type[;Type][,zone line data]".</summary>
    public static ZoneRegions Parse(IEnumerable<string> lines)
    {
        var nodes = new List<Node>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;
            var p = line.Split(',');
            if (p.Length >= 6 && TryFloat(p[0], out float a) && TryFloat(p[1], out float b) && TryFloat(p[2], out float c)
                && TryFloat(p[3], out float d) && int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int left)
                && int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int right))
            {
                nodes.Add(new Node(a, b, c, d, left, right));
                continue;
            }
            var kind = RegionKind.Normal;
            if (p.Length > 1)
                foreach (var type in p[1].Split(';'))
                    kind |= type.Trim() switch
                    {
                        "Water" => RegionKind.Water,
                        "Lava" => RegionKind.Lava,
                        "Pvp" => RegionKind.Pvp,
                        "Zoneline" => RegionKind.Zoneline,
                        "WaterBlockLos" => RegionKind.WaterBlockLos | RegionKind.Water,
                        "FreezingWater" => RegionKind.FreezingWater | RegionKind.Water,
                        "Slippery" => RegionKind.Slippery,
                        _ => RegionKind.Normal,
                    };
            nodes.Add(new Node(kind));
        }
        return new ZoneRegions(nodes.ToArray());
    }

    private static bool TryFloat(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    /// <summary>The region at an EverQuest position.</summary>
    public RegionKind At(Vec3 p)
    {
        int i = 0;
        for (int steps = 0; steps < _nodes.Length && i >= 0 && i < _nodes.Length; steps++)
        {
            var n = _nodes[i];
            if (n.Leaf)
                return n.Kind;
            float distance = n.A * p.Y + n.B * p.Z + n.C * p.X + n.D;
            i = distance > 0 ? n.Left : n.Right;
        }
        return RegionKind.Normal;
    }

    public bool InWater(Vec3 p) => (At(p) & RegionKind.Water) != 0;
    public bool InLava(Vec3 p) => (At(p) & RegionKind.Lava) != 0;
}
