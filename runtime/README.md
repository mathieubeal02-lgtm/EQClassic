# runtime/ — files the servers read from their working directory

`cmake --install build --prefix <dir>` copies this folder (minus `.keep`/README) next to the
executables. Copy manually if you use the legacy `.sln` build.

| Path | Read by | Notes |
|---|---|---|
| `db.ini.example` | all | Rename to `db.ini`, fill `[Database]` host/user/pass/data. |
| `LoginServer.ini.example` | world (`[LoginServer]`), login (`[LoginConfig]`) | Rename to `LoginServer.ini`. Use 127.0.0.1 locally, your LAN/public IP otherwise. |
| `startserver.bat`, `boot1zone.bat`, `Boot5zones.bat`, `BootAll.bat` | — | Launch scripts. `zone . <ip> <port> <world ip>` starts a *dynamic* zone; `BootAll.bat` starts every zone statically on ports 30000+. |
| `cfg/*.cfg` | zone | Binary zone headers (weather, sky, fog…), one per zone. `#zsave` writes them. |
| `spdat.eff`, `spells_en.txt` | zone | Spell data. `spells_en.txt` is the file from the Trilogy client. Zones crash without it. |
| `spellResistMods.txt` | zone (`SpellsHandler.cpp`) | `spell_id resist_modifier` per line. |
| `eqtime.cfg` | world | Persisted in-game clock. |
| `plugin.pl`, `commands.pl`, `plugins/*.pl` | zone (embedded Perl) | Quest helper plugins and `#` commands implemented in Perl. |
| `quests/` | zone | Put the PEQ quest pack here: `quests/<zone>/<npcid>.pl`, `quests/<zone>/player.pl`, `quests/plugins/*.pl`, `quests/items/`. Not in git (external download). |
| `Maps/Maps/*.map` | zone (`Map.cpp`) | Collision/LOS maps from the EQEmu map pack. **NPCs do not spawn without them.** Not in git. |
| `Maps/3DGraphs/`, `Graphs/`, `Grids/`, `ZoneLines/` | zone | Pathing/zone-line data, generated in game (`#` commands) or shipped by the community. Empty here. |
| `Maps/Nodes/`, `Maps/Paths/` | zone (`zone.cpp`) | NPC roaming nodes/paths (`<zone>Nodes.txt`, `<zone>Paths.txt`). Only gfaydark is provided. |
| `Maps/Boats/World/*.txt`, `Maps/Boats/Zone/*.txt` | world (`Boat.cpp`), zone (`npc.cpp`) | Boat routes. |

Sources: `Zone/` (cfg, spdat, spells) and the upstream "EQClassic Server Files" package
(https://archive.org/details/open-eqc-server) for the rest.
