-- Table used by Common/Source/database.cpp (Database::AddPlayerToBoat / RemovePlayerFromBoat /
-- FindPlayersOnBoat, ~line 4346) but missing from the eqclassic_db dump.
CREATE TABLE IF NOT EXISTS `boat_passengers` (
  `player_name` varchar(64) NOT NULL DEFAULT '',
  `boat_id` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`player_name`),
  KEY `boat_id` (`boat_id`)
) ENGINE=InnoDB DEFAULT CHARSET=latin1;
