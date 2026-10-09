-- 022: the special abilities of the NPCs imported by 013-018 (the warders and The Sleeper summon, enrage,
-- cannot be mezzed, charmed, stunned, snared or feared...). Quarm numbers them (special_abilities); ours are
-- letters in npcspecialattks: 1 S summon, 2 E enrage, 3 R rampage, 5 F flurry, 6 T triple attack, 7 Q quad attack,
-- 12 U unslowable, 13 M unmezzable, 14 C uncharmable, 15 N unstunnable, 16 I unsnareable, 17 D unfearable,
-- 19 A immune to melee, 20 B immune to magic, 21 f never flees, 22 O bane only, 23 W magical weapons only,
-- 24 H never aggro. The others have no letter here. Both NPC tables are written (see 021). Their spells: this
-- server gives NPCs spells by class and level (npc_spells), not by NPC; nothing to import.

UPDATE npc_types SET npcspecialattks = 'SQMCN' WHERE id = 114630;	-- a_Huge_Golem_Sentry
UPDATE npc_types_without SET npcspecialattks = 'SQMCN' WHERE id = 114630;	-- a_Huge_Golem_Sentry
UPDATE npc_types SET npcspecialattks = 'ABH' WHERE id = 126303;	-- Doo_
UPDATE npc_types_without SET npcspecialattks = 'ABH' WHERE id = 126303;	-- Doo_
UPDATE npc_types SET npcspecialattks = 'ABH' WHERE id = 126305;	-- knightspawner
UPDATE npc_types_without SET npcspecialattks = 'ABH' WHERE id = 126305;	-- knightspawner
UPDATE npc_types SET npcspecialattks = 'S' WHERE id = 126312;	-- a_white_stallion
UPDATE npc_types_without SET npcspecialattks = 'S' WHERE id = 126312;	-- a_white_stallion
UPDATE npc_types SET npcspecialattks = 'SETQMCNIDfW' WHERE id = 128090;	-- #Nanzata_the_Warder
UPDATE npc_types_without SET npcspecialattks = 'SETQMCNIDfW' WHERE id = 128090;	-- #Nanzata_the_Warder
UPDATE npc_types SET npcspecialattks = 'SERTQMCNIDfW' WHERE id = 128091;	-- #Ventani_the_Warder
UPDATE npc_types_without SET npcspecialattks = 'SERTQMCNIDfW' WHERE id = 128091;	-- #Ventani_the_Warder
UPDATE npc_types SET npcspecialattks = 'SERTQMCNIDfW' WHERE id = 128092;	-- #Tukaarak_the_Warder
UPDATE npc_types_without SET npcspecialattks = 'SERTQMCNIDfW' WHERE id = 128092;	-- #Tukaarak_the_Warder
UPDATE npc_types SET npcspecialattks = 'SERTQMCNIDf' WHERE id = 128093;	-- #Hraashna_the_Warder
UPDATE npc_types_without SET npcspecialattks = 'SERTQMCNIDf' WHERE id = 128093;	-- #Hraashna_the_Warder
UPDATE npc_types SET npcspecialattks = 'SEFQUMCNIDfH' WHERE id = 128094;	-- #The_Sleeper
UPDATE npc_types_without SET npcspecialattks = 'SEFQUMCNIDfH' WHERE id = 128094;	-- #The_Sleeper
UPDATE npc_types SET npcspecialattks = 'SEFTQMCND' WHERE id = 128101;	-- an_ancient_sentry
UPDATE npc_types_without SET npcspecialattks = 'SEFTQMCND' WHERE id = 128101;	-- an_ancient_sentry
UPDATE npc_types SET npcspecialattks = 'SFQMCND' WHERE id = 128111;	-- a_debris_covered_guardian
UPDATE npc_types_without SET npcspecialattks = 'SFQMCND' WHERE id = 128111;	-- a_debris_covered_guardian
