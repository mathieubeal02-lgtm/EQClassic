#!/usr/bin/env python3
"""watchdog: server-side sensors while bots play (tools/botd/bugreport.py groups what it finds).

Every 10 s it looks at the servers under Wine (~/eqc-server by default) and writes lines like the
bots' (`t=<epoch> bot=server zone=<port> action=Anomaly kind=<kind> ...`) to its output file:
  zone_crash  a zone.exe went away; the backtrace Wine printed in logs/zoneN.log (function names
              come from zone.pdb) is the signature
  zone_hang   a zone.exe above 90 % of a core for 30 s; winedbg is asked for the busy thread's stack
  zone_log    new lines in the zone logs that say something is wrong: refused casts, wrong packet
              sizes, errors, exceptions
It never restarts or kills anything. Usage: watchdog.py <out file> [server dir]  (stops on Ctrl-C/TERM)
"""
import glob
import os
import re
import signal
import subprocess
import sys
import time

TICK = os.sysconf('SC_CLK_TCK')
SERVER = os.path.expanduser('~/eqc-server')
WATCH = re.compile(r'(refused|Wrong size|Error|error:|EXCEPTION|Unhandled|corrupt|not found)', re.I)
NOISE = re.compile(r'(Error adding this item|error deleting spawn|Water Map .* not found)', re.I)


def zones():
    """{pid: port} of the zone.exe processes."""
    out = {}
    for p in os.listdir('/proc'):
        if not p.isdigit():
            continue
        try:
            cmd = open('/proc/%s/cmdline' % p, 'rb').read().split(b'\0')
        except OSError:
            continue
        if cmd and cmd[0].endswith(b'zone.exe') and len(cmd) > 3:
            out[int(p)] = cmd[3].decode(errors='replace')
    return out


def cpu(pid):
    try:
        f = open('/proc/%d/stat' % pid).read().rsplit(')', 1)[1].split()
        return int(f[11]) + int(f[12])
    except OSError:
        return None


def clean(s):
    return re.sub(r'\s+', '_', s.strip())[:160]


class Watchdog:
    def __init__(self, out, server):
        self.out = open(out, 'a')
        self.server = server
        self.logs = os.path.join(server, 'logs')
        self.offsets = {}
        self.hot = {}
        self.known = zones()
        self.ticks = {p: cpu(p) for p in self.known}
        for f in glob.glob(os.path.join(self.logs, 'zone*.log')):
            self.offsets[f] = os.path.getsize(f)	# only what happens from now on

    def emit(self, zone, kind, **kv):
        line = 't=%.3f bot=server zone=%s action=Anomaly kind=%s %s' % (
            time.time(), zone, kind, ' '.join('%s=%s' % (k, clean(str(v))) for k, v in kv.items()))
        self.out.write(line + '\n')
        self.out.flush()
        print(line, flush=True)

    def backtrace_from_log(self, text):
        """The first frames of the last Wine backtrace in a log chunk: 'Func+0x..() [file:line]'."""
        i = text.rfind('Backtrace:')
        if i < 0:
            return None
        frames = re.findall(r'^\s*=?>?\d+ 0x[0-9a-f]+ ([A-Za-z_:~<>0-9]+)\+0x[0-9a-f]+\(.*?\) \[.*?\\([^\\\]]+:\d+)\]',
                            text[i:], re.M)
        return ' < '.join('%s(%s)' % f for f in frames[:4]) or None

    def scan_logs(self):
        for f in glob.glob(os.path.join(self.logs, 'zone*.log')):
            size = os.path.getsize(f)
            start = self.offsets.get(f, 0)
            if size < start:
                start = 0	# rotated
            if size == start:
                continue
            with open(f, 'rb') as fh:
                fh.seek(start)
                chunk = fh.read(size - start).decode('latin-1', 'replace')
            self.offsets[f] = size
            zone = os.path.basename(f)[:-4]
            if 'Unhandled' in chunk:
                self.emit(zone, 'zone_crash', where=self.backtrace_from_log(chunk) or 'no_backtrace', log=os.path.basename(f))
            for line in chunk.splitlines():
                if 'linked list cycle' in line:
                    self.emit(zone, 'list_cycle', log=os.path.basename(f))
                    continue
                if WATCH.search(line) and not NOISE.search(line) and 'Unhandled' not in line:
                    self.emit(zone, 'zone_log', text=re.sub(r'\d+', 'N', line.strip())[:120], log=os.path.basename(f))

    def winedbg_stack(self, port):
        """The zone main thread's stack (the one under ProcessLoop), via winedbg."""
        env = dict(os.environ, WINEPREFIX=os.path.expanduser('~/.wine-eqc'), WINEDEBUG='-all')
        run = lambda cmds: subprocess.run(['winedbg'], input=cmds, env=env, capture_output=True, text=True, timeout=90).stdout
        try:
            procs = subprocess.run(['winedbg', '--command', 'info proc'], env=env, capture_output=True, text=True, timeout=30).stdout
        except (OSError, subprocess.TimeoutExpired):
            return None
        best, best_depth = None, -2
        for wpid in re.findall(r'^\s*([0-9a-f]{8})\s+\d+\s+.*zone\.exe', procs, re.M):
            try:
                threads = run('attach 0x%s\ninfo thread\ndetach\nquit\n' % wpid)
                tids = re.findall(r'^\t([0-9a-f]{8})', threads.split(wpid, 1)[-1].split("'", 1)[-1], re.M)[:8]
                for tid in tids:
                    frames = re.findall(r'\d+ 0x[0-9a-f]+ ([A-Za-z_:~<>0-9]+)\+0x', run('attach 0x%s\nbt 0x%s\ndetach\nquit\n' % (wpid, tid)))
                    frames = [f for f in frames if f not in ('Advance', 'MoreElements', 'GetData')] or frames
                    # the zone's main loop is the thread that matters (Process, packets, entities)
                    # (an idle one sleeps right under ProcessLoop; the busy one is deep below it)
                    depth = frames.index('ProcessLoop') if 'ProcessLoop' in frames else -1
                    if frames and (best is None or depth > best_depth):
                        best, best_depth = frames, depth
            except (OSError, subprocess.TimeoutExpired):
                continue
        return ' < '.join(best[:6]) if best else None

    def tick(self):
        now = zones()
        for pid, port in self.known.items():
            if pid not in now:
                self.emit(port, 'zone_gone', pid=pid)
        for pid, port in now.items():
            c = cpu(pid)
            prev = self.ticks.get(pid)
            if c is not None and prev is not None:
                pct = 100.0 * (c - prev) / (10 * TICK)
                self.hot[pid] = self.hot.get(pid, 0) + 1 if pct > 90 else 0
                if self.hot[pid] == 3:
                    self.emit(port, 'zone_hang', cpu='%.0f' % pct, stack=self.winedbg_stack(port) or 'unknown')
            self.ticks[pid] = c
        self.known = now
        self.scan_logs()


def main():
    out = sys.argv[1]
    server = sys.argv[2] if len(sys.argv) > 2 else SERVER
    w = Watchdog(out, server)
    running = [True]
    signal.signal(signal.SIGTERM, lambda *a: running.__setitem__(0, False))
    try:
        while running[0]:
            time.sleep(10)
            w.tick()
    except KeyboardInterrupt:
        pass


if __name__ == '__main__':
    main()
