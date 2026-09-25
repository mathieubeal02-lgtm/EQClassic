-- 006: remove spawns of NPCs whose race the Trilogy client does not have.
--
-- The Trilogy (Velious) client crashes when a spawn uses a race model it does not know. The
-- EQClassic dump spawns a few later-era NPCs: Vah Shir (race 130, Luclin) scouts in Permafrost,
-- the Feerrott, Timorous Deep, Lesser Faydark and more, and races 200+ (Jaggedpine, Plane of
-- Mischief mushrooms, the Hole's elementals, revamped City of Mist mobs). None of them is in
-- the 2000-2002 ShowEQ capture (tools/npc_stats/EQSpawns-showeq-2002.txt). Seen: the client
-- crashed twice about 30 s after zoning into Permafrost, next to Scout_Janomin.
--
-- Their spawnentry rows are moved to spawnentry_post_trilogy (restore with
-- INSERT INTO spawnentry SELECT * FROM spawnentry_post_trilogy). Idempotent.

CREATE TABLE IF NOT EXISTS spawnentry_post_trilogy LIKE spawnentry;

INSERT IGNORE INTO spawnentry_post_trilogy
SELECT e.* FROM spawnentry e JOIN npc_types n ON n.id = e.npcID
WHERE n.race = 130 OR n.race >= 200;

DELETE e FROM spawnentry e JOIN npc_types n ON n.id = e.npcID
WHERE n.race = 130 OR n.race >= 200;
