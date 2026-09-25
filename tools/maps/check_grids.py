#!/usr/bin/env python3
"""Check NPC waypoint grids (grid_entries) against zone geometry (.map files).

For each zone that has grids and a Maps/Maps/<zone>.map file, reports:
  - waypoints with no floor under them (under the world or off the mesh),
  - waypoints floating well above the floor below them,
  - consecutive waypoints whose segment goes through a wall (steep geometry at body and head
    height), a step (steep, body height only: a ledge or low obstacle), or only through floor/terrain (a hill or stairs between them: the NPC briefly sinks into the ground),
and writes a summary per zone plus a CSV of the problems.

Why: NPCs now walk grid_entries point by point (NPC::SetWaypointGrid). Grid data captured from
other servers is not always consistent with our maps; an NPC walking through a wall ends up where
players cannot reach or see it (seen with a Qeynos sewer rat).

.map format (Utils/azone/map.h, as read by Zone/Source/Map.cpp): packed header
<uint32 version, uint32 face_count, uint16 node_count, uint32 facelist_count>, then face_count
faces of 13 floats: vertices a, b, c (x, y, z each) and the plane normal (nx, ny, nz, d).

Usage: check_grids.py --maps ~/eqc-server/Maps/Maps [--zone qeynos2] [--csv out.csv]
DB access through environment: EQC_DB_HOST (127.0.0.1), EQC_DB_USER (eqc), EQC_DB_PASS (eqc), EQC_DB_NAME (eqclassic).
Needs numpy and pymysql.
"""
import argparse
import csv
import os
import struct
import sys

import numpy as np
import pymysql

HEADER = struct.Struct('<IIHI')
FACE_FLOATS = 13

FLOOR_TOLERANCE = 10.0   # a waypoint may sit this far below the surface it stands on (grid z noise)
FLOAT_LIMIT = 40.0       # above this distance from the floor below, the waypoint is floating
EYE_HEIGHT = 5.0         # segments are tested this far above the waypoints, like an NPC's body...
HEAD_HEIGHT = 15.0       # ...and again this high: a wall blocks both, a step or ledge only the first
CELL = 64.0              # XY bucket size for the triangle index
STEEP_NZ = 0.5           # |normal z| below this (slope over 60 degrees): the triangle is a wall


def load_map(path):
    """Triangles of a .map file, or None when the file is truncated (the zone rejects it too)."""
    data = open(path, 'rb').read()
    _version, faces, _nodes, _facelists = HEADER.unpack_from(data, 0)
    if len(data) < HEADER.size + faces * FACE_FLOATS * 4:
        return None
    tri = np.frombuffer(data, dtype='<f4', count=faces * FACE_FLOATS, offset=HEADER.size)
    return tri.reshape(faces, FACE_FLOATS)[:, :9].reshape(faces, 3, 3).astype(np.float64)


class Mesh:
    def __init__(self, tris):
        self.tris = tris
        self.v0 = tris[:, 0]
        self.e1 = tris[:, 1] - tris[:, 0]
        self.e2 = tris[:, 2] - tris[:, 0]
        n = np.cross(self.e1, self.e2)
        self.steep = np.abs(n[:, 2]) < STEEP_NZ * np.maximum(np.linalg.norm(n, axis=1), 1e-12)
        lo = tris[:, :, :2].min(axis=1)
        hi = tris[:, :, :2].max(axis=1)
        self.buckets = {}
        for i in range(len(tris)):
            for bx in range(int(np.floor(lo[i, 0] / CELL)), int(np.floor(hi[i, 0] / CELL)) + 1):
                for by in range(int(np.floor(lo[i, 1] / CELL)), int(np.floor(hi[i, 1] / CELL)) + 1):
                    self.buckets.setdefault((bx, by), []).append(i)

    def near(self, x, y):
        return self.buckets.get((int(np.floor(x / CELL)), int(np.floor(y / CELL))), [])

    def surfaces_at(self, x, y):
        """Z of every triangle whose XY projection contains (x, y)."""
        zs = []
        for i in self.near(x, y):
            (ax, ay, az), (bx, by, bz), (cx, cy, cz) = self.tris[i]
            d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy)
            if abs(d) < 1e-9:
                continue
            l1 = ((by - cy) * (x - cx) + (cx - bx) * (y - cy)) / d
            l2 = ((cy - ay) * (x - cx) + (ax - cx) * (y - cy)) / d
            l3 = 1.0 - l1 - l2
            if min(l1, l2, l3) >= -1e-6:
                zs.append(l1 * az + l2 * bz + l3 * cz)
        return zs

    def segment_blocked(self, p, q):
        """Kind of geometry segment p->q crosses (Moller-Trumbore, end points excluded):
        'wall' if any crossed triangle is steep, 'floor' if it only crosses flat ones (a hill or
        stairs between two waypoints: the NPC briefly sinks into the ground), None if the way is clear."""
        p = np.asarray(p, dtype=float)
        q = np.asarray(q, dtype=float)
        d = q - p
        length = np.linalg.norm(d)
        if length < 1e-6:
            return None
        steps = int(length / (CELL / 2)) + 1
        candidates = set()
        for s in range(steps + 1):
            x, y = p[:2] + d[:2] * (s / steps)
            candidates.update(self.near(x, y))
        if not candidates:
            return None
        idx = np.fromiter(candidates, dtype=np.int64)
        v0, e1, e2 = self.v0[idx], self.e1[idx], self.e2[idx]
        h = np.cross(d, e2)
        a = np.einsum('ij,ij->i', e1, h)
        ok = np.abs(a) > 1e-9
        f = np.where(ok, 1.0 / np.where(ok, a, 1.0), 0.0)
        s_ = p - v0
        u = f * np.einsum('ij,ij->i', s_, h)
        qv = np.cross(s_, e1)
        v = f * (qv @ d)
        t = f * np.einsum('ij,ij->i', e2, qv)
        hits = ok & (u >= 0) & (u <= 1) & (v >= 0) & (u + v <= 1) & (t > 0.02) & (t < 0.98)
        if not hits.any():
            return None
        return 'wall' if self.steep[idx][hits].any() else 'floor'


def zone_ids(db):
    with db.cursor() as c:
        c.execute("SELECT zoneidnumber, short_name FROM zone_ids")
        return {int(z): n.lower() for z, n in c.fetchall()}


def grids_of_zone(db, zoneid):
    with db.cursor() as c:
        c.execute("SELECT gridid, number, x, y, z FROM grid_entries WHERE zoneid = %s ORDER BY gridid, number", (zoneid,))
        grids = {}
        for gid, num, x, y, z in c.fetchall():
            grids.setdefault(int(gid), []).append((int(num), float(x), float(y), float(z)))
        return grids


def check_zone(mesh, grids):
    problems = []
    points = 0
    for gid, pts in grids.items():
        for idx, (num, x, y, z) in enumerate(pts):
            points += 1
            surfaces = mesh.surfaces_at(x, y)
            below = [s for s in surfaces if s <= z + FLOOR_TOLERANCE]
            if not below:
                problems.append((gid, num, 'no_floor', x, y, z, ''))
            elif z - max(below) > FLOAT_LIMIT:
                problems.append((gid, num, 'floating', x, y, z, round(z - max(below), 1)))
            if idx > 0:
                _, px, py, pz = pts[idx - 1]
                hit = mesh.segment_blocked((px, py, pz + EYE_HEIGHT), (x, y, z + EYE_HEIGHT))
                if hit == 'wall' and mesh.segment_blocked((px, py, pz + HEAD_HEIGHT), (x, y, z + HEAD_HEIGHT)) != 'wall':
                    hit = 'step'
                if hit:
                    problems.append((gid, num, 'through_' + hit, x, y, z, ''))
    return points, problems


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--maps', required=True, help='directory with <zone>.map files')
    ap.add_argument('--zone', help='only this zone short name')
    ap.add_argument('--csv', help='write every problem to this CSV file')
    ap.add_argument('--start', help='skip zones whose short name sorts before this one (resume a run)')
    args = ap.parse_args()

    db = pymysql.connect(host=os.environ.get('EQC_DB_HOST', '127.0.0.1'), user=os.environ.get('EQC_DB_USER', 'eqc'),
                         password=os.environ.get('EQC_DB_PASS', 'eqc'), database=os.environ.get('EQC_DB_NAME', 'eqclassic'),
                         ssl_disabled=True)
    writer = None
    if args.csv:
        writer = csv.writer(open(args.csv, 'w', newline=''))
        writer.writerow(['zone', 'gridid', 'number', 'problem', 'x', 'y', 'z', 'detail'])

    total_points = total_problems = 0
    print(f"{'zone':<14}{'grids':>6}{'points':>8}{'no_floor':>9}{'floating':>9}{'walls':>7}{'steps':>7}{'floors':>7}")
    for zoneid, name in sorted(zone_ids(db).items(), key=lambda kv: kv[1]):
        if args.zone and name != args.zone:
            continue
        path = os.path.join(args.maps, name + '.map')
        grids = grids_of_zone(db, zoneid)
        if args.start and name < args.start:
            continue
        if not grids or not os.path.exists(path):
            continue
        tris = load_map(path)
        if tris is None:
            print(f"{name:<14}{len(grids):>6}  map file truncated: the zone cannot load it either")
            continue
        points, problems = check_zone(Mesh(tris), grids)
        kinds = [p[2] for p in problems]
        print(f"{name:<14}{len(grids):>6}{points:>8}{kinds.count('no_floor'):>9}{kinds.count('floating'):>9}{kinds.count('through_wall'):>7}{kinds.count('through_step'):>7}{kinds.count('through_floor'):>7}")
        sys.stdout.flush()
        total_points += points
        total_problems += len(problems)
        if writer:
            for p in problems:
                writer.writerow([name] + list(p))
    print(f"total: {total_points} waypoints, {total_problems} problems")


if __name__ == '__main__':
    main()
