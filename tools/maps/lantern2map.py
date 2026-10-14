#!/usr/bin/env python3
"""Builds a zone collision map (.map, the format of Zone/Include/map.h) from a LanternExtractor export:
the zone's mesh plus every placed object (trees, huts, bridges...). Utils/azone only reads the zone
mesh, so its maps let NPCs and bots see and walk through whatever stands on the ground.

Usage: lantern2map.py <Exports directory> <output directory> [zone ...]   (default: every zone exported)

Lantern's axes (x, up, z) become the server's (z, x, up). An object uses its collision mesh when
Lantern wrote one (it does when some polygons are not solid), its whole mesh otherwise."""
import math
import os
import struct
import sys

MAX_FACES = 150		# a quadtree node with no more faces than this is final...
MIN_SIZE = 64.0		# ...and so is one smaller than this
NODE_NONE = 65534


def read_mesh(path):
    verts, tris = [], []
    with open(path, encoding='latin-1') as f:
        for line in f:
            p = line.rstrip('\n').split(',')
            if p[0] == 'v':
                verts.append((float(p[1]), float(p[2]), float(p[3])))
            elif p[0] == 'i':
                tris.append((int(p[2]), int(p[3]), int(p[4])))
    return verts, tris


def placed(v, pos, rot, scale):
    """Unity's transform: scale, then rotate about z, x, y (degrees, left-handed), then move."""
    x, y, z = v[0] * scale[0], v[1] * scale[1], v[2] * scale[2]
    rx, ry, rz = (math.radians(a) for a in rot)
    x, y = x * math.cos(rz) - y * math.sin(rz), x * math.sin(rz) + y * math.cos(rz)
    y, z = y * math.cos(rx) - z * math.sin(rx), y * math.sin(rx) + z * math.cos(rx)
    x, z = x * math.cos(ry) + z * math.sin(ry), -x * math.sin(ry) + z * math.cos(ry)
    return (x + pos[0], y + pos[1], z + pos[2])


def turned(v, q):
    """v rotated by the quaternion q = (x, y, z, w)."""
    x, y, z, w = q
    tx, ty, tz = 2 * (y * v[2] - z * v[1]), 2 * (z * v[0] - x * v[2]), 2 * (x * v[1] - y * v[0])
    return (v[0] + w * tx + y * tz - z * ty, v[1] + w * ty + z * tx - x * tz, v[2] + w * tz + x * ty - y * tx)


def object_mesh(objects_dir, model):
    """The mesh of a placed model, None if Lantern exported none. A model with a skeleton (the swaying
    trees of Kunark and Velious) is one mesh per bone: put together in the pose of its first frame."""
    def part(name):
        for suffix in ('_collision.txt', '.txt'):
            path = os.path.join(objects_dir, 'Meshes', name + suffix)
            if os.path.exists(path):
                return read_mesh(path)
        return None

    skeleton = os.path.join(objects_dir, 'Skeletons', model + '.txt')
    pose = os.path.join(objects_dir, 'Animations', model + '_pos.txt')
    if not os.path.exists(skeleton) or not os.path.exists(pose):
        return part(model)
    bone_mesh, local = {}, {}
    for line in open(skeleton, encoding='latin-1'):
        p = line.strip().split(',')
        if not line.startswith('#') and len(p) >= 3 and p[2]:
            bone_mesh[p[0].lower()] = p[2].lower()
    for line in open(pose, encoding='latin-1'):
        p = line.strip().split(',')
        if len(p) >= 10 and '/' in p[0] + '/' and p[1] == '0':	# path, frame, position, quaternion, scale
            n = [float(x) for x in p[2:10]]
            local[p[0].lower()] = (n[0:3], n[3:7], n[7])
    verts, tris = [], []
    for path in local:
        mesh = part(bone_mesh.get(path.split('/')[-1], ''))
        if mesh is None:
            continue
        chain = path.split('/')
        base = len(verts)
        for v in mesh[0]:
            for depth in range(len(chain), 0, -1):	# from the bone up to the root
                pos, quat, scale = local.get('/'.join(chain[:depth]), ((0, 0, 0), (0, 0, 0, 1), 1))
                r = turned((v[0] * scale, v[1] * scale, v[2] * scale), quat)
                v = (r[0] + pos[0], r[1] + pos[1], r[2] + pos[2])
            verts.append(v)
        tris.extend((a + base, b + base, c + base) for a, b, c in mesh[1])
    return (verts, tris) if tris else None


def zone_faces(zone_dir):
    """Triangles in server axes, and how many objects were placed / had no mesh."""
    name = os.path.basename(zone_dir.rstrip('/'))
    faces = []

    def add(verts, tris):
        for a, b, c in tris:
            if max(a, b, c) < len(verts):
                faces.append(tuple((verts[i][2], verts[i][0], verts[i][1]) for i in (a, b, c)))

    add(*read_mesh(os.path.join(zone_dir, 'Zone', 'Meshes', name + '.txt')))
    meshes, done, missing = {}, 0, set()
    instances = os.path.join(zone_dir, 'Zone', 'object_instances.txt')
    for line in open(instances, encoding='latin-1') if os.path.exists(instances) else []:
        p = line.strip().split(',')
        if line.startswith('#') or len(p) < 10:
            continue
        model = p[0].lower()
        if model not in meshes:
            meshes[model] = object_mesh(os.path.join(zone_dir, 'Objects'), model)
        if meshes[model] is None:
            missing.add(model)
            continue
        n = [float(x) for x in p[1:10]]
        verts, tris = meshes[model]
        add([placed(v, n[0:3], n[3:6], n[6:9]) for v in verts], tris)
        done += 1
    return faces, done, sorted(missing)


def write_map(faces, path):
    records, boxes = [], []
    for a, b, c in faces:
        ux, uy, uz = b[0] - a[0], b[1] - a[1], b[2] - a[2]
        vx, vy, vz = c[0] - a[0], c[1] - a[1], c[2] - a[2]
        nx, ny, nz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
        length = math.sqrt(nx * nx + ny * ny + nz * nz)
        if length < 1e-9:
            continue		# no area
        nx, ny, nz = nx / length, ny / length, nz / length
        records.append(struct.pack('<13f', *a, *b, *c, nx, ny, nz, -(nx * a[0] + ny * a[1] + nz * a[2])))
        boxes.append((min(a[0], b[0], c[0]), min(a[1], b[1], c[1]), max(a[0], b[0], c[0]), max(a[1], b[1], c[1])))
    nodes, lists = [], []

    def build(ids, minx, miny, maxx, maxy):
        """Appends the node and its children (depth first, as azone does); returns its index."""
        me = len(nodes)
        nodes.append(None)
        if len(ids) <= MAX_FACES or maxx - minx < MIN_SIZE or maxy - miny < MIN_SIZE:
            nodes[me] = struct.pack('<4fBII', minx, miny, maxx, maxy, 1, len(ids), len(lists))
            lists.extend(ids)
            return me
        midx, midy = (minx + maxx) / 2, (miny + maxy) / 2
        kids = []
        # map.h's order: x>=mid y>=mid, x<mid y>=mid, x<mid y<mid, x>=mid y<mid
        for x0, y0, x1, y1 in ((midx, midy, maxx, maxy), (minx, midy, midx, maxy), (minx, miny, midx, midy), (midx, miny, maxx, midy)):
            inside = [i for i in ids if boxes[i][0] <= x1 and boxes[i][2] >= x0 and boxes[i][1] <= y1 and boxes[i][3] >= y0]
            kids.append(build(inside, x0, y0, x1, y1) if inside else NODE_NONE)
        nodes[me] = struct.pack('<4fB4H', minx, miny, maxx, maxy, 0, *kids)
        return me

    build(list(range(len(records))), min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
    if len(nodes) >= NODE_NONE:
        raise SystemExit('%s: %d nodes, more than the format holds' % (path, len(nodes)))
    with open(path, 'wb') as out:
        out.write(struct.pack('<IIHI', 0x01000000, len(records), len(nodes), len(lists)))
        out.write(b''.join(records))
        out.write(b''.join(nodes))
        out.write(struct.pack('<%dI' % len(lists), *lists))
    return len(records), len(nodes), len(lists)


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    exports, out = sys.argv[1], sys.argv[2]
    zones = sys.argv[3:] or sorted(d for d in os.listdir(exports) if os.path.exists(os.path.join(exports, d, 'Zone', 'Meshes', d + '.txt')))
    os.makedirs(out, exist_ok=True)
    for zone in zones:
        faces, objects, missing = zone_faces(os.path.join(exports, zone))
        count, nodes, lists = write_map(faces, os.path.join(out, zone + '.map'))
        print('%-14s %7d faces %5d nodes %7d list entries, %4d objects%s' % (zone, count, nodes, lists, objects,
              ', no mesh for: ' + ' '.join(missing) if missing else ''))


if __name__ == '__main__':
    main()
