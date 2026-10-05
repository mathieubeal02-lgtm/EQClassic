# Player bots: design study

Status: **design only**. Nothing described here is implemented. This document studies how to add
"fake player" bots to the EQClassic servers: characters that live in the world like real players.
They hunt, sit to regain mana, go back to town, sell, group, and talk on the chat channels. It
compares the two ways to build them, recommends one, and splits the work into milestones that
tests or logs can verify.

Sources studied:

- this repository on `master` (`Zone/`, `World/`, `Common/`, `LS/Login`, `tools/eqbot`);
- [mod-playerbots](https://github.com/mod-playerbots/mod-playerbots), the playerbots module for
  AzerothCore (WoW 3.3.5). Commit `037c014` was cloned read-only into `externals/mod-playerbots/`.
  That path is git-ignored and is not a submodule. Only its architecture was studied: no code is
  copied, and nothing here is a translation of it.

Contents:

1. [What mod-playerbots does](#1-what-mod-playerbots-does)
2. [Moving the model to EverQuest](#2-moving-the-model-to-everquest)
3. [Option 1: server-side bots](#3-option-1-server-side-bots)
4. [Option 2: headless clients](#4-option-2-headless-clients)
5. [Comparison and recommendation](#5-comparison-and-recommendation)
6. [The bot program (recommended design)](#6-the-bot-program-recommended-design)
7. [Chat and the LLM](#7-chat-and-the-llm)
8. [Scale: 50 to 200 bots on a home server](#8-scale-50-to-200-bots-on-a-home-server)
9. [Milestones](#9-milestones)
10. [Risks and rules](#10-risks-and-rules)

---

## 1. What mod-playerbots does

All paths in this section are relative to `externals/mod-playerbots/`.

### 1.1 The engine: triggers, actions and relevance

Each bot runs a small rule engine (`src/Bot/Engine/Engine.{h,cpp}`):

- A **strategy** (`src/Bot/Engine/Strategy/Strategy.h`) is a named bundle of rules. Its
  `InitTriggers` declares pairs "when this trigger fires, queue these actions at these relevance
  values". Example: in `src/Ai/Class/Warrior/Strategy/TankWarriorStrategy.cpp`, "enemy out of melee
  range" queues a charge or a ranged pull at a relevance just above plain movement.
- A **trigger** (`src/Bot/Engine/Trigger/Trigger.h`) is a cheap boolean test, for example "low
  health", "no target" or "has aggro". Each trigger has its own check interval. Triggers can also be
  fired from outside the engine: by a chat command or by a packet the bot received.
- An **action** (`src/Bot/Engine/Action/Action.h`) answers three questions: `isUseful()` (is it
  worth doing now), `isPossible()` (can it be done), and `Execute()`. An `ActionNode` can carry
  three lists:
  - prerequisites, done first (for example "move into range" before "melee");
  - alternatives, tried when the action is impossible (for example devastate falls back to sunder
    armor);
  - continuers, queued after the action succeeds.
- A **value** (`src/Bot/Engine/Value/Value.h`) is a cached computed fact, such as the current tank
  target, the party member to heal, or the attackers list. Triggers and actions share values, so
  each fact is computed only once per tick.
- A **multiplier** (`src/Bot/Engine/Multiplier.h`) rescales relevance. For example
  `ThreatMultiplier` in `src/Ai/Base/Strategy/ThreatStrategy.cpp` zeroes damage actions when the bot
  is close to taking aggro.

Each tick, `Engine::DoNextAction` works in four steps:

1. Evaluate the triggers that are due.
2. Push the actions they fired, plus the strategies' default actions, into a queue sorted by
   relevance. The relevance scale runs from `ACTION_IDLE` 0 to `ACTION_EMERGENCY` 90; "minimal" mode
   only looks at 100 and above.
3. Pop the most relevant entry and run it through its usefulness, multiplier, possibility and
   prerequisite checks.
4. Execute exactly **one** action, then end the tick.

The factories are name-based registries: `NamedObjectContext.h`, `AiObjectContext.h`, and the
`*Context.h` files in `src/Ai/Base/`. Strategies are therefore addressed by strings like `"tank"`,
`"heal"`, `"grind"` or `"food"`, and can be added or removed at runtime from configuration or from
chat.

Bots have **three engines**: combat, non-combat and dead (`enum BotState` in
`src/Bot/PlayerbotAI.h`). `PlayerbotAI::ChangeEngine` switches between them when the bot enters
combat (`src/Ai/Base/Actions/AttackAction.cpp`, `PullActions.cpp`) and when it leaves combat
(`ChooseTargetActions.cpp`). `src/Bot/Factory/AiFactory.cpp` builds each engine with its default
strategies. The non-combat engine gets `nc`, `food`, `loot`, `follow`, `quest`, `chat`, `emote` and
others; the dead engine gets `dead`, `stay` and `chat`.

### 1.2 Tick rate and throttling

- The tick is `PlayerbotAI::UpdateAI`, called from a hook after each player update
  (`src/Script/Playerbots.cpp`).
- Each bot then waits a "react delay" (`ReactDelay=100` ms in `conf/playerbots.conf.dist`). The delay
  is multiplied by 5 in combat and by 10 to 30 out of combat, and each bot gets a 0–200 ms stagger
  so the bots do not all tick together (`PlayerbotAIBase.cpp`).
- `PlayerbotAI::AllowActive` / `AutoScaleActivity` decide whether a bot does anything at all. Bots
  that are in combat, grouped with a real player, or near a real player are always active. Of the
  others, only about 10 % are active at a time, rotating every 30 s. Inactive bots run "minimal"
  ticks every 10 s. A smart-scale mode lowers that 10 % further when the world update gets slow.
- This throttling is how the module claims thousands of bots. The default is 500 random bots.

### 1.3 How bots exist: sessions without a socket

- **Random bots.** `src/Bot/Factory/RandomPlayerbotFactory.cpp` creates their accounts (prefix
  `rndbot`) and characters. `src/Bot/RandomPlayerbotMgr.cpp` then keeps the online count between a
  minimum and a maximum, logging bots in and out in batches (60 bots every 20 s by default).
- **Login.** `PlayerbotHolder::AddPlayerBot` (`src/Bot/PlayerbotMgr.cpp`) creates a `WorldSession`
  with no socket and loads the character through the normal database login path.
- **Server to bot.** Packets the server sends to a bot are intercepted by a hook
  (`OnPacketSent` → `PlayerbotAI::HandleBotOutgoingPacket`), which turns them into triggers.
- **Bot to server.** The bot's actions mostly call server functions directly, and some build
  client packets that `HandleBotPackets` feeds back to the server.
- **Server changes.** This needs a forked AzerothCore core with headless-session support.
- **Alt bots.** `PlayerbotMgr` lets a real player log in their own alts as bots, up to 40.

### 1.4 Roles

Roles come from flags on the active strategies or from the character's talent tree:

- **Role detection.** `PlayerbotAI::IsTank`, `IsHeal`, `IsDps` and `IsMainTank` in
  `src/Bot/PlayerbotAI.cpp`. `AiFactory` maps class + spec to strategy names; a protection warrior,
  for example, gets `tank`, `tank assist`, `pull` and `aoe`.
- **Per-class rules** live in `src/Ai/Class/<Class>/Strategy/`.
- **Shared behaviours** live in `src/Ai/Base/Strategy/`: tank assist, dps assist, threat, pull,
  flee, kite, melee and ranged.
- **Target choices** are values: `TankTargetValue`, `DpsTargetValue`, `PartyMemberToHeal`,
  `GrindTargetValue`, `CcTargetValue` (`src/Ai/Base/Value/`).

### 1.5 Life outside combat

- **Rpg state machine.** `src/Ai/World/Rpg/` (`NewRpgInfo.h`, `NewRpgStrategy.cpp`,
  `NewRpgAction.cpp`) gives each random bot an "rpg" status that it moves through: idle, go grind,
  go to camp, wander near NPCs, do a quest, take a flight, rest.
- **Travel.** `src/Mgr/Travel/TravelMgr.h` and `TravelNode.h` hold a node graph of destinations and
  routes, stored in database tables.
- **Rest.** `src/Ai/Base/Strategy/UseFoodStrategy.cpp` handles eating and drinking.
- **Loot.** `LootNonCombatStrategy.cpp` and `LootAction.cpp`.
- **Vendors.** `SellAction.cpp` and `RepairAllAction.cpp`.
- **Levels and gear.** `src/Bot/Factory/PlayerbotFactory.cpp` and `RandomBotLevelMgr.cpp` randomise
  them. `RandomPlayerbotMgr` also teleports bots to level-appropriate places.
  Bots are partly simulated: they are teleported, given gear, and repaired for free.

### 1.6 Chat

- **Commands.** Chat sent to a bot goes through `PlayerbotAI::HandleCommand` →
  `ExternalEventHelper::ParseChatCommand` (`src/Bot/Engine/ExternalEventHelper.cpp`). That fires the
  chat trigger with the same name, which `ChatCommandHandlerStrategy.cpp` maps to an action. So a
  command is just another trigger.
- **Ambient talk.** `SayAction.cpp`, `SuggestWhatToDoAction.cpp` and `src/Mgr/Text/PlayerbotTextMgr.cpp`
  pick lines from template tables in the database (`ai_playerbot_texts`, with per-line chances).
- **No LLM.** A search of `src/` and `conf/` found no LLM integration.

### 1.7 What to take, and what not to

| Take (as ideas) | Leave |
|---|---|
| Trigger → action rules with relevance; one action per tick | The class hierarchy and macros: our bots are much smaller |
| Separate combat / non-combat / dead rule sets | Teleporting bots, free repairs and gear randomisation: our bots must play by the rules |
| Strategies as named bundles, switchable at runtime (role = strategies) | WoW concepts: talents, quests, flight paths, battlegrounds, LFG |
| Values cached per tick, shared by rules | A forked core: we will not fork our server |
| Activity throttling: idle bots tick rarely | |
| Commands are triggers too | |

---

## 2. Moving the model to EverQuest

EverQuest 1999–2001 is not WoW. The differences change what the bots must do.

### 2.1 Roles by class

| Role | Classes (Trilogy) | What the bot must do |
|---|---|---|
| Tank | Warrior; Paladin and Shadow Knight as hybrids | Hold aggro (taunt, bash or kick, and for hybrids stun or lifetap), never sit in combat |
| Healer | Cleric; Druid and Shaman as secondary healers | Watch group HP (`OP_HPUpdate` and the group window), heal under a threshold, meditate between fights; a shaman slows |
| Melee DPS | Monk, Rogue, Ranger, Bard (melee + songs) | Assist the tank's target, stay behind the mob (rogue backstab), do not pull aggro |
| Caster DPS | Wizard, Magician (pet), Necromancer (pet, DoTs, lifetaps) | Nuke on assist only after the tank has aggro, otherwise "root and rot" or pet tank when solo |
| Crowd control | Enchanter (mez, slow, haste, clarity), Bard | Mez adds, buff mana regeneration |
| Puller | Monk (feign death), Bard, Ranger, or the tank | Bring one mob to the camp; fall back to the camp, never fight at the spawn |
| Support | Druid/Wizard/Shaman (SoW, ports, buffs) | Buff between fights, port to bind or rest zones |

In mod-playerbots terms, each role is a set of strategies (`tank`, `heal`, `assist`, `pull`, `cc`,
`pet`). The bot's class and level choose the default set, as `AiFactory` does by talent spec. There
are no talents in the Trilogy era, so class is enough.

### 2.2 EverQuest-specific behaviours

- **Camping.** Players do not roam a quest hub; they pick a camp (a spot near spawn points), pull
  to it, and wait for respawns. A bot's non-combat state machine is therefore: *pick camp → walk
  there → pull/fight → rest → repeat → leave (bags full, out of food, bad faction, level too high)*.
  Camps can come from the `spawn2` table: spawn points of NPCs whose level matches the bot's,
  clustered by distance.
- **Resting means sitting.** HP and mana come back slowly. Sitting speeds this up; for mana, sitting
  is how a caster meditates (`Mob::DoManaRegen`, `Zone/Source/mob.cpp`; sitting is
  `OP_SpawnAppearance`, `Client::ProcessOP_SpawnAppearance`). Sitting also changes aggro.
  - Casters sit whenever mana is under about 90 % and nothing is fighting.
  - Everyone sits under about 70 % HP.
  - Bots stand up on aggro.
  - Food and drink only prevent the penalties; they do not heal.
- **Con colours.** Consider (`OP_Consider`) gives the NPC's faction standing and level colour. Bots
  hunt blue/white and avoid red, and they never attack an NPC whose faction is better than dubious:
  killing guards turns a city hostile.
- **Faction.** Kills change faction (`Zone/Source/faction.cpp`). A bot whose faction with its home
  city drops too far must stop killing those NPCs. The rule is simply: never attack NPCs that con
  indifferent or better.
- **Death and corpse runs.** On death the character respawns at its bind point with nothing; its
  gear stays on its corpse (`Zone/Source/PlayerCorpse.cpp`). A bot must remember where it died, go
  back, loot its corpse (`OP_LootRequest`), and give up after a few tries (it can ask for help on
  `/ooc`). Experience is lost from level 6 onward. Early milestones can simply re-equip and log it.
- **Zoning.** Each zone is a separate process. Leaving a zone is a full reconnection: zone line →
  `OP_ZoneChange` → World → new zone server (`Client::ProcessOP_ZoneChange`,
  `Zone/Source/client_process.cpp:987`). Zone lines come from `zone_points` and the `Maps/` data.
- **Loot.** There is no auto-loot. The bot opens the corpse and takes items one by one, decided by
  item value and the free space in its bags.
- **Selling.** Merchants buy at a fixed ratio (`ShopPlayerSell`, `client_process.cpp:3940`). A bot
  sells its vendor trash when bags are full or every N kills, then goes back to camp.
- **No quest hubs, no auto-routes.** Questing is rare and hand-in based. Bots can skip quests
  entirely, or at most do classic hails and hand-ins later. They level by camping.
- **Groups.** EQ groups are 6 people with experience split (`Group::SplitExp`,
  `Zone/Source/groups.cpp`). Bots form groups with "LFG" chat. In the group, the leader or main
  tank is the assist target, and the puller is designated. Real players can invite bots with the
  normal `/invite`, and the bot accepts if the level gap allows.

---

## 3. Option 1: server-side bots

A bot is an entity inside a zone server process, and its AI runs in that zone's loop.

### 3.1 What the zone has

The class tree is `Entity` (`Zone/Include/entity.h`) → `Mob` (`mob.h`) → `NPC` (`npc.h`) and
`Client` (`client.h`).

NPC AI:

- `NPC::Process` (`Zone/Source/npc.cpp:678`) is a set of timer-gated checks:
  - target choice every 200 ms (`CheckMyTargetStatus`);
  - aggro scan every 1.25 s (`CheckMyAgroStatus` → `EntityList::AddHateToCloseMobs`,
    `Zone/Source/NpcAI.cpp`);
  - line of sight every 2.25 s;
  - offensive spells every 6 s and defensive spells every 3 s;
  - fleeing, going home, roam boxes, `grid_entries` waypoints, and walking every 200 ms
    (`CheckMyWalkingStatus` → `MoveTowards`).
- The hate list is `HateList` (`Zone/Source/hate_list.cpp`).
- Spells come from `Database::LoadNPCSpells(class, level, …)` (`Common/Source/database.cpp`).
- Zone geometry is `Map` (`Zone/Source/Map.cpp`: `FindBestZ`, `LineIntersectsZone`), plus path
  nodes and grids from `Maps/`.

Pets are the closest existing thing to a bot: an `NPC` with an owner that follows, assists and
guards (`Mob::MakePet` in `Zone/Source/spells.cpp`, `NPC::CheckMyOwnerStatus` in `npc.cpp`).

### 3.2 Two ways to make a server-side bot

**(a) A `Client` without a socket.** This gives the bot all player rules for free: experience,
groups, loot, merchants, spells, the profile save (`Client::Save`). But `Client` is tied to its
network session:

- It owns its `EQPacketManager`. `QueuePacket` (`Zone/Source/client.cpp:471`) is not virtual and has
  about 200 call sites. When packets wait unacknowledged, it busy-waits with `Sleep(5)`.
- Resends past 15 tries close the session (`Common/Source/EQPacketManager.cpp`), and a 90 s timeout
  resets only on received data.
- Entry into the zone is a five-step handshake (`Process_ClientConnection1..5`).
- So every outgoing path would need an `isBot` short-circuit. Every incoming action (move, target,
  cast, loot, sell) would have to be either faked as a packet or written as a direct call that
  duplicates the opcode handlers in `client_process.cpp` (6816 lines).

**(b) An `NPC` subclass that looks like a player.** `Spawn_Struct.NPC` (`Common/Include/eq_packet_structs.h`)
is 0 for players. A `BotNPC::FillSpawnStruct` override can set it and fill the equipment, as
`Client::FillSpawnStruct` (`client.cpp:1766`) does, and the client will then draw a player model.
But the server treats players by type, not by flag: `IsClient()` appears 406 times in `Zone/` and
`CastToClient()` 496 times. That code covers experience, faction, groups (`Group` members are
`Client*`), tells, `/who`, trades and looting. So an NPC bot would not get experience, could not
join a real player's group, and would not show up in `/who` or receive tells, unless each of those
places learned about bots.

### 3.3 Cross-zone, persistence, groups, chat

- **Zones.** A bot lives in one process, and moving it to another zone means serialising it through
  World like a player.
  - World knows clients through `ServerOP_ClientList` (`EntityList::UpdateWho`), so bots would need
    entries there too.
  - `Zone::Process` shuts an empty zone down after 30 s, and `entity_list.Process()` only runs when
    `numclients > 0` (`Zone/Source/net.cpp`). A zone with only bots would freeze or shut down unless
    bots count as clients.
- **Persistence.** `character_` holds the `PlayerProfile_Struct` blob (`Database::GetPlayerProfile` /
  `SetPlayerProfile`, `Common/Source/database.cpp:829/875`). Option (a) reuses it; option (b) needs
  its own save code.
- **Groups.** These are kept by the zone (`groups.cpp`) and synchronised through World
  (`ServerOP_GroupRefresh`). They are `Client*`-only.
- **Chat.** Say, shout, OOC and auction are zone-local in `EntityList::ChannelMessage`
  (`EntityList.cpp:226`). Tells and guild chat go through World
  (`World/Source/zoneserver_process.cpp`). A server-side bot would hook `ChannelMessageReceived`
  (`Zone/Source/Client_Messaging.cpp:83`).

### 3.4 Assessment

Option 1 would be the most CPU-efficient: no UDP, no encryption, no client-side copy of the world.
But it touches the hottest and least-tested code in the tree:

- the packet path;
- `Client` state;
- the type checks spread over `Zone/`;
- the `numclients` gates.

That code is Windows-only, built with MSVC and run under Wine here. It is tested only through CI's
e2e job, and every change risks breaking real players. Bot code would also be tied to the legacy
zone server, which the `lantern-rewrite` branch plans to replace.

---

## 4. Option 2: headless clients

A bot is a separate program that logs in with an account and password like the real client,
speaks the Trilogy UDP protocol, and plays through the same packets a player sends.

### 4.1 What `tools/eqbot` already does

`tools/eqbot/eqbot.cpp` (1157 lines) and `EqSession.{h,cpp}` already form a Linux client that
reuses the server's own UDP layer (`Common/Source/EQPacketManager.cpp`: sequencing, acks, resends,
fragments). It already does the following:

- **Login.** Credentials are DES-encrypted like the client's; the bot reads the server list and the
  session key.
- **World.** It reads the character list, creates characters from a captured packet
  (`charcreate_template.inc`), and enters the world.
- **Zone entry.** It runs the whole handshake (`EnterZone`): the player profile decrypted and
  inflated (`DecodeProfile`), the zone header, and the spawns (`DecodeSpawns`, `ParseSpawn`: id,
  name, NPC flag, level, position).
- **Play.** It moves with `OP_ClientUpdate` (heading computed by `HeadingTowards`), targets
  (`OP_ClientTarget`), turns auto-attack on, follows a moving NPC from its `MobUpdate` packets,
  counts hits (`OP_Action`) and deaths (`OP_Death`), speaks (`Say`), moves items (`MoveItem`), and
  uses GM commands (`#loc`, `#si`, `#kill`).
- **Tests.** `eqbot test` turns these into `[ OK ]`/`[FAIL]` steps. CI's `e2e` job
  (`tools/eqbot/ci-e2e.sh`) runs them against the Release servers under Wine and MariaDB.

### 4.2 What is missing for autonomous play

1. **Many sessions in one process.** `EqSession::Poll`/`WaitFor` block one session at a time.
   - Bots need a non-blocking loop: one UDP socket per bot (200 sockets is fine with
     `poll`/`epoll`) and a `Pump()` per session called from a single event loop.
   - `EQPacketManager` and `Timer::SetCurrentTime` are single-threaded, which suits one loop
     thread, or one process per 50 bots.
2. **A world model.** Each bot keeps a table of spawns and updates it from:
   - new spawns, `OP_DeleteSpawn` and `MobUpdate` positions;
   - `OP_HPUpdate` and mob health;
   - `OP_ManaChange` and `OP_ExpUpdate`;
   - loot and merchant windows;
   - its own inventory, from the profile and item packets.

   The parsing exists for spawns and the profile and must be extended to the other opcodes
   (`Common/Include/eq_opcodes.h`).
3. **Geometry.** The server does not check movement: `Client::ProcessOP_ClientUpdate`
   (`Zone/Source/client_process.cpp:2130`) takes the client's x/y/z as given. This trust cuts both
   ways:
   - The bot can never "get stuck" on the server's side.
   - It must still move believably: run speed, ground height, no walking through walls, or real
     players will see bots slide through houses.

   `Zone/Source/Map.cpp` only needs standard headers and `moremath.h`, so it can be linked into the
   bot (`tools/maps/check_grids.py` already reads the same `.map` files on Linux). That
   gives `FindBestZ` for height and `LineIntersectsZone` for walls and line of sight.

   For paths, the bot can reuse what NPCs walk:
   - `grid_entries` waypoints are known to be walkable (`check_grids.py` checks them against the
     maps);
   - the `Maps/` path nodes;
   - zone points.

   Maps are 1–5 MB for most Trilogy zones (`qeynos2.map` 1.2 MB, `gfaydark.map` 4.5 MB) and are
   loaded once per zone per bot process.
4. **Static knowledge.** Item values, merchant lists, zone points and camp candidates (`spawn2` by
   level) are read **read-only** from MariaDB, or from a JSON export made by a script, when the bot
   process starts. Bots never write to the database.
5. **Zoning.** eqbot has never zoned. The client side of `OP_ZoneChange` → World → new zone must be
   reproduced (see the risks in section 10).
6. **No GM powers.** `eqbot test` needs a GM account (`#si`, `#kill`, `#loc`). Autonomous bots must
   run on ordinary accounts (status 0) and do everything through normal play.

### 4.3 Server-side limits a fleet of clients will hit

- **One login per account.** Login refuses an account that is already in `active_accounts`
  (`Database::AccountLoggedIn`, `LS/Login/logindatabase.cpp:510`). So there must be one account per
  bot: 200 bots means 200 accounts.
- **Concurrent logins from one IP can mix up accounts.** World inserts an `active_accounts` row
  keyed by IP when a new UDP client arrives (`LogAccountInPartI`, `World/Source/net.cpp:163`).
  Later it fills in the login account with
  `UPDATE active_accounts … WHERE ip = … AND lsaccount = 0` (`LogAccountInPartII`,
  `Common/Source/database.cpp`). Two bots from 127.0.0.1 logging in at the same moment can match
  each other's rows. The bot fleet must therefore serialise logins: one login at a time, about 2 s
  apart, so 200 bots log in over about 7 minutes. Fixing the query to use the session's own row is
  a possible server fix later; it is not required.
- **Zone processes.** One process per zone. Bots in N zones need N zone processes, either started
  ahead of time (`runtime/Boot5zones.bat`, `run-wine.sh`) or by World's autoboot
  (`autobootzones=true`, at most `autobootzones_max` = 20 by default, `World/Source/ZSList.cpp`).
  World waits up to 90 s for a zone to boot.
- **No player cap was found** in World or Zone. `Zone::AggroLimitReached` (more than 80 aggroed NPCs
  per zone, `zone.h`) is the first gameplay limit a crowded zone hits.

### 4.4 Value as a load test

Headless bots are real clients, so they load every server path a player loads:

- the UDP layer and its resend logic;
- the profile encryption on zone-in;
- the spawn fan-out (each moving client's update is sent to every other client in range);
- `Client::Save` and the MySQL writes;
- World's login handshake and zoning.

This is exactly what CI's e2e job cannot exercise with one bot. Several bugs fixed this year (acks,
Error 1018 after logout, disconnect spam) were of the "only with real sessions" kind. Server-side
bots skip every one of those paths, so they would hide such bugs instead.

---

## 5. Comparison and recommendation

| | Option 1: server-side | Option 2: headless clients |
|---|---|---|
| Server changes | Deep: `Client`/`NPC`, the packet path, `IsClient` checks, `numclients`, World client list | **None** for milestones 1–4 |
| Risk to real players | High: shared hot code | Low: the bots go through the same doors as a player |
| Build and debug | MSVC, Windows, runs under Wine | Linux g++, gdb, sanitizers, runs in CI (already built there) |
| Player rules (exp, groups, loot, faction) | Option (a): free but invasive; option (b): must be rewritten | **Free**: the server enforces them |
| Groups with real players | Hard (`Group` holds `Client*`) | Free (`/invite`, `OP_GroupInvite`/`OP_GroupFollow`) |
| Seen in `/who`, receives tells | Needs World changes | Free |
| Cross-zone | Serialise through World, new code | The normal zoning path (to implement on the client side) |
| CPU per bot | Lowest | Higher: UDP, encryption, a world copy (estimated in section 8) |
| Geometry | The zone's `Map` | The same `Map.cpp` linked into the bot |
| Load-testing value | None | High |
| Survives the rewrite | No: tied to the legacy zone | Yes, through the rewrite's planned legacy bridge (M7), and the AI design ports to the rewrite's C# client core |
| Licence | Same (GPLv2) | Same (GPLv2) |

### Recommendation: headless clients (option 2), built on `tools/eqbot`

The three main reasons:

1. **It never touches the servers.** Milestones 1–4 need zero changes in `Zone/`, `World/` or
   `Common/`. Real players' code paths stay as they are, and the bots are "feature-flagged" by
   simply not running the bot program. Option 1 needs `isBot` branches in the packet path and in
   hundreds of `IsClient()`/`CastToClient()` sites of the least-tested code in the tree.
2. **The server already enforces every player rule.** Experience, group split, faction, loot
   rights, merchants, corpses and chat routing all apply to the bots because they are players. The
   bots cannot cheat by accident, and a real player can `/invite` a bot like anyone else.
3. **It reuses what works and doubles as a load test.** eqbot already logs in, enters zones,
   decodes the profile and spawns, moves and fights on CI. Fifty bots are also the load test the
   servers have never had: concurrent logins, zone fan-out, saves. And the bot code is Linux C++
   that does not die with the legacy zone server.

The price is CPU and bandwidth (section 8: affordable at 200 bots) and client-side geometry. The
geometry comes from `Zone/Source/Map.cpp` linked in.

Option 1 remains open for a later, narrow use, such as a merchant or guide NPC that chats through
the LLM. That fits the existing Perl quest system (`EVENT_SAY`) better than a fake player does.

---

## 6. The bot program (recommended design)

A new program, `tools/eqbot/` growing a `bots` command or a sibling `tools/botd/`, built like
eqbot on Linux and linked with `Common/Source/EQPacket*.cpp`, `Fragment*.cpp` and
`Zone/Source/Map.cpp`.

```
botd --config bots.ini
  ├── Fleet: login scheduler (1 login at a time), bot list, accounts from bots.ini
  ├── EventLoop: poll() over all bot sockets, 50 ms timer tick
  └── Bot (one per character)
        ├── Session: EqSession made non-blocking (Pump, Send)
        ├── World model: self (profile, HP, mana, inventory), spawns, group, chat inbox
        ├── Body: move-to with Map::FindBestZ, LOS, path over grid/node graph, sit/stand, target,
        │         attack on/off, cast (OP_CastSpell), loot, merchant, group accept, say/ooc/tell
        └── Brain: three rule sets (combat / idle / dead), strategies per role,
                  triggers + actions with relevance, one action per tick
```

### 6.1 Brain

The brain follows the shape of mod-playerbots, but is far smaller and written from scratch:

- A **trigger** is a pure function of the world model. Examples: `LowHp(70)`, `LowMana(30)`,
  `HasAggro`, `GroupMemberHurt(60)`, `NoTarget`, `TargetInRange`, `BagsFull`, `Dead`,
  `ChatAddressed`.
- An **action** has `useful()`, `possible()` and `run()`, and returns *done*, *running* or *failed*.
  Examples: `Sit`, `Stand`, `PickCampTarget`, `Pull`, `Engage`, `CastHeal`, `LootCorpse`,
  `GoToMerchant`, `SellJunk`, `ReturnToCamp`, `AcceptInvite`, `Reply`.
- A **strategy** is a table of `trigger → [(action, relevance)]`. A bot has a role set chosen by
  class and level, e.g. `{tank, melee, rest, loot, camp}` for a warrior or
  `{heal, rest, follow-assist}` for a cleric in a group.
- Each tick (every 250 ms in combat, 1 s out of combat, 5 s when idle and far from any real player),
  the brain queues the fired actions and runs the most relevant possible one. A running action,
  such as a move or a cast, keeps the bot until it finishes or a higher relevance pre-empts it.
- **Every decision is logged.** One line per bot per action change:
  `bot=Qbarl zone=qeynos2 state=combat action=Pull target=a_rat(312) rel=60`. These lines are what
  the milestones are verified on.

Combat logic per class (which spell when, mana thresholds) is inspired by EQEmu's bot code at most
(see section 10). It must be written for our rules, `CombatFormulas` and `spdat.eff`.

### 6.2 Configuration and accounts

`bots.ini` lists, for each bot: the login account and password, the character name, class, home
zone, role preference, and talk level. Each bot has its own login account, created by a separate
setup script that is reviewed before it runs. The bot accounts:

- have a recognisable prefix (`bot_*`);
- have status 0 (never GM);
- can be told apart from players in SQL and in `/who` (for example, the guild "Wanderers" or an
  account flag) when wanted.

---

## 7. Chat and the LLM

### 7.1 Principle

The LLM writes **words, never decisions**. Combat, movement, looting and grouping are decided by
the rule engine and run on its tick. A blocked or slow LLM call must never stall a bot: the chat
layer is asynchronous, and the bot keeps playing while a line is pending. If a reply is not ready
within 10 s, it is dropped.

### 7.2 Where it plugs in

- **Input.** Each bot's world model receives `OP_ChannelMessage` (say within 100 units, shout,
  OOC and auction in the zone, tells, group). A `ChatAddressed` trigger fires when the bot is named,
  sent a tell, or spoken to in group.
- **Templates first.** Most lines need no model. Templates with variables cover LFG ("LFG 12 cleric
  in Blackburrow"), "inc", "oom, medding", "thanks for the group", and death and level-up
  reactions. This is the `PlayerbotTextMgr` idea: a text table with chances.
- **The LLM** is used only for:
  - answering a real player's tell or say addressed to the bot;
  - an occasional flavour line on OOC (at most one per bot per 20 minutes);
  - reactions to rare events (a named mob, a death).
- **The request** contains a short persona (name, race, class, level, home city, mood), the zone,
  the last few lines heard, and the event. It asks for one line of at most 120 characters in the
  2001 player register, with no out-of-game references. The prompt is fixed and cacheable; only the
  event part changes.
- **A separate `ChatService` thread** owns the HTTP client, a queue, and the rate limiter, and
  returns the text to the bot through a lock-free inbox. The bot then sends `OP_ChannelMessage`
  itself.

### 7.3 Model, rate limits and cost

- **Hosted small model.** For example `claude-haiku-4-5` at $1 / $5 per million input / output
  tokens (API price list as of 2026-09).
  - Per call: about 600 input tokens and 40 output tokens, so about $0.0008.
  - With a global cap of 60 calls per hour: about 1,400 calls a day, about **$1.20/day**.
  - At a cap of 300 per hour (lively): about $6/day.
  - Caching the fixed prompt lowers the input part.
- **Local model.** For example a 3–8B instruct model through llama.cpp: free per call, but on this
  i5-7600K (4 cores, no GPU) it would take 5–20 s per line and compete with the zone servers for
  CPU. It is viable only with a low cap (about 20/hour), on another machine, or with a GPU.
- **Rate limits**, all configurable:
  - global calls per hour;
  - per bot, one call per 2 minutes;
  - per channel, at most 1 bot line on OOC per 30 s, with bots never answering each other
    through the LLM (bot-to-bot uses templates);
  - a daily budget in dollars that switches to templates when reached.

### 7.4 Safety filtering

- **Output filter.** Length cut; a word blocklist (the server's own `name_filter` table plus a
  list); no URLs; no mention of AI, models, prompts or real-world topics. On failure, the line is
  discarded and nothing is sent, or a template is used.
- **Input handling.** Player text goes into the prompt only as quoted data. The system prompt says
  to ignore instructions inside it, and the bot never executes anything from chat apart from the
  fixed command words.
- **Fixed command words** come before the LLM: `invite`, `follow`, `assist`, `camp`, `sit`. Like
  mod-playerbots' chat-command triggers, they come from the group leader only.
- **Logs.** Every LLM line is logged with its prompt hash, so complaints can be traced.
- **Kill switch.** `chat.llm=off` in `bots.ini`, and a per-bot mute.

---

## 8. Scale: 50 to 200 bots on a home server

Measurements on this machine (i5-7600K, 4 cores, 15 GB RAM), with the servers running under Wine
and idle:

| Process | RSS | CPU |
|---|---|---|
| zone.exe (each of 13) | 50–67 MB | 6–9 % of a core each (`Sleep(1)` main loops, `Zone/Source/net.cpp`) |
| all 13 zones | 724 MB | 93 % of a core |
| world.exe | 35 MB | 25 % of a core |
| login.exe | 27 MB | 9 % of a core |

The idle cost is dominated by **the number of zone processes**, not by the number of players.
Bots should therefore be concentrated in a dozen hunting zones (newbie zones around the cities,
Blackburrow, Crushbone, Unrest, the Commons and the Karanas) instead of spread over 60 zones.

Estimates for the recommended option (to be measured at milestone 1, see section 9). None of the
numbers in this list are measured yet:

- **Bot process, CPU.**
  - Per bot: the UDP and ack work for about 10–40 packets/s received (spawn position batches,
    HP, chat), plus a brain tick of 1–4 Hz.
  - Estimate: 0.1–0.3 % of a core per bot, so **0.2–0.6 core for 200 bots**.
  - Encryption and inflation happen only on zone-in.
- **Bot process, memory.**
  - About 1–3 MB per bot (spawn table, packet queues, profile), so 200–600 MB.
  - Plus about 2–5 MB per loaded zone map, about 50 MB for a dozen zones.
- **Bandwidth (loopback).**
  - The zone sends each client position updates for the mobs and players around it. Estimate:
    2–10 KB/s per bot in a busy zone, so **0.4–2 MB/s for 200 bots**.
  - Upstream is small: a position update of 15 bytes every 0.5–1 s while moving.
  - Negligible on loopback; on a LAN it stays under 20 Mbit/s.
- **Zone servers.** The real cost is the fan-out: each moving entity's update goes to every client
  in range, so the work grows as clients² in a zone. Rules of thumb:
  - at most **25–30 bots per zone**;
  - at most 15 zones with bots;
  - add 1–2 cores of zone CPU at 200 bots on top of the idle 1 core.

  This is the number to measure first, with milestone 1's bots simply walking in a single zone.
- **MySQL.** `Client::Save` runs on zoning, camping and periodic saves, each a profile blob of a
  few KB. 200 clients add a few writes per second at most, so MariaDB does not notice.
- **Verdict.** 50 bots fit comfortably on this machine next to the servers. 200 bots probably fit
  but would leave little headroom on 4 cores. Run the bot process on another machine on the LAN, or
  cap at about 100, until milestone 1's measurements say otherwise.

---

## 9. Milestones

Each milestone is merged only when its checks pass.

- **Automated checks** are `eqbot`-style `[ OK ]`/`[FAIL]` steps run by a script against the local
  servers, and where possible in CI's `e2e` job.
- **Explicit logs** are the decision lines of section 6.1. A small script, `tools/eqbot/botlog.py`,
  greps and counts them.

None of milestones 1–4 changes server code.

### M1: a bot connects and moves (and the fleet measures itself)

- **Code.**
  - Make `EqSession` non-blocking.
  - Write the event loop, the login scheduler (one at a time), and `bots.ini`.
  - Build the world model for spawns, self and HP.
  - Link `Map.cpp`; add `MoveTo` with ground height, line of sight, and paths on the grid/node graph.
  - Add `Sit` and `Stand`.
- **Checks.**
  - `botd --once 1` logs in, enters `qeynos2`, walks to 3 points from `grid_entries`, and logs out
    cleanly (no Error 1018 on the next run).
  - Each arrival is within 5 units. The ground height reported by `#loc` from a GM observer bot
    stays within 3 units of `FindBestZ`.
  - A scale run: N = 10, 25, 50 walking bots in one zone for 10 minutes. The run logs zone and bot
    CPU and RSS, and packets per second. It passes if there are no disconnects and the zone's
    `SetAverageProcessTime` stays under 50 ms.
- **Output.** The measured numbers replace the estimates in section 8.

### M2: hunts alone (pull, fight, rest and meditate, loot)

- **Code.**
  - World model: con colour from level, HP and mana from packets, corpses, loot window.
  - Rule sets: combat (engage, melee, a class nuke/heal for casters, flee at low HP), idle (pick a
    target, rest when HP < 70 % or mana < 90 %, loot, return to camp), dead (log, wait, re-login at
    bind).
  - Camp selection from `spawn2` by level.
- **Checks.**
  - A level-1 warrior bot and a level-1 caster bot in `qeynos2` play for 30 minutes.
  - The logs must show at least 10 kills each, `Sit` between fights, no attack on an NPC conning
    indifferent or better, at least one loot, experience gained (`OP_ExpUpdate`), and no stuck bot
    (no position change for over 2 minutes outside rest).
  - The test fails on any guard aggro.

- **Status (2026-10-04).** Done in `eqbot hunt` (`tools/botd/README.md`), in `qeytoqrg` rather
  than `qeynos2` (the level-1 prey is outside the city). A human warrior and a human cleric pass the
  checks over 30 minutes (warrior 37 kills and 1 death, cleric 10 kills and 9 deaths). Melee plus a self-heal for clerics; the camp
  is the place of the first login, not yet chosen from `spawn2`; the dead go back to their corpse.

### M3: groups with bots and players

- **Code.**
  - Accept an invite (`OP_GroupInvite` → `OP_GroupFollow`); a leader bot invites.
  - Roles: tank, healer, assist, puller. Assist on the leader's target, the healer heals at 60 %,
    and the puller brings one mob to camp.
  - The fixed command words from the leader (`follow`, `assist`, `camp`, `sit`).
- **Checks.**
  - Three bots (warrior, cleric, wizard) form a group and hunt for 30 minutes. The logs show
    shared kills with split experience (each member's `OP_ExpUpdate` after a group kill), heals cast
    on the tank, and no member pulling its own mob.
  - A real player or a GM eqbot session sends `/invite` to a bot, and the bot joins and follows.

- **Status (2026-10-05).** Done in `eqbot hunt` (`EQBOT_INVITE`, `EQBOT_ROLE=member`, `fleet.sh
  group`, `tools/botd/README.md`). A warrior leads a cleric and a wizard in `qeytoqrg` for 30
  minutes: every kill was shared (29 of 29 in one run, 13 of 13 in the last; each member's
  `OP_ExpUpdate` within 5 s), the wizard assists with `/assist` and nukes once the tank holds the
  mob, a member attacked calls `help` and the leader comes, no member pulled. The cleric heals
  whoever drops under 60 %: the level-5 tank never did against that prey, so its heals went to the
  wizard. Another account (an eqbot GM session) invited a member bot, which joined and followed.
  The zone got fixes on the way: a grouped player's solo kill is split with its group; a group
  disbanding while its members drop no longer corrupts the entity list (the zone hung); zone lines
  load the target map once (a player standing in one reloaded and leaked it at every update); null
  dereferences in `OP_GroupFollow`, `OP_AssistTarget` and `Group::DelMember`. Release builds now ship
  `zone.pdb`. Still open: one more hang seen once after bots looted many of their own corpses, not
  reproduced since (the PDB is there for the next time).

### M4: talks

- **Code.** The chat inbox and `ChatAddressed` trigger; templates (LFG, inc, oom, thanks, level-up);
  `ChatService` with an LLM backend, rate limits, filters and budget; `chat.llm` on/off.
- **Checks.**
  - With `chat.llm=off`: a test bot sends a tell, and the bot answers with a template within 5 s.
    An LFG bot posts on OOC at most once per configured interval.
  - With a stub LLM server (a local HTTP mock in the test): a reply comes back and passes the
    filter, and a reply with a URL or a blocked word is dropped (logged as `chat=filtered`). The
    per-hour cap is respected over a 10-minute burst.
  - A manual check with the real model is the only step not automated.

### Optional later milestones

- **M5: going back to town and selling.** Bags full → walk to the zone's merchant (`merchantlist`),
  sell items by value, buy food and drink, return to camp. Check: copper increases and bag slots
  are freed, both in the logs.
- **M6: zoning.** Walk to a zone point, client-side zone change through World, continue in the new
  zone. Check: a bot travels Qeynos → Qeynos Hills → Blackburrow and back, with the zone names
  logged.
- **M7: deaths and corpse runs.** Go back to the corpse and loot it; ask for help on OOC after 2
  failures.
- **M8: a living world.** Bots log in and out on a schedule (evening peaks), level up and move to
  harder zones, and rotate activity like mod-playerbots' `AllowActive` (idle bots far from any real
  player tick every 10 s).

---

## 10. Risks and rules

### Rules

- **Never break current behaviour.** The bots live outside the servers. A server change, if one
  ever becomes necessary (such as fixing the `active_accounts` login race), is a separate branch
  with its own e2e test, and the bots must keep working without it.
- **Feature-flagged.** The bots run only when `botd` runs, and `bots.ini` can disable chat, the LLM,
  or individual bots. No bot logic goes into the servers.
- **Bot accounts are separate from players.** They have a `bot_` prefix and status 0, and are
  created by a reviewed script, never by the servers. Bots never write to the database directly;
  everything goes through the game.
- **Licences.**
  - Our code is GPLv2. mod-playerbots is GPL-2.0 and used as **architecture inspiration only**:
    its code is never copied or translated, and it is not committed (`externals/mod-playerbots/`
    is git-ignored).
  - EQEmu's bot code (GPL) may inspire class combat logic only (which spells, when).
  - EQMacEmu is GPLv3 and is **never copied**, as the project already rules for combat formulas.
- **LLM.** Words only; asynchronous; capped by budget; filtered; can be switched off.

### Risks

| Risk | Mitigation |
|---|---|
| Concurrent logins from one IP mix up `active_accounts` rows (section 4.3) | One login at a time, 2 s apart; a later server fix of the query on its own branch |
| The zone fan-out makes 30+ clients in one zone slow | Measured in M1; a per-zone bot cap in `bots.ini` |
| Bots walking through walls or floating, visible to players | `Map.cpp` ground height and line of sight; paths on NPC grids, which `check_grids.py` already validates; M1 checks |
| The client side of zoning is unknown (eqbot never zoned) | M6 is optional and comes after the rest; capture a real client's zoning with `EQBOT_RAW`-style dumps first |
| Movement is trusted by the server, so a buggy bot can "teleport" | Speed and step limits enforced in the bot's `Body`; logged when exceeded |
| Bot behaviour annoys players (kill stealing, OOC spam) | Never engage a mob already on another player's hate list (its HP < 100 % and not ours); chat caps; per-bot mute |
| Wine zone processes are the CPU bottleneck | Bots concentrated in about 12 zones; the bot process can run on another machine |
| LLM cost or misbehaviour | Daily budget, templates fallback, filters, logs, kill switch |
| Rewrite migration | The bots only depend on the Trilogy protocol; the rewrite's legacy bridge (M7 of `docs/architecture-rewrite.md` on `lantern-rewrite`) would accept them unchanged |

### Not verified in this study

- The client side of zoning (section 4.2, item 5): not read in detail.
- All CPU, memory and bandwidth numbers for the bots: estimates. The zone and World numbers are
  measured, but only idle.
- Whether `Map.cpp` compiles as is with g++: from its includes it should, but it was not tried.
- Whether group invites between two eqbot sessions work end to end on the legacy server.
