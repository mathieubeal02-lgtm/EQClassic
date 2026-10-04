#!/bin/bash
# Runs N walking bots (bots milestone 1, docs/bots-design.md 9) and measures the servers meanwhile.
# Usage: fleet.sh <count> <waypoints file> <seconds> [password, default botpass]
# Each bot is `eqbot walk` on bot_NNN (see setup-accounts.sh), started 2 s apart (one login at a
# time). Logs go to $BOTD_LOGS (default ./logs/<date>); botlog.py summarises them.
cd "$(dirname "$0")"
COUNT=${1:?count}; WAYPOINTS=$(readlink -f "${2:?waypoints file}"); SECONDS_=${3:?seconds}; PASS=${4:-botpass}
EQBOT=$(readlink -f "${EQBOT:-../../build-linux/bin/eqbot}")
LOGS=${BOTD_LOGS:-logs/$(date +%Y%m%d-%H%M%S)}
mkdir -p "$LOGS"
pids=()
for i in $(seq 1 "$COUNT"); do
  account=$(printf 'bot_%03d' "$i")
  n=$((i - 1))
  name="Bot$(printf "\\x$(printf %x $((97 + n / 26 % 26)))")$(printf "\\x$(printf %x $((97 + n % 26)))")"
  # the last ones start up to 2 x COUNT s later: they walk that much less
  "$EQBOT" walk 127.0.0.1 "$account" "$PASS" "$name" "$WAYPOINTS" "$((SECONDS_ - 2 * (i - 1)))" > "$LOGS/$name.log" 2>&1 &
  pids+=($!)
  sleep 2
done
# load: zone.exe (all processes) and the bots, every 10 s while they run
python3 load.py "$LOGS/load.txt" "${pids[@]}"
wait
python3 botlog.py "$LOGS" "$COUNT"
