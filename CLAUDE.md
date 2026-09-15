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
| `legacy/` | Dead code moved out of the build (unused parts of `LS/common`, `LS/zone`, stale makefiles). Not compiled. |

## Conventions and gotchas

- Sources are ISO-8859-1 (non-ASCII in comments). Use `LC_ALL=C grep -a` or grep silently
  skips files as "binary".
- Windows only. `Win32` (x86) configurations, toolset v143, SDK 10.0. There is no working
  Linux build (the old makefiles are in `legacy/makefiles/` for reference only).
- `Dependencies/` is empty in git; MySQL, zlib, Perl 5.12 and OpenSSL must be dropped in
  (layout in `docs/BUILD.md`).
- Config files are read from the working directory: `db.ini` (`[Database]`),
  `LoginServer.ini` (`[LoginServer]` for World, `[LoginConfig]` for Login).
- Filename case matters for any future non-Windows build: project files were fixed to
  match on-disk names; keep new entries case-exact.
- No tests, no CI. Verify changes by building the solution in Visual Studio.
- Do not "fix" the `LS/` tree by re-importing files from `legacy/`; Login only needs what
  is left in `LS/common` and `LS/zone` (transitive include closure, see `legacy/README.md`).
