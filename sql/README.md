# Database

EQClassic uses one MySQL database (`db.ini` → `[Database]` host/user/pass/database) shared by
login, world and zone.

| File | Content |
|---|---|
| `schema.sql` | Structure only (81 `CREATE TABLE`), extracted from the dump below. Convenient for reading and diffing. |
| `eqclassic_db/` | Git submodule → https://github.com/erfg12/eqclassic_db (one `.sql` per table, ~114 MB, structure + data, MariaDB 10.0 dump). `git submodule update --init` to fetch. |
| `extract_schema.py` | Regenerates `schema.sql` from the submodule. |
| `patches/*.sql` | Apply **after** the dump, in order: `001` missing `boat_passengers`, `002` item blob encoding repair, `003` corrupted sample character, `004` NPC combat stats from Quarm (needed by the melee model, see below). |
| `../LS/Login/loginserver.sql` | Original login schema; the dump above already contains the `login_*` tables. |

## Import

```sh
mysql -u root -p -e "CREATE DATABASE eqclassic CHARACTER SET latin1"
cat sql/eqclassic_db/sql/*.sql | mysql -u root -p eqclassic
cat sql/patches/*.sql        | mysql -u root -p eqclassic
```

or MySQL Workbench → Server → Data Import → "Import from Dump Project Folder" → `sql/eqclassic_db/sql`.

MySQL 8 needs `mysql_native_password` for the server account (the 5.7 client library cannot
use `caching_sha2_password`):

```sql
CREATE USER 'eqc'@'localhost' IDENTIFIED WITH mysql_native_password BY 'eqc';
GRANT ALL ON eqclassic.* TO 'eqc'@'localhost';
```

## Notes

- Mixed engines (47 InnoDB, 34 MyISAM) and charsets (latin1, a few utf8), as dumped.
- `boat_passengers` is used by the code but absent from the dump → `patches/001_boat_passengers.sql`.
  Two queries used `zone_point` (singular) instead of `zone_points`; fixed in `Common/Source/database.cpp`.
- Login accounts: `login.exe` checks `password = SHA(<clear>)`, so create accounts with
  `INSERT INTO login_accounts (name, password) VALUES ('user', SHA1('pass'));`. The in-code
  registration path uses MySQL's `PASSWORD()`, which no longer exists in MySQL 8.
  The world-side `account` row is created automatically on first login (`World/Source/client_process.cpp:415`).
- Several tables look like leftovers (`*_old`, `*_new`, `*_copy`, `*_backup`, `*_test`,
  `tradeskillrecipe` vs `tradeskill_recipe`, `items_axclassic` vs `items`). Which one the
  code reads is in `Common/Source/database.cpp`.

## NPC combat stats (patch 004)

The melee model (`Zone/Include/CombatFormulas.h`, from EQMacEmu/Quarm) and NPC stats must match:
the original dump used its own AC scale (4-8x Quarm's) and lower HP. Patch 004 adds `ATK`,
`Accuracy`, `avoidance` to `npc_types` and `npc_types_without` (the server reads the latter) and sets
hp/min/max damage/AC/ATK/Accuracy/avoidance from the Quarm DB for the 19,919 NPCs matched on
name + zone (id / 1000) + level range (NPC ids differ between the two DBs), AC rescaled for the rest.
Regenerate it with `tools/npc_stats/gen_004.sh` from a newer Quarm dump imported as `quarm_ref`.
