# botd: player bots (milestones 1 to 4)

Bots that log in like real players and live in the world, as designed in `docs/bots-design.md`:
headless clients built on `tools/eqbot`, nothing in the servers. Milestone 1 is a fleet that connects
and walks, milestone 2 bots hunt alone, milestone 3 bots hunt in groups, milestone 4 bots talk.

## Pieces

| File | What it does |
|---|---|
| `../eqbot` `walk` | One bot: logs in, enters its zone, walks a list of waypoints back and forth (about 15 units/s, a position every 250 ms, sits and stands at every third point), logs one line per action, logs out cleanly |
| `../eqbot` `hunt` | One bot hunts alone around the place it logs in (its camp, 800 units): picks the closest small NPC (`a_`/`an_`, level up to its own + 1, nobody on it, no guard), considers it and never attacks one that cons amiable or better, walks to it, auto-attacks, loots every item of the corpse, sits until healed (it only pulls above 90 % HP), flees to the camp under 20 % HP, fights back whatever attacks it, strolls around the camp when nothing is up. Killed: logs, waits 15 s, logs in again, goes back to its corpse and loots it (the zone puts worn items back on), then hunts around its first camp again. A cleric casts Minor Healing on itself under 50 % HP in a fight and under 70 % before resting, and meditates until its mana is back |
| `../eqbot` `hunt` in a group | `EQBOT_INVITE=Botca,Botcb` makes a hunter the leader: it targets and invites them (again after a death), pulls only when they are near, above 80 % HP and nobody said "oom", takes yellow prey too, and says `assist` on each pull, `sit` after the loot, `follow` when it moves on. `EQBOT_ROLE=member` makes a hunter that never pulls: it joins whoever invites it (a real player too), follows, answers `assist` with `/assist` on the leader (melee, or Frost Bolt from 30 units for a wizard), heals the lowest member under 60 % (cleric), says `oom, medding` and `ready`, obeys `sit`, `follow` and `camp` (stay) |
| `chatd.py` | The chat service all bots share (milestone 4, `docs/bots-design.md` 7): on 127.0.0.1:7780, a bot asks it for a line (`/line`: lfg, inc, death...) or for an answer to a player who spoke to it (`/reply`: tell, say with its name, group). Templates (`chat/templates.txt`) unless `llm = on` in `bots.ini` and `ANTHROPIC_API_KEY` is set; then the model answers real players only, filtered (length, URLs, `chat/blocklist.txt`, out-of-game talk) and limited (calls per hour, per bot, one bot line on OOC per 30 s, a daily budget). Bots never get an answer to a bot. One log line per decision (`chat=template|llm|filtered|ratelimited|budget`), LLM lines with their prompt hash |
| `../eqbot` `hunt`, chat | A hunter answers tells, says with its name and group chat through chatd, without waiting on it (an answer later than 10 s is dropped); `EQBOT_LFG=1` makes a lone member post LFG on OOC; a leader says `inc <mob>` on each pull, a bot back from a death says so to its group |
| `../eqbot` `travel` | A bot changes zones (milestone 6): it walks to the next zone's line (`paths/zonelines.tsv`), the zone sends `OP_TeleportPC`, the bot asks `OP_ZoneChange`, the zone confirms with the new zone's name, the bot goes back to World with the ticket of its login (no new login, as the real client) and enters the world again into the new zone. Each change is logged (`action=Zoned from= to= ms=`) |
| `../eqbot` `hunt`, town | A hunter goes to town (milestone 5) when it runs out of food or drink, or every `EQBOT_TOWN_EVERY` minutes: `EQBOT_TOWN=<zone>` names the town next door (`grobb` for Innothule), `EQBOT_TOWN_NPC=<name>` a merchant to try first. It rests to 90% HP, walks to the zone line, and in town asks up to six merchants, closest first: the first that opens buys what the packs hold (all but food and drink), each is asked for the cheapest drink and food it sells (5 of each, then up to 20 with what money is left); then it walks back and hunts again. Lines: `LeaveForTown`, `TownSell`, `TownBuy`, `Town`, `TownBack`. It eats and drinks from its packs by itself (`action=Consume`) |
| `zonelines.sh` | Writes `paths/zonelines.tsv` from `zone_points`: a point and its range, or an X/Y line crossed past a trigger within bounds (the zone's `ScanForZoneLines` rules) |
| `setup-accounts.sh` | Creates the bot accounts `bot_001`... (status 0, never GM) and one character each (`Botaa`, `Botab`...), placed at the first waypoint. `BOTD_FIRST` picks the first index, `EQBOT_CLASS=warrior`, `cleric` or `wizard` makes humans of that class (default: troll shamans, which Qeynos guards kill on sight); casters get their starting spell scribed and memorized (Minor Healing, Frost Bolt), as a player would do with the scroll |
| `fleet.sh` | Starts N bots (`walk` along a waypoints file, `hunt`, or `group`: the first bot leads the others) 2 s apart (one login at a time), samples the load every 10 s, summarises |
| `load.py` | CPU (interval, from `/proc`) and RSS of the `zone.exe` processes and of the bots |
| `botlog.py` | Reads the action lines and checks milestone 1 (every bot entered and finished, with arrivals, no login failure, server-side positions within 5 units) or, for hunters, milestone 2 (finished, at least 10 kills, an item looted, sat between fights, experience gained, no attack on a refused NPC or a guard, never still for over 2 minutes outside a rest) or, for a group, milestone 3 (every member joined, at least 10 leader kills, each member got experience within 5 s of at least half the kills made while it was grouped, heals on the leader from a healer, no member pulled) |
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

A group (milestone 3): a warrior leads a cleric and a wizard for 30 minutes:

```sh
BOTD_FIRST=54 EQBOT_CLASS=wizard ./setup-accounts.sh 1 qeytoqrg paths/qeytoqrg-safe.txt   # Botcb
BOTD_FIRST=52 ./fleet.sh 3 group 1800                    # Botbz leads Botca and Botcb
```

To group with them from the real client: target a member (`EQBOT_ROLE=member`, not grouped) and
`/invite`; it follows you and assists when you say `assist` in `/gsay`.

Chat (milestone 4):

```sh
python3 test_chatd.py                               # chatd against a stub LLM (no network); also in CI
cp bots.ini.example bots.ini                        # llm = off: templates only
./chat-test.sh 54 bot bot Qbottwo                   # in game: LFG on OOC, a tell answered within 5 s
```

To try the model: `llm = on` in `bots.ini`, `ANTHROPIC_API_KEY` in the environment of `chatd.py`,
then send a bot a tell from the real client. `curl 127.0.0.1:7780/health` shows the calls of the
last hour and the money spent today.

Zoning (milestone 6):

```sh
./travel-test.sh          # North Qeynos -> Qeynos Hills -> Blackburrow -> Qeynos Hills -> North Qeynos
```

Bots walk straight to a zone line: the zone takes a client's positions as they come (no collision
check), so walls are no obstacle for them.

## Bug hunting

The bots also report what a player would find wrong. `eqbot hunt` writes `action=Anomaly kind=...`
lines (at most one per kind and bot per minute, with the repeats it stood for):

| kind | when |
|---|---|
| `no_profile` | the zone took the bot in and never sent its profile |
| `zone_silent` | no packet from the zone for 30 s |
| `bad_spawn` | a spawn with a size outside -1..100, a level 0 or over 100 NPC, an absurd position |
| `npc_data` | an NPC sent with size -1 (1339 `npc_types` rows: probably the import's "race default") |
| `exp_missing` | the bot's own kill of a non-green NPC gave no experience within 5 s |
| `consider_timeout` | `/consider` got no answer |
| `no_hit` | 30 s of melee without a hit (with the zone's "too far" / "can't see" counts) |
| `cast_refused` | five refused spells in a row |
| `unreachable` | a target not reached in 60 s (pathing) |
| `zone_line` | the zone tried to send the bot through a zone line (bots do not zone yet) |
| `login_refused` | three refused logins in a row |
| `death_loop` | three deaths in 10 minutes |

`watchdog.py` adds the server's side every 10 s: `zone_crash` (with the first frames of the backtrace
Wine prints, named from `zone.pdb`), `zone_hang` (a zone above 90 % of a core for 30 s, with a
`winedbg` stack), `zone_gone`, and `zone_log` (zone log lines about errors, refusals, wrong packet
sizes). It never restarts or kills anything.

`bugreport.py <run dir> [--known known-issues.txt]` groups all of it by kind and signature (numbers and
spawn suffixes left out), ranks by severity and count, keeps three sample lines per group, and writes
`report.md`; signatures listed in `known-issues.txt` go to "Already known".

```sh
./night.sh 6          # 6 hours: 16 troll shamans alone in Innothule, a group in the Qeynos Hills
                      # -> logs/night-<date>/report.md
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
- Groups: `/invite` reaches the player the inviter targets; the member answers `OP_GroupFollow`
  (leader, invited). `/gsay` is one `OP_ChannelMessage` per member (its name first, channel 2).
  `/assist` (`OP_AssistTarget`, 0x0022, the leader's spawn id) comes back with the leader's target.
  The zone refreshes a group as "you leave" (`OP_GroupUpdate` action 4) then every member again
  (action 0). Player HP updates reach players nearby, so a healer sees its group's HP.
- The design asked for the test in `qeynos2`; it runs in `qeytoqrg`, where the level-1 prey is.
  Not done yet: nukes and other spells, camps chosen from `spawn2`, corpse runs to another zone.
- Level-1 casters die often in the Qeynos Hills (gnoll scouts, level 3-4, roam and aggro): deaths are
  logged and counted, not a failure.
