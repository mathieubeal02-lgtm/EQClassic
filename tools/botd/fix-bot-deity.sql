-- One-off for bot characters made before setup-accounts.sh set a deity: troll bots whose deity (int16 at 4156
-- of the profile) is not a valid one follow Cazic-Thule (203). The profiles changed are kept in
-- bot_profile_before_deity. Run with no bot logged in. Safe to run again.
CREATE TABLE IF NOT EXISTS bot_profile_before_deity (id INT NOT NULL PRIMARY KEY, name VARCHAR(16) NOT NULL, profile BLOB);
INSERT IGNORE INTO bot_profile_before_deity
  SELECT c.id, c.name, c.profile FROM character_ c JOIN account a ON a.id = c.account_id
  WHERE a.name LIKE 'bot\_%' AND ORD(SUBSTRING(c.profile, 57, 1)) = 9
    AND NOT (CAST(CONV(HEX(REVERSE(SUBSTRING(c.profile, 4157, 2))), 16, 10) AS UNSIGNED) BETWEEN 201 AND 216
             OR CAST(CONV(HEX(REVERSE(SUBSTRING(c.profile, 4157, 2))), 16, 10) AS UNSIGNED) = 396);
UPDATE character_ c JOIN account a ON a.id = c.account_id
  SET c.profile = INSERT(c.profile, 4157, 2, UNHEX('CB00'))
  WHERE a.name LIKE 'bot\_%' AND ORD(SUBSTRING(c.profile, 57, 1)) = 9
    AND NOT (CAST(CONV(HEX(REVERSE(SUBSTRING(c.profile, 4157, 2))), 16, 10) AS UNSIGNED) BETWEEN 201 AND 216
             OR CAST(CONV(HEX(REVERSE(SUBSTRING(c.profile, 4157, 2))), 16, 10) AS UNSIGNED) = 396);
SELECT ROW_COUNT() AS bots_changed;
