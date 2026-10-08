# NPC stats: sources and patches

| Patch | What | Source |
|---|---|---|
| `sql/patches/004_npc_combat_stats.sql` | hp, min/max damage, AC, ATK, Accuracy, avoidance for 19,919 NPCs | Quarm DB (EQMacEmu), `gen_004.sh` |
| `sql/patches/005_npc_era_level_hp.sql` | level and HP clamped to the era capture for 1,231 spawned NPCs | `EQSpawns-showeq-2002.txt` |
| `sql/patches/006_remove_post_trilogy_races.sql` | spawns of races the Trilogy client lacks (Vah Shir 130, races 200+) removed; they crash the client | none in `EQSpawns-showeq-2002.txt` |

`EQSpawns-showeq-2002.txt`: ShowEQ capture of the live servers, 2000-2002 (Kunark/Velious), name /
zone / level range / HP range / race / class / positions for 5,577 NPCs. It is the source Project 1999
cites for NPC HP and levels (the P99 wiki's own stat boxes come from PEQ and are not era-accurate).
Archived at http://web.archive.org/web/20021129210131/http://www.geocities.com/ficticiousname9/.

Validation of 004 against it (8,325 spawned NPCs present in both): HP within 5% of the capture for
91% with Quarm values vs 38% with the original EQClassic values. Quarm is later than Trilogy, so some
NPCs carry Quarm-specific choices (e.g. unkillable level 61 / 2,000,000 HP city NPCs); 005 puts every
spawned NPC found in the capture back inside its captured level/HP range.

Order: dump, 001-003, 004, 005, 006. Regenerating 004 needs the original dump (before 004/005).

## Zones in a later state

The dump has some zones as they were after a revamp. Share of our spawned NPCs that Quarm also has
in the same zone: Sleeper's Tomb 12%, Nurga 16%, Droga 23%, Plane of Mischief 29%, Skyshrine 49%,
The Hole 59% (most other zones are above 85%).

`gen_zone_from_quarm.py <zone> <number>` writes a patch that replaces a zone's spawn points, spawn
groups, entries and path grids with Quarm's (plain rows: applying it needs no Quarm database), keeps
our rows in `*_before_era` tables, and keeps our NPCs, matched by name. It suits a zone whose era
NPCs we already have (Skyshrine: 185 of 186, `sql/patches/013`; The Hole: 41 of 43, `014`).
`test_zone_patch.sh <patch> <zone> <zone id>` applies a patch twice to copies of the tables and counts
what would dangle. Zones where they are missing
(Sleeper's Tomb, Nurga, Droga, Mischief) would also need their loot, factions and spells imported.
