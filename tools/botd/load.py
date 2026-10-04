#!/usr/bin/env python3
"""CPU (from /proc/<pid>/stat utime+stime deltas) and RSS of the zone processes and of given bot pids,
one line every 10 s while any bot runs: `t zone_cpu_pct zone_rss_mb bots_cpu_pct bots_rss_mb`.
CPU is percent of one core over the last interval (not ps' lifetime average).
Usage: load.py <out file> <bot pid>...
"""
import os
import sys
import time

TICK = os.sysconf('SC_CLK_TCK')
PAGE = os.sysconf('SC_PAGE_SIZE')


def zone_pids():
    pids = []
    for p in os.listdir('/proc'):
        if p.isdigit():
            try:
                cmd = open('/proc/%s/cmdline' % p, 'rb').read().split(b'\0')[0]
            except OSError:
                continue
            if cmd.endswith(b'zone.exe'):
                pids.append(int(p))
    return pids


def usage(pids):
    """{pid: utime+stime ticks}, total RSS bytes."""
    cpu, rss = {}, 0
    for p in pids:
        try:
            f = open('/proc/%d/stat' % p).read().rsplit(')', 1)[1].split()
        except OSError:
            continue
        cpu[p] = int(f[11]) + int(f[12])
        rss += int(f[21]) * PAGE
    return cpu, rss


def delta(now, before):
    # only processes alive at both samples: one that exited (or started) in between would count
    # its whole lifetime, or a negative one
    return sum(now[p] - before[p] for p in now if p in before)


def main():
    out, bots = sys.argv[1], [int(p) for p in sys.argv[2:]]
    with open(out, 'w') as f:
        f.write('t zone_cpu_pct zone_rss_mb bots_cpu_pct bots_rss_mb\n')
        last_z, _ = usage(zone_pids())
        last_b, _ = usage(bots)
        last_t = time.time()
        while any(os.path.exists('/proc/%d' % p) for p in bots):
            time.sleep(10)
            z, zr = usage(zone_pids())
            b, br = usage(bots)
            now = time.time()
            dt = (now - last_t) * TICK
            f.write('%d %.1f %.0f %.1f %.0f\n' % (now, 100.0 * delta(z, last_z) / dt, zr / 2**20, 100.0 * delta(b, last_b) / dt, br / 2**20))
            f.flush()
            last_z, last_b, last_t = z, b, now


if __name__ == '__main__':
    main()
