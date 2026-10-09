#!/usr/bin/env python3
"""Checks world.py (milestone 8) without servers or bots: the schedule against a simulated clock,
the routes over the zone lines, the ladder. Exit 1 on failure."""
import configparser
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import world  # noqa: E402

failures = 0


def check(ok, what):
    global failures
    print('[ OK ]' if ok else '[FAIL]', what)
    failures += 0 if ok else 1


def main():
    random.seed(7)
    cfg = configparser.ConfigParser()
    cfg.optionxform = str
    cfg.read_string('[world]\nbots = 16\nsession_minutes = 40-120\n[ladder]\ninnothule = 1-7 grobb Ootok\nfeerrott = 8-14 - -\n')
    check(world.bot_name(1) == 'Botaa' and world.bot_name(52) == 'Botbz', 'bot names as setup-accounts.sh makes them')

    w = world.World(cfg, dry=True)
    midnight = time.mktime(time.localtime()[:3] + (0, 0, 0, 0, 0, -1))
    peak = w.dry_run(48, midnight)
    check(peak[4] <= 3, 'night: at most 3 of 16 online at 4 h (%d)' % peak[4])
    check(peak[20] >= 14, 'evening: at least 14 of 16 online at 20 h (%d)' % peak[20])
    check(all(peak[h] <= 16 for h in peak), 'never more bots than there are')
    check(all(b.sessions >= 2 for b in w.bots), 'every bot played more than once in two days (least: %d)' % min(b.sessions for b in w.bots))

    g = w.graph
    check(world.route(g, 'innothule', 'feerrott') == ['feerrott'], 'route: Innothule -> Feerrott is one zone line')
    check(world.route(g, 'qeynos2', 'blackburrow') == ['qeytoqrg', 'blackburrow'], 'route: North Qeynos -> Blackburrow through the Qeynos Hills')
    check(world.route(g, 'innothule', 'nowhere') == [], 'route: none to a zone that is not there')

    bot = w.bots[0]
    bot.zone, bot.level = 'innothule', 5
    check(w.step_for(bot).zone == 'innothule', 'ladder: level 5 hunts in Innothule')
    bot.level = 9
    check(w.step_for(bot).zone == 'feerrott' and w.step_for(bot).town == '', 'ladder: level 9 moves to the Feerrott')
    bot.level = 40
    check(w.step_for(bot).zone == 'feerrott', 'ladder: past the last step, the last zone')
    print('[ OK ] world checks' if not failures else '[FAIL] world checks')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
