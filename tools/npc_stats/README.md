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
