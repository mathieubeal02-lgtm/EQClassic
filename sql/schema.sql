-- EQClassic world/zone/login schema (81 tables), structure only.
-- Extracted from the eqclassic_db dump (git submodule sql/eqclassic_db, commit 3fb126a, 2021-09-08,
-- dumped from MariaDB 10.0.21). Regenerate with:  python3 sql/extract_schema.py
-- Data is imported from sql/eqclassic_db/sql/*.sql (see sql/README.md).
SET NAMES utf8;
SET FOREIGN_KEY_CHECKS=0;

CREATE TABLE `aa_actions` (
  `aaid` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `rank` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `reuse_time` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `spell_id` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `cast_time` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `target` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `nonspell_action` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `nonspell_mana` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `nonspell_duration` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `redux_aa` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `redux_rate` tinyint(4) NOT NULL DEFAULT '0',
  PRIMARY KEY (`aaid`,`rank`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `aa_levels` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `aa_id` int(10) unsigned NOT NULL DEFAULT '0',
  `ability` int(10) unsigned NOT NULL DEFAULT '0',
  `increase_amt` int(10) unsigned NOT NULL DEFAULT '0',
  `level` tinyint(3) unsigned NOT NULL DEFAULT '1',
  `unknown08` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `NewIndex` (`aa_id`,`level`)
) ENGINE=MyISAM AUTO_INCREMENT=155 DEFAULT CHARSET=latin1;

CREATE TABLE `aa_swarmpets` (
  `spell_id` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `count` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `npc_id` int(11) NOT NULL DEFAULT '0',
  `duration` mediumint(8) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`spell_id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `account` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(255) NOT NULL DEFAULT '',
  `password` varchar(255) NOT NULL DEFAULT '',
  `lsadmin` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `lsstatus` int(11) unsigned DEFAULT NULL,
  `worldadmin` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `user_active` tinyint(4) unsigned NOT NULL DEFAULT '0',
  `minilogin_ip` varchar(255) DEFAULT NULL,
  `status` int(11) DEFAULT NULL,
  `lsaccount_id` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`name`),
  UNIQUE KEY `lsstatus` (`lsstatus`)
) ENGINE=InnoDB AUTO_INCREMENT=24 DEFAULT CHARSET=latin1;

CREATE TABLE `active_accounts` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `lsaccount` int(11) unsigned NOT NULL DEFAULT '0',
  `account` varchar(31) NOT NULL DEFAULT '',
  `ip` varchar(16) NOT NULL DEFAULT '',
  `status` varchar(45) NOT NULL DEFAULT 'AT_CHARACTER_SELECT',
  `lastAction` varchar(45) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=latin1;

CREATE TABLE `authentication` (
  `account_id` int(11) NOT NULL DEFAULT '0',
  `char_name` varchar(16) NOT NULL DEFAULT '',
  `zone_name` varchar(16) NOT NULL DEFAULT '',
  `time` int(11) NOT NULL DEFAULT '0',
  `ip` int(10) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`account_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `boat_infos` (
  `id` smallint(16) NOT NULL DEFAULT '0',
  `boat_name` varchar(30) NOT NULL DEFAULT '',
  `zone` varchar(16) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `boats` (
  `name` varchar(30) NOT NULL DEFAULT '',
  `race` int(11) unsigned DEFAULT '72',
  `gender` int(11) DEFAULT '2',
  `texture` int(11) unsigned DEFAULT '1',
  `body` int(11) DEFAULT '5',
  `speed` float(11,2) unsigned DEFAULT '1.25',
  `size` int(11) unsigned DEFAULT '6',
  `lineroute` tinyint(1) DEFAULT NULL,
  PRIMARY KEY (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `books` (
  `name` varchar(15) NOT NULL DEFAULT '',
  `txtfile` text NOT NULL,
  UNIQUE KEY `name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `bug_reports` (
  `causes_crash` int(11) NOT NULL DEFAULT '0',
  `can_duplicate` int(11) NOT NULL DEFAULT '0',
  `player` tinytext NOT NULL,
  `report` text NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `character_` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `account_id` int(11) NOT NULL DEFAULT '0',
  `name` varchar(16) NOT NULL DEFAULT '',
  `profile` blob,
  `inventory` blob,
  `guild` int(11) DEFAULT '0',
  `guildrank` tinyint(2) unsigned DEFAULT '5',
  `x` float NOT NULL DEFAULT '0',
  `y` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `zonename` varchar(30) NOT NULL DEFAULT '',
  `GID` int(11) NOT NULL DEFAULT '0',
  `groupleader` tinyint(4) NOT NULL DEFAULT '0',
  `debug_system` blob,
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`name`)
) ENGINE=InnoDB AUTO_INCREMENT=15 DEFAULT CHARSET=latin1;

CREATE TABLE `doors` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `doorid` smallint(4) NOT NULL DEFAULT '0',
  `zone` varchar(16) CHARACTER SET utf8 NOT NULL DEFAULT '',
  `name` varchar(16) CHARACTER SET utf8 NOT NULL DEFAULT '',
  `pos_y` float NOT NULL DEFAULT '0',
  `pos_x` float NOT NULL DEFAULT '0',
  `pos_z` float NOT NULL DEFAULT '0',
  `heading` float NOT NULL DEFAULT '0',
  `opentype` smallint(4) NOT NULL DEFAULT '0',
  `guild` smallint(4) NOT NULL DEFAULT '0',
  `lockpick` smallint(4) NOT NULL DEFAULT '0',
  `keyitem` int(11) NOT NULL DEFAULT '0',
  `triggerdoor` smallint(4) NOT NULL DEFAULT '0',
  `triggertype` smallint(4) NOT NULL DEFAULT '0',
  `doorisopen` smallint(4) NOT NULL DEFAULT '0',
  `door_param` int(4) NOT NULL DEFAULT '0',
  `dest_zone` varchar(16) CHARACTER SET utf8 DEFAULT 'NONE',
  `dest_x` float DEFAULT '0',
  `dest_y` float DEFAULT '0',
  `dest_z` float DEFAULT '0',
  `dest_heading` float DEFAULT '0',
  `invert_state` int(11) DEFAULT '0',
  `incline` int(11) DEFAULT '0',
  `size` smallint(5) unsigned NOT NULL DEFAULT '100',
  PRIMARY KEY (`id`),
  UNIQUE KEY `DoorIndex` (`zone`,`doorid`)
) ENGINE=MyISAM AUTO_INCREMENT=22450 DEFAULT CHARSET=latin1 ROW_FORMAT=DYNAMIC;

CREATE TABLE `faction_list` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(50) NOT NULL DEFAULT '',
  `base` smallint(6) NOT NULL DEFAULT '0',
  `mod_c1` smallint(6) NOT NULL DEFAULT '0',
  `mod_c2` smallint(6) NOT NULL DEFAULT '0',
  `mod_c3` smallint(6) NOT NULL DEFAULT '0',
  `mod_c4` smallint(6) NOT NULL DEFAULT '0',
  `mod_c5` smallint(6) NOT NULL DEFAULT '0',
  `mod_c6` smallint(6) NOT NULL DEFAULT '0',
  `mod_c7` smallint(6) NOT NULL DEFAULT '0',
  `mod_c8` smallint(6) NOT NULL DEFAULT '0',
  `mod_c9` smallint(6) NOT NULL DEFAULT '0',
  `mod_c10` smallint(6) NOT NULL DEFAULT '0',
  `mod_c11` smallint(6) NOT NULL DEFAULT '0',
  `mod_c12` smallint(6) NOT NULL DEFAULT '0',
  `mod_c13` smallint(6) NOT NULL DEFAULT '0',
  `mod_c14` smallint(6) NOT NULL DEFAULT '0',
  `mod_c15` smallint(6) NOT NULL DEFAULT '0',
  `mod_r1` smallint(6) NOT NULL DEFAULT '0',
  `mod_r2` smallint(6) NOT NULL DEFAULT '0',
  `mod_r3` smallint(6) NOT NULL DEFAULT '0',
  `mod_r4` smallint(6) NOT NULL DEFAULT '0',
  `mod_r5` smallint(6) NOT NULL DEFAULT '0',
  `mod_r6` smallint(6) NOT NULL DEFAULT '0',
  `mod_r7` smallint(6) NOT NULL DEFAULT '0',
  `mod_r8` smallint(6) NOT NULL DEFAULT '0',
  `mod_r9` smallint(6) NOT NULL DEFAULT '0',
  `mod_r10` smallint(6) NOT NULL DEFAULT '0',
  `mod_r11` smallint(6) NOT NULL DEFAULT '0',
  `mod_r12` smallint(6) NOT NULL DEFAULT '0',
  `mod_r14` smallint(6) NOT NULL DEFAULT '0',
  `mod_r60` smallint(6) NOT NULL DEFAULT '0',
  `mod_r75` smallint(6) NOT NULL DEFAULT '0',
  `mod_r108` smallint(6) NOT NULL DEFAULT '0',
  `mod_r120` smallint(6) NOT NULL DEFAULT '0',
  `mod_r128` smallint(6) NOT NULL DEFAULT '0',
  `mod_r130` smallint(6) NOT NULL DEFAULT '0',
  `mod_r161` smallint(6) NOT NULL DEFAULT '0',
  `mod_d140` smallint(6) NOT NULL DEFAULT '0',
  `mod_d201` smallint(6) NOT NULL DEFAULT '0',
  `mod_d202` smallint(6) NOT NULL DEFAULT '0',
  `mod_d203` smallint(6) NOT NULL DEFAULT '0',
  `mod_d204` smallint(6) NOT NULL DEFAULT '0',
  `mod_d205` smallint(6) NOT NULL DEFAULT '0',
  `mod_d206` smallint(6) NOT NULL DEFAULT '0',
  `mod_d207` smallint(6) NOT NULL DEFAULT '0',
  `mod_d208` smallint(6) NOT NULL DEFAULT '0',
  `mod_d209` smallint(6) NOT NULL DEFAULT '0',
  `mod_d210` smallint(6) NOT NULL DEFAULT '0',
  `mod_d211` smallint(6) NOT NULL DEFAULT '0',
  `mod_d212` smallint(6) NOT NULL DEFAULT '0',
  `mod_d213` smallint(6) NOT NULL DEFAULT '0',
  `mod_d214` smallint(6) NOT NULL DEFAULT '0',
  `mod_d215` smallint(6) NOT NULL DEFAULT '0',
  `mod_d216` smallint(6) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`name`),
  UNIQUE KEY `id` (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=396 DEFAULT CHARSET=latin1 PACK_KEYS=0;

CREATE TABLE `faction_list_test` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(50) NOT NULL DEFAULT '',
  `base` smallint(6) NOT NULL DEFAULT '0',
  `mod_c1` smallint(6) NOT NULL DEFAULT '0',
  `mod_c2` smallint(6) NOT NULL DEFAULT '0',
  `mod_c3` smallint(6) NOT NULL DEFAULT '0',
  `mod_c4` smallint(6) NOT NULL DEFAULT '0',
  `mod_c5` smallint(6) NOT NULL DEFAULT '0',
  `mod_c6` smallint(6) NOT NULL DEFAULT '0',
  `mod_c7` smallint(6) NOT NULL DEFAULT '0',
  `mod_c8` smallint(6) NOT NULL DEFAULT '0',
  `mod_c9` smallint(6) NOT NULL DEFAULT '0',
  `mod_c10` smallint(6) NOT NULL DEFAULT '0',
  `mod_c11` smallint(6) NOT NULL DEFAULT '0',
  `mod_c12` smallint(6) NOT NULL DEFAULT '0',
  `mod_c13` smallint(6) NOT NULL DEFAULT '0',
  `mod_c14` smallint(6) NOT NULL DEFAULT '0',
  `mod_c15` smallint(6) NOT NULL DEFAULT '0',
  `mod_r1` smallint(6) NOT NULL DEFAULT '0',
  `mod_r2` smallint(6) NOT NULL DEFAULT '0',
  `mod_r3` smallint(6) NOT NULL DEFAULT '0',
  `mod_r4` smallint(6) NOT NULL DEFAULT '0',
  `mod_r5` smallint(6) NOT NULL DEFAULT '0',
  `mod_r6` smallint(6) NOT NULL DEFAULT '0',
  `mod_r7` smallint(6) NOT NULL DEFAULT '0',
  `mod_r8` smallint(6) NOT NULL DEFAULT '0',
  `mod_r9` smallint(6) NOT NULL DEFAULT '0',
  `mod_r10` smallint(6) NOT NULL DEFAULT '0',
  `mod_r11` smallint(6) NOT NULL DEFAULT '0',
  `mod_r12` smallint(6) NOT NULL DEFAULT '0',
  `mod_r14` smallint(6) NOT NULL DEFAULT '0',
  `mod_r60` smallint(6) NOT NULL DEFAULT '0',
  `mod_r75` smallint(6) NOT NULL DEFAULT '0',
  `mod_r108` smallint(6) NOT NULL DEFAULT '0',
  `mod_r120` smallint(6) NOT NULL DEFAULT '0',
  `mod_r128` smallint(6) NOT NULL DEFAULT '0',
  `mod_r130` smallint(6) NOT NULL DEFAULT '0',
  `mod_r161` smallint(6) NOT NULL DEFAULT '0',
  `mod_d140` smallint(6) NOT NULL DEFAULT '0',
  `mod_d201` smallint(6) NOT NULL DEFAULT '0',
  `mod_d202` smallint(6) NOT NULL DEFAULT '0',
  `mod_d203` smallint(6) NOT NULL DEFAULT '0',
  `mod_d204` smallint(6) NOT NULL DEFAULT '0',
  `mod_d205` smallint(6) NOT NULL DEFAULT '0',
  `mod_d206` smallint(6) NOT NULL DEFAULT '0',
  `mod_d207` smallint(6) NOT NULL DEFAULT '0',
  `mod_d208` smallint(6) NOT NULL DEFAULT '0',
  `mod_d209` smallint(6) NOT NULL DEFAULT '0',
  `mod_d210` smallint(6) NOT NULL DEFAULT '0',
  `mod_d211` smallint(6) NOT NULL DEFAULT '0',
  `mod_d212` smallint(6) NOT NULL DEFAULT '0',
  `mod_d213` smallint(6) NOT NULL DEFAULT '0',
  `mod_d214` smallint(6) NOT NULL DEFAULT '0',
  `mod_d215` smallint(6) NOT NULL DEFAULT '0',
  `mod_d216` smallint(6) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `id` (`id`),
  UNIQUE KEY `name` (`name`)
) ENGINE=InnoDB AUTO_INCREMENT=303 DEFAULT CHARSET=latin1;

CREATE TABLE `faction_values` (
  `char_id` int(4) NOT NULL DEFAULT '0',
  `faction_id` int(4) NOT NULL DEFAULT '0',
  `current_value` smallint(6) NOT NULL DEFAULT '0',
  PRIMARY KEY (`char_id`,`faction_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `failed_login_attempts` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `ip` varchar(16) NOT NULL DEFAULT '',
  `failed_attempts` int(11) NOT NULL DEFAULT '0',
  `lockout_time_stamp` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `fishing` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `zoneid` int(4) NOT NULL DEFAULT '0',
  `Itemid` int(11) NOT NULL DEFAULT '0',
  `shortname` varchar(60) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=187 DEFAULT CHARSET=latin1;

CREATE TABLE `forage` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `zoneid` int(4) NOT NULL DEFAULT '0',
  `Itemid` int(11) NOT NULL DEFAULT '0',
  `level` smallint(6) unsigned NOT NULL DEFAULT '0',
  `shortname` varchar(60) DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=200 DEFAULT CHARSET=latin1;

CREATE TABLE `grid` (
  `id` int(10) NOT NULL DEFAULT '0',
  `zoneid` int(10) NOT NULL DEFAULT '0',
  `type` int(10) NOT NULL DEFAULT '0',
  `type2` int(10) NOT NULL DEFAULT '0',
  PRIMARY KEY (`zoneid`,`id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `grid_entries` (
  `gridid` int(10) NOT NULL DEFAULT '0',
  `zoneid` int(10) NOT NULL DEFAULT '0',
  `number` int(10) NOT NULL DEFAULT '0',
  `x` float NOT NULL DEFAULT '0',
  `y` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `heading` float NOT NULL DEFAULT '0',
  `pause` int(10) NOT NULL DEFAULT '0',
  PRIMARY KEY (`zoneid`,`gridid`,`number`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `guilds` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `eqid` smallint(4) NOT NULL DEFAULT '0',
  `name` varchar(32) NOT NULL DEFAULT '',
  `leader` int(11) NOT NULL DEFAULT '0',
  `motd` text NOT NULL,
  `rank0title` varchar(100) NOT NULL DEFAULT '',
  `rank1title` varchar(100) NOT NULL DEFAULT '',
  `rank1` varchar(8) NOT NULL DEFAULT '',
  `rank2title` varchar(100) NOT NULL DEFAULT '',
  `rank2` varchar(8) NOT NULL DEFAULT '',
  `rank3title` varchar(100) NOT NULL DEFAULT '',
  `rank3` varchar(8) NOT NULL DEFAULT '',
  `rank4title` varchar(100) NOT NULL DEFAULT '',
  `rank4` varchar(8) NOT NULL DEFAULT '',
  `rank5title` varchar(100) NOT NULL DEFAULT '',
  `rank5` varchar(8) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  UNIQUE KEY `leader` (`leader`),
  UNIQUE KEY `name` (`name`),
  UNIQUE KEY `eqid` (`eqid`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `inventory` (
  `charid` int(11) unsigned DEFAULT '0',
  `slotid` mediumint(7) unsigned DEFAULT '0',
  `itemid` int(11) unsigned DEFAULT '0',
  `charges` tinyint(3) unsigned DEFAULT '0',
  `color` int(11) unsigned NOT NULL DEFAULT '0',
  UNIQUE KEY `slotid` (`slotid`,`charid`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `items` (
  `id` int(11) NOT NULL DEFAULT '0',
  `raw_data` blob,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `items_axclassic` (
  `id` int(11) NOT NULL DEFAULT '0',
  `minstatus` smallint(5) NOT NULL DEFAULT '0',
  `Name` varchar(64) NOT NULL DEFAULT '',
  `aagi` int(11) NOT NULL DEFAULT '0',
  `ac` int(11) NOT NULL DEFAULT '0',
  `accuracy` int(11) NOT NULL DEFAULT '0',
  `acha` int(11) NOT NULL DEFAULT '0',
  `adex` int(11) NOT NULL DEFAULT '0',
  `aint` int(11) NOT NULL DEFAULT '0',
  `artifactflag` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `asta` int(11) NOT NULL DEFAULT '0',
  `astr` int(11) NOT NULL DEFAULT '0',
  `attack` int(11) NOT NULL DEFAULT '0',
  `augrestrict` int(11) NOT NULL DEFAULT '0',
  `augslot1type` tinyint(3) NOT NULL DEFAULT '0',
  `augslot1visible` tinyint(3) DEFAULT NULL,
  `augslot2type` tinyint(3) NOT NULL DEFAULT '0',
  `augslot2visible` tinyint(3) DEFAULT NULL,
  `augslot3type` tinyint(3) NOT NULL DEFAULT '0',
  `augslot3visible` tinyint(3) DEFAULT NULL,
  `augslot4type` tinyint(3) NOT NULL DEFAULT '0',
  `augslot4visible` tinyint(3) DEFAULT NULL,
  `augslot5type` tinyint(3) NOT NULL DEFAULT '0',
  `augslot5visible` tinyint(3) DEFAULT NULL,
  `augtype` int(11) NOT NULL DEFAULT '0',
  `avoidance` int(11) NOT NULL DEFAULT '0',
  `awis` int(11) NOT NULL DEFAULT '0',
  `bagsize` int(11) NOT NULL DEFAULT '0',
  `bagslots` int(11) NOT NULL DEFAULT '0',
  `bagtype` int(11) NOT NULL DEFAULT '0',
  `bagwr` int(11) NOT NULL DEFAULT '0',
  `banedmgamt` int(11) NOT NULL DEFAULT '0',
  `banedmgraceamt` int(11) NOT NULL DEFAULT '0',
  `banedmgbody` int(11) NOT NULL DEFAULT '0',
  `banedmgrace` int(11) NOT NULL DEFAULT '0',
  `bardtype` int(11) NOT NULL DEFAULT '0',
  `bardvalue` int(11) NOT NULL DEFAULT '0',
  `book` int(11) NOT NULL DEFAULT '0',
  `casttime` int(11) NOT NULL DEFAULT '0',
  `casttime_` int(11) NOT NULL DEFAULT '0',
  `charmfile` varchar(32) NOT NULL DEFAULT '',
  `charmfileid` varchar(32) NOT NULL DEFAULT '',
  `classes` int(11) NOT NULL DEFAULT '0',
  `color` int(10) unsigned NOT NULL DEFAULT '0',
  `combateffects` varchar(10) NOT NULL DEFAULT '',
  `extradmgskill` int(11) NOT NULL DEFAULT '0',
  `extradmgamt` int(11) NOT NULL DEFAULT '0',
  `price` int(11) NOT NULL DEFAULT '0',
  `cr` int(11) NOT NULL DEFAULT '0',
  `damage` int(11) NOT NULL DEFAULT '0',
  `damageshield` int(11) NOT NULL DEFAULT '0',
  `deity` int(11) NOT NULL DEFAULT '0',
  `delay` int(11) NOT NULL DEFAULT '0',
  `augdistiller` int(11) NOT NULL DEFAULT '0',
  `dotshielding` int(11) NOT NULL DEFAULT '0',
  `dr` int(11) NOT NULL DEFAULT '0',
  `clicktype` int(11) NOT NULL DEFAULT '0',
  `clicklevel2` int(11) NOT NULL DEFAULT '0',
  `elemdmgtype` int(11) NOT NULL DEFAULT '0',
  `elemdmgamt` int(11) NOT NULL DEFAULT '0',
  `endur` int(11) NOT NULL DEFAULT '0',
  `factionamt1` int(11) NOT NULL DEFAULT '0',
  `factionamt2` int(11) NOT NULL DEFAULT '0',
  `factionamt3` int(11) NOT NULL DEFAULT '0',
  `factionamt4` int(11) NOT NULL DEFAULT '0',
  `factionmod1` int(11) NOT NULL DEFAULT '0',
  `factionmod2` int(11) NOT NULL DEFAULT '0',
  `factionmod3` int(11) NOT NULL DEFAULT '0',
  `factionmod4` int(11) NOT NULL DEFAULT '0',
  `filename` varchar(32) NOT NULL DEFAULT '',
  `focuseffect` int(11) NOT NULL DEFAULT '0',
  `fr` int(11) NOT NULL DEFAULT '0',
  `fvnodrop` int(11) NOT NULL DEFAULT '0',
  `haste` int(11) NOT NULL DEFAULT '0',
  `clicklevel` int(11) NOT NULL DEFAULT '0',
  `hp` int(11) NOT NULL DEFAULT '0',
  `regen` int(11) NOT NULL DEFAULT '0',
  `icon` int(11) NOT NULL DEFAULT '0',
  `idfile` varchar(30) NOT NULL DEFAULT '',
  `itemclass` int(11) NOT NULL DEFAULT '0',
  `itemtype` int(11) NOT NULL DEFAULT '0',
  `ldonprice` int(11) NOT NULL DEFAULT '0',
  `ldontheme` int(11) NOT NULL DEFAULT '0',
  `ldonsold` int(11) NOT NULL DEFAULT '0',
  `light` int(11) NOT NULL DEFAULT '0',
  `lore` varchar(80) NOT NULL DEFAULT '',
  `loregroup` int(11) NOT NULL DEFAULT '0',
  `magic` int(11) NOT NULL DEFAULT '0',
  `mana` int(11) NOT NULL DEFAULT '0',
  `manaregen` int(11) NOT NULL DEFAULT '0',
  `enduranceregen` int(11) NOT NULL DEFAULT '0',
  `material` int(11) NOT NULL DEFAULT '0',
  `maxcharges` int(11) NOT NULL DEFAULT '0',
  `mr` int(11) NOT NULL DEFAULT '0',
  `nodrop` int(11) NOT NULL DEFAULT '0',
  `norent` int(11) NOT NULL DEFAULT '0',
  `pendingloreflag` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `pr` int(11) NOT NULL DEFAULT '0',
  `procrate` int(11) NOT NULL DEFAULT '0',
  `races` int(11) NOT NULL DEFAULT '0',
  `range` int(11) NOT NULL DEFAULT '0',
  `reclevel` int(11) NOT NULL DEFAULT '0',
  `recskill` int(11) NOT NULL DEFAULT '0',
  `reqlevel` int(11) NOT NULL DEFAULT '0',
  `sellrate` float NOT NULL DEFAULT '0',
  `shielding` int(11) NOT NULL DEFAULT '0',
  `size` int(11) NOT NULL DEFAULT '0',
  `skillmodtype` int(11) NOT NULL DEFAULT '0',
  `skillmodvalue` int(11) NOT NULL DEFAULT '0',
  `slots` int(11) NOT NULL DEFAULT '0',
  `clickeffect` int(11) NOT NULL DEFAULT '0',
  `spellshield` int(11) NOT NULL DEFAULT '0',
  `strikethrough` int(11) NOT NULL DEFAULT '0',
  `stunresist` int(11) NOT NULL DEFAULT '0',
  `summonedflag` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `tradeskills` int(11) NOT NULL DEFAULT '0',
  `favor` int(11) NOT NULL DEFAULT '0',
  `weight` int(11) NOT NULL DEFAULT '0',
  `unknown002` int(11) NOT NULL DEFAULT '0',
  `unknown003` int(11) NOT NULL DEFAULT '0',
  `unknown005` int(11) NOT NULL DEFAULT '0',
  `unknown007` int(11) NOT NULL DEFAULT '0',
  `unknown018` int(11) NOT NULL DEFAULT '0',
  `unknown019` int(11) NOT NULL DEFAULT '0',
  `unknown020` int(11) NOT NULL DEFAULT '0',
  `UNK012` int(11) NOT NULL DEFAULT '0',
  `UNK013` int(11) NOT NULL DEFAULT '0',
  `benefitflag` int(11) NOT NULL DEFAULT '0',
  `unknown061` int(11) NOT NULL DEFAULT '0',
  `UNK054` int(11) NOT NULL DEFAULT '0',
  `unknown067` int(11) NOT NULL DEFAULT '0',
  `unknown069` int(11) NOT NULL DEFAULT '0',
  `UNK059` int(11) NOT NULL DEFAULT '0',
  `UNK061` int(11) NOT NULL DEFAULT '0',
  `unknown081` int(11) NOT NULL DEFAULT '0',
  `unknown105` int(11) NOT NULL DEFAULT '0',
  `booktype` int(11) NOT NULL DEFAULT '0',
  `unknown122` int(11) NOT NULL DEFAULT '0',
  `unknown123` varchar(11) NOT NULL DEFAULT '0',
  `unknown124` varchar(11) NOT NULL DEFAULT '0',
  `recastdelay` int(11) NOT NULL DEFAULT '0',
  `recasttype` int(11) NOT NULL DEFAULT '0',
  `guildfavor` int(11) NOT NULL DEFAULT '0',
  `unknown128` varchar(11) NOT NULL DEFAULT '0',
  `UNK123` int(11) NOT NULL DEFAULT '0',
  `UNK124` int(11) NOT NULL DEFAULT '0',
  `attuneable` int(11) NOT NULL DEFAULT '0',
  `nopet` int(11) NOT NULL DEFAULT '0',
  `unknown133` varchar(11) NOT NULL DEFAULT '0',
  `updated` datetime NOT NULL DEFAULT '0000-00-00 00:00:00',
  `comment` varchar(255) NOT NULL DEFAULT '',
  `UNK127` int(11) NOT NULL DEFAULT '0',
  `pointtype` int(11) NOT NULL DEFAULT '0',
  `potionbelt` int(11) NOT NULL DEFAULT '0',
  `potionbeltslots` int(11) NOT NULL DEFAULT '0',
  `stacksize` int(11) NOT NULL DEFAULT '0',
  `notransfer` int(11) NOT NULL DEFAULT '0',
  `stackable` int(11) NOT NULL DEFAULT '0',
  `UNK134` varchar(255) NOT NULL DEFAULT '',
  `UNK137` int(11) NOT NULL DEFAULT '0',
  `proceffect` int(11) NOT NULL DEFAULT '0',
  `proctype` int(11) NOT NULL DEFAULT '0',
  `proclevel2` int(11) NOT NULL DEFAULT '0',
  `proclevel` int(11) NOT NULL DEFAULT '0',
  `UNK142` int(11) NOT NULL DEFAULT '0',
  `worneffect` int(11) NOT NULL DEFAULT '0',
  `worntype` int(11) NOT NULL DEFAULT '0',
  `wornlevel2` int(11) NOT NULL DEFAULT '0',
  `wornlevel` int(11) NOT NULL DEFAULT '0',
  `UNK147` int(11) NOT NULL DEFAULT '0',
  `focustype` int(11) NOT NULL DEFAULT '0',
  `focuslevel2` int(11) NOT NULL DEFAULT '0',
  `focuslevel` int(11) NOT NULL DEFAULT '0',
  `UNK152` int(11) NOT NULL DEFAULT '0',
  `scrolleffect` int(11) NOT NULL DEFAULT '0',
  `scrolltype` int(11) NOT NULL DEFAULT '0',
  `scrolllevel2` int(11) NOT NULL DEFAULT '0',
  `scrolllevel` int(11) NOT NULL DEFAULT '0',
  `UNK157` int(11) NOT NULL DEFAULT '0',
  `serialized` datetime DEFAULT NULL,
  `verified` datetime DEFAULT NULL,
  `serialization` text,
  `source` varchar(20) NOT NULL DEFAULT '',
  `UNK033` int(11) NOT NULL DEFAULT '0',
  `lorefile` varchar(32) NOT NULL DEFAULT '',
  `UNK014` int(11) NOT NULL DEFAULT '0',
  `svcorruption` int(11) NOT NULL DEFAULT '0',
  `UNK038` int(11) NOT NULL DEFAULT '0',
  `UNK060` int(11) NOT NULL DEFAULT '0',
  `augslot1unk2` int(11) NOT NULL DEFAULT '0',
  `augslot2unk2` int(11) NOT NULL DEFAULT '0',
  `augslot3unk2` int(11) NOT NULL DEFAULT '0',
  `augslot4unk2` int(11) NOT NULL DEFAULT '0',
  `augslot5unk2` int(11) NOT NULL DEFAULT '0',
  `UNK098` int(11) NOT NULL DEFAULT '0',
  `UNK109` int(11) NOT NULL DEFAULT '0',
  `UNK120` int(11) NOT NULL DEFAULT '0',
  `UNK121` int(11) NOT NULL DEFAULT '0',
  `questitemflag` int(11) NOT NULL DEFAULT '0',
  `UNK131` int(11) NOT NULL DEFAULT '0',
  `UNK132` text NOT NULL,
  `clickunk5` int(11) NOT NULL DEFAULT '0',
  `clickunk6` varchar(32) NOT NULL DEFAULT '',
  `clickunk7` int(11) NOT NULL DEFAULT '0',
  `procunk1` int(11) NOT NULL DEFAULT '0',
  `procunk2` int(11) NOT NULL DEFAULT '0',
  `procunk3` int(11) NOT NULL DEFAULT '0',
  `procunk4` int(11) NOT NULL DEFAULT '0',
  `procunk6` varchar(32) NOT NULL DEFAULT '',
  `procunk7` int(11) NOT NULL DEFAULT '0',
  `wornunk1` int(11) NOT NULL DEFAULT '0',
  `wornunk2` int(11) NOT NULL DEFAULT '0',
  `wornunk3` int(11) NOT NULL DEFAULT '0',
  `wornunk4` int(11) NOT NULL DEFAULT '0',
  `wornunk5` int(11) NOT NULL DEFAULT '0',
  `wornunk6` varchar(32) NOT NULL DEFAULT '',
  `wornunk7` int(11) NOT NULL DEFAULT '0',
  `focusunk1` int(11) NOT NULL DEFAULT '0',
  `focusunk2` int(11) NOT NULL DEFAULT '0',
  `focusunk3` int(11) NOT NULL DEFAULT '0',
  `focusunk4` int(11) NOT NULL DEFAULT '0',
  `focusunk5` int(11) NOT NULL DEFAULT '0',
  `focusunk6` varchar(32) NOT NULL DEFAULT '',
  `focusunk7` int(11) NOT NULL DEFAULT '0',
  `scrollunk1` int(11) NOT NULL DEFAULT '0',
  `scrollunk2` int(11) NOT NULL DEFAULT '0',
  `scrollunk3` int(11) NOT NULL DEFAULT '0',
  `scrollunk4` int(11) NOT NULL DEFAULT '0',
  `scrollunk5` int(11) NOT NULL DEFAULT '0',
  `scrollunk6` varchar(32) NOT NULL DEFAULT '',
  `scrollunk7` int(11) NOT NULL DEFAULT '0',
  `UNK193` int(11) NOT NULL DEFAULT '0',
  `purity` int(11) NOT NULL DEFAULT '0',
  `evolvinglevel` int(11) NOT NULL DEFAULT '0',
  `UNK129` int(11) NOT NULL DEFAULT '0',
  `clickname` varchar(64) NOT NULL DEFAULT '',
  `procname` varchar(64) NOT NULL DEFAULT '',
  `wornname` varchar(64) NOT NULL DEFAULT '',
  `focusname` varchar(64) NOT NULL DEFAULT '',
  `scrollname` varchar(64) NOT NULL DEFAULT '',
  `dsmitigation` smallint(6) NOT NULL DEFAULT '0',
  `heroic_str` smallint(6) NOT NULL DEFAULT '0',
  `heroic_int` smallint(6) NOT NULL DEFAULT '0',
  `heroic_wis` smallint(6) NOT NULL DEFAULT '0',
  `heroic_agi` smallint(6) NOT NULL DEFAULT '0',
  `heroic_dex` smallint(6) NOT NULL DEFAULT '0',
  `heroic_sta` smallint(6) NOT NULL DEFAULT '0',
  `heroic_cha` smallint(6) NOT NULL DEFAULT '0',
  `healamt` smallint(6) NOT NULL DEFAULT '0',
  `spelldmg` smallint(6) NOT NULL DEFAULT '0',
  `clairvoyance` smallint(6) NOT NULL DEFAULT '0',
  `backstabdmg` smallint(6) NOT NULL DEFAULT '0',
  `created` varchar(64) NOT NULL DEFAULT '',
  UNIQUE KEY `ID` (`id`),
  KEY `name_idx` (`Name`),
  KEY `lore_idx` (`lore`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `items_nonblob` (
  `id` int(11) NOT NULL DEFAULT '0',
  `Name` varchar(64) NOT NULL DEFAULT '',
  `aagi` int(11) NOT NULL DEFAULT '0',
  `ac` int(11) NOT NULL DEFAULT '0',
  `accuracy` int(11) NOT NULL DEFAULT '0',
  `acha` int(11) NOT NULL DEFAULT '0',
  `adex` int(11) NOT NULL DEFAULT '0',
  `aint` int(11) NOT NULL DEFAULT '0',
  `asta` int(11) NOT NULL DEFAULT '0',
  `astr` int(11) NOT NULL DEFAULT '0',
  `attack` int(11) NOT NULL DEFAULT '0',
  `awis` int(11) NOT NULL DEFAULT '0',
  `bagsize` int(11) NOT NULL DEFAULT '0',
  `bagslots` int(11) NOT NULL DEFAULT '0',
  `bagtype` int(11) NOT NULL DEFAULT '0',
  `bagwr` int(11) NOT NULL DEFAULT '0',
  `bardtype` int(11) NOT NULL DEFAULT '0',
  `bardvalue` int(11) NOT NULL DEFAULT '0',
  `book` int(11) NOT NULL DEFAULT '0',
  `casttime` int(11) NOT NULL DEFAULT '0',
  `casttime_` int(11) NOT NULL DEFAULT '0',
  `charmfile` varchar(32) NOT NULL DEFAULT '',
  `charmfileid` varchar(32) NOT NULL DEFAULT '',
  `classes` int(11) NOT NULL DEFAULT '0',
  `color` int(11) NOT NULL DEFAULT '0',
  `price` int(11) NOT NULL DEFAULT '0',
  `cr` int(11) NOT NULL DEFAULT '0',
  `damage` int(11) NOT NULL DEFAULT '0',
  `damageshield` int(11) NOT NULL DEFAULT '0',
  `deity` int(11) NOT NULL DEFAULT '0',
  `delay` int(11) NOT NULL DEFAULT '0',
  `dotshielding` int(11) NOT NULL DEFAULT '0',
  `dr` int(11) NOT NULL DEFAULT '0',
  `clicktype` int(11) NOT NULL DEFAULT '0',
  `clicklevel2` int(11) NOT NULL DEFAULT '0',
  `filename` varchar(32) NOT NULL DEFAULT '',
  `fr` int(11) NOT NULL DEFAULT '0',
  `fvnodrop` int(11) NOT NULL DEFAULT '0',
  `haste` int(11) NOT NULL DEFAULT '0',
  `clicklevel` int(11) NOT NULL DEFAULT '0',
  `hp` int(11) NOT NULL DEFAULT '0',
  `regen` int(11) NOT NULL DEFAULT '0',
  `icon` int(11) NOT NULL DEFAULT '0',
  `idfile` varchar(30) NOT NULL DEFAULT '',
  `itemclass` int(11) NOT NULL DEFAULT '0',
  `itemtype` int(11) NOT NULL DEFAULT '0',
  `light` int(11) NOT NULL DEFAULT '0',
  `lore` varchar(80) NOT NULL DEFAULT '',
  `loreflag` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `magic` int(11) NOT NULL DEFAULT '0',
  `mana` int(11) NOT NULL DEFAULT '0',
  `manaregen` int(11) NOT NULL DEFAULT '0',
  `material` int(11) NOT NULL DEFAULT '0',
  `maxcharges` int(11) NOT NULL DEFAULT '0',
  `mr` int(11) NOT NULL DEFAULT '0',
  `nodrop` int(11) NOT NULL DEFAULT '0',
  `norent` int(11) NOT NULL DEFAULT '0',
  `pendingloreflag` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `pr` int(11) NOT NULL DEFAULT '0',
  `races` int(11) NOT NULL DEFAULT '0',
  `range` int(11) NOT NULL DEFAULT '0',
  `reclevel` int(11) NOT NULL DEFAULT '0',
  `recskill` int(11) NOT NULL DEFAULT '0',
  `reqlevel` int(11) NOT NULL DEFAULT '0',
  `sellrate` float NOT NULL DEFAULT '0',
  `shielding` int(11) NOT NULL DEFAULT '0',
  `size` int(11) NOT NULL DEFAULT '0',
  `skillmodtype` int(11) NOT NULL DEFAULT '0',
  `skillmodvalue` int(11) NOT NULL DEFAULT '0',
  `slots` int(11) NOT NULL DEFAULT '0',
  `clickeffect` int(11) NOT NULL DEFAULT '0',
  `spellshield` int(11) NOT NULL DEFAULT '0',
  `stunresist` int(11) NOT NULL DEFAULT '0',
  `summonedflag` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `tradeskills` int(11) NOT NULL DEFAULT '0',
  `weight` int(11) NOT NULL DEFAULT '0',
  `booktype` int(11) NOT NULL DEFAULT '0',
  `recastdelay` int(11) NOT NULL DEFAULT '0',
  `recasttype` int(11) NOT NULL DEFAULT '0',
  `attuneable` int(11) NOT NULL DEFAULT '0',
  `nopet` int(11) NOT NULL DEFAULT '0',
  `pointtype` int(11) NOT NULL DEFAULT '0',
  `potionbelt` int(11) NOT NULL DEFAULT '0',
  `stacksize` int(11) NOT NULL DEFAULT '0',
  `proceffect` int(11) NOT NULL DEFAULT '0',
  `proctype` int(11) NOT NULL DEFAULT '0',
  `proclevel2` int(11) NOT NULL DEFAULT '0',
  `proclevel` int(11) NOT NULL DEFAULT '0',
  `worneffect` int(11) NOT NULL DEFAULT '0',
  `worntype` int(11) NOT NULL DEFAULT '0',
  `wornlevel2` int(11) NOT NULL DEFAULT '0',
  `wornlevel` int(11) NOT NULL DEFAULT '0',
  `focustype` int(11) NOT NULL DEFAULT '0',
  `focuslevel2` int(11) NOT NULL DEFAULT '0',
  `focuslevel` int(11) NOT NULL DEFAULT '0',
  `scrolleffect` int(11) NOT NULL DEFAULT '0',
  `scrolltype` int(11) NOT NULL DEFAULT '0',
  `scrolllevel2` int(11) NOT NULL DEFAULT '0',
  `scrolllevel` int(11) NOT NULL DEFAULT '0',
  UNIQUE KEY `ID` (`id`),
  KEY `name_idx` (`Name`),
  KEY `lore_idx` (`lore`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `login_accounts` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(18) NOT NULL DEFAULT '',
  `password` varchar(45) NOT NULL DEFAULT '',
  `ip` varchar(16) NOT NULL DEFAULT '',
  `auth` tinyblob,
  `lsadmin` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `lsstatus` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `worldadmin` varchar(45) NOT NULL DEFAULT '',
  `user_active` varchar(45) NOT NULL DEFAULT '',
  `user_lastvisit` varchar(45) NOT NULL DEFAULT '',
  `email` varchar(100) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`name`)
) ENGINE=MyISAM AUTO_INCREMENT=16 DEFAULT CHARSET=latin1;

CREATE TABLE `login_authchange` (
  `account_id` int(11) unsigned NOT NULL DEFAULT '0',
  `ip` varchar(16) NOT NULL DEFAULT '',
  `time` timestamp NOT NULL DEFAULT '0000-00-00 00:00:00',
  PRIMARY KEY (`account_id`),
  UNIQUE KEY `ip` (`ip`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `login_versions` (
  `approval` int(10) unsigned NOT NULL DEFAULT '0',
  `version` varchar(45) NOT NULL DEFAULT '',
  PRIMARY KEY (`approval`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `login_worldservers` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `account` varchar(30) NOT NULL DEFAULT '',
  `password` varchar(30) NOT NULL DEFAULT '',
  `name` varchar(250) NOT NULL DEFAULT '',
  `admin_id` int(11) unsigned NOT NULL DEFAULT '0',
  `greenname` tinyint(1) unsigned NOT NULL DEFAULT '0',
  `showdown` tinyint(4) NOT NULL DEFAULT '0',
  `chat` tinyint(4) NOT NULL DEFAULT '0',
  `note` tinytext,
  PRIMARY KEY (`id`),
  UNIQUE KEY `account` (`account`)
) ENGINE=InnoDB AUTO_INCREMENT=2 DEFAULT CHARSET=latin1;

CREATE TABLE `lootdrop` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(255) CHARACTER SET utf8 NOT NULL DEFAULT '',
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=87477 DEFAULT CHARSET=latin1 PACK_KEYS=0 ROW_FORMAT=DYNAMIC;

CREATE TABLE `lootdrop_entries` (
  `lootdrop_id` int(10) unsigned NOT NULL DEFAULT '0',
  `item_id` int(11) NOT NULL DEFAULT '0',
  `item_charges` tinyint(2) unsigned NOT NULL DEFAULT '1',
  `equip_item` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `chance` tinyint(2) unsigned NOT NULL DEFAULT '1',
  PRIMARY KEY (`lootdrop_id`,`item_id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1 ROW_FORMAT=DYNAMIC;

CREATE TABLE `loottable` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `name` varchar(255) CHARACTER SET utf8 NOT NULL DEFAULT '',
  `mincash` int(10) unsigned NOT NULL DEFAULT '0',
  `maxcash` int(10) unsigned NOT NULL DEFAULT '0',
  `avgcoin` smallint(4) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=87478 DEFAULT CHARSET=latin1 PACK_KEYS=0 ROW_FORMAT=DYNAMIC;

CREATE TABLE `loottable_entries` (
  `loottable_id` int(10) unsigned NOT NULL DEFAULT '0',
  `lootdrop_id` int(10) unsigned NOT NULL DEFAULT '0',
  `multiplier` tinyint(2) unsigned NOT NULL DEFAULT '1',
  `probability` tinyint(2) unsigned NOT NULL DEFAULT '100',
  PRIMARY KEY (`loottable_id`,`lootdrop_id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1 ROW_FORMAT=DYNAMIC;

CREATE TABLE `mb_messages` (
  `id` int(11) NOT NULL DEFAULT '0',
  `date` varchar(10) NOT NULL DEFAULT '',
  `author` varchar(30) NOT NULL DEFAULT '',
  `language` tinyint(4) NOT NULL DEFAULT '0',
  `subject` varchar(30) NOT NULL DEFAULT '',
  `message` text NOT NULL,
  `category` tinyint(4) NOT NULL DEFAULT '0',
  `time` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`,`category`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `merchantlist` (
  `merchantid` int(11) NOT NULL DEFAULT '0',
  `slot` int(11) NOT NULL DEFAULT '0',
  `item` int(11) NOT NULL DEFAULT '0',
  `stack` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`merchantid`,`slot`),
  UNIQUE KEY `merchantid` (`merchantid`,`item`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1 ROW_FORMAT=DYNAMIC;

CREATE TABLE `name_filter` (
  `name` varchar(30) NOT NULL DEFAULT '',
  PRIMARY KEY (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `npc_faction` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` tinytext,
  `primaryfaction` int(11) NOT NULL DEFAULT '0',
  `ignore_primary_assist` tinyint(3) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=19529 DEFAULT CHARSET=latin1 PACK_KEYS=0;

CREATE TABLE `npc_faction_entries` (
  `npc_faction_id` int(11) unsigned NOT NULL DEFAULT '0',
  `faction_id` int(11) unsigned NOT NULL DEFAULT '0',
  `value` int(11) NOT NULL DEFAULT '0',
  `npc_value` tinyint(3) NOT NULL DEFAULT '0',
  PRIMARY KEY (`npc_faction_id`,`faction_id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `npc_spells` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `class` int(11) unsigned NOT NULL DEFAULT '0',
  `level` int(11) unsigned NOT NULL DEFAULT '0',
  `buff1` int(11) unsigned NOT NULL DEFAULT '50000',
  `buff2` int(11) unsigned NOT NULL DEFAULT '50000',
  `heal` int(11) unsigned NOT NULL DEFAULT '50000',
  `gate` int(11) unsigned NOT NULL DEFAULT '50000',
  `debuff1` int(11) unsigned NOT NULL DEFAULT '50000',
  `debuff2` int(11) unsigned NOT NULL DEFAULT '50000',
  `debuff3` int(11) unsigned NOT NULL DEFAULT '50000',
  `debuff4` int(11) unsigned NOT NULL DEFAULT '50000',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=50 DEFAULT CHARSET=latin1;

CREATE TABLE `npc_spells_effects` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `name` tinytext,
  `parent_list` int(11) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=31 DEFAULT CHARSET=latin1;

CREATE TABLE `npc_spells_effects_entries` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `npc_spells_effects_id` int(11) NOT NULL DEFAULT '0',
  `spell_effect_id` smallint(5) NOT NULL DEFAULT '0',
  `minlevel` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `maxlevel` tinyint(3) unsigned NOT NULL DEFAULT '255',
  `se_base` int(11) NOT NULL DEFAULT '0',
  `se_limit` int(11) NOT NULL DEFAULT '0',
  `se_max` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `spellsid_spellid` (`npc_spells_effects_id`,`spell_effect_id`)
) ENGINE=InnoDB AUTO_INCREMENT=31 DEFAULT CHARSET=latin1;

CREATE TABLE `npc_spells_entries` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `npc_spells_id` int(11) NOT NULL DEFAULT '0',
  `spellid` smallint(5) NOT NULL DEFAULT '0',
  `type` smallint(5) unsigned NOT NULL DEFAULT '0',
  `minlevel` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `maxlevel` tinyint(3) unsigned NOT NULL DEFAULT '255',
  `manacost` smallint(5) NOT NULL DEFAULT '-1',
  `recast_delay` int(11) NOT NULL DEFAULT '-1',
  `priority` smallint(5) NOT NULL DEFAULT '0',
  `resist_adjust` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `spellsid_spellid` (`npc_spells_id`,`spellid`)
) ENGINE=MyISAM AUTO_INCREMENT=18556 DEFAULT CHARSET=latin1;

CREATE TABLE `npc_types` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` text CHARACTER SET utf8 NOT NULL,
  `lastname` varchar(32) CHARACTER SET utf8 DEFAULT NULL,
  `level` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `race` smallint(5) unsigned NOT NULL DEFAULT '0',
  `class` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `bodytype` int(11) DEFAULT NULL,
  `hp` int(11) NOT NULL DEFAULT '0',
  `gender` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `texture` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `helmtexture` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `size` float NOT NULL DEFAULT '0',
  `hp_regen_rate` int(11) NOT NULL DEFAULT '0',
  `mana_regen_rate` int(11) NOT NULL DEFAULT '0',
  `loottable_id` int(10) unsigned NOT NULL DEFAULT '0',
  `merchant_id` int(10) unsigned NOT NULL DEFAULT '0',
  `npc_spells_id` int(10) unsigned NOT NULL DEFAULT '0',
  `npc_faction_id` int(11) NOT NULL DEFAULT '0',
  `mindmg` int(10) unsigned NOT NULL DEFAULT '0',
  `maxdmg` int(10) unsigned NOT NULL DEFAULT '0',
  `npcspecialattks` varchar(36) CHARACTER SET utf8 NOT NULL DEFAULT '',
  `aggroradius` int(10) unsigned NOT NULL DEFAULT '0',
  `face` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_hairstyle` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_haircolor` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_eyecolor` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_eyecolor2` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_beardcolor` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_beard` int(10) unsigned NOT NULL DEFAULT '0',
  `d_meele_texture1` int(10) unsigned NOT NULL DEFAULT '0',
  `d_meele_texture2` int(10) unsigned NOT NULL DEFAULT '0',
  `runspeed` float NOT NULL DEFAULT '0',
  `MR` smallint(5) NOT NULL DEFAULT '0',
  `CR` smallint(5) NOT NULL DEFAULT '0',
  `DR` smallint(5) NOT NULL DEFAULT '0',
  `FR` smallint(5) NOT NULL DEFAULT '0',
  `PR` smallint(5) NOT NULL DEFAULT '0',
  `see_invis` smallint(4) NOT NULL DEFAULT '0',
  `see_invis_undead` smallint(4) NOT NULL DEFAULT '0',
  `qglobal` int(2) unsigned NOT NULL DEFAULT '0',
  `AC` smallint(5) NOT NULL DEFAULT '0',
  `npc_aggro` tinyint(4) NOT NULL DEFAULT '0',
  `spawn_limit` tinyint(4) NOT NULL DEFAULT '0',
  `attack_speed` float NOT NULL DEFAULT '0',
  `findable` tinyint(4) NOT NULL DEFAULT '0',
  `STR` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `STA` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `DEX` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `AGI` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `_INT` mediumint(8) unsigned NOT NULL DEFAULT '80',
  `WIS` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `CHA` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `see_hide` tinyint(4) NOT NULL DEFAULT '0',
  `see_improved_hide` tinyint(4) NOT NULL DEFAULT '0',
  `trackable` tinyint(4) NOT NULL DEFAULT '1',
  `isbot` float DEFAULT '0',
  `exclude` float DEFAULT '1',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=337033 DEFAULT CHARSET=latin1 PACK_KEYS=0 ROW_FORMAT=DYNAMIC;

CREATE TABLE `npc_types_metadata` (
  `npc_types_id` int(11) NOT NULL DEFAULT '0',
  `isPKMob` tinyint(4) NOT NULL DEFAULT '0',
  `isNamedMob` tinyint(4) NOT NULL DEFAULT '0',
  `isRaidTarget` tinyint(4) NOT NULL DEFAULT '0',
  `isCreatedMob` tinyint(4) NOT NULL DEFAULT '0',
  `isCustomFeatureNPC` tinyint(4) NOT NULL DEFAULT '0',
  PRIMARY KEY (`npc_types_id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `npc_types_tint` (
  `id` int(10) unsigned NOT NULL DEFAULT '0',
  `tint_set_name` text NOT NULL,
  `red1h` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn1h` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu1h` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `red2c` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn2c` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu2c` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `red3a` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn3a` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu3a` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `red4b` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn4b` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu4b` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `red5g` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn5g` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu5g` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `red6l` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn6l` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu6l` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `red7f` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn7f` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu7f` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `red8x` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn8x` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu8x` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `red9x` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `grn9x` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `blu9x` tinyint(3) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `npc_types_without` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` text CHARACTER SET utf8 NOT NULL,
  `lastname` varchar(32) CHARACTER SET utf8 DEFAULT NULL,
  `level` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `race` smallint(5) unsigned NOT NULL DEFAULT '0',
  `class` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `bodytype` int(11) DEFAULT NULL,
  `hp` int(11) NOT NULL DEFAULT '0',
  `gender` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `texture` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `helmtexture` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `size` float NOT NULL DEFAULT '0',
  `hp_regen_rate` int(11) NOT NULL DEFAULT '0',
  `mana_regen_rate` int(11) NOT NULL DEFAULT '0',
  `loottable_id` int(10) unsigned NOT NULL DEFAULT '0',
  `merchant_id` int(10) unsigned NOT NULL DEFAULT '0',
  `npc_spells_id` int(10) unsigned NOT NULL DEFAULT '0',
  `npc_faction_id` int(11) NOT NULL DEFAULT '0',
  `mindmg` int(10) unsigned NOT NULL DEFAULT '0',
  `maxdmg` int(10) unsigned NOT NULL DEFAULT '0',
  `npcspecialattks` varchar(36) CHARACTER SET utf8 NOT NULL DEFAULT '',
  `aggroradius` int(10) unsigned NOT NULL DEFAULT '0',
  `face` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_hairstyle` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_haircolor` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_eyecolor` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_eyecolor2` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_beardcolor` int(10) unsigned NOT NULL DEFAULT '1',
  `luclin_beard` int(10) unsigned NOT NULL DEFAULT '0',
  `d_meele_texture1` int(10) unsigned NOT NULL DEFAULT '0',
  `d_meele_texture2` int(10) unsigned NOT NULL DEFAULT '0',
  `runspeed` float NOT NULL DEFAULT '0',
  `MR` smallint(5) NOT NULL DEFAULT '0',
  `CR` smallint(5) NOT NULL DEFAULT '0',
  `DR` smallint(5) NOT NULL DEFAULT '0',
  `FR` smallint(5) NOT NULL DEFAULT '0',
  `PR` smallint(5) NOT NULL DEFAULT '0',
  `see_invis` smallint(4) NOT NULL DEFAULT '0',
  `see_invis_undead` smallint(4) NOT NULL DEFAULT '0',
  `qglobal` int(2) unsigned NOT NULL DEFAULT '0',
  `AC` smallint(5) NOT NULL DEFAULT '0',
  `npc_aggro` tinyint(4) NOT NULL DEFAULT '0',
  `spawn_limit` tinyint(4) NOT NULL DEFAULT '0',
  `attack_speed` float NOT NULL DEFAULT '0',
  `findable` tinyint(4) NOT NULL DEFAULT '0',
  `STR` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `STA` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `DEX` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `AGI` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `_INT` mediumint(8) unsigned NOT NULL DEFAULT '80',
  `WIS` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `CHA` mediumint(8) unsigned NOT NULL DEFAULT '75',
  `see_hide` tinyint(4) NOT NULL DEFAULT '0',
  `see_improved_hide` tinyint(4) NOT NULL DEFAULT '0',
  `trackable` tinyint(4) NOT NULL DEFAULT '1',
  `isbot` float DEFAULT '0',
  `exclude` float DEFAULT '1',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=336167 DEFAULT CHARSET=latin1 PACK_KEYS=0 ROW_FORMAT=DYNAMIC;

CREATE TABLE `object` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `zone` varchar(20) NOT NULL DEFAULT '',
  `type` int(16) NOT NULL DEFAULT '0',
  `itemid` int(16) NOT NULL DEFAULT '0',
  `ypos` float NOT NULL DEFAULT '0',
  `xpos` float NOT NULL DEFAULT '0',
  `zpos` float NOT NULL DEFAULT '0',
  `heading` int(8) NOT NULL DEFAULT '0',
  `objectname` varchar(5) NOT NULL DEFAULT '',
  `charges` int(8) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=174 DEFAULT CHARSET=latin1;

CREATE TABLE `object_new` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `zoneid` int(10) unsigned NOT NULL DEFAULT '0',
  `xpos` float NOT NULL DEFAULT '0',
  `ypos` float NOT NULL DEFAULT '0',
  `zpos` float NOT NULL DEFAULT '0',
  `heading` float NOT NULL DEFAULT '0',
  `itemid` int(11) NOT NULL DEFAULT '0',
  `charges` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `objectname` varchar(16) CHARACTER SET utf8 NOT NULL DEFAULT '',
  `type` int(11) NOT NULL DEFAULT '0',
  `icon` int(11) NOT NULL DEFAULT '0',
  `linked_list_addr_01` int(11) NOT NULL DEFAULT '0',
  `linked_list_addr_02` int(11) NOT NULL DEFAULT '0',
  `unknown08` mediumint(5) NOT NULL DEFAULT '0',
  `unknown10` mediumint(5) NOT NULL DEFAULT '0',
  `unknown20` int(11) NOT NULL DEFAULT '0',
  `unknown24` int(11) NOT NULL DEFAULT '0',
  `unknown60` int(11) NOT NULL DEFAULT '0',
  `unknown64` int(11) NOT NULL DEFAULT '0',
  `unknown68` int(11) NOT NULL DEFAULT '0',
  `unknown72` int(11) NOT NULL DEFAULT '0',
  `unknown76` int(11) NOT NULL DEFAULT '0',
  `unknown84` int(11) NOT NULL DEFAULT '0',
  `unknown88` int(11) NOT NULL DEFAULT '0',
  `short_name` varchar(16) NOT NULL DEFAULT '',
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=91399 DEFAULT CHARSET=latin1 ROW_FORMAT=DYNAMIC;

CREATE TABLE `petitions` (
  `dib` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `petid` int(10) unsigned NOT NULL DEFAULT '0',
  `charname` varchar(32) NOT NULL DEFAULT '',
  `accountname` varchar(32) NOT NULL DEFAULT '',
  `lastgm` varchar(32) NOT NULL DEFAULT '',
  `petitiontext` text NOT NULL,
  `zone` varchar(32) NOT NULL DEFAULT '',
  `urgency` int(11) NOT NULL DEFAULT '0',
  `charclass` int(11) NOT NULL DEFAULT '0',
  `charrace` int(11) NOT NULL DEFAULT '0',
  `charlevel` int(11) NOT NULL DEFAULT '0',
  `checkouts` int(11) NOT NULL DEFAULT '0',
  `unavailables` int(11) NOT NULL DEFAULT '0',
  `ischeckedout` tinyint(4) NOT NULL DEFAULT '0',
  `senttime` bigint(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`dib`),
  KEY `petid` (`petid`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=latin1;

CREATE TABLE `pets` (
  `id` int(11) NOT NULL DEFAULT '0',
  `max_hp` int(11) NOT NULL DEFAULT '0',
  `cur_hp` int(11) NOT NULL DEFAULT '0',
  `min_dmg` int(10) unsigned NOT NULL DEFAULT '0',
  `max_dmg` int(10) unsigned NOT NULL DEFAULT '0',
  `race` smallint(5) unsigned NOT NULL DEFAULT '0',
  `class` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `level` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `size` float NOT NULL DEFAULT '0',
  `texture` tinyint(2) unsigned NOT NULL DEFAULT '0',
  `description` text NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `player_corpses` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `charid` int(11) unsigned NOT NULL DEFAULT '0',
  `charname` varchar(30) NOT NULL DEFAULT '',
  `accountid` int(11) unsigned NOT NULL DEFAULT '0',
  `zonename` varchar(16) NOT NULL DEFAULT '',
  `x` float NOT NULL DEFAULT '0',
  `y` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `heading` float NOT NULL DEFAULT '0',
  `data` blob NOT NULL,
  `time` int(11) unsigned NOT NULL DEFAULT '0',
  `reztime` int(11) unsigned NOT NULL DEFAULT '0',
  `rezexp` int(11) unsigned NOT NULL DEFAULT '0',
  `rezed` int(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `zonename` (`zonename`)
) ENGINE=InnoDB AUTO_INCREMENT=16 DEFAULT CHARSET=latin1;

CREATE TABLE `quest` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `zone` varchar(16) NOT NULL DEFAULT '',
  `name` varchar(32) NOT NULL DEFAULT '',
  `activator` varchar(64) NOT NULL DEFAULT '',
  `text` varchar(255) NOT NULL DEFAULT '',
  `end` varchar(64) NOT NULL DEFAULT '',
  `npcID` int(11) NOT NULL DEFAULT '0',
  `questobject` int(11) unsigned NOT NULL DEFAULT '0',
  `priceobject` int(11) unsigned NOT NULL DEFAULT '0',
  `cash` int(11) unsigned NOT NULL DEFAULT '0',
  `exp` int(11) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `quest_globals` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `charid` int(11) NOT NULL DEFAULT '0',
  `npcid` int(11) NOT NULL DEFAULT '0',
  `zoneid` int(11) NOT NULL DEFAULT '0',
  `name` varchar(65) NOT NULL DEFAULT '',
  `value` varchar(65) NOT NULL DEFAULT '?',
  `expdate` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `qname` (`name`,`charid`,`npcid`,`zoneid`)
) ENGINE=MyISAM AUTO_INCREMENT=12 DEFAULT CHARSET=utf8 ROW_FORMAT=DYNAMIC;

CREATE TABLE `spawn2` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `spawngroupID` int(11) NOT NULL DEFAULT '0',
  `zone` varchar(16) NOT NULL DEFAULT '',
  `x` float(14,6) NOT NULL DEFAULT '0.000000',
  `y` float(14,6) NOT NULL DEFAULT '0.000000',
  `z` float(14,6) NOT NULL DEFAULT '0.000000',
  `heading` float(14,6) NOT NULL DEFAULT '0.000000',
  `respawntime` int(11) NOT NULL DEFAULT '0',
  `variance` smallint(4) NOT NULL DEFAULT '0',
  `pathgrid` int(10) NOT NULL DEFAULT '0',
  `timeleft` bigint(16) NOT NULL DEFAULT '0',
  `_condition` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `cond_value` mediumint(9) NOT NULL DEFAULT '1',
  `roamRange` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `ZoneGroup` (`zone`,`spawngroupID`)
) ENGINE=MyISAM AUTO_INCREMENT=365234 DEFAULT CHARSET=latin1;

CREATE TABLE `spawn_backup` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `spawngroupID` int(11) NOT NULL DEFAULT '0',
  `zone` varchar(16) CHARACTER SET utf8 NOT NULL DEFAULT '',
  `x` float(14,6) NOT NULL DEFAULT '0.000000',
  `y` float(14,6) NOT NULL DEFAULT '0.000000',
  `z` float(14,6) NOT NULL DEFAULT '0.000000',
  `heading` float(14,6) NOT NULL DEFAULT '0.000000',
  `respawntime` int(11) NOT NULL DEFAULT '0',
  `variance` smallint(4) NOT NULL DEFAULT '0',
  `pathgrid` int(10) NOT NULL DEFAULT '0',
  `timeleft` bigint(16) NOT NULL DEFAULT '0',
  `_condition` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `cond_value` mediumint(9) NOT NULL DEFAULT '1',
  `roamRange` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `ZoneGroup` (`zone`,`spawngroupID`)
) ENGINE=InnoDB AUTO_INCREMENT=45641 DEFAULT CHARSET=latin1;

CREATE TABLE `spawn_condition_values` (
  `id` int(10) unsigned NOT NULL,
  `value` tinyint(3) unsigned DEFAULT NULL,
  `zone` varchar(64) NOT NULL,
  `instance_id` int(10) unsigned NOT NULL,
  UNIQUE KEY `instance` (`id`,`instance_id`,`zone`),
  KEY `zoneinstance` (`zone`,`instance_id`)
) ENGINE=MyISAM DEFAULT CHARSET=utf8;

CREATE TABLE `spawn_conditions` (
  `zone` varchar(16) NOT NULL DEFAULT '',
  `id` mediumint(8) unsigned NOT NULL DEFAULT '1',
  `value` mediumint(9) NOT NULL DEFAULT '0',
  `onchange` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `name` varchar(255) NOT NULL DEFAULT '',
  PRIMARY KEY (`zone`,`id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `spawn_events` (
  `id` int(10) unsigned NOT NULL AUTO_INCREMENT,
  `zone` varchar(16) NOT NULL DEFAULT '',
  `cond_id` mediumint(8) unsigned NOT NULL DEFAULT '0',
  `name` varchar(255) NOT NULL DEFAULT '',
  `period` int(10) unsigned NOT NULL DEFAULT '0',
  `next_minute` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `next_hour` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `next_day` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `next_month` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `next_year` int(10) unsigned NOT NULL DEFAULT '0',
  `enabled` tinyint(4) NOT NULL DEFAULT '1',
  `action` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `argument` mediumint(9) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `spawnentry` (
  `spawngroupID` int(11) NOT NULL DEFAULT '0',
  `npcID` int(11) NOT NULL DEFAULT '0',
  `chance` smallint(4) NOT NULL DEFAULT '0',
  `time_of_day` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`spawngroupID`,`npcID`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;

CREATE TABLE `spawngroup` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(30) NOT NULL DEFAULT '',
  `spawnlimit` tinyint(4) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  UNIQUE KEY `name` (`name`)
) ENGINE=MyISAM AUTO_INCREMENT=190257 DEFAULT CHARSET=latin1;

CREATE TABLE `start_zones` (
  `x` float NOT NULL DEFAULT '0',
  `y` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `zone_id` int(4) NOT NULL DEFAULT '0',
  `bind_id` int(4) NOT NULL DEFAULT '0',
  `player_choice` int(2) NOT NULL DEFAULT '0',
  `player_class` int(2) NOT NULL DEFAULT '0',
  `player_deity` int(4) NOT NULL DEFAULT '0',
  `player_race` int(4) NOT NULL DEFAULT '0',
  `start_zone` int(4) NOT NULL DEFAULT '0',
  `bind_x` float NOT NULL DEFAULT '0',
  `bind_y` float NOT NULL DEFAULT '0',
  `bind_z` float NOT NULL DEFAULT '0',
  `select_rank` tinyint(3) unsigned NOT NULL DEFAULT '50',
  PRIMARY KEY (`player_choice`,`player_race`,`player_class`,`player_deity`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1 ROW_FORMAT=DYNAMIC;

CREATE TABLE `start_zones_old` (
  `x` float NOT NULL DEFAULT '0',
  `y` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `zone_id` int(4) NOT NULL DEFAULT '0',
  `bind_id` int(4) NOT NULL DEFAULT '0',
  `player_choice` int(2) NOT NULL DEFAULT '0',
  `player_class` int(2) NOT NULL DEFAULT '0',
  `player_deity` int(4) NOT NULL DEFAULT '0',
  `player_race` int(4) NOT NULL DEFAULT '0',
  `start_zone` int(4) NOT NULL DEFAULT '0',
  `bind_x` float NOT NULL DEFAULT '0',
  `bind_y` float NOT NULL DEFAULT '0',
  `bind_z` float NOT NULL DEFAULT '0',
  `select_rank` tinyint(3) unsigned NOT NULL DEFAULT '50',
  PRIMARY KEY (`player_choice`,`player_race`,`player_class`,`player_deity`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `starting_items` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `race` int(11) NOT NULL DEFAULT '0',
  `class` int(11) NOT NULL DEFAULT '0',
  `itemid` int(11) NOT NULL DEFAULT '0',
  `gm` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`,`race`)
) ENGINE=InnoDB AUTO_INCREMENT=238 DEFAULT CHARSET=latin1;

CREATE TABLE `starting_items_new` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `race` int(11) NOT NULL DEFAULT '0',
  `class` int(11) NOT NULL DEFAULT '0',
  `deityid` int(11) NOT NULL DEFAULT '0',
  `zoneid` int(11) NOT NULL DEFAULT '0',
  `itemid` int(11) NOT NULL DEFAULT '0',
  `item_charges` tinyint(3) unsigned NOT NULL DEFAULT '1',
  `gm` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`,`race`)
) ENGINE=MyISAM AUTO_INCREMENT=178 DEFAULT CHARSET=latin1;

CREATE TABLE `static_zones` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `ZoneName` char(50) DEFAULT NULL,
  `ZPort` int(11) DEFAULT NULL,
  `ZServerID` int(11) DEFAULT NULL,
  PRIMARY KEY (`id`),
  UNIQUE KEY `ZoneName` (`ZoneName`),
  UNIQUE KEY `Port` (`ZPort`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8 COMMENT='List of Zones and Ports to attempt loading';

CREATE TABLE `time_of_day` (
  `hour` tinyint(4) NOT NULL DEFAULT '0',
  `day` tinyint(4) NOT NULL DEFAULT '0',
  `month` tinyint(4) NOT NULL DEFAULT '0',
  `year` bigint(20) NOT NULL DEFAULT '0',
  `is_daytime` tinyint(4) NOT NULL DEFAULT '0',
  PRIMARY KEY (`hour`,`day`,`month`,`year`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `tradeskill_recipe` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `name` varchar(64) NOT NULL DEFAULT '',
  `tradeskill` smallint(6) NOT NULL DEFAULT '0',
  `skillneeded` smallint(6) NOT NULL DEFAULT '0',
  `trivial` smallint(6) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=4152 DEFAULT CHARSET=latin1;

CREATE TABLE `tradeskill_recipe_entries` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `recipe_id` int(11) NOT NULL DEFAULT '0',
  `item_id` int(11) NOT NULL DEFAULT '0',
  `successcount` tinyint(2) NOT NULL DEFAULT '0',
  `failcount` tinyint(2) NOT NULL DEFAULT '0',
  `componentcount` tinyint(2) NOT NULL DEFAULT '1',
  `iscontainer` tinyint(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `recipe_id` (`recipe_id`),
  KEY `item_id` (`item_id`)
) ENGINE=MyISAM AUTO_INCREMENT=44647 DEFAULT CHARSET=latin1;

CREATE TABLE `tradeskillrecipe` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `tradeskill` smallint(6) NOT NULL DEFAULT '0',
  `skillneeded` smallint(6) NOT NULL DEFAULT '0',
  `trivial` smallint(6) NOT NULL DEFAULT '0',
  `product` smallint(6) NOT NULL DEFAULT '0',
  `product2` smallint(6) NOT NULL DEFAULT '0',
  `failproduct` smallint(6) NOT NULL DEFAULT '0',
  `productcount` smallint(6) NOT NULL DEFAULT '0',
  `i1` smallint(6) NOT NULL DEFAULT '0',
  `i2` smallint(6) NOT NULL DEFAULT '0',
  `i3` smallint(6) NOT NULL DEFAULT '0',
  `i4` smallint(6) NOT NULL DEFAULT '0',
  `i5` smallint(6) NOT NULL DEFAULT '0',
  `i6` smallint(6) NOT NULL DEFAULT '0',
  `i7` smallint(6) NOT NULL DEFAULT '0',
  `i8` smallint(6) NOT NULL DEFAULT '0',
  `i9` smallint(6) NOT NULL DEFAULT '0',
  `i10` smallint(6) NOT NULL DEFAULT '0',
  `notes` text,
  PRIMARY KEY (`id`),
  UNIQUE KEY `id` (`id`),
  UNIQUE KEY `name` (`product`)
) ENGINE=InnoDB AUTO_INCREMENT=27015 DEFAULT CHARSET=latin1;

CREATE TABLE `variables` (
  `varname` varchar(25) NOT NULL DEFAULT '',
  `value` text NOT NULL,
  `ts` timestamp NOT NULL DEFAULT '0000-00-00 00:00:00' ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`varname`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `zone` (
  `short_name` varchar(16) NOT NULL DEFAULT '',
  `file_name` varchar(16) DEFAULT NULL,
  `long_name` text,
  `safe_x` float NOT NULL DEFAULT '0',
  `safe_y` float NOT NULL DEFAULT '0',
  `safe_z` float NOT NULL DEFAULT '0',
  `safe_heading` float NOT NULL DEFAULT '0',
  `safe_gm_x` float NOT NULL DEFAULT '0',
  `safe_gm_y` float NOT NULL DEFAULT '0',
  `safe_gm_z` float NOT NULL DEFAULT '0',
  `safe_gm_heading` float NOT NULL DEFAULT '0',
  `minium_level` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `minium_status` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `weather` tinyint(1) DEFAULT NULL,
  `shutdowndelay` bigint(20) NOT NULL DEFAULT '30000' COMMENT 'Delay until zone goes to sleep in milliseconds (0 for zone to never sleep / static zone)',
  PRIMARY KEY (`short_name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `zone_copy` (
  `short_name` varchar(16) NOT NULL DEFAULT '',
  `long_name` text,
  `safe_x` float NOT NULL DEFAULT '0',
  `safe_y` float NOT NULL DEFAULT '0',
  `safe_z` float NOT NULL DEFAULT '0',
  `minium_level` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `minium_status` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `weather` tinyint(1) DEFAULT NULL,
  PRIMARY KEY (`short_name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `zone_ids` (
  `short_name` varchar(16) NOT NULL DEFAULT '',
  `zoneidnumber` int(11) unsigned NOT NULL DEFAULT '0',
  PRIMARY KEY (`short_name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `zone_line_nodes` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `zone` char(16) NOT NULL DEFAULT '',
  `x` float NOT NULL DEFAULT '0',
  `y` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `target_zone` char(16) NOT NULL DEFAULT '',
  `target_x` float NOT NULL DEFAULT '0',
  `target_y` float NOT NULL DEFAULT '0',
  `target_z` float NOT NULL DEFAULT '0',
  `range` int(11) NOT NULL DEFAULT '75',
  `heading` int(11) NOT NULL DEFAULT '0',
  `maxZDiff` int(11) NOT NULL DEFAULT '0',
  `keepX` int(11) NOT NULL DEFAULT '0',
  `keepY` int(11) NOT NULL DEFAULT '0',
  `keepZ` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=247 DEFAULT CHARSET=latin1;

CREATE TABLE `zone_points` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `zone` char(16) NOT NULL DEFAULT '',
  `y` float NOT NULL DEFAULT '0',
  `x` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `heading` float NOT NULL DEFAULT '0',
  `target_y` float NOT NULL DEFAULT '0',
  `target_x` float NOT NULL DEFAULT '0',
  `target_z` float NOT NULL DEFAULT '0',
  `target_zone` char(16) NOT NULL DEFAULT '',
  `keepX` int(11) NOT NULL DEFAULT '0',
  `keepY` int(11) NOT NULL DEFAULT '0',
  `keepZ` int(11) NOT NULL DEFAULT '0',
  `maxZDiff` int(11) NOT NULL DEFAULT '0',
  `Zrange` int(11) NOT NULL DEFAULT '15',
  `UseNewZoning` tinyint(4) NOT NULL DEFAULT '0' COMMENT '0 = Old Zoning, 1 = X based Zoning, 2 = Y based Zoning',
  `CenterPoint` float NOT NULL DEFAULT '0' COMMENT 'Center coord (relating to X or Y based on given new zoning type - only needed if zones do not line up)',
  `MaxVert` float NOT NULL DEFAULT '0' COMMENT 'Max coord (relating to X or Y based on given new zoning type - only needed if zones do not line up)',
  `MinVert` float NOT NULL DEFAULT '0' COMMENT 'Min coord (relating to X or Y based on given new zoning type - only needed if zones do not line up)',
  `ToZoneID` int(11) NOT NULL DEFAULT '0' COMMENT 'The cooresponding ID of zone_points table for where this zone is zoning into',
  PRIMARY KEY (`id`)
) ENGINE=MyISAM AUTO_INCREMENT=9033 DEFAULT CHARSET=latin1;

CREATE TABLE `zone_roam_boxes` (
  `id` int(11) unsigned NOT NULL AUTO_INCREMENT,
  `zone` varchar(30) NOT NULL DEFAULT '',
  `minX` int(11) NOT NULL DEFAULT '0',
  `maxX` int(11) NOT NULL DEFAULT '0',
  `minY` int(11) NOT NULL DEFAULT '0',
  `maxY` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=8 DEFAULT CHARSET=latin1;

CREATE TABLE `zone_rules` (
  `short_name` varchar(16) NOT NULL DEFAULT '',
  `can_bind` tinyint(1) NOT NULL DEFAULT '0',
  `can_lev` tinyint(1) NOT NULL DEFAULT '0',
  `castoutdoor` tinyint(1) NOT NULL DEFAULT '1',
  PRIMARY KEY (`short_name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `zone_server` (
  `name` varchar(16) NOT NULL DEFAULT '',
  `address` text NOT NULL,
  `port` int(11) NOT NULL DEFAULT '0',
  `player_count` int(11) NOT NULL DEFAULT '0',
  `last_alive` timestamp NOT NULL DEFAULT '0000-00-00 00:00:00',
  `rain` char(1) NOT NULL DEFAULT '0',
  PRIMARY KEY (`name`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `zone_state_dump` (
  `zonename` varchar(16) NOT NULL DEFAULT '',
  `spawn2_count` int(10) unsigned NOT NULL DEFAULT '0',
  `npc_count` int(10) unsigned NOT NULL DEFAULT '0',
  `npcloot_count` int(10) unsigned NOT NULL DEFAULT '0',
  `gmspawntype_count` int(10) unsigned NOT NULL DEFAULT '0',
  `spawn2` mediumblob,
  `npcs` mediumblob,
  `npc_loot` mediumblob,
  `gmspawntype` mediumblob,
  `time` timestamp NOT NULL DEFAULT '0000-00-00 00:00:00',
  PRIMARY KEY (`zonename`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `zoneserver_auth` (
  `host` varchar(30) NOT NULL DEFAULT '',
  `note` text,
  PRIMARY KEY (`host`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;

CREATE TABLE `zonevars` (
  `zoneID` int(4) NOT NULL DEFAULT '0',
  `short_name` varchar(16) NOT NULL DEFAULT '',
  `sky` tinyint(3) unsigned NOT NULL DEFAULT '1',
  `fog_red` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `fog_green` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `fog_blue` tinyint(3) unsigned NOT NULL DEFAULT '0',
  `fog_minclip` float NOT NULL DEFAULT '450',
  `fog_maxclip` float NOT NULL DEFAULT '450',
  `minclip` float NOT NULL DEFAULT '450',
  `maxclip` float NOT NULL DEFAULT '450',
  `safe_x` float NOT NULL DEFAULT '0',
  `safe_y` float NOT NULL DEFAULT '0',
  `safe_z` float NOT NULL DEFAULT '0',
  `underworld` float NOT NULL DEFAULT '0',
  `ztype` tinyint(3) unsigned NOT NULL DEFAULT '1',
  PRIMARY KEY (`short_name`),
  UNIQUE KEY `zoneidnumber` (`zoneID`)
) ENGINE=MyISAM DEFAULT CHARSET=latin1;
