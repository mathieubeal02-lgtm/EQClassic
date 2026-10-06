#!/bin/bash
# A night of bots that look for bugs (tools/botd/README.md, "Bug hunting"): troll shamans hunt alone
# in Innothule Swamp (troll guards are their friends), a warrior leads a cleric and a wizard in the
# Qeynos Hills, chatd answers players, the watchdog watches the zones. At the end bugreport.py writes
# report.md (problems grouped, ranked, with evidence; those in known-issues.txt listed apart).
# Usage: night.sh <hours> [solo bots, default 16]     Logs: logs/night-<date>/
# The bots never write the database; this script, like setup-accounts.sh, places them and clears
# their own stale sessions (active_accounts rows of bot_* accounts) before starting.
cd "$(dirname "$0")"
HOURS=${1:?hours}; SOLO=${2:-16}
SECS=$(python3 -c "print(int($HOURS * 3600))")
LOGS=$(readlink -f "${BOTD_LOGS:-logs/night-$(date +%Y%m%d-%H%M)}")
mkdir -p "$LOGS/solo" "$LOGS/group"
DB="mysql --skip-ssl -u${EQC_DB_USER:-eqc} -p${EQC_DB_PASS:-eqc} ${EQC_DB_NAME:-eqclassic}"
echo "logs: $LOGS"
$DB -e "DELETE FROM active_accounts WHERE lsaccount IN (SELECT id FROM login_accounts WHERE name LIKE 'bot\\_%')"

# places: the zones' safe points
read IX IY IZ <<<"$($DB -N -e "SELECT safe_x, safe_y, safe_z FROM zone WHERE short_name = 'innothule'")"
echo "$IX $IY $IZ" > "$LOGS/innothule.txt"
./setup-accounts.sh "$SOLO" innothule "$LOGS/innothule.txt" > "$LOGS/setup.txt" 2>&1
BOTD_FIRST=52 ./setup-accounts.sh 1 qeytoqrg paths/qeytoqrg-safe.txt >> "$LOGS/setup.txt" 2>&1
BOTD_FIRST=53 EQBOT_CLASS=cleric ./setup-accounts.sh 1 qeytoqrg paths/qeytoqrg-safe.txt >> "$LOGS/setup.txt" 2>&1
BOTD_FIRST=54 EQBOT_CLASS=wizard ./setup-accounts.sh 1 qeytoqrg paths/qeytoqrg-safe.txt >> "$LOGS/setup.txt" 2>&1

chatd=
if ! curl -s "127.0.0.1:${CHATD_PORT:-7780}/health" > /dev/null; then
  python3 chatd.py bots.ini > "$LOGS/chatd.out" 2>&1 &
  chatd=$!
fi
python3 watchdog.py "$LOGS/watchdog.log" > /dev/null 2>&1 &
watchdog=$!

EQBOT_LFG=1 BOTD_LOGS="$LOGS/solo" ./fleet.sh "$SOLO" hunt "$SECS" > "$LOGS/solo/fleet.txt" 2>&1 &
solo=$!
sleep $((2 * SOLO + 5))	# one login at a time
BOTD_FIRST=52 BOTD_LOGS="$LOGS/group" ./fleet.sh 3 group "$SECS" > "$LOGS/group/fleet.txt" 2>&1 &
group=$!
wait $solo $group

kill $watchdog 2>/dev/null
[ -n "$chatd" ] && kill $chatd 2>/dev/null
python3 bugreport.py "$LOGS" --known known-issues.txt
