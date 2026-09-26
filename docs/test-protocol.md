# Test protocol

Two parts: the scripted play test, which drives the real client library against a running server,
and a short visual checklist for what only a screen shows.

## 1. Scripted play test (`tools/playtest.sh`)

`rewrite/src/PlayTest` logs in like a player (EQClassic.ClientCore, the library the Unity client uses)
and plays these scenarios, each reported OK / FAIL with what it saw, in a Markdown report
(`build/playtest-<date>.md`):

| Scenario | What it does | Checks |
|---|---|---|
| connect and log in | login server, encrypted login | the server list |
| world and characters | world, character list | the character is there |
| enter the world | zone key, zone server | in zone, entities received |
| GM: #zone qeynos2 | GM zoning | in North Qeynos |
| say, /who, /loc | chat | the echo, the who list, the location |
| GM: #clearinventory | empties the packs | the eight general slots are empty |
| GM: #level 10 | level change | the new level and hit points |
| target and consider | Tab, C | the consider line (faction and level) |
| quest: hail Brohan | #goto, hail | Brohan Ironforge's Perl quest answer |
| melee: kill a rodent | auto attack | "You have slain a rodent!" |
| loot the corpse | loot window, take all | the window opens and closes |
| spells: scribe, memorise, cast | #scribespells, gem, cast | casting starts, mana goes down |
| GM: #summonitem, eat | a muffin, eat it | the eating message |
| equipment change | a chest piece for the class, worn | slot 17 holds it (others get EntityLooks) |
| GM: #givemoney | platinum | the purse |
| merchant: buy | U on a merchant, sell the packs, buy | the goods, sold items, money spent |
| bank: deposit | South Qeynos banker | one platinum more in the vault |
| zone change and back | #zone qeynos, back | both zones entered |
| second player enters | bot2 / bot2, Qpartner (made if missing) | both players in one zone |
| group: invite, follow, chat | /invite, /follow, /g | both see the group, the line reaches the other |
| trade an item | /trade, offer, accept both | the other player gets the item |
| quest: proximity greeting | #zone erudnint, carry the note 18729, go to Lanken Rjarn | EVENT_SPAWN sets his box, EVENT_ENTER greets |

Setup: a server with the database (`--db`), a GM account for the first player (`UPDATE account SET
status = 255 WHERE id = <world account>`), and a second login account `bot2` (SHA-1 password). The
tests use their own characters; run them on a test server (another port) when players are on.

    tools/playtest.sh 127.0.0.1 6998 <fingerprint> bot bot Qbot

## 2. Visual checklist (the Unity client)

The Linux player under Xvfb gives screenshots (`DISPLAY=:99`, `xdotool` for keys, `import` for the
screen); `EQC_DEBUG_MODELS=1` logs every character model (meshes, bounds). Check:

- the classic frame: buttons, gauges, gems in their sockets, the six boxes, chat on the parchment;
- F12: the window layout, windows moved and closed, the layout kept after a restart;
- PERSONA: the worn slots and the general slots with icons, a bag opened with a right click;
- merchant, bank, trade and loot windows with the Trilogy art;
- the spell book (B): pages, icons, memorising into a gem;
- characters: guards with body and helmet, dwarves and gnomes at their size, NPCs of every race;
- mouse targeting in the 3D view, not through the interface;
- chat colours: experience yellow, damage red;
- the night fog (Oasis at night), water, weather.
