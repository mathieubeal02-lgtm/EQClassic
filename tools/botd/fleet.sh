#!/bin/bash
# Runs N bots (bots milestones 1 and 2, docs/bots-design.md 9) and measures the servers meanwhile.
# Usage: fleet.sh <count> <waypoints file | hunt | group> <seconds> [password, default botpass]
# Each bot is `eqbot walk` (or `eqbot hunt`) on bot_NNN (see setup-accounts.sh; BOTD_FIRST: first
# index, default 1), started 2 s apart (one login at a time). group: one hunting group, the first bot
# leads (EQBOT_INVITE) and the others are members (EQBOT_ROLE=member), started before it. Logs go to $BOTD_LOGS (default ./logs/<date>); botlog.py summarises them.
cd "$(dirname "$0")"
COUNT=${1:?count}; MODE=${2:?waypoints file, hunt or group}; [ "$MODE" = hunt ] || [ "$MODE" = group ] || WAYPOINTS=$(readlink -f "$MODE"); SECONDS_=${3:?seconds}; PASS=${4:-botpass}
EQBOT=$(readlink -f "${EQBOT:-../../build-linux/bin/eqbot}")
LOGS=${BOTD_LOGS:-logs/$(date +%Y%m%d-%H%M%S)}
mkdir -p "$LOGS"
pids=()
FIRST=${BOTD_FIRST:-1}
botname() {
  local n=$(($1 - 1))
  echo "Bot$(printf "\\x$(printf %x $((97 + n / 26 % 26)))")$(printf "\\x$(printf %x $((97 + n % 26)))")"
}
ORDER=$(seq "$FIRST" $((FIRST + COUNT - 1)))
if [ "$MODE" = group ]; then
  ORDER="$(seq $((FIRST + 1)) $((FIRST + COUNT - 1))) $FIRST"	# members first, then the leader
  INVITE=$(for i in $(seq $((FIRST + 1)) $((FIRST + COUNT - 1))); do botname "$i"; done | paste -sd,)
fi
k=0
for i in $ORDER; do
  account=$(printf 'bot_%03d' "$i")
  name=$(botname "$i")
  # the last ones start up to 2 x COUNT s later: they play that much less
  left=$((SECONDS_ - 2 * k))
  k=$((k + 1))
  if [ "$MODE" = group ] && [ "$i" = "$FIRST" ]; then
    EQBOT_INVITE=$INVITE "$EQBOT" hunt 127.0.0.1 "$account" "$PASS" "$name" "$left" > "$LOGS/$name.log" 2>&1 &
  elif [ "$MODE" = group ]; then
    EQBOT_ROLE=member "$EQBOT" hunt 127.0.0.1 "$account" "$PASS" "$name" "$left" > "$LOGS/$name.log" 2>&1 &
  elif [ "$MODE" = hunt ]; then
    "$EQBOT" hunt 127.0.0.1 "$account" "$PASS" "$name" "$left" > "$LOGS/$name.log" 2>&1 &
  else
    "$EQBOT" walk 127.0.0.1 "$account" "$PASS" "$name" "$WAYPOINTS" "$left" > "$LOGS/$name.log" 2>&1 &
  fi
  pids+=($!)
  sleep 2
done
# load: zone.exe (all processes) and the bots, every 10 s while they run
python3 load.py "$LOGS/load.txt" "${pids[@]}"
wait
python3 botlog.py "$LOGS" "$COUNT"
