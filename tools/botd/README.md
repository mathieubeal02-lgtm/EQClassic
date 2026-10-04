# botd: player bots (milestone 1)

Bots that log in like real players and live in the world, as designed in `docs/bots-design.md`:
headless clients built on `tools/eqbot`, nothing in the servers. Milestone 1 is a fleet that connects
and walks; hunting, groups and talking come next.

## Pieces

| File | What it does |
|---|---|
| `../eqbot` `walk` | One bot: logs in, enters its zone, walks a list of waypoints back and forth (about 15 units/s, a position every 250 ms, sits and stands at every third point), logs one line per action, logs out cleanly |
| `setup-accounts.sh` | Creates the bot accounts `bot_001`... (status 0, never GM) and one character each (`Botaa`, `Botab`...), placed at the first waypoint |
| `fleet.sh` | Starts N bots 2 s apart (one login at a time), samples the load every 10 s, summarises |
| `load.py` | CPU (interval, from `/proc`) and RSS of the `zone.exe` processes and of the bots |
| `botlog.py` | Reads the action lines, checks milestone 1 (every bot entered and finished, with arrivals, no login failure, server-side positions within 5 units) |
| `paths/` | Waypoint files, `x y z` per line: NPC grids from `grid_entries`, which NPCs already walk |

## Run

```sh
cd tools/eqbot && ./build.sh ../../build-linux/bin/eqbot      # the bot program
cd ../botd
./setup-accounts.sh 10 qeynos2 paths/qeynos2-grid2.txt         # once (servers up)
./fleet.sh 10 paths/qeynos2-grid2.txt 600                      # 10 bots, 10 minutes
```

A waypoint file from another grid:

```sh
mysql -ueqc -peqc eqclassic -N -e "select x, y, z from grid_entries where zoneid=2 and gridid=2 order by number limit 40" > paths/qeynos2-grid2.txt
```

`EQBOT_VERIFY=1` (GM accounts only, e.g. the test account) makes a walking bot ask the zone where it
is (`#loc`) every 5th arrival and log the gap (`action=Verify diff=`).

## Log lines

```
t=1791128980.872 bot=Botaa zone=qeynos2 action=MoveTo wp=3 target=-492.0,-222.2,-5.0 dist=9.7
t=1791128981.622 bot=Botaa zone=qeynos2 action=Arrive wp=3 at=-492.0,-222.2,-5.0
t=1791129012.677 bot=Botaa zone=qeynos2 action=Status pos=... arrivals=21 in_pps=21.9 out_pps=2.3
t=1791129016.770 bot=Botaa zone=qeynos2 action=Done arrivals=255 seconds=600 in_pps=4.7 out_pps=2.5
```

## Notes

- One process per bot for now (the design's single event loop comes when the numbers ask for it).
- The bots never write to the database. `setup-accounts.sh` does, once, like an admin would.
- The login server sent World an empty account name, so World could create only one new game account
  (`Duplicate entry ''`): `setup-accounts.sh` creates the game accounts itself; the server fix is a
  separate change.
- z goes on the wire x10 from a client (the zone keeps it so and divides by 10 in `GetZ`).
