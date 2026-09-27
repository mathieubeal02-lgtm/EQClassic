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
| quest: hand-in | #givemoney, give Moodoro Finharn 2 gold in a trade | he answers and hands over the Testament of Vanear (17918) |
| quest: keyword dialog | say "What testament of Vanear?" to Moodoro | he answers the keyword |
| experience and a level | no target, #addexp 100, #addexp 3000000, #level back | "You gain experience!!", a level gained, then the old level again |
| door: open and close | #goto next to North Qeynos' DOOR2, U, U again | the door opens, then closes |
| melee: kill a rodent | auto attack | "You have slain a rodent!" |
| loot the corpse | loot window, take all | the window opens and closes |
| pet: kills, its owner loots | #cast 164 (Companion Spirit), /pet attack a rodent, loot its corpse | the pet kills, the owner may loot |
| spells: scribe a scroll | #unscribespells 267, #summonitem 15267 (Spell: Inner Fire), scribe it | Inner Fire back in the book, the scroll used up |
| spells: scribe, memorise, cast | #scribespells 10, memorise Inner Fire, cast on self (up to 3 tries) | mana spent, no interruption nor fizzle, the buff shows |
| death: corpse and recovery | carry a muffin, #kill self, come back, loot your corpse | the muffin leaves with the corpse and comes back from it |
| bags: put in and take out | #summonitem a Backpack and a muffin, move the muffin into the first cell, then back | the bag cell holds it, then the general slot again |
| GM: #summonitem, eat | a muffin, eat it | the eating message |
| equipment change | a chest piece for the class, worn | slot 17 holds it (others get EntityLooks) |
| GM: #givemoney | platinum | the purse |
| merchant: buy | U on a merchant, sell the packs, buy | the goods, sold items, money spent |
| bank: deposit and withdraw | #zone qeynos, U on the banker, 1p in, then 1p out | the vault goes up by 1p, then back, and the purse gets it |
| aggro: a gnoll attacks | #zone qeynos2 (#repopzone if Fippy is dead), go next to Fippy Darkpaw, no target | he attacks by himself (then #kill) |
| buffs: levitate | #cast 261 on yourself | the buff list and the client say levitating |
| buffs: spirit of wolf | #cast 278 on yourself | the buff list gives +movement, the client runs faster |
| sit and regenerate | #damage 60 self, /sit | hit points come back within a tic |
| bind and gate | #cast 35 (Bind Affinity) in qeynos2, #zone qeynos, #cast 36 (Gate) | back in qeynos2 |
| zone change and back | #zone qeynos, back | both zones entered |
| walk through a zone line | #goto next to North Qeynos' east line (zone_points 978), walk into it | the client lands in South Qeynos (qeynos) |
| second player enters | bot2 / bot2, Qpartner (made if missing) | both players in one zone |
| group: invite, follow, chat, experience | /invite, /follow, /g, #kill an NPC of level 7-12, #level Qpartner back | both grouped, the group chat arrives, Qpartner gets a share of the experience |
| trade an item | /trade, offer, accept both | the other player gets the item |
| translocate: asked, accepted | group Qpartner, #cast 1336 (Fay) or 1337 (Tox, when already in gfaydark) on them | Qpartner gets the offer, says yes, lands in the other zone |
| GM: #flymode | #flymode on, then off | the client flies, then falls again |
| quest: proximity greeting | #zone erudnint, carry the note 18729, go to Lanken Rjarn | EVENT_SPAWN sets his box, EVENT_ENTER greets |
| character creation: rules and starting items | bot2 builds Qcreate (troll shaman): 30 clicks on STR, then a request with 30 in STR, then the valid one | the builder stops at 25, the server refuses 30, accepts 25+5, the packs hold the starting items; Qcreate is deleted |
| camp and come back | /camp, wait 30 s, log in again | same zone, same place |

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
