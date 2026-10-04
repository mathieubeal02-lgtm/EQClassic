#!/usr/bin/env python3
"""Summarises bot decision logs (docs/bots-design.md 6.1): one line per action,
`t=<epoch> bot=<name> zone=<zone> action=<Action> key=value...`.

Usage: botlog.py <log dir> [expected bot count]
Checks (milestone 1): every bot entered its zone and finished (action=Done) with arrivals, no
login failure; server-side positions (action=Verify, GM bots only) within 5 units. Exit 1 on failure.
"""
import os
import re
import sys

LINE = re.compile(r'^t=(\S+) bot=(\S+) zone=(\S+) action=(\S+)(.*)$')


def fields(rest):
    return dict(kv.split('=', 1) for kv in rest.split() if '=' in kv)


def main():
    logs = sys.argv[1]
    expected = int(sys.argv[2]) if len(sys.argv) > 2 else None
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


if __name__ == '__main__':
    sys.exit(main())
