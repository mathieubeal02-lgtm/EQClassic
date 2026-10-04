-- 010: detour points for the Qeynos NPC grid segments that went through walls.
--
-- Reported in game: Qeynos guards walking into walls. NPCs walk grid_entries in straight lines;
-- tools/maps/check_grids.py found 21 segments crossing a wall in qeynos and qeynos2 (guards Relam,
-- Naret in South Qeynos, Rashik in North Qeynos among them). tools/maps/fix_grid_walls.py found a
-- detour point beside 17 of them (both halves clear at body and head height, on a floor); this
-- inserts those points (later points of the grid shift by one). Generated, do not edit:
--   fix_grid_walls.py --maps Maps/Maps --zone qeynos ; ... --zone qeynos2
-- Not idempotent: run once (grid_entries before it: table grid_entries_before_010).
CREATE TABLE IF NOT EXISTS grid_entries_before_010 AS SELECT * FROM grid_entries WHERE zoneid IN (1, 2);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 1 AND gridid = 3 AND number >= 226 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (3, 1, 226, -566.93, 378.86, 1.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 1 AND gridid = 4 AND number >= 279 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (4, 1, 279, -247.37, 506.96, 15.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 1 AND gridid = 4 AND number >= 278 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (4, 1, 278, -266.18, 514.60, 1.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 1 AND gridid = 4 AND number >= 270 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (4, 1, 270, -253.99, 503.07, 15.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 1 AND gridid = 43 AND number >= 114 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (43, 1, 114, -44.21, 437.12, 1.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 1 AND gridid = 47 AND number >= 61 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (47, 1, 61, -394.71, -214.38, 1.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 4 AND number >= 2 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (4, 2, 2, 131.28, 188.51, -18.00, 0, 0);
-- qeynos2 grid 23: no detour found between points 143 and 144
-- qeynos2 grid 23: no detour found between points 289 and 290
-- qeynos2 grid 23: no detour found between points 432 and 433
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 51 AND number >= 34 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (51, 2, 34, -490.00, 792.70, 1.00, 0, 0);
-- qeynos2 grid 52: no detour found between points 48 and 49
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 60 AND number >= 3 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (60, 2, 3, 683.19, 792.40, 1.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 60 AND number >= 2 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (60, 2, 2, 711.48, 825.48, 1.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 78 AND number >= 2 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (78, 2, 2, -52.61, 121.32, 0.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 80 AND number >= 6 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (80, 2, 6, 322.19, 111.67, 11.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 81 AND number >= 2 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (81, 2, 2, 236.41, 56.81, -7.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 82 AND number >= 29 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (82, 2, 29, 332.29, 113.41, 13.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 91 AND number >= 427 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (91, 2, 427, 117.97, 245.06, 1.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 93 AND number >= 18 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (93, 2, 18, -500.49, 808.66, 1.00, 0, 0);
UPDATE grid_entries SET number = number + 1 WHERE zoneid = 2 AND gridid = 101 AND number >= 2 ORDER BY number DESC;
INSERT INTO grid_entries (gridid, zoneid, number, x, y, z, heading, pause) VALUES (101, 2, 2, -543.50, 811.02, 1.00, 0, 0);
