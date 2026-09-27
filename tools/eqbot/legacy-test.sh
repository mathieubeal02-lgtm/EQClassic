#!/bin/bash
# Runs `eqbot test` against the legacy servers, retrying while World still holds the previous session
# ("Error 1018: You currently have an active character"). Usage: legacy-test.sh [host] [user] [password] [character]
# EQBOT_HAIL=<npc name> picks the quest NPC to hail (default Brohan_Ironforge, North Qeynos).
cd "$(dirname "$0")"
BOT=${EQBOT:-../../build-linux/bin/eqbot}
[ -x "$BOT" ] || ./build.sh "$BOT" >/dev/null || exit 2
for attempt in 1 2 3 4 5 6; do
  out=$("$BOT" test "${1:-127.0.0.1}" "${2:-bot}" "${3:-bot}" "${4:-Qbottwo}" 2>&1)
  code=$?
  if ! grep -q "Error 1018" <<<"$out"; then
    echo "$out"
    exit $code
  fi
  echo "(World still holds the last session, retrying in 20 s: attempt $attempt)" >&2
  sleep 20
done
echo "$out"
exit 1
