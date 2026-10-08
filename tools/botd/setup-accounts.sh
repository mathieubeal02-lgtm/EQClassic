#!/bin/bash
# Creates the bot accounts and characters (docs/bots-design.md 6.2): login accounts bot_001..bot_NNN
# (status 0, never GM), one character each (Bot + 2 letters: Botaa, Botab...), placed at the first
# waypoint of a waypoints file in a zone. Safe to run again: existing accounts and characters are
# kept, only their place is reset.
# Usage: setup-accounts.sh <count> <zone> <waypoints file> [password, default botpass]
# BOTD_FIRST: index of the first account (default 1). EQBOT_CLASS=warrior|cleric|wizard: humans of that
# class instead of troll shamans (eqbot create), casters with their starting spell.
# Database: EQC_DB_USER/EQC_DB_PASS/EQC_DB_NAME (eqc/eqc/eqclassic). Servers must be up (characters
# are created through World, like a player would).
set -e
cd "$(dirname "$0")"
COUNT=${1:?count}; ZONE=${2:?zone}; WAYPOINTS=${3:?waypoints file}; PASS=${4:-botpass}
DB="mysql --skip-ssl -u${EQC_DB_USER:-eqc} -p${EQC_DB_PASS:-eqc} ${EQC_DB_NAME:-eqclassic}"
EQBOT=${EQBOT:-../../build-linux/bin/eqbot}
read X Y Z <<<"$(head -1 "$WAYPOINTS")"
# y, x, z, heading: 16 bytes from 2408, then the zone name at 2424
POS=$(python3 -c "import struct;print(struct.pack('<ffff',$Y,$X,$Z,0.0).hex())")
FIRST=${BOTD_FIRST:-1}
for i in $(seq "$FIRST" $((FIRST + COUNT - 1))); do
  account=$(printf 'bot_%03d' "$i")
  n=$((i - 1))
  name="Bot$(printf "\\x$(printf %x $((97 + n / 26 % 26)))")$(printf "\\x$(printf %x $((97 + n % 26)))")"
  $DB -e "INSERT IGNORE INTO login_accounts (name, password, lsadmin, lsstatus, worldadmin, user_active) VALUES ('$account', SHA1('$PASS'), 0, 0, '0', '1')"
  # The game account too: the login server sends World an empty account name (LS/Login
  # logindatabase.cpp CheckEQLogin), so World's own CreateAccount makes '' once and then fails.
  $DB -e "INSERT IGNORE INTO account (name, password, status, lsaccount_id) SELECT '$account', '', 0, id FROM login_accounts WHERE name = '$account'"
  "$EQBOT" create 127.0.0.1 "$account" "$PASS" "$name" > /dev/null 2>&1 || { echo "$account: could not create $name"; continue; }
  sleep 2	# one login at a time (World's active_accounts rows are matched by IP)
  $DB -e "UPDATE character_ SET profile = CONCAT(SUBSTRING(profile, 1, 2408), UNHEX('$POS'), RPAD('$ZONE', 15, '\0'), SUBSTRING(profile, 2440)) WHERE name = '$name'"
  # The starting spell in the spell book (int16[256] at 1878) and in gem 1 (int16[8] at 2390), as a
  # player would scribe and memorize the starting scroll: Minor Healing (200) or Frost Bolt (54)
  case "${EQBOT_CLASS:-}" in
    cleric) SPELL=C800 ;;
    wizard) SPELL=3600 ;;
    *) SPELL= ;;
  esac
  if [ -n "$SPELL" ]; then
    $DB -e "UPDATE character_ SET profile = CONCAT(SUBSTRING(profile, 1, 1878), UNHEX('$SPELL'), SUBSTRING(profile, 1881, 2390 - 1880), UNHEX('$SPELL'), SUBSTRING(profile, 2393)) WHERE name = '$name'"
  fi
  # A stack of 20 Iron Rations (13005) and 20 Water Flasks (13006) in the first two pack slots (item ids:
  # uint16[30] at 168; charges: byte 2 of the 10-byte properties at 348): a famished player regenerates
  # nothing, and until the bots buy their own food (milestone 5) this is where it comes from.
  $DB -e "UPDATE character_ SET profile = CONCAT(SUBSTRING(profile, 1, 212), UNHEX('CD32CE32'), SUBSTRING(profile, 217, 570 - 216), UNHEX('14'), SUBSTRING(profile, 572, 9), UNHEX('14'), SUBSTRING(profile, 582)) WHERE name = '$name'"
  echo "$account $PASS $name"
done
