#!/usr/bin/env python3
"""bugreport: what the bots and the watchdog found during a run, grouped, ranked, with evidence.

Reads every *.log of a run directory (bot logs and watchdog.log), takes the `action=Anomaly` lines,
groups them by kind and signature (the fields that tell one problem from another, numbers and spawn
suffixes left out), ranks the groups by severity then count, and writes report.md next to the logs.
A short summary goes to stdout. Usage: bugreport.py <run dir> [--known known.txt]

known.txt (optional): signatures already reported, one per line; those groups are listed apart, so
a nightly report shows what is new.
"""
import os
import re
import sys
import time
from collections import OrderedDict

LINE = re.compile(r'^t=(\S+) bot=(\S+) zone=(\S+) action=(\S+)(.*)$')

# kind -> (severity, what it means, fields that make the signature)
KINDS = {
    'zone_crash':       (1, 'A zone process crashed', ['where']),
    'zone_hang':        (1, 'A zone process spins at full CPU', ['stack']),
    'list_cycle':       (1, 'The entity list turned into a loop (the zone would have hung)', []),
    'zone_gone':        (2, 'A zone process went away', []),
    'no_profile':       (1, 'A zone took a player in and never sent its profile', []),
    'bad_spawn':        (2, 'A spawn the client cannot make sense of', ['why', 'spawn']),
    'exp_missing':      (2, 'A kill that should give experience gave none', ['grouped']),
    'session_lost':     (2, 'The zone dropped a bot (an unacked packet) and the bot logged in again', []),
    'zone_silent':      (2, 'The zone stopped talking to a player for 30 s', []),
    'login_refused':    (2, 'Logins refused again and again', []),
    'npc_data':         (3, 'NPC data that looks wrong', ['why', 'spawn']),
    'consider_timeout': (3, '/consider got no answer', ['target']),
    'no_hit':           (3, 'A melee fight where no swing ever landed', ['target', 'why_hit']),
    'cast_refused':     (3, 'Spells refused again and again', ['why']),
    'death_loop':       (3, 'A bot dies over and over', []),
    'zone_log':         (3, 'A zone log line that says something is wrong', ['text']),
    'unreachable':      (4, 'A target the bot could not walk to (pathing)', ['target']),
    'zone_line':        (4, 'A zone line where the bot walked (bots do not zone yet)', ['to']),
}
SEVERITY = {1: 'critical', 2: 'major', 3: 'minor', 4: 'info'}


def fields(rest):
    return dict(kv.split('=', 1) for kv in rest.split() if '=' in kv)


def generic(value):
    """a_fire_beetle08(117) -> a_fire_beetle; numbers out, so one problem is one group."""
    v = re.sub(r'\(\d+\)$', '', value)
    v = re.sub(r'\d+$', '', v)
    return re.sub(r'(?<=[a-z_])\d+', '', v)


def signature(kind, kv):
    keys = KINDS.get(kind, (3, '', []))[2]
    parts = [kind]
    for k in keys:
        if k == 'why_hit':
            parts.append('cant_see' if kv.get('cant_see', '0') != '0' else 'too_far' if kv.get('too_far', '0') != '0' else 'silent')
        elif k in kv:
            parts.append('%s=%s' % (k, generic(kv[k]) if k in ('target', 'spawn') else kv[k]))
    return ' '.join(parts)


def main():
    run = sys.argv[1]
    known = set()
    if '--known' in sys.argv:
        path = sys.argv[sys.argv.index('--known') + 1]
        if os.path.exists(path):
            known = {l.strip() for l in open(path) if l.strip() and not l.startswith('#')}
    groups = OrderedDict()
    bots, kills, deaths, first, last = set(), 0, 0, None, None
    paths = sorted(os.path.join(d, f) for d, _, fs in os.walk(run) for f in fs if f.endswith('.log'))
    for path in paths:
        name = os.path.relpath(path, run)
        for line in open(path, errors='replace'):
            m = LINE.match(line.strip())
            if not m:
                continue
            t, bot, zone, action, kv = float(m.group(1)), m.group(2), m.group(3), m.group(4), fields(m.group(5))
            first = t if first is None else min(first, t)
            last = t if last is None else max(last, t)
            if bot != 'server':
                bots.add(bot)
            kills += action == 'Kill'
            deaths += action == 'Killed'
            if action != 'Anomaly':
                continue
            kind = kv.get('kind', '?')
            sig = signature(kind, kv)
            g = groups.setdefault(sig, {'kind': kind, 'count': 0, 'bots': set(), 'zones': set(), 'first': t, 'last': t, 'samples': []})
            g['count'] += 1 + int(kv.get('repeats', '0') or 0)
            g['bots'].add(bot)
            g['zones'].add(zone)
            g['first'], g['last'] = min(g['first'], t), max(g['last'], t)
            if len(g['samples']) < 3:
                g['samples'].append('%s %s' % (name, line.strip()))
    ranked = sorted(groups.items(), key=lambda kv: (KINDS.get(kv[1]['kind'], (3,))[0], -kv[1]['count']))
    new = [(s, g) for s, g in ranked if s not in known]
    old = [(s, g) for s, g in ranked if s in known]
    hours = (last - first) / 3600 if first and last else 0
    out = ['# Bot run report: %s' % os.path.basename(os.path.abspath(run)), '',
           '%d bots, %.1f h (%s to %s), %d kills, %d deaths. %d problems found, %d new.' % (
               len(bots), hours, time.strftime('%Y-%m-%d %H:%M', time.localtime(first or 0)),
               time.strftime('%H:%M', time.localtime(last or 0)), kills, deaths, len(ranked), len(new)), '']
    for title, items in (('New', new), ('Already known', old)):
        if not items:
            continue
        out.append('## %s' % title)
        out.append('')
        for sig, g in items:
            sev, what, _ = KINDS.get(g['kind'], (3, g['kind'], []))
            out.append('### [%s] %s (%d times, %d bots, %s)' % (SEVERITY[sev], what, g['count'], len(g['bots']), ', '.join(sorted(g['zones']))))
            out.append('')
            out.append('Signature: `%s`  ' % sig)
            out.append('From %s to %s.' % (time.strftime('%H:%M:%S', time.localtime(g['first'])), time.strftime('%H:%M:%S', time.localtime(g['last']))))
            out.append('')
            out.append('```')
            out.extend(g['samples'])
            out.append('```')
            out.append('')
    report = os.path.join(run, 'report.md')
    open(report, 'w').write('\n'.join(out) + '\n')
    print(out[2])
    for sig, g in new[:15]:
        print('  [%s] x%d %s' % (SEVERITY[KINDS.get(g['kind'], (3,))[0]], g['count'], sig))
    print('report:', report)
    return 0


if __name__ == '__main__':
    sys.exit(main())
