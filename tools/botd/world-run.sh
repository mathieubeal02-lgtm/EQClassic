#!/bin/bash
# A run of the living world (milestone 8) with what a bug-hunting night has around it: the bots placed
# and fed (setup-accounts.sh), chatd, the watchdog, and bugreport.py at the end.
# Usage: world-run.sh <hours> [world.ini, default world.ini.example]     Logs: logs/world-<date>/
# After <hours> world.py is told to stop; the bots still playing finish their session (two hours at
# most with the example settings), then the report is written.
cd "$(dirname "$0")"
HOURS=${1:?hours}; INI=$(readlink -f "${2:-world.ini.example}")
SECS=$(python3 -c "print(int($HOURS * 3600))")
export BOTD_LOGS=$(readlink -f "${BOTD_LOGS:-logs/world-$(date +%Y%m%d-%H%M)}")
mkdir -p "$BOTD_LOGS"
DB="mysql --skip-ssl -u${EQC_DB_USER:-eqc} -p${EQC_DB_PASS:-eqc} ${EQC_DB_NAME:-eqclassic}"
echo "logs: $BOTD_LOGS"
BOTS=$(sed -n 's/^bots *= *//p' "$INI"); FIRST=$(sed -n 's/^first *= *//p' "$INI")
ZONE=$(awk '/^\[ladder\]/{f=1;next} f&&/^[a-z]/{print $1; exit}' "$INI")
$DB -e "DELETE FROM active_accounts WHERE lsaccount IN (SELECT id FROM login_accounts WHERE name LIKE 'bot\\_%')"
read X Y Z <<<"$($DB -N -e "SELECT safe_x, safe_y, safe_z FROM zone WHERE short_name = '$ZONE'")"
echo "$X $Y $Z" > "$BOTD_LOGS/start.txt"
BOTD_FIRST=${FIRST:-1} ./setup-accounts.sh "${BOTS:-16}" "$ZONE" "$BOTD_LOGS/start.txt" > "$BOTD_LOGS/setup.txt" 2>&1

chatd=
if ! curl -s "127.0.0.1:${CHATD_PORT:-7780}/health" > /dev/null; then
  python3 chatd.py bots.ini > "$BOTD_LOGS/chatd.out" 2>&1 &
  chatd=$!
fi
python3 watchdog.py "$BOTD_LOGS/watchdog.log" > /dev/null 2>&1 &
watchdog=$!

timeout -s TERM "$SECS" python3 world.py "$INI" > "$BOTD_LOGS/world.out" 2>&1
# the sessions under way end by themselves
while pgrep -x eqbot > /dev/null; do sleep 30; done

kill $watchdog 2>/dev/null
[ -n "$chatd" ] && kill $chatd 2>/dev/null
python3 bugreport.py "$BOTD_LOGS" --known known-issues.txt
