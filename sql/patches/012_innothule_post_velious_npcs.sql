-- 012: remove the Legacy of Ykesha (2003) NPCs from Innothule Swamp.
--
-- The EQClassic dump spawns the Gukta outpost and its surroundings in Innothule: froglok guards
-- (Dar_Guard_*, level 55, faction Protectors of Gukta, hostile to trolls), trainers, the ghoulish
-- and fallen trolls/frogloks, bone spirits, bleeders, corpse spores. None existed in the Trilogy
-- era: these 34 NPCs are in neither the Quarm database (classic to Velious, tools/npc_stats) nor
-- the 2000-2002 ShowEQ capture (tools/npc_stats/EQSpawns-showeq-2002.txt, names compared without
-- '#' and leading a_/an_). Seen: the guards killed the bot fleet's level 1-3 troll shamans.
-- Jojongua (a level 18 roaming kobold, in Quarm) stays.
--
-- Their spawnentry rows (45 spawn groups, 41 of which held nothing else) are moved to
-- spawnentry_post_velious (restore with INSERT INTO spawnentry SELECT * FROM spawnentry_post_velious).
-- Idempotent.

CREATE TABLE IF NOT EXISTS spawnentry_post_velious LIKE spawnentry;

INSERT IGNORE INTO spawnentry_post_velious
SELECT * FROM spawnentry WHERE npcID IN (
  46001, 46046, 46047, 46049, 46050, 46051, 46052, 46057, 46058, 46059, 46062, 46063,
  46064, 46066, 46069, 46070, 46071, 46072, 46075, 46079, 46081, 46082, 46083, 46084,
  46085, 46087, 46088, 46089, 46090, 46092, 46093, 46095, 46097, 46100);

DELETE FROM spawnentry WHERE npcID IN (
  46001, 46046, 46047, 46049, 46050, 46051, 46052, 46057, 46058, 46059, 46062, 46063,
  46064, 46066, 46069, 46070, 46071, 46072, 46075, 46079, 46081, 46082, 46083, 46084,
  46085, 46087, 46088, 46089, 46090, 46092, 46093, 46095, 46097, 46100);
