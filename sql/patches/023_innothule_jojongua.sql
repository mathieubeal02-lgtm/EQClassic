-- 023: Jojongua out of the trolls' newbie yard in Innothule.
--
-- The dump has Jojongua (46053, level 18, aggressive) on half the spawns of point 307659 (-77, -2134), a level 1-2
-- point next to the Grobb entrance, every 350 seconds: he killed the new characters there over and over.
-- In the era data (Quarm) he is a 13% spawn of the kobold camps in the north-east of the zone
-- (x 438..1280, y 707..1310). Here he becomes a 13% spawn of the kobold hunter at (1336, 1119), in that area,
-- and the newbie point keeps its lesser kobold.
--
-- The rows changed are kept in spawnentry_before_023. To restore: delete the spawnentry rows of groups 46100
-- and 46089 and insert the spawnentry_before_023 rows back. Idempotent.

CREATE TABLE IF NOT EXISTS spawnentry_before_023 LIKE spawnentry;
INSERT INTO spawnentry_before_023 SELECT * FROM spawnentry WHERE spawngroupID IN (46100, 46089)
  AND NOT EXISTS (SELECT 1 FROM spawnentry_before_023);

DELETE FROM spawnentry WHERE spawngroupID = 46100 AND npcID = 46053;
UPDATE spawnentry SET chance = 100 WHERE spawngroupID = 46100 AND npcID = 46026;
UPDATE spawnentry SET chance = 87 WHERE spawngroupID = 46089 AND npcID = 46024;
INSERT IGNORE INTO spawnentry (spawngroupID, npcID, chance) VALUES (46089, 46053, 13);
