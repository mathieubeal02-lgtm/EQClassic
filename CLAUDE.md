# EQClassic — project guide

EverQuest *Trilogy* (2001-era client, Classic→Velious) server emulator. C++ snapshot of the
EQClassic codebase dated 2010-01-01, published on GitHub in 2015. ~175k LOC, Windows/x86, MSVC.

See `docs/ARCHITECTURE.md` (components, data flow, hotspots) and `docs/BUILD.md`
(dependencies, external resources, runtime setup).

## Layout

| Path | What it is |
|---|---|
| `EQCEmu.sln` | Main solution: **World**, **Zone**, **Login**, **SharedMemory** (DLL), **Azone** (map tool) |
| `Common/` | Shared code: EQ UDP protocol (`EQPacket*`, `Fragment*`), packet structs/opcodes, MySQL access (`database.cpp`, `DatabaseHandler.cpp`), items |
| `EQC/` | Small utility layer (`EQCException`, `EQCUtils`, `PacketUtils`) |
| `World/` | World server (accounts, zone list, guilds, boats, time of day, console) |
| `Zone/` | Zone server (one process per zone: combat, spells, NPC AI, loot, factions, tradeskills, embedded Perl quests) |
| `SharedMemory/` | Windows DLL sharing item data between World and Zone processes |
| `LS/Login` | Login server (uses `LS/common` + a few headers from `LS/zone`, an older EQEmu-derived tree) |
| `Utils/azone` | Builds `.map` collision files from client zone data; `Utils/perlxs` regenerates `Zone/Source/perl_*.cpp` |
| `runtime/` | Files the servers read from their working directory: `cfg/`, `spdat.eff`, `spells_en.txt`, `Maps/` skeleton, Perl plugins, boot scripts, `*.ini.example`. Installed by `cmake --install`. |
| `sql/` | DB schema, patches, import notes, `eqclassic_db` submodule (data dump) |
| `cmake/` | `EQCDependencies.cmake`: imported targets for the prebuilt libs in `Dependencies/` |
| `legacy/` | Dead code moved out of the build (unused parts of `LS/common`, `LS/zone`, stale makefiles). Not compiled. |

## Conventions and gotchas

- Sources are ISO-8859-1 (non-ASCII in comments). Use `LC_ALL=C grep -a` or grep silently
  skips files as "binary".
- Windows only. `Win32` (x86), toolset v143, SDK 10.0. Build with CMake
  (`cmake -S . -B build -G "Visual Studio 17 2022" -A Win32`) or the legacy `EQCEmu.sln`;
  keep both in sync when adding/removing sources (`*/CMakeLists.txt` lists them explicitly).
  On Linux only `azone` builds (`-DEQC_BUILD_SERVERS=OFF`).
- `Dependencies/` is empty in git; MySQL, zlib, Perl 5.12 and OpenSSL must be dropped in
  (layout in `docs/BUILD.md`).
- Everything is read from the working directory: `db.ini` (`[Database]`), `LoginServer.ini`
  (`[LoginServer]` for World, `[LoginConfig]` for Login), `cfg/`, `Maps/*`, `quests/` (incl. `quests/plugins/`), `plugin.pl`, `commands.pl`,
  `spdat.eff`, `spells_en.txt`, `spellResistMods.txt`, `eqtime.cfg`. Templates live in `runtime/`.
  End-to-end setup: `docs/RUNBOOK.md`.
- Filename case matters for any future non-Windows build: project files were fixed to
  match on-disk names; keep new entries case-exact.
- Unit tests live in `tests/` (portable, no MySQL): `ctest --test-dir build`. CI
  (`.github/workflows/build.yml`) builds Windows Debug/Release via CMake, the legacy solution,
  azone and `tools/eqbot` on Linux, and runs the tests on both. Job `e2e` then runs the Release
  servers under Wine + MariaDB and plays them with eqbot: login, character creation, zone entry
  (`tools/eqbot/README.md`). Push to see it run.
- Melee combat uses `Zone/Include/CombatFormulas.h` (pure functions, EQMacEmu/Quarm model, tested in
  `tests/combat_test.cpp`); NPC stats come from `sql/patches/004`. Keep formulas and stats in step.
- EQMacEmu (GPLv3) is a reference, not a source: reimplement, never paste its code (ours is GPLv2).
- SQL: wrap every string interpolated into a quoted SQL literal with
  `SQLEscape(x).c_str()` (`Common/Include/SQLEscape.h`). Never interpolate into unquoted SQL.
- Database: `sql/schema.sql` (structure), `sql/eqclassic_db` (submodule with the data dump).
- NPC data lives twice: the zone loads `npc_types_without` (`Database::LoadNPCTypes`), `npc_types` only
  gives the highest id. A patch that changes NPC stats, sizes or levels writes both
  (`tools/npc_stats/README.md`).
- Do not "fix" the `LS/` tree by re-importing files from `legacy/`; Login only needs what
  is left in `LS/common` and `LS/zone` (transitive include closure, see `legacy/README.md`).
