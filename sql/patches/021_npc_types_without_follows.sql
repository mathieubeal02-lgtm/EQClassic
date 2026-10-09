-- 021: the NPC table the zone reads gets the sizes of 011 and the levels of 013-020.
--
-- The zone loads its NPCs from npc_types_without (Database::LoadNPCTypes joins it with spawnentry);
-- npc_types is a second copy that only gives the highest id. Patch 011 (2,571 sizes from Quarm) and
-- the level / HP / damage / AC corrections of the era-spawn patches 013-020 (35 NPCs) were written
-- to npc_types alone: nothing of them showed in game (a_young_dire_wolf stayed at size 2 for 4).
-- This copies them over, for the rows those patches changed (their backups tell which): the other
-- differences between the two tables are left alone. The values replaced are kept in
-- npc_types_without_before_021. Idempotent. Patches 004 and 005 already wrote both tables.

CREATE TABLE IF NOT EXISTS npc_types_without_before_021 (
  id INT PRIMARY KEY, size FLOAT, level INT, hp INT, mindmg INT, maxdmg INT, AC INT);

INSERT IGNORE INTO npc_types_without_before_021
SELECT w.id, w.size, w.level, w.hp, w.mindmg, w.maxdmg, w.AC
FROM npc_types_without w JOIN npc_types n ON n.id = w.id
WHERE (w.id IN (SELECT b.id FROM npc_types_size_before_011 b JOIN npc_types n2 ON n2.id = b.id WHERE n2.size <> b.size) AND w.size <> n.size)
   OR (w.id IN (SELECT id FROM npc_types_before_era)
       AND (w.level <> n.level OR w.hp <> n.hp OR w.mindmg <> n.mindmg OR w.maxdmg <> n.maxdmg OR w.AC <> n.AC));

UPDATE npc_types_without w JOIN npc_types n ON n.id = w.id JOIN npc_types_size_before_011 b ON b.id = n.id
SET w.size = n.size WHERE n.size <> b.size;

UPDATE npc_types_without w JOIN npc_types n ON n.id = w.id JOIN npc_types_before_era b ON b.id = n.id
SET w.level = n.level, w.hp = n.hp, w.mindmg = n.mindmg, w.maxdmg = n.maxdmg, w.AC = n.AC;
