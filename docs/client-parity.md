# Unity client — parity with the Trilogy client

What the 2001 EverQuest Trilogy client does, and where the Unity client (`unity/`, logic in
`rewrite/src/ClientCore`) stands. The goal is to replace the old client feature for feature; this
list is the plan, in the order of the priority column (1 first). Server-side rules come with each
feature (combat, spells, loot...) and are tracked in `docs/architecture-rewrite.md`.

Status: **done**, **partial** (usable, details missing), **todo**.

## World and rendering

| Feature | Trilogy client | Status | Priority |
|---|---|---|---|
| Zones | Every zone of the install (s3d/wld) | Partial: zones imported with Lantern; a zone that is not imported shows a message | 1 |
| Placed objects | Trees, lamps, furniture | Done (without per-instance vertex colours) | — |
| Doors | Click to open, lifts, teleports | Done (animation approximated per open type) | — |
| Characters | Race/gender models, textures, size | Partial: models by race, scaled by spawn size for the playable races; no armour textures, no face/hair | 2 |
| Equipment | Weapons in hand, armour tint and material | Todo | 3 |
| Animations | Walk, run, idle, combat, damage, death, social | Partial: idle/walk/run, attack, flinch | 2 |
| Sky, day and night | Sky dome, sun/moon, day/night light from the server clock | Written (Lantern sky on its own URP camera, sky type from the zone header, Norrath's clock from time_of_day, night ambient), to check in Unity | 2 |
| Weather | Rain, snow | Written (the zone's weather type from the zone table, the legacy clear/weather cycle on the server, rain streaks and snow around the camera), to check in Unity | 4 |
| Fog and clip plane | Per zone (legacy cfg/<zone>.cfg: fog colour and distances, clip) | Written (linear fog, far clip, fog-coloured background), to check in Unity | 2 |
| Underworld | Falling below the zone's floor returns you to the safe point | Done (server, from the zone header) | — |
| Water and lava | Surfaces, swimming, underwater tint | Partial: water and lava regions from the zone's BSP tree (Lantern export); swimming (no gravity, slower, Space up, Ctrl down, the head stays at the surface), blue-green fog under water, the server lets swimmers rise; no lava damage, drowning or swimming skill yet | 2 |
| Light sources | Torches and lanterns in hand, zone lights | Partial: one light around the player | 3 |
| Particles | Spell effects | Todo (casting works, no particles yet) | 3 |
| Name plates | Names above heads, consider colours | Written (con colour after considering, target in brackets), to check in Unity | 1 |
| Loading screen | Between zones | Done (black screen, "Loading, please wait...") | — |

## Camera and movement

| Feature | Trilogy client | Status | Priority |
|---|---|---|---|
| First-person view | Default view | Written (F9, wheel), to check in Unity | 1 |
| Third-person views | Chase and overhead, cycled with F9, zoom | Written, to check in Unity (pulls in at walls) | 1 |
| Mouse look | Right button held: look around and turn | Written, to check in Unity | 1 |
| Look up/down, centre | Page Up / Page Down, Home | Written, to check in Unity | 1 |
| Walk, run, strafe, turn | Arrows/WASD, run toggle | Done: Shift walks | 1 |
| Autorun | Num Lock / R | Written, to check in Unity | 1 |
| Jump and falls | Space; falling off ledges | Done: jump (about 5.7 units), gravity, falls from ledges; the server limits climbing, not falling | — |
| Sit, stand, crouch | Sitting regenerates faster | Partial: X or /sit, seen by others, walking stands up; no crouch | 1 |
| Swimming, levitation, falling damage | | Partial: swimming, levitation floats you down; no falling damage | 3 |
| Collisions | Walls, objects, steps | Done (ground, steps, walls, solid objects of the export) | — |

## Interface

| Feature | Trilogy client | Status | Priority |
|---|---|---|---|
| Chat window | Say, shout, OOC, tells, group, guild, emotes, channels, scrolling | Partial: Enter to type; say, shout, OOC, auction, tells (across zones), group, emotes; 12 lines on screen in the channel colours (tells purple, group blue, shout red, OOC and auction green), 500 kept: the wheel over the chat or Page Up/Down while typing scrolls back, the arrows recall typed lines. No guild channel | 2 |
| Slash commands | /who, /loc, /sit, /camp, /con, /target... | Partial: /say /shout /ooc /auction /tell /em /who /loc /sit /stand /camp /con /target /cast and the abilities | 1 |
| Player window | HP, mana, stamina, experience bars | Partial: HP, mana and experience bars, level; no stamina yet | 1 |
| Target window | Name, health, consider colour | Written (name in con colour, health bar), to check in Unity | 1 |
| Consider (C) | Con colour and faction message | Done (standings from the faction tables and the character's faction values) | — |
| Inventory | Worn slots, bags, weight, money | Partial: window (I) with every worn and general slot and money; click an item then a slot to move or equip it (server checks slot, class, race, two-handed; armour and weapon apply to combat); bags in the general slots with their contents listed below them (sizes checked, a bag carries its contents, loot and purchases go into bags when the general slots are full); saved in the profile. No weight or icons | 2 |
| Loot | Corpse window | Done: NPCs leave corpses with their loot table's items and coins (legacy rolls, rot timers, killer's rights then free for all), L opens the loot window, Take / Done | — |
| Merchants, bank, trade | | Partial: merchants (U on a targeted merchant): goods from `merchantlist` at 2.5 times their value, selling at 0.4 times from the inventory, the legacy refusals (busy, dubious); trades between players (/trade on a target: up to 8 items with bags and their contents, money, both accept); the bank (U on a banker: 8 slots with bags, money in and out coin by coin); giving to NPCs (/trade or /give on a targeted NPC: up to 4 items and money, accepted at once, for quest hand-ins); /hail and H hail the target | 3 |
| Spell book, spell gems, casting bar | Book pages, 8 gems, memorising, scribing scrolls, casting bar | Partial: book window (B, memorise by gem number), 8 gems (keys 1-8 or click, /cast N), casting bar, Scribe button on scrolls in the inventory, casting animation (t05). Direct damage, heals, buffs (stats, AC, HP, haste, slow, speed, damage and heals over time), root, mez, stun, invisibility, levitation, bind, gate, teleports, summoning, area spells; pets (summoning, /pet attack, /pet back off, /pet get lost); no charm, fear or cures yet; no icons, no memorising time, no spell particles | 2 |
| Hot buttons, abilities, skills window | | Partial: skills window (K), ability buttons and /kick /bash /taunt /mend /hide /sneak /forage for the classes that have them (reuse times on the server); no hot bar editing, bind wound, backstab, fishing or tracking yet | 2 |
| Group and guild windows | | Partial: groups (/invite by name or target, /follow, /decline, /disband, /g), group window with the members' health in the zone; shared experience and group spells on the server; no guilds yet | 3 |
| Buff window | | Partial: list of buffs with time left, detrimental ones in red; no icons, no clicking off | 3 |
| Options, key bindings | | Todo | 3 |
| Login and server select | | Done (IMGUI, to restyle) | — |
| Character select and creation | 3D models, race/class/deity/city choices, stat points | Partial: race, gender, class, deity, city among the start_zones combinations, bonus points, name; no 3D preview, no face choice | 2 |

## Sound

| Feature | Trilogy client | Status | Priority |
|---|---|---|---|
| Zone music | XMI tracks (LanternUnityTools reads them) | Written (music regions with day and night tracks of the zone's XMI, synthesized at run time with MeltySynth and Lantern's soundfont), to check | 3 |
| Ambient and 3D sounds | Zone sound emitters (in the Lantern export) | Written (ambient loops by region, day and night; positional emitters; the client's WAV sounds), to check | 3 |
| Combat and spell sounds | | Todo | 3 |

## Order of work

1. Priority 1: camera (first person, chase views, mouse look), movement keys (autorun, sit), name
   plates, chat window and slash commands, player/target windows with consider colours, every zone
   imported.
2. Priority 2: sky/day-night, fog, water, animations and model details, inventory and loot, spells,
   character creation.
3. Priority 3 and 4: the rest.

Each feature lands with its ClientCore tests where it has logic, and is checked in Unity before it
is marked done here.
