#!/usr/bin/env python3
"""Summarises bot decision logs (docs/bots-design.md 6.1): one line per action,
`t=<epoch> bot=<name> zone=<zone> action=<Action> key=value...`.

Usage: botlog.py <log dir> [expected bot count] [minimum kills per hunter, default 10]
Checks, walking bots (milestone 1): every bot entered its zone and finished (action=Done) with
arrivals, no login failure; server-side positions (action=Verify, GM bots only) within 5 units.
Hunting bots (milestone 2, eqbot hunt): finished, enough kills, at least one item looted, sat
between fights, gained experience, never attacked an NPC it refused (amiable or better) nor a guard,
and never stood still for over 2 minutes outside a rest. Exit 1 on failure.
"""
import os
import re
import sys

LINE = re.compile(r'^t=(\S+) bot=(\S+) zone=(\S+) action=(\S+)(.*)$')


def fields(rest):
    return dict(kv.split('=', 1) for kv in rest.split() if '=' in kv)


def hunter(path):
    """Milestone 2 summary of one hunting bot's log."""
    h = {'kills': 0, 'items': 0, 'sits': 0, 'exp': 0, 'deaths': 0, 'refused': set(), 'bad': [], 'done': None,
         'stuck': 0.0, 'failed': 0}
    last_pos, still_since, state = None, None, None
    for line in open(path, errors='replace'):
        m = LINE.match(line.strip())
        if not m:
            continue
        t, action, kv = float(m.group(1)), m.group(4), fields(m.group(5))
        if action == 'Kill':
            h['kills'] += 1
        elif action == 'LootItem' and state == 'Loot':  # not our own corpse's items
            h['items'] += 1
        elif action == 'Sit':
            h['sits'] += 1
        elif action == 'Exp':
            h['exp'] += int(kv.get('gained', 0))
        elif action == 'Killed':
            h['deaths'] += 1
        elif action == 'Login':
            h['failed'] += 1
        elif action == 'Done':
            h['done'] = kv
        elif action == 'Refuse':
            h['refused'].add(kv['target'])
        elif action in ('Approach', 'Fight') and kv.get('why') != 'attacked':
            if kv.get('target') in h['refused'] or 'guard' in kv.get('target', '').lower():
                h['bad'].append(kv['target'])
        if action in ('Seek', 'Consider', 'Approach', 'Fight', 'Loot', 'Rest', 'Flee', 'Dead'):
            state = action
        elif action == 'EnterZone':
            state, last_pos, still_since = 'Seek', None, None
        if action == 'Status':
            pos = kv.get('pos')
            if pos != last_pos or kv.get('state') in ('Rest', 'Dead'):
                last_pos, still_since = pos, t
            else:
                h['stuck'] = max(h['stuck'], t - still_since)
    return h


def main():
    logs = sys.argv[1]
    expected = int(sys.argv[2]) if len(sys.argv) > 2 else None
    min_kills = int(sys.argv[3]) if len(sys.argv) > 3 else 10
    names = sorted(n for n in os.listdir(logs) if n.endswith('.log'))
    if names and all(' action=Kill ' in open(os.path.join(logs, n), errors='replace').read() or
                     'Seek' in open(os.path.join(logs, n), errors='replace').read() for n in names):
        return hunters(logs, names, expected, min_kills)
    bots = {}
    for name in sorted(os.listdir(logs)):
        if not name.endswith('.log'):
            continue
        b = bots.setdefault(name[:-4], {'enter': 0, 'arrive': 0, 'done': None, 'failed': 0, 'verify': []})
        for line in open(os.path.join(logs, name), errors='replace'):
            m = LINE.match(line.strip())
            if not m:
                continue
            action, kv = m.group(4), fields(m.group(5))
            if action == 'EnterZone':
                b['enter'] += 1
            elif action == 'Arrive':
                b['arrive'] += 1
            elif action == 'Done':
                b['done'] = kv
            elif action == 'Login':
                b['failed'] += 1
            elif action == 'Verify' and 'diff' in kv:
                b['verify'].append(float(kv['diff']))
    ok = True
    print('%-8s %6s %8s %8s %8s %s' % ('bot', 'enter', 'arrivals', 'in_pps', 'out_pps', 'verify max diff'))
    for name, b in bots.items():
        d = b['done'] or {}
        vmax = max(b['verify']) if b['verify'] else None
        print('%-8s %6d %8d %8s %8s %s' % (name, b['enter'], b['arrive'], d.get('in_pps', '-'), d.get('out_pps', '-'),
                                          '-' if vmax is None else '%.1f' % vmax))
        if b['enter'] != 1 or b['done'] is None or b['arrive'] == 0 or b['failed'] or (vmax is not None and vmax > 5):
            ok = False
    if expected is not None and len(bots) != expected:
        print('expected %d bots, found %d logs' % (expected, len(bots)))
        ok = False
    load = os.path.join(logs, 'load.txt')
    if os.path.exists(load):
        rows = [l.split() for l in open(load).read().splitlines()[1:] if l.strip()]
        if rows:
            zc = [float(r[1]) for r in rows]; zr = [float(r[2]) for r in rows]
            bc = [float(r[3]) for r in rows if len(r) > 4]; br = [float(r[4]) for r in rows if len(r) > 4]
            print('load: zone cpu avg %.1f%% max %.1f%%, zone rss max %.0f MB; bots cpu avg %.1f%% max %.1f%%, bots rss max %.0f MB'
                  % (sum(zc) / len(zc), max(zc), max(zr), sum(bc) / max(len(bc), 1), max(bc or [0]), max(br or [0])))
    print('[ OK ] milestone 1 checks' if ok else '[FAIL] milestone 1 checks')
    return 0 if ok else 1


def hunters(logs, names, expected, min_kills):
    ok = True
    print('%-8s %6s %6s %5s %7s %7s %6s %8s %s' % ('bot', 'kills', 'items', 'sits', 'exp', 'deaths', 'refused', 'stuck_s', 'bad targets'))
    for n in names:
        h = hunter(os.path.join(logs, n))
        print('%-8s %6d %6d %5d %7d %7d %6d %8.0f %s' % (n[:-4], h['kills'], h['items'], h['sits'], h['exp'], h['deaths'],
                                                       len(h['refused']), h['stuck'], ','.join(h['bad']) or '-'))
        if (h['done'] is None or h['failed'] or h['kills'] < min_kills or h['items'] < 1 or h['sits'] < 1 or h['exp'] <= 0
                or h['bad'] or h['stuck'] > 120):
            ok = False
    if expected is not None and len(names) != expected:
        print('expected %d bots, found %d logs' % (expected, len(names)))
        ok = False
    print('[ OK ] milestone 2 checks' if ok else '[FAIL] milestone 2 checks')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
