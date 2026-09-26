# EQClassic rewrite — target architecture

Status: proposal and first bricks, branch `lantern-rewrite`. The existing C++ servers (`Common/`,
`World/`, `Zone/`, `LS/`) keep running unchanged; nothing here replaces them until a milestone below
is met and demonstrated.

## Why a rewrite, and why this shape

The 2010 C++ servers work, but every session this year hit the same limits: a UDP layer copied
between four programs, Windows-only builds, global state shared across threads (the reused-zone
crash), and a 2001 client that crashes on data it does not expect (Permafrost). The rewrite keeps
the *game* (rules, data, database) and replaces the *technology* around it:

- a **Unity client** that renders the original zones and models, imported with the Lantern tools
  from the player's own Trilogy install;
- a **C# server** (.NET 10, Linux first) that owns all game state;
- **one set of message contracts** compiled into both.

```
 Trilogy client files (player's install)
        │ tools/lantern/extract.sh  (LanternExtractor, externals/)
        ▼
 Lantern intermediate exports ──► Unity importer (LanternUnityTools, externals/) ──► Unity client
        │                                                                              │
        │ collision meshes (<zone>_collision.txt)                     EQClassic.Shared (netstandard2.1)
        ▼                                                                              │ LiteNetLib UDP
 EQClassic.Server (net10.0)  ◄───────────────────────────────────────────────────────────┘
   Login ─ key hand-off ─► World ─► Zone instances (tick loop, NPC AI, combat)
        │
        ▼
 MariaDB (same schema as today: login_accounts, account, character_, npc_types, spawn2, grid...)
```

### Components

| Component | Role | Status |
|---|---|---|
| `rewrite/src/Shared` | Message contracts, codec, client library (`LoginClient`), zone collision mesh. Targets `net10.0` and `netstandard2.1` (Unity 2021.3). | Started |
| `rewrite/src/Server` | Login (authentication against MariaDB or memory, encrypted login, world list, world keys), World (characters, creation, enter world) and the zone server (instances, 20 Hz tick, NPC grids, player movement) in one process. | M1, M2, M3 done |
| `rewrite/src/Cli` | Command-line client: login, world list, world key, character list, `--create`, `--enter`; one line per step. | Done |
| `rewrite/tests/Tests` | xUnit: unit tests, UDP end-to-end tests on localhost, MariaDB tests when `EQC_REWRITE_TEST_DB` is set. | 187 tests |
| `rewrite/src/ClientCore` | Client logic without engine (netstandard2.1): connection flow, zone view, interpolation, local movement. | Done, tested |
| `unity/` | Unity layer (3 scripts, plus an editor script for scripted imports and play sessions) over ClientCore and LanternUnityTools; `unity/StubCheck` compiles it without Unity; `tools/unity/setup-client.sh` builds the project. | Runs in Unity 2021.3.18f1 (Qeynos, Linux) |
| `tools/lantern/extract.sh` | Builds LanternExtractor and exports zones from a client install (exports are not in git). | Done |

### Choices already made

1. **Server-authoritative.** The client sends intentions (move, attack, cast); the server decides and
   broadcasts. Same model as EverQuest and our current servers, and the only one that keeps a
   multiplayer game honest.
2. **One server process, zones as isolated instances.** Today each zone is a Windows process, and a
   process reused for another zone crashed twice on stale global state. In the rewrite a zone is an
   object with its own tick loop and no statics: booting or dropping one cannot affect another.
   Splitting zones across machines stays possible later because zones only talk through World.
3. **Same database schema.** The rewrite reads the MariaDB tables the current servers use, so both can
   run side by side on the same data during the transition, and the data work (patches 004–006)
   carries over.
4. **Behaviour parity first.** Each server feature starts as a port of the current logic with tests
   that pin its behaviour. Login is the first: `LoginServiceTests` encode every branch of
   `LS/Login/client.cpp`: messages, SHA-1 passwords, the 20-character limit, and the lsadmin bypass.
5. **Shared contracts compile for Unity.** `EQClassic.Shared` targets `netstandard2.1` next to
   `net10.0`, so the Unity client uses the exact types and codec the server uses. The project must
   not use APIs newer than netstandard2.1 (a polyfill covers records).

## Protocol choice

The transport carries login, world and zone traffic: many small unreliable updates (positions),
some ordered reliable messages (chat, inventory, combat results), and occasional large ones
(zone entry, spawn lists).

| Option | For | Against |
|---|---|---|
| **Legacy Trilogy UDP protocol** (what `Common/Source/EQPacket*` implements) | The original client would keep working; the code exists and eqbot speaks it. | Reverse-engineered and full of edge cases (acks, fragments; we fixed several this year); fixed 2001 message layouts; DES "encryption"; ties the rewrite to the client we are trying to leave. |
| **LiteNetLib** (reliable UDP library, MIT) — **chosen** | Reliable-ordered, reliable-unordered, sequenced and unreliable channels over one UDP socket; fragmentation, MTU discovery, NAT punch; pure C#, works in Unity and .NET; small and maintained. | No built-in encryption; we write the message layer ourselves (done: `MessageCodec`); fewer ready-made tools than HTTP. |
| ENet (via ENet-CSharp) | Proven in games, similar channel model. | Native library to ship per platform (including Unity); C interop. |
| TCP + WebSocket (+ protobuf) | Simple, firewall-friendly, encryption with TLS for free, web clients possible. | Head-of-line blocking: one lost packet stalls position updates; poor fit for 20 Hz movement. |
| QUIC / gRPC | Encrypted, multiplexed streams. | Heavy for Unity; QUIC support in .NET on Linux depends on libmsquic; still stream-oriented for movement. |

**Decision: LiteNetLib with our own binary messages** (`EQClassic.Shared.Protocol`): one type byte,
then the fields, decoded strictly (unknown type, truncation or trailing bytes are errors). The
protocol version is the connection key (`EQClassic/2` since the doors), so mismatched builds are refused at
connect time. Movement will use unreliable-sequenced delivery, everything else reliable-ordered.

**Encryption (M1, done).** LiteNetLib itself sends plaintext, so the login adds its own layer, built
only from primitives that exist on netstandard2.1 *and* Unity's Mono runtime (no `AesGcm`, no X25519):

1. On connect the server sends `ServerHello`: its RSA-2048 public key and a fresh 32-byte nonce.
2. The client sends `SecureLoginRequest`: RSA-OAEP(SHA-1) of {nonce, 32-byte client secret, name,
   password}. The password never appears on the wire; a recorded request is useless on another
   connection (nonce), and every failed attempt renews the nonce.
3. Both sides derive session keys from the secret and the nonce (HMAC-SHA256); sensitive answers,
   today the world key, travel `Sealed` (AES-256-CBC, then HMAC-SHA256 over IV and ciphertext).
4. The server prints its key fingerprint at start-up; a client configured with it
   (`LoginClient.ExpectedServerFingerprint`, `--fingerprint`) refuses any other server before
   sending credentials, which stops a man in the middle. An unpinned client accepts any key: it is
   safe against eavesdropping but not against an active man in the middle. Remembering the first
   key seen (as SSH does) is left for the Unity client (M4).

The plaintext `LoginRequest` stays in the protocol for tests and tools but is refused unless the
server runs with `--allow-plaintext`. Movement and chat will not be encrypted (not secret, and
per-packet cost matters at 20 Hz); anything that grants access (keys, tokens) will be sealed.

The legacy credential block (`TrilogyCredentials`) is kept in Shared, tested against OpenSSL, for an
optional bridge that would let the original client log in to the new login server.

## Milestones

Each milestone ends with automated tests plus one manual check, and is merged only when both pass.

| # | Milestone | Done when |
|---|---|---|
| M0 | **Foundations** (this branch): Lantern tools, solution, login server, shared client library, collision mesh, waypoint walker. | `dotnet build` and `dotnet test` pass in `rewrite/`; Permafrost and Qeynos export with `tools/lantern/extract.sh`; `EQClassic.Server` accepts `test`/`test`. |
| M1 | **Real accounts and a secure login** (done): `login_accounts` read from MariaDB (MySqlConnector), encrypted login with a pinnable server key, sealed world key, `EQClassic.Cli`. | Store tests against a MariaDB container in CI; the bot account logs in from the CLI against the live database as `LS#16`; tests prove the login packet holds no password, replays fail, and the world key is sealed. |
| M2 | **World** (done): world key redemption (single use), world account created on first visit like the legacy World, character list from `character_` with the profile blob decoded (`PlayerProfile`), enter world → saved zone and position, character creation (name rules and `name_filter`, `start_zones`, `starting_items`, food/drink charges) writing a legacy-compatible profile built on the packet captured from the Trilogy client. Known limit: skills and languages come from that template (troll shaman) until the race/class tables are ported. | Met: the CLI created `Qrewrite` through the rewrite in the live database, and eqbot then entered Grobb with it on the current C++ World and zone servers; the rewrite lists and enters characters the C++ servers created (Qbot, Lanlaan, Qtest). |
| M3 | **Zone core** (done): zone server hosting every zone in one process (instances booted on first use, no globals), 20 Hz tick, spawns from `spawn2`/`spawnentry`/`npc_types_without` (weighted by chance), grids walked on the Lantern collision mesh at the legacy speed, World → zone key hand-off, entity list, arrivals and departures, movement validation (70 u/s cap, corrections), position updates within 600 units (unreliable, split per datagram); **aggro** with the legacy rules (KOS standings, radius by consider color, sitting, undead, green divisors; line of sight on the mesh; scan every 1.25 s; chase at run speed to melee range; back to the grid when the target leaves); **death and respawn** (`respawntime`, `variance`, legacy quirk: variance only shortens); **zone lines** from `zone_points` (range, keepX/keepY) with a zone change hand-off; position and zone **saved into the profile** on leaving, readable by the C++ servers. Faction standings are an interface: the legacy faction computation comes with M6 (until then NPCs are indifferent). | Met: over UDP with the live database and the Lantern export of Qeynos, the CLI enters `qeynos2` (181 NPCs), 409 updates in 20 s; a 2-minute simulation keeps every NPC within a step up and a ledge down per tick; end-to-end tests walk a character over Grobb's zone line into Innothule and find the new zone saved in its profile; MariaDB tests cover the loaders and the profile write. |
| M4 | **Unity client, first light** (in progress): engine-free client core (`EQClassic.ClientCore`, netstandard2.1: login → world → zone chain with zone changes, zone state with entity interpolation, local movement on the zone's collision mesh with server corrections, EQ ↔ Unity coordinates at Lantern's 0.5 scale, race → Lantern model codes); Unity layer (`unity/Assets/EQClassic`: IMGUI screens, zone and character prefabs from LanternUnityTools, third-person camera) assembled by `tools/unity/setup-client.sh` (docs/unity-client.md). | Done: the client core plays login → character creation → zone → zone line → next zone against the real servers in tests, and sees another player move smoothly; first light in Unity 2021.3.18f1 on Linux: logged in against the rewrite server and the live database, entered Qeynos (zone and citizens drawn from the Lantern imports, 182 entities) and walked it, held by the ground and the walls. Since then: player race models (`global_chr`), stand/walk/run animations, and a standalone Linux build that loads zones and models from asset bundles. Two standalone clients played Qeynos together and saw each other move. Zone collision now includes the placed objects that have a collision mesh, on the server and the client. A Windows build is produced (not yet played on Windows). |
| M4+ | **Doors** (done): `doors` table loaded with each zone, `ZoneDoors` / `ClickDoor` / `DoorState` / `ZoneMessage` messages, the rules of `Client::ProcessOP_ClickDoor` (toggle, trigger door, reset after 12 s untouched, locked doors, teleport doors within or out of the zone), drawn and animated in Unity. | `DoorTests` pin the rules; end-to-end tests: a door one player opens is seen open by another, a locked door answers with the legacy message; checked in Unity in Qeynos. |
| M5 | **Combat and spells** (melee done, spells started): `CombatFormulas` ported with the C++ test values; `SkillCaps` (Mob::CheckMaxSkill for melee skills); fighters from `npc_types_without` (class, hp, min/max damage, AC, ATK, accuracy, avoidance, attack speed) and from the profile (stats, skills, primary weapon and worn AC from `items_axclassic`, max HP from Client::CalcBaseHP); targets (Tab cycles), auto-attack, swings at each fighter's delay within reach, NPCs fight back, legacy combat and death messages, NPC death with respawn, HP saved in the profile, regeneration out of combat. Experience (Client::GetEXPForLevel, AddEXP, ExpLost, NPC::Death, same single-precision arithmetic): level² × 75 per kill to the player who did the most damage, nothing for green NPCs, capped at a tenth of the level, level ups rebuild the fighter; death costs experience from level 6 and returns the player to their bind point (bind_location slot 0, in this zone or another). Corpses and loot (Database::AddLootTableToNPC and AddLootDropToNPC, Corpse rot and loot rights): NPCs leave corpses carrying their loot table's items and coins, the killer loots first, then anyone after 165 s; the inventory and money are saved in the profile. Moving and equipping items (slot, class, race, two-handed checks; armour and weapon feed the fighter). Spells, first slice (Mob::CastSpell, SpellFinished, SpellOnTarget, CheckFizzle, CalcResistValue, CalcSpellValue, CalcMaxMana, DoManaRegen, channeling): `spdat.eff` read at start (3000 records, CHA spacer rule), mana pool and regeneration (meditate when sitting), refilled on level change; scribing the starting scrolls into the book, memorising the 8 gems, casting with cast time, fizzles (whole cost spent), interruption by moving 3 units or failing to channel through damage, range checks, NPC resists (MR/CR/DR/FR/PR from `npc_types_without`), direct damage and heals (effects 0 and 79) with the spell's messages, kills and experience; book, gems and mana saved in the profile. Buffs (Mob::AddBuff, CheckStackConflict, CalcSpellBuffTics, ApplySpellsBonuses, tic processing): 15 slots, stronger spells replace weaker ones and weaker ones do not take hold, damage over time from several casters stacks on NPCs; AC (AproximateSpellAC), ATK, stats, hit points, resists, haste and slow, movement speed (the server's speed check follows), damage, heals and mana over time with the caster credited for kills; buffs fade with the spell's message, are lost at death and kept in the profile (buffs[15]). Other effects: root (NPCs stop chasing; the client stops moving), mez (the NPC does nothing and forgets its foes, damage wakes it), stun, invisibility and invisibility versus undead (no aggro, broken by attacking or casting a hostile spell), levitation (the client floats down), bind affinity, gate, teleports, summoned items, mana; area spells around the caster or the target, group spells on the caster (no groups yet). `zone_rules` (can_bind, can_lev, castoutdoor) refuses binding, levitation and outdoor spells as SpellFinished does, mana spent. Still refused (no mana spent): charm, fear, pets, lull/harmony, cures and dispels, illusions and the rest. Still to do: those, player corpses, groups. Factions (faction.cpp CalculateFaction, GetFactionLevel, SetFactionLevel; mob.cpp GetSpecialFactionCon): standings from `faction_list` (base, class, race and deity modifiers), the NPC's `npc_faction` primary faction and the character's `faction_values`; NPCs without a primary faction scowl when they are of a monster race; merchants never worse than dubious; aggro asks with an agnostic deity and consider with the player's, as the legacy zone did; kills apply the `npc_faction_entries` hits with the legacy messages and are saved (the total is kept within ±1500, where the legacy zone stored the limit as the raw value). Merchants (ProcessOP_ShopRequest, ShopPlayerBuy, ShopPlayerSell, UpdateGoods): the first 30 existing items of `merchantlist`, price multiplier 2.5 (sell at 0.4), money taken and given as TakeMoneyFromPP / AddMoneyToPP do, refused when the merchant fights or the player is dubious or worse. Zone regions: the BSP tree of the Lantern export (`ZoneRegions`: water, lava, zone lines) for swimming; the server's climb check does not apply in water. Skills: every cap of Mob::CheckMaxSkill (74 skills by class, up to 50 and beyond); Client::CheckAddSkill on swings (weapon skill, offense), melee damage taken (defense), meditating, channeling through damage and each cast's skill (the legacy zone never raised casting skills; EQMacEmu does), with the client's message; saved in the profile, untrained markers kept. Abilities (ProcessOP_CombatAbility, Taunt, Mend, Hide, Sneak, Forage): kick and bash (the legacy bash did nothing below level 10 from an integer division; the rewrite keeps the fraction), taunt (the NPC turns on the taunter), mend, hide (unseen by NPCs until moving unless sneaking, or attacking), sneak, forage (common foods or the zone's `forage` items), with server-side reuse times. Groups (groups.cpp): up to six, the leader invites, leaving hands the lead over, a group of one is disbanded, groups span zones (kept by the zone server, dropped at camp or disconnection); Group::SplitExp shares the experience by (level + 5) / (sum of levels + 5 × members) among the members in the zone; group spells land on the members in range; group chat. Caster NPCs (NPC::CheckMyOffenseCastStatus, CheckMyDefenseCastStatus, the low-health rescue, `npc_spells` by class and level): debuffs and nukes every 6 s with the legacy chance and spell credits, heals on hurt allies of their faction, a heal on themselves when losing; they stand still while casting; players resist with their buffs only (the legacy clients had no base resists). NPCs flee at low health (NPC::Damage's emergency check, CheckMyFleeStatus: not undead, no ally of their faction nearby, back at a quarter of their health); every entity regenerates each tic from Mob::GetLevelRegen's table (trolls and iksar faster, sitting faster, an NPC's hp_regen_rate when higher). No idle buffing yet. | `CombatFormulasTests` (parity), `SpellRulesTests` (the file's numbers, value formulas, mana, fizzle and resist values), `SpellCastingTests` (nuke, kill, interrupt, heal, fizzle, scribe), `BuffTests` (tics, stacking, bonuses, fading, damage over time, spirit of wolf, saving), `SpellEffectTests` (root, mez, stun, invisibility, bind and gate, zone rules, area spells, summoning), `FactionTests` and `MySqlFactionTests` (standings, special con, kill hits, aggro), `MerchantTests` (prices, coins, refusals, buying, selling), `SkillUpTests` (caps, chances, raised by fighting, saving), `AbilityTests` (kick, bash, taunt, mend, hide, forage), `GroupTests` (invitations, leaders, split experience, group heal), `NpcCastingTests` (nukes, rescue heals), `FleeTests`, end-to-end grouping and group chat, `MeleeTests` (a level 1 rat hits a level 1 shaman 81 % of the time for 1-4), `CombatTests` (kill and respawn, fighting back, reach, death, regeneration), end-to-end over UDP (Tab, auto-attack, blows both ways); played in Unity: a level 1 troll shaman fought a Qeynos rodent. |
| M6 | **Parity for play**: loot, inventory, merchants, factions, quests (Perl replaced by C# scripts or Lua). | A list of classic play scenarios, each automated with the bot client. |
| M7 | Optional **legacy bridge**: accept the Trilogy client on the new server through the legacy protocol. | eqbot's `play` scenario passes against the rewrite. |

## How to run what exists

```sh
cd rewrite
dotnet build && dotnet test
dotnet run --project src/Server -- --port 5999      # login server with the test account (test / test)
dotnet run --project src/Server -- --db "Server=127.0.0.1;User ID=eqc;Password=eqc;Database=eqclassic;SslMode=None"
dotnet run --project src/Cli -- 127.0.0.1 test test --fingerprint <printed by the server> --enter Qtest --zone-seconds 20
dotnet run --project src/Server -- --db "..." --lantern ../build/lantern-work/Exports   # zones use the exported collision meshes
EQC_REWRITE_TEST_DB="Server=127.0.0.1;User ID=eqc;Password=eqc;Database=eqclassic;SslMode=None" dotnet test

tools/lantern/extract.sh ~/eq-client permafrost qeynos2   # from the repo root; needs libgdiplus on Linux
```
