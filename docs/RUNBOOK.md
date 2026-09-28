# Runbook — from a clean Windows machine to a character in game

Everything below runs on one Windows PC (server + client). Replace `127.0.0.1` with the
server's LAN/public IP where noted to let other machines connect.

## 1. Build or download the server binaries

- **Build**: `docs/BUILD.md` → `cmake --install build --config Debug --prefix C:\eqc` gives
  `C:\eqc` with `login.exe`, `world.exe`, `zone.exe`, `SharedMemory.dll`, the dependency DLLs
  and the contents of `runtime/`.
- **Or** download the prebuilt "EQClassic Server Files" (`OpenEQC Server.zip`,
  https://archive.org/details/open-eqc-server) — it is the same layout, built from the same era of code.

Install **ActivePerl 5.12 x86** (`Perl/bin` in `PATH`, or copy `perl512.dll` next to `zone.exe`
and its library: `Perl/lib` and `Perl/site/lib` of `Dependencies.zip` into `perl/`, with
`PERL5LIB=perl/lib;perl/site/lib` as `runtime/run-wine.sh` sets it. Without the library the zone
starts but every quest fails with "Undefined subroutine &main::eval_file";
`cmake --install` already copies it). The community reports that quests may need `Perl514.dll`
renamed to `Perl512.dll`; try the plain 5.12 install first.

## 2. Database

1. Install MySQL 8 (or MariaDB / MySQL 5.7). With MySQL 8 the server account must use
   `mysql_native_password` (the client library is 5.7):
   ```sql
   CREATE DATABASE eqclassic CHARACTER SET latin1;
   CREATE USER 'eqc'@'localhost' IDENTIFIED WITH mysql_native_password BY 'eqc';
   GRANT ALL ON eqclassic.* TO 'eqc'@'localhost';
   ```
   MariaDB ≥ 11 needs two server settings for the 2017 MySQL 5.7 client library and the
   2010-era SQL (e.g. `/etc/mysql/mariadb.conf.d/60-eqclassic.cnf`):
   ```ini
   [mariadb]
   sql_mode = NO_ENGINE_SUBSTITUTION   # no strict mode
   skip-ssl                            # the old client cannot negotiate modern TLS (error 2026)
   bind-address = 127.0.0.1
   ```
2. Import the dump (`git submodule update --init` first), then the patches:
   ```sh
   cat sql/eqclassic_db/sql/*.sql | mysql -u root -p eqclassic
   cat sql/patches/*.sql        | mysql -u root -p eqclassic
   ```
   (MySQL Workbench: Server → Data Import → "Import from Dump Project Folder" → `sql/eqclassic_db/sql`,
   then run every file in `sql/patches/` in order.)
   `002_items_raw_data_encoding.sql` matters: the dump's item blobs are corrupted; without the patch
   (and without `items_axclassic`, which the servers now read first) the world logs thousands of
   "Invalid items" and no item exists in game.
3. Create a login account (password is checked as `SHA()`; without `user_active='1'` the login
   server answers "Your account has not been verified by e-mail"):
   ```sql
   INSERT INTO login_accounts (name, password, lsadmin, lsstatus, worldadmin, user_active)
     VALUES ('test', SHA1('test'), 0, 0, '0', '1');
   ```
   The `account` row on the world side is created automatically on first login. To make
   that account a GM afterwards: `UPDATE account SET status=250 WHERE name='test';`
   (`world.exe adduser <name> <pass> <status>` also exists, but expects a plain-text password
   in `account` — legacy path, avoid.)

## 3. Configure

In `C:\eqc`:

- `db.ini` (from `db.ini.example`):
  ```ini
  [Database]
  host=127.0.0.1
  user=eqc
  pass=eqc
  data=eqclassic
  ```
- `LoginServer.ini` (from `LoginServer.ini.example`). `[LoginServer]` is read by `world.exe`,
  `[LoginConfig]` by `login.exe`:
  ```ini
  [LoginServer]
  loginserver=127.0.0.1      ; IP of login.exe
  worldname=EverQuest Classic
  account=
  password=
  locked=false
  worldaddress=127.0.0.1     ; IP clients use to reach world.exe → LAN/public IP for remote players
  loginport=5999
  autobootzones=false        ; true: world.exe starts a zone process when none is free
  autobootzones_firstport=7100
  autobootzones_max=20

  [LoginConfig]
  ServerMode=Standalone
  ServerPort=5999
  ```
- Boot scripts: `zone . <zone ip> <port> <world ip>` starts a **dynamic** zone that world
  assigns on demand. `Boot5zones.bat` starts five (ports 1000–1004). For remote players put
  the server's real IP in the *zone ip* argument. `BootAll.bat` starts every zone statically
  (ports 30000+), which needs a lot of RAM — use it only if dynamic zones misbehave.
  With `autobootzones=true`, world.exe itself starts `zone.exe . <worldaddress> <port> 127.0.0.1`
  (ports from `autobootzones_firstport`) when a player asks for a zone and every zone process is
  busy, instead of answering "zone unavailable"; the player waits up to 90 s for it to boot.
  `worldaddress` is then also the address the zones give the clients.
- Client data the server needs (not in git):
  - `Maps/Maps/*.map` → EQEmu Map Pack 1.0 (`Maps.tar.gz`, see `docs/BUILD.md`). Without them
    **no NPC spawns**.
  - `quests/` → PEQ Velious quest pack (unzip so that `quests/<zone>/*.pl` exists; merge the
    pack's `plugins/` into the existing `quests/plugins/`).
  - `spells_en.txt` is already in `runtime/`; if your client's file differs, copy the client's.

## 3b. Running on Linux with Wine (tested)

The Win32 servers run under Wine 10 (`sudo dpkg --add-architecture i386 && sudo apt install wine wine32:i386 mariadb-server`).
Use the **Release** build (or any build made after the static-CRT change): older Debug artifacts need
Visual Studio's debug CRT DLLs (`MSVCP140D.dll`…), which exist on no normal machine.
`run-wine.sh start [zones]` / `stop` / `status` starts login, world and N dynamic zones with logs in
`logs/`. The servers' own logs (`logs/eqc_debug_*.log`) are more complete than stdout, which is buffered.
Zones that print `Entering sleep mode` are healthy: they wait for world to assign them a zone.
Each dynamic zone process hosts one zone, and an empty zone stays up for its `zone.shutdowndelay`
(5 min for most, up to 1 h): with too few processes (5 used to be the default) players get "zone
unavailable" after visiting a few zones. `run-wine.sh` starts 12 (about 50 MB each under Wine).
Zone UDP ports start at 7000 under Wine: on Linux, ports below 1024 need root, and a zone that cannot
bind its port logs `NetConnection::Init failed` (the client then gets "That zone is unavailable").
First successful end-to-end session (2026-09-25): Trilogy client on Windows → login → world →
character creation → zone (grobb) on this setup.

## 4. Start

Order matters: `login.exe` → `world.exe` (must log "LoginServer.ini read." and connect to the
login server) → `Boot5zones.bat`. `startserver.bat` does the three.

Expected console lines: world prints `Zoneserver SetZone: <ip>:<port> <zone>` when a dynamic zone
is assigned; a zone prints `<zone> boot was successful.`

Ports: login **5999/udp+tcp**, world **9000/tcp** (zones/console) + UDP client port, zones
**1000–1004/udp** (or whatever the .bat says). Open them on the firewall for remote players.

## 5. Client

1. Install EverQuest Trilogy (CD images: https://archive.org/details/EverQuestTrilogy). Do not
   run the installed shortcuts (they patch).
2. Edit `eqhost.txt` in the game folder. The Trilogy client uses this brace format (not the
   `Host=` format of later EQEmu clients); change **both** entries, quotes included, no `host=`:
   ```
   [Registration Servers]
   {
   "192.168.1.2:5999"
   }
   [Login Servers]
   {
   "192.168.1.2:5999"
   }
   ```
   Save it (Notepad marks unsaved tabs with a dot) and restart EQW: it reads the file at start-up.
   If the game is under `Program Files`, Windows may redirect the old client to a stale copy in
   `%LOCALAPPDATA%\VirtualStore\…`; install it somewhere like `C:\EverQuest` instead.
   Error 1001 with no packet reaching the server means the client is not using this address.
3. Launch through **EQW.exe** (https://archive.org/details/eqwindow-beta-v-2.32), pointing it
   at `eqgame.exe`; on modern Windows add dgVoodoo2 + `d3drm.dll` for rendering. The README's
   warning: the `patchme` suffix gets you disconnected — do not use it.
4. Log in with the `login_accounts` credentials, pick the world, create a character, enter.

## 6. Troubleshooting

| Symptom | Check |
|---|---|
| `Couldn't open the db.ini file` | Working directory: the exes read `db.ini` from *where they are started*, not from where they live. |
| world: `[LoginServer] block not found` | `LoginServer.ini` must contain both `[LoginServer]` and `[LoginConfig]` sections. |
| zone exits at start about `spells_en.txt` | File missing from the zone's working directory. |
| "That zone is unavailable" | Zone log shows `NetConnection::Init failed`: its UDP port is taken or < 1024 on Linux. |
| Character in the list cannot enter the world, name starts with `��` | Corrupted character from the dump: run `sql/patches/003`. |
| Zone loads, no NPCs | `Maps/Maps/<zone>.map` missing (names lowercase). |
| NPCs never move | Roaming needs `Maps/Nodes|Paths/<zone>*.txt` and grid data in DB (`grid`, `grid_entries`). |
| Login accepted, world list empty | `world.exe` not connected to login: `loginserver=`/`loginport=` in `LoginServer.ini`, or login started after world. |
| Quests silent | `quests/<zone>/<npcid>.pl` path/case; `perl512.dll` present; the Perl library in `perl/` ("Undefined subroutine &main::eval_file" in the zone log); zone log lines `Warning - plugin.pl`. `tools/eqbot/legacy-test.sh` hails a quest NPC. |
| MySQL `Authentication plugin 'caching_sha2_password' cannot be loaded` | Recreate the DB user with `mysql_native_password`. |
| `ZoneLoopDebug.txt` appears | Debug hook `ZONE_FREEZE_DEBUG` in `Common/Include/config.h`; should be 0. |
