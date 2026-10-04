# botd: player bots (milestones 1 and 2)

Bots that log in like real players and live in the world, as designed in `docs/bots-design.md`:
headless clients built on `tools/eqbot`, nothing in the servers. Milestone 1 is a fleet that connects
and walks, milestone 2 bots hunt alone; groups and talking come next.

## Pieces

| File | What it does |
|---|---|
| `../eqbot` `walk` | One bot: logs in, enters its zone, walks a list of waypoints back and forth (about 15 units/s, a position every 250 ms, sits and stands at every third point), logs one line per action, logs out cleanly |
| `../eqbot` `hunt` | One bot hunts alone around the place it logs in (its camp, 800 units): picks the closest small NPC (`a_`/`an_`, level up to its own + 1, nobody on it, no guard), considers it and never attacks one that cons amiable or better, walks to it, auto-attacks, loots every item of the corpse, sits until healed (it only pulls above 90 % HP), flees to the camp under 20 % HP, fights back whatever attacks it, strolls around the camp when nothing is up. Killed: logs, waits 15 s, logs in again, goes back to its corpse and loots it (the zone puts worn items back on), then hunts around its first camp again. A cleric casts Minor Healing on itself under 50 % HP in a fight and under 70 % before resting, and meditates until its mana is back |
| `setup-accounts.sh` | Creates the bot accounts `bot_001`... (status 0, never GM) and one character each (`Botaa`, `Botab`...), placed at the first waypoint. `BOTD_FIRST` picks the first index, `EQBOT_CLASS=warrior` or `cleric` makes humans of that class (default: troll shamans, which Qeynos guards kill on sight); clerics get Minor Healing scribed and memorized, as a player would do with the starting scroll |
| `fleet.sh` | Starts N bots (`walk` along a waypoints file, or `hunt`) 2 s apart (one login at a time), samples the load every 10 s, summarises |
| `load.py` | CPU (interval, from `/proc`) and RSS of the `zone.exe` processes and of the bots |
| `botlog.py` | Reads the action lines and checks milestone 1 (every bot entered and finished, with arrivals, no login failure, server-side positions within 5 units) or, for hunters, milestone 2 (finished, at least 10 kills, an item looted, sat between fights, experience gained, no attack on a refused NPC or a guard, never still for over 2 minutes outside a rest) |
| `paths/` | Waypoint files, `x y z` per line: NPC grids from `grid_entries`, which NPCs already walk |

## Run

```sh
cd tools/eqbot && ./build.sh ../../build-linux/bin/eqbot      # the bot program
cd ../botd
./setup-accounts.sh 10 qeynos2 paths/qeynos2-grid2.txt         # once (servers up)
./fleet.sh 10 paths/qeynos2-grid2.txt 600                      # 10 bots, 10 minutes
```

Hunters (milestone 2), a human warrior and a human cleric in the Qeynos Hills for 30 minutes:

```sh
echo "83 508 -6" > paths/qeytoqrg-safe.txt                                  # the zone's safe point
BOTD_FIRST=52 EQBOT_CLASS=warrior ./setup-accounts.sh 1 qeytoqrg paths/qeytoqrg-safe.txt
BOTD_FIRST=53 EQBOT_CLASS=cleric  ./setup-accounts.sh 1 qeytoqrg paths/qeytoqrg-safe.txt
BOTD_FIRST=52 ./fleet.sh 2 hunt 1800
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

A hunter:

```
t=1791135619.017 bot=Botbz zone=qeytoqrg action=Consider target=a_decaying_skeleton11(155) hp=100% why=closest prey
t=1791135619.267 bot=Botbz zone=qeytoqrg action=Approach target=a_decaying_skeleton11(155) hp=100% why=cons indifferent or worse
t=1791135631.280 bot=Botbz zone=qeytoqrg action=Fight target=a_decaying_skeleton11(155) hp=90% why=in reach
t=1791135638.492 bot=Botbz zone=qeytoqrg action=Kill target=a_decaying_skeleton11(155) level=1 hits=6 hp=69%
t=1791135638.496 bot=Botbz zone=qeytoqrg action=Exp exp=165 gained=75
t=1791135638.513 bot=Botbz zone=qeytoqrg action=LootItem corpse=155 item=13329 slot=1
t=1791135640.534 bot=Botbz zone=qeytoqrg action=Rest target=-(155) hp=72% why=looted
```

## Notes

- One process per bot for now (the design's single event loop comes when the numbers ask for it).
- The bots never write to the database. `setup-accounts.sh` does, once, like an admin would.
- The login server sent World an empty account name, so World could create only one new game account
  (`Duplicate entry ''`): `setup-accounts.sh` creates the game accounts itself; the server fix is a
  separate change.
- z goes on the wire x10 from a client (the zone keeps it so and divides by 10 in `GetZ`).
- An NPC's corpse keeps the NPC's spawn id (`attack.cpp`, `AddCorpse(..., GetID())`): the hunter loots
  with `OP_LootRequest` on the id it killed. Items come as `OP_ItemOnCorpse` (0x5220, an Item_Struct
  whose `equipSlot` is the loot slot), each taken with `OP_LootItem` (type 1: into the packs).
- `OP_Consider` must be exactly 28 bytes (`Consider_Struct`) or the zone drops it. The answer's
  faction: 0 indifferent, positive amiable to ally, negative apprehensive to scowls. NPCs without a
  faction (rats, snakes, skeletons) con indifferent.
- The design asked for the test in `qeynos2`; it runs in `qeytoqrg`, where the level-1 prey is.
  Not done yet: nukes and other spells, camps chosen from `spawn2`, corpse runs to another zone.
- Level-1 casters die often in the Qeynos Hills (gnoll scouts, level 3-4, roam and aggro): deaths are
  logged and counted, not a failure.
