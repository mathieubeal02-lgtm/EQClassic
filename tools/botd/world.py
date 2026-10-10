#!/usr/bin/env python3
"""world: a living world (bots milestone 8, docs/bots-design.md 9).

Instead of a fleet that starts together and stops together, this keeps a population that follows
the hour of the day, as players do: few at night, most in the evening. Each bot plays a session of
random length (eqbot hunt), logs out, and comes back later. A bot that outgrew its zone walks to the
next one of its ladder at its next login (eqbot travel, then hunt there), and shops in that zone's
town (milestone 5).

    world.py [world.ini] [--dry-run HOURS]

world.ini (see world.ini.example):
    [world]   bots, first, host, password, eqbot, logs, session_minutes = MIN-MAX, tick_seconds,
              curve = 24 numbers, the share of the bots online at each hour (local time)
    [ladder]  <zone> = <min level>-<max level> <town zone or -> <merchant to try first or ->
              in the order the bots climb it; they start in the first zone

It never touches the database (setup-accounts.sh places the bots before the first run) and never
kills a bot: sessions end by themselves. One line per decision on stdout and in <logs>/world.log:
    t=<epoch> bot=world zone=- action=Start|End|Move|Population ...
--dry-run HOURS plays the schedule against a simulated clock without starting any bot (the test);
--verbose prints its decisions.
Stops on Ctrl-C or TERM; the bots already playing finish their session.
"""
import collections
import configparser
import os
import random
import re
import shlex
import signal
import subprocess
import sys
import time

DEFAULT_CURVE = '0.15 0.10 0.10 0.05 0.05 0.05 0.10 0.15 0.20 0.25 0.30 0.35 0.40 0.40 0.40 0.45 0.50 0.60 0.80 1.00 1.00 0.90 0.60 0.30'
HERE = os.path.dirname(os.path.abspath(__file__))


def bot_name(i):
    """bot_001 -> Botaa, as setup-accounts.sh and fleet.sh name them."""
    n = i - 1
    return 'Bot' + chr(97 + n // 26 % 26) + chr(97 + n % 26)


def load_lines(path):
    """zone -> [neighbour zones], from paths/zonelines.tsv (zone x y z range mode min max target)."""
    graph = collections.defaultdict(list)
    try:
        for line in open(path):
            f = line.split()
            if len(f) >= 9 and f[8] not in graph[f[0]]:
                graph[f[0]].append(f[8])
    except OSError:
        pass
    return graph


def route(graph, start, goal):
    """Zones to cross from start to goal (goal included), fewest changes; [] when there is no way."""
    if start == goal:
        return []
    seen, queue = {start: None}, collections.deque([start])
    while queue:
        z = queue.popleft()
        for n in graph.get(z, []):
            if n in seen:
                continue
            seen[n] = z
            if n == goal:
                path = [n]
                while seen[path[0]] != start:
                    path.insert(0, seen[path[0]])
                return path
            queue.append(n)
    return []


class Step:
    def __init__(self, zone, spec):
        f = spec.split()
        lo, hi = f[0].split('-')
        self.zone, self.min, self.max = zone, int(lo), int(hi)
        self.town = f[1] if len(f) > 1 and f[1] != '-' else ''
        self.npc = f[2] if len(f) > 2 and f[2] != '-' else ''


class Bot:
    def __init__(self, index, zone):
        self.index, self.name, self.account = index, bot_name(index), 'bot_%03d' % index
        self.zone, self.level = zone, 1
        self.proc, self.until, self.back_at, self.sessions = None, 0, 0, 0


class World:
    def __init__(self, cfg, dry=False):
        w = cfg['world'] if cfg.has_section('world') else {}
        self.dry = dry
        self.verbose = '--verbose' in sys.argv
        self.host = w.get('host', '127.0.0.1')
        self.password = w.get('password', 'botpass')
        self.eqbot = os.path.join(HERE, w.get('eqbot', '../../build-linux/bin/eqbot'))
        self.logs = os.path.join(HERE, os.environ.get('BOTD_LOGS') or w.get('logs', 'logs/world'))	# BOTD_LOGS: as fleet.sh
        lo, hi = w.get('session_minutes', '40-120').split('-')
        self.session = (int(lo), int(hi))
        self.tick = int(w.get('tick_seconds', '30'))
        self.curve = [float(x) for x in w.get('curve', DEFAULT_CURVE).split()]
        if len(self.curve) != 24:
            raise SystemExit('curve: 24 numbers, one per hour')
        self.ladder = [Step(z, s) for z, s in cfg['ladder'].items()] if cfg.has_section('ladder') else [Step('innothule', '1-60')]
        self.graph = load_lines(os.path.join(HERE, w.get('zonelines', 'paths/zonelines.tsv')))
        first, count = int(w.get('first', '1')), int(w.get('bots', '16'))
        self.bots = [Bot(i, self.ladder[0].zone) for i in range(first, first + count)]
        self.out = None
        if not dry:
            os.makedirs(self.logs, exist_ok=True)
            self.out = open(os.path.join(self.logs, 'world.log'), 'a')
            for b in self.bots:
                self.read_log(b)	# where an earlier run left it

    def say(self, now, action, **kv):
        line = 't=%.0f bot=world zone=- action=%s %s' % (now, action, ' '.join('%s=%s' % i for i in kv.items()))
        if self.dry and not self.verbose:
            return
        print(line, flush=True)
        if self.out:
            self.out.write(line + '\n')
            self.out.flush()

    def read_log(self, bot):
        """The zone and level of the bot's last zone-in, from its log."""
        try:
            text = open(os.path.join(self.logs, bot.name + '.log'), errors='replace').read()
        except OSError:
            return
        m = re.findall(r'bot=\S+ zone=(\S+) action=EnterZone .*?level=(\d+)', text)
        if m:
            bot.zone, bot.level = m[-1][0], int(m[-1][1])

    def step_for(self, bot):
        """The ladder step the bot should hunt in: the first whose top level it has not passed."""
        for s in self.ladder:
            if bot.level <= s.max:
                return s
        return self.ladder[-1]

    def wanted(self, now):
        return int(round(len(self.bots) * self.curve[time.localtime(now).tm_hour]))

    def start(self, bot, now):
        minutes = random.randint(*self.session)
        step = self.step_for(bot)
        legs = route(self.graph, bot.zone, step.zone) if step.zone != bot.zone else []
        if step.zone != bot.zone and not legs:
            self.say(now, 'Move', bot=bot.name, result='no_route', **{'from': bot.zone, 'to': step.zone})
            step = next((s for s in self.ladder if s.zone == bot.zone), step)	# stay where it is
        bot.until, bot.sessions = now + minutes * 60, bot.sessions + 1
        self.say(now, 'Start', bot=bot.name, level=bot.level, zone=step.zone, minutes=minutes, town=step.town or '-')
        if legs:
            self.say(now, 'Move', bot=bot.name, level=bot.level, route=','.join(legs), **{'from': bot.zone})
        if self.dry:
            bot.proc = True
            bot.zone = step.zone
            return
        env = dict(os.environ, EQBOT_LFG='1')
        if step.town:
            env['EQBOT_TOWN'] = step.town
        if step.npc:
            env['EQBOT_TOWN_NPC'] = step.npc
        hunt = [self.eqbot, 'hunt', self.host, bot.account, self.password, bot.name, str(minutes * 60)]
        log = open(os.path.join(self.logs, bot.name + '.log'), 'a')
        if legs:
            # walk there first; the hunt's own login retries wait out World's minute after the travel session
            travel = [self.eqbot, 'travel', self.host, bot.account, self.password, bot.name, ','.join(legs)]
            cmd = ['/bin/sh', '-c', ' '.join(shlex.quote(a) for a in travel) + ' ; exec ' + ' '.join(shlex.quote(a) for a in hunt)]
        else:
            cmd = hunt
        bot.proc = subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT, env=env, cwd=HERE, start_new_session=True)

    def reap(self, now):
        for b in self.bots:
            if b.proc is None:
                continue
            done = now >= b.until if self.dry else b.proc.poll() is not None
            if not done:
                continue
            b.proc = None
            if not self.dry:
                self.read_log(b)
            # away for a while: a short break more often than a long one
            away = random.choice([10, 20, 30, 60, 120])
            b.back_at = now + away * 60
            self.say(now, 'End', bot=b.name, zone=b.zone, level=b.level, sessions=b.sessions, away=away)

    def step(self, now):
        self.reap(now)
        online = [b for b in self.bots if b.proc is not None]
        want = self.wanted(now)
        if len(online) < want:
            rested = [b for b in self.bots if b.proc is None and now >= b.back_at]
            pool = rested or [b for b in self.bots if b.proc is None]	# the hour asks for more than have rested
            if pool:
                self.start(min(pool, key=lambda b: (b.back_at, b.index)), now)	# one login a tick
        return len(online), want

    def run(self):
        stop = [False]
        signal.signal(signal.SIGTERM, lambda *a: stop.__setitem__(0, True))
        last_report = 0
        try:
            while not stop[0]:
                now = time.time()
                online, want = self.step(now)
                if now - last_report >= 600:
                    self.say(now, 'Population', online=online, wanted=want, hour=time.localtime(now).tm_hour)
                    last_report = now
                time.sleep(self.tick)
        except KeyboardInterrupt:
            pass
        self.say(time.time(), 'Stop', online=sum(b.proc is not None for b in self.bots))

    def dry_run(self, hours, start=None):
        """Plays `hours` of the schedule; returns {hour of day: highest number online}."""
        now = start if start is not None else time.mktime(time.localtime()[:3] + (0, 0, 0, 0, 0, -1))
        end, peak = now + hours * 3600, collections.defaultdict(int)
        while now < end:
            online, _ = self.step(now)
            h = time.localtime(now).tm_hour
            peak[h] = max(peak[h], online)
            now += self.tick
        return peak


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    cfg = configparser.ConfigParser()
    cfg.optionxform = str
    cfg.read(args[0] if args else os.path.join(HERE, 'world.ini'))
    if '--dry-run' in sys.argv:
        hours = float(sys.argv[sys.argv.index('--dry-run') + 1])
        w = World(cfg, dry=True)
        peak = w.dry_run(hours)
        print('online at most, by hour: ' + ' '.join('%02d:%d' % (h, peak[h]) for h in sorted(peak)))
        return 0
    World(cfg).run()
    return 0


if __name__ == '__main__':
    sys.exit(main())
