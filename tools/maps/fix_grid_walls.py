#!/usr/bin/env python3
"""Detour points for NPC grid segments that go through a wall (check_grids.py's through_wall).

NPCs walk grid_entries in straight lines from point to point. When a segment crosses a wall, the
NPC walks into it (reported in game: Qeynos guards walking into walls). For each such segment A->B
this looks for one detour point P beside it, the closest to the segment, such that A->P and P->B
are clear at body and head height (Mesh.segment_blocked, as check_grids.py tests them) and P stands
on a floor. It prints the SQL that inserts P between A and B (numbers after it shift by one).

Usage: fix_grid_walls.py --maps ~/eqc-server/Maps/Maps --zone qeynos [--sql out.sql]
DB access as check_grids.py (EQC_DB_*), read only. Needs numpy and pymysql.
"""
import argparse
import math
import os
import sys

import pymysql

from check_grids import EYE_HEIGHT, HEAD_HEIGHT, Mesh, grids_of_zone, load_map, zone_ids

OFFSETS = [s * d for d in range(4, 81, 4) for s in (1, -1)]   # sideways, closest first
ALONG = [0.5, 0.35, 0.65, 0.2, 0.8]                           # where along the segment


def clear(mesh, a, b):
    """No wall, as check_grids.py counts one: blocked at body height and at head height (a step
    or a ledge only blocks the first, a door frame only the second)."""
    def wall(h):
        return mesh.segment_blocked((a[0], a[1], a[2] + h), (b[0], b[1], b[2] + h)) == 'wall'
    return not (wall(EYE_HEIGHT) and wall(HEAD_HEIGHT))


def floor_at(mesh, x, y, near_z):
    below = [s for s in mesh.surfaces_at(x, y) if s <= near_z + 15]
    return max(below) if below else None


def detour(mesh, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    length = math.hypot(dx, dy)
    if length < 1e-6:
        return None
    px, py = -dy / length, dx / length
    for off in OFFSETS:
        for t in ALONG:
            x = a[0] + dx * t + px * off
            y = a[1] + dy * t + py * off
            z = floor_at(mesh, x, y, a[2] + (b[2] - a[2]) * t)
            if z is None:
                continue
            p = (x, y, z + 1.0)
            if clear(mesh, a, p) and clear(mesh, p, b):
                return p
    return None


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--maps', required=True)
    ap.add_argument('--zone', required=True)
    ap.add_argument('--sql', help='write the SQL here (default stdout)')
    args = ap.parse_args()
    db = pymysql.connect(host=os.environ.get('EQC_DB_HOST', '127.0.0.1'), user=os.environ.get('EQC_DB_USER', 'eqc'),
                         password=os.environ.get('EQC_DB_PASS', 'eqc'), database=os.environ.get('EQC_DB_NAME', 'eqclassic'),
                         ssl_disabled=True)
    ids = {n: z for z, n in zone_ids(db).items()}
    zoneid = ids[args.zone]
    mesh = Mesh(load_map(os.path.join(args.maps, args.zone + '.map')))
    out = open(args.sql, 'w') if args.sql else sys.stdout
    fixed = unresolved = 0
    for gid, pts in sorted(grids_of_zone(db, zoneid).items()):
        inserts = []
        for idx in range(1, len(pts)):
            _, ax, ay, az = pts[idx - 1]
            num, bx, by, bz = pts[idx]
            a, b = (ax, ay, az), (bx, by, bz)
            if clear(mesh, a, b):
                continue
            p = detour(mesh, a, b)
            if p is None:
                unresolved += 1
                print(f'-- {args.zone} grid {gid}: no detour found between points {pts[idx - 1][0]} and {num}', file=out)
                continue
            inserts.append((num, p))
        # insert from the end so the numbers given are still right
        for num, p in reversed(inserts):
            print(f'UPDATE grid_entries SET number = number + 1 WHERE zoneid = {zoneid} AND gridid = {gid} AND number >= {num} ORDER BY number DESC;', file=out)
            print(f'INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES ({gid}, {zoneid}, {num}, {p[0]:.2f}, {p[1]:.2f}, {p[2]:.2f}, 0, 0);', file=out)
            fixed += 1
    print(f'{args.zone}: {fixed} detour points, {unresolved} segments without one', file=sys.stderr)


if __name__ == '__main__':
    main()
