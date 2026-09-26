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
| Characters | Race/gender models, textures, size | Partial: models by race; no armour textures, no face/hair, size ignored | 2 |
| Equipment | Weapons in hand, armour tint and material | Todo | 3 |
| Animations | Walk, run, idle, combat, damage, death, social | Partial: idle/walk/run, attack, flinch | 2 |
| Sky, day and night | Sky dome, sun/moon, day/night light from the server clock | Written (Lantern sky on its own URP camera, sky type from the zone header, Norrath's clock from time_of_day, night ambient), to check in Unity | 2 |
| Weather | Rain, snow | Todo | 4 |
| Fog and clip plane | Per zone (legacy cfg/<zone>.cfg: fog colour and distances, clip) | Written (linear fog, far clip, fog-coloured background), to check in Unity | 2 |
| Underworld | Falling below the zone's floor returns you to the safe point | Done (server, from the zone header) | — |
| Water and lava | Surfaces, swimming, underwater tint | Todo | 2 |
| Light sources | Torches and lanterns in hand, zone lights | Partial: one light around the player | 3 |
| Particles | Spell effects | Todo (with spells) | 3 |
| Name plates | Names above heads, consider colours | Written (con colour after considering, target in brackets), to check in Unity | 1 |
| Loading screen | Between zones | Todo | 3 |

## Camera and movement

| Feature | Trilogy client | Status | Priority |
|---|---|---|---|
| First-person view | Default view | Written (F9, wheel), to check in Unity | 1 |
| Third-person views | Chase and overhead, cycled with F9, zoom | Written, to check in Unity (pulls in at walls) | 1 |
| Mouse look | Right button held: look around and turn | Written, to check in Unity | 1 |
| Look up/down, centre | Page Up / Page Down, Home | Written, to check in Unity | 1 |
| Walk, run, strafe, turn | Arrows/WASD, run toggle | Done: Shift walks | 1 |
| Autorun | Num Lock / R | Written, to check in Unity | 1 |
| Jump | Space | Todo | 2 |
| Sit, stand, crouch | Sitting regenerates faster | Partial: X or /sit, seen by others, walking stands up; no crouch | 1 |
| Swimming, levitation, falling damage | | Todo | 3 |
| Collisions | Walls, objects, steps | Done (ground, steps, walls, solid objects of the export) | — |

## Interface

| Feature | Trilogy client | Status | Priority |
|---|---|---|---|
| Chat window | Say, shout, OOC, tells, group, guild, emotes, channels, scrolling | Partial: Enter to type; say, shout, OOC, auction, tells (across zones), emotes; last 12 lines on screen. No group/guild channels, no scrolling back | 1 |
| Slash commands | /who, /loc, /sit, /camp, /con, /target... | Partial: /say /shout /ooc /auction /tell /em /who /loc /sit /stand /camp /con /target | 1 |
| Player window | HP, mana, stamina, experience bars | Partial: HP and experience bars, level; no mana or stamina yet | 1 |
| Target window | Name, health, consider colour | Written (name in con colour, health bar), to check in Unity | 1 |
| Consider (C) | Con colour and faction message | Done (faction always indifferent until the faction port) | — |
| Inventory | Worn slots, bags, weight, money | Todo (server: items, profile slots) | 2 |
| Loot | Corpse window | Todo (server: loot tables) | 2 |
| Merchants, bank, trade | | Todo | 3 |
| Spell book, spell gems, casting bar | | Todo (server: spells) | 2 |
| Hot buttons, abilities, skills window | | Todo | 2 |
| Group and guild windows | | Todo | 3 |
| Buff window | | Todo | 3 |
| Options, key bindings | | Todo | 3 |
| Login and server select | | Done (IMGUI, to restyle) | — |
| Character select and creation | 3D models, race/class/deity/city choices, stat points | Partial: list and a fixed troll shaman | 2 |

## Sound

| Feature | Trilogy client | Status | Priority |
|---|---|---|---|
| Zone music | XMI tracks (LanternUnityTools reads them) | Todo | 3 |
| Ambient and 3D sounds | Zone sound emitters (in the Lantern export) | Todo | 3 |
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
