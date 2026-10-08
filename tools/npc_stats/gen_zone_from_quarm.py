#!/usr/bin/env python3
"""Generates an SQL patch that gives a zone the spawns it had from Classic to Velious.

The EQClassic dump has several zones in a later, revamped state (Skyshrine as of Dragons of Norrath:
level 62 golems where Velious had level 35-60 kin). The Quarm database (quarm_ref, see README.md)
has the era's layout. For one zone this writes, as plain rows (the patch needs no quarm_ref):
  - a backup of our spawn2 / spawngroup / spawnentry / grid / grid_entries rows of the zone
    (tables *_before_era, restore notes in the patch header);
  - Quarm's spawn points, spawn groups, entries and path grids in their place.
NPCs stay ours: each Quarm NPC is matched to one of our npc_types by name ('#', case and trailing
'_' ignored; one of the zone's, else one elsewhere within 3 levels), so stats, loot, factions and
quests keep working. One of ours more than 3 levels off takes Quarm's level, HP, damage and AC
(the old row goes to npc_types_before_era). A Quarm NPC we do not have is imported with its basic
stats, its loot table (drops there at Velious, items the server can load) and, when we have none for
its name, its faction list. Both are listed in the patch header. Headings: Quarm's are ours x 1.0198 (seen on the spawn points both have).

Usage: gen_zone_from_quarm.py <zone short name> <patch number> > sql/patches/NNN_<zone>_era_spawns.sql
Environment: EQC_DB_USER, EQC_DB_PASS, EQC_DB_NAME (eqc / eqc / eqclassic), QUARM_DB (quarm_ref).
"""
import os
import re
import subprocess
import sys

USER = os.environ.get('EQC_DB_USER', 'eqc')
PASS = os.environ.get('EQC_DB_PASS', 'eqc')
OURS = os.environ.get('EQC_DB_NAME', 'eqclassic')
QUARM = os.environ.get('QUARM_DB', 'quarm_ref')
ID_OFFSET = 1000000		# Quarm's spawn2 and spawngroup ids overlap ours: theirs + 1,000,000
HEADING = 1.0198


def rows(sql):
    out = subprocess.run(['mysql', '--skip-ssl', '-u' + USER, '-p' + PASS, '-N', '-B', '--raw', OURS, '-e', sql],
                         capture_output=True, text=True, check=True).stdout
    return [line.split('\t') for line in out.splitlines()]


def norm(name):
    return name.replace('#', '').lower().strip('_')


def q(s):
    return "'" + s.replace('\\', '\\\\').replace("'", "''") + "'"


def heading(h):
    h = float(h)
    return 0 if h < 0 else round(h / HEADING) % 256


def values(table, columns, data, per_line=1):
    if not data:
        return []
    out = ['REPLACE INTO %s (%s) VALUES' % (table, ', '.join(columns))]
    lines = ['(' + ', '.join(str(v) for v in r) + ')' for r in data]
    out.append(',\n'.join(lines) + ';')
    return out


def main():
    zone, number = sys.argv[1], sys.argv[2]
    zid = rows("SELECT zoneidnumber FROM %s.zone WHERE short_name = %s" % (QUARM, q(zone)))[0][0]
    era = "(%s = -1 OR %s <= 2) AND (%s = -1 OR %s >= 2)"	# there at Velious
    s2era = era % (('s.min_expansion',) * 2 + ('s.max_expansion',) * 2)
    seera = era % (('e.min_expansion',) * 2 + ('e.max_expansion',) * 2)
    seera += " AND e.npcID < 1000000"	# ids from 1,000,000: NPCs of the Quarm server itself (Agent of Druzzil), not the era's
    points = rows("SELECT s.id, s.spawngroupID, s.x, s.y, s.z, s.heading, s.respawntime, s.variance, s.pathgrid "
                  "FROM %s.spawn2 s WHERE s.zone = %s AND s.enabled = 1 AND %s ORDER BY s.id" % (QUARM, q(zone), s2era))
    inlist = ','.join(sorted({p[1] for p in points}, key=int))
    # a point whose group has no entry there at Velious spawns nothing: left out
    filled = {r[0] for r in rows("SELECT DISTINCT e.spawngroupID FROM %s.spawnentry e WHERE e.spawngroupID IN (%s) AND %s" % (QUARM, inlist, seera))}
    points = [p for p in points if p[1] in filled]
    groups = sorted({p[1] for p in points}, key=int)
    inlist = ','.join(groups)
    group_rows = rows("SELECT id, name, spawn_limit FROM %s.spawngroup WHERE id IN (%s) ORDER BY id" % (QUARM, inlist))
    entries = rows("SELECT e.spawngroupID, e.npcID, e.chance, n.name, n.level FROM %s.spawnentry e JOIN %s.npc_types n ON n.id = e.npcID "
                   "WHERE e.spawngroupID IN (%s) AND %s ORDER BY e.spawngroupID, e.npcID" % (QUARM, QUARM, inlist, seera))
    ours = rows("SELECT id, name, level FROM npc_types")
    by_name = {}
    for oid, name, level in ours:
        by_name.setdefault(norm(name), []).append((int(oid), int(level)))
    used_ids = {int(o[0]) for o in ours}

    # each Quarm NPC -> one of ours, or an import
    mapping, imported, notes, restat = {}, {}, [], {}
    for _, npc, _, name, level in entries:
        if npc in mapping:
            continue
        cands = by_name.get(norm(name), [])
        in_zone = [c for c in cands if c[0] // 1000 == int(zid)]
        near = [c for c in cands if abs(c[1] - int(level)) <= 3]
        if in_zone or near:
            best = min(in_zone or near, key=lambda c: (abs(c[1] - int(level)), c[0]))
            mapping[npc] = best[0]
            if abs(best[1] - int(level)) > 3:
                # ours has the wrong level (some are placeholders at level 1): Quarm's basic stats
                if best[0] not in restat:
                    restat[best[0]] = npc
                    notes.append('%s: ours %d was level %d, Quarm has %s' % (name, best[0], best[1], level))
        else:
            key = norm(name)
            if key not in imported:
                new_id = int(npc)
                while new_id in used_ids:
                    new_id += 1
                used_ids.add(new_id)
                imported[key] = new_id
            mapping[npc] = imported[key]

    out = []
    w = out.append
    w('-- %s: %s as it was from Classic to Velious (spawn points, groups and path grids from Quarm).' % (number, zone))
    w('--')
    w('-- The dump has this zone in a later, revamped state. Generated by tools/npc_stats/gen_zone_from_quarm.py')
    w('-- (regenerate rather than edit): %d spawn points, %d spawn groups, %d entries from the Quarm database,' % (len(points), len(group_rows), len(entries)))
    w('-- ids + %d. NPCs are ours, matched by name; headings are Quarm\'s / %s.' % (ID_OFFSET, HEADING))
    for n in notes:
        w('-- Level, HP, damage and AC taken from Quarm: ' + n)
    for key, new_id in imported.items():
        w('-- Imported from Quarm (basic stats, loot table, faction list when we have none for the name): %s as npc_types %d' % (key, new_id))
    w('--')
    w('-- Our rows are kept in spawn2_before_era, spawngroup_before_era, spawnentry_before_era, grid_before_era and')
    w('-- grid_entries_before_era. To restore: delete the zone\'s rows with id >= %d (spawn2, and spawngroup /' % ID_OFFSET)
    w('-- spawnentry by spawngroupID), delete its grid / grid_entries rows, and insert the *_before_era rows back.')
    w('-- Idempotent.')
    w('')
    ours_points = "zone = %s AND id < %d" % (q(zone), ID_OFFSET)
    for t in ('spawn2', 'spawngroup', 'spawnentry', 'grid', 'grid_entries'):
        w('CREATE TABLE IF NOT EXISTS %s_before_era LIKE %s;' % (t, t))
    w('INSERT IGNORE INTO spawn2_before_era SELECT * FROM spawn2 WHERE %s;' % ours_points)
    # the grids are backed up only while our spawn points are still there (a second run finds Quarm's)
    w('INSERT IGNORE INTO grid_before_era SELECT * FROM grid WHERE zoneid = %s AND EXISTS (SELECT 1 FROM spawn2 WHERE %s);' % (zid, ours_points))
    w('INSERT IGNORE INTO grid_entries_before_era SELECT * FROM grid_entries WHERE zoneid = %s AND EXISTS (SELECT 1 FROM spawn2 WHERE %s);' % (zid, ours_points))
    # groups only this zone uses
    w('CREATE TEMPORARY TABLE era_groups AS SELECT DISTINCT spawngroupID AS id FROM spawn2 WHERE %s' % ours_points)
    w('  AND spawngroupID NOT IN (SELECT spawngroupID FROM spawn2 WHERE zone <> %s);' % q(zone))
    w('INSERT IGNORE INTO spawngroup_before_era SELECT * FROM spawngroup WHERE id IN (SELECT id FROM era_groups);')
    w('INSERT IGNORE INTO spawnentry_before_era SELECT * FROM spawnentry WHERE spawngroupID IN (SELECT id FROM era_groups);')
    w('DELETE FROM spawnentry WHERE spawngroupID IN (SELECT id FROM era_groups);')
    w('DELETE FROM spawngroup WHERE id IN (SELECT id FROM era_groups);')
    w('DELETE FROM spawn2 WHERE %s;' % ours_points)
    w('DROP TEMPORARY TABLE era_groups;')
    w('DELETE FROM grid_entries WHERE zoneid = %s;' % zid)
    w('DELETE FROM grid WHERE zoneid = %s;' % zid)
    w('')

    if imported:
        ids = ','.join(n for n in mapping if mapping[n] in imported.values())
        cols = ['name', 'lastname', 'level', 'race', 'class', 'bodytype', 'hp', 'gender', 'texture', 'helmtexture', 'size',
                'hp_regen_rate', 'mana_regen_rate', 'mindmg', 'maxdmg', 'aggroradius', 'face', 'runspeed', 'MR', 'CR', 'DR', 'FR', 'PR',
                'see_invis', 'see_invis_undead', 'AC', 'npc_aggro', 'STR', 'STA', 'DEX', 'AGI', '_INT', 'WIS', 'CHA', 'ATK', 'Accuracy']
        done = set()
        data, loot, factions = [], {}, []
        for r in rows("SELECT id, loottable_id, npc_faction_id, %s FROM %s.npc_types WHERE id IN (%s) ORDER BY id" % (', '.join(cols), QUARM, ids)):
            new_id = mapping[r[0]]
            if new_id in done:
                continue
            done.add(new_id)
            if int(r[1]):
                loot[r[1]] = int(r[1]) + ID_OFFSET
            if int(r[2]):
                factions.append((r[3].lstrip('#'), r[2]))	# npc_faction names carry no '#'
            vals = [new_id, loot.get(r[1], 0)] + [q(v) if c in ('name', 'lastname') else v for c, v in zip(cols, r[3:])]
            data.append(vals)
        # npc_types_without is the copy the zone reads an NPC's name from to find its faction (npc_faction, by name)
        out += values('npc_types', ['id', 'loottable_id'] + cols, data)
        out += values('npc_types_without', ['id', 'loottable_id'] + cols, data)
        w('')
        if loot:
            # Their loot tables, ids + 1,000,000: the drops there at Velious, items the server can load (ids up to 33000)
            lt = ','.join(loot)
            out += values('loottable', ['id', 'name', 'mincash', 'maxcash', 'avgcoin'],
                          [(int(r[0]) + ID_OFFSET, q(r[1]), r[2], r[3], min(int(r[4]), 32767)) for r in
                           rows("SELECT id, name, mincash, maxcash, avgcoin FROM %s.loottable WHERE id IN (%s) ORDER BY id" % (QUARM, lt))])
            entries_ = rows("SELECT loottable_id, lootdrop_id, multiplier, probability FROM %s.loottable_entries WHERE loottable_id IN (%s) ORDER BY 1, 2" % (QUARM, lt))
            drops = sorted({e[1] for e in entries_}, key=int)
            items = rows("SELECT e.lootdrop_id, e.item_id, e.item_charges, e.equip_item, e.chance FROM %s.lootdrop_entries e "
                         "WHERE e.lootdrop_id IN (%s) AND (e.min_expansion = -1 OR e.min_expansion <= 2) AND (e.max_expansion = -1 OR e.max_expansion >= 2) "
                         "AND e.item_id <= 33000 AND e.item_id IN (SELECT id FROM items_axclassic) ORDER BY 1, 2" % (QUARM, ','.join(drops))) if drops else []
            kept = {i[0] for i in items}
            out += values('loottable_entries', ['loottable_id', 'lootdrop_id', 'multiplier', 'probability'],
                          [(int(e[0]) + ID_OFFSET, int(e[1]) + ID_OFFSET, e[2], e[3]) for e in entries_ if e[1] in kept])
            if kept:
                out += values('lootdrop', ['id', 'name'], [(int(r[0]) + ID_OFFSET, q(r[1])) for r in
                              rows("SELECT id, name FROM %s.lootdrop WHERE id IN (%s) ORDER BY id" % (QUARM, ','.join(sorted(kept, key=int))))])
                out += values('lootdrop_entries', ['lootdrop_id', 'item_id', 'item_charges', 'equip_item', 'chance'],
                              [(int(i[0]) + ID_OFFSET, i[1], min(int(i[2]), 127), i[3], max(1, min(100, round(float(i[4]))))) for i in items])
            w('')
        for name, qf in factions:
            # a faction list for the name, unless we have one: Quarm's, with our faction ids (same faction names)
            if rows("SELECT 1 FROM npc_faction WHERE name = %s" % q(name)):
                continue
            prim = rows("SELECT o.id FROM %s.npc_faction f JOIN %s.faction_list qf ON qf.id = f.primaryfaction JOIN faction_list o ON o.name = qf.name WHERE f.id = %s" % (QUARM, QUARM, qf))
            w("INSERT INTO npc_faction (name, primaryfaction, ignore_primary_assist) SELECT %s, %s, 0 FROM DUAL WHERE NOT EXISTS (SELECT 1 FROM npc_faction WHERE name = %s);"
              % (q(name), prim[0][0] if prim else 0, q(name)))
            for fid, value, npc_value in rows("SELECT o.id, e.value, e.npc_value FROM %s.npc_faction_entries e JOIN %s.faction_list qf ON qf.id = e.faction_id "
                                              "JOIN faction_list o ON o.name = qf.name WHERE e.npc_faction_id = %s ORDER BY o.id" % (QUARM, QUARM, qf)):
                w("INSERT IGNORE INTO npc_faction_entries (npc_faction_id, faction_id, value, npc_value) SELECT id, %s, %s, %s FROM npc_faction WHERE name = %s;" % (fid, value, npc_value, q(name)))
        w('')

    if restat:
        w('CREATE TABLE IF NOT EXISTS npc_types_before_era LIKE npc_types;')
        w('INSERT IGNORE INTO npc_types_before_era SELECT * FROM npc_types WHERE id IN (%s);' % ','.join(str(i) for i in sorted(restat)))
        stats = {r[0]: r[1:] for r in rows("SELECT id, level, hp, mindmg, maxdmg, AC FROM %s.npc_types WHERE id IN (%s)" % (QUARM, ','.join(restat.values())))}
        for oid in sorted(restat):
            level, hp, mindmg, maxdmg, ac = stats[restat[oid]]
            w('UPDATE npc_types SET level = %s, hp = %s, mindmg = %s, maxdmg = %s, AC = %s WHERE id = %d;' % (level, hp, mindmg, maxdmg, ac, oid))
        w('')

    out += values('spawngroup', ['id', 'name', 'spawnlimit'], [(int(g[0]) + ID_OFFSET, q(g[1]), g[2]) for g in group_rows])
    w('')
    seen = set()
    data = []
    for g, npc, chance, _, _ in entries:
        key = (g, mapping[npc])
        if key in seen:		# two Quarm NPCs that are one of ours: their chances add up
            for d in data:
                if (d[0], d[1]) == (int(g) + ID_OFFSET, mapping[npc]):
                    d[2] = int(d[2]) + int(chance)
            continue
        seen.add(key)
        data.append([int(g) + ID_OFFSET, mapping[npc], int(chance), 0])
    out += values('spawnentry', ['spawngroupID', 'npcID', 'chance', 'time_of_day'], data)
    w('')
    out += values('spawn2', ['id', 'spawngroupID', 'zone', 'x', 'y', 'z', 'heading', 'respawntime', 'variance', 'pathgrid', 'timeleft', '_condition', 'cond_value', 'roamRange'],
                  [(int(p[0]) + ID_OFFSET, int(p[1]) + ID_OFFSET, q(zone), p[2], p[3], p[4], heading(p[5]), p[6], p[7], p[8], 0, 0, 0, 0) for p in points])
    w('')
    out += values('grid', ['id', 'zoneid', 'type', 'type2'], rows("SELECT id, zoneid, type, type2 FROM %s.grid WHERE zoneid = %s ORDER BY id" % (QUARM, zid)))
    w('')
    out += values('grid_entries', ['gridid', 'zoneid', 'number', 'x', 'y', 'z', 'heading', 'pause'],
                  [(g[0], g[1], g[2], g[3], g[4], g[5], heading(g[6]), g[7]) for g in
                   rows("SELECT gridid, zoneid, number, x, y, z, heading, pause FROM %s.grid_entries WHERE zoneid = %s ORDER BY gridid, number" % (QUARM, zid))])
    print('\n'.join(out))
    print('%s: %d points, %d groups, %d entries, %d NPCs matched, %d imported' % (
        zone, len(points), len(group_rows), len(data), len(set(mapping.values())) - len(imported), len(imported)), file=sys.stderr)
    for n in notes:
        print('  note: ' + n, file=sys.stderr)


if __name__ == '__main__':
    main()
