#!/bin/bash
# Milestone 4 in game, chat.llm off: chatd answers with templates. A member bot looking for a group
# posts LFG on OOC (at most once per lfg_seconds), and answers a tell within 5 s.
# Usage: chat-test.sh <bot index> <tester user> <tester password> <tester character> [seconds=240]
# The tester is any other account (a GM test account is fine); tells cross zones through World.
cd "$(dirname "$0")"
BOT=${1:?bot index}; TUSER=${2:?tester user}; TPASS=${3:?tester password}; TCHAR=${4:?tester character}; SECS=${5:-240}
EQBOT=$(readlink -f "${EQBOT:-../../build-linux/bin/eqbot}")
LOGS=${BOTD_LOGS:-logs/chat-$(date +%Y%m%d-%H%M%S)}
mkdir -p "$LOGS"
n=$((BOT - 1))
name="Bot$(printf "\\x$(printf %x $((97 + n / 26 % 26)))")$(printf "\\x$(printf %x $((97 + n % 26)))")"
account=$(printf 'bot_%03d' "$BOT")
started=
if ! curl -s 127.0.0.1:${CHATD_PORT:-7780}/health > /dev/null; then
  python3 chatd.py bots.ini > "$LOGS/chatd.log" 2>&1 &
  started=$!
  sleep 1
fi
EQBOT_ROLE=member EQBOT_LFG=1 "$EQBOT" hunt 127.0.0.1 "$account" botpass "$name" "$SECS" > "$LOGS/$name.log" 2>&1 &
bot=$!
until grep -q "action=EnterZone" "$LOGS/$name.log" 2>/dev/null; do sleep 2; done
sleep 5
"$EQBOT" tell 127.0.0.1 "$TUSER" "$TPASS" "$TCHAR" "$name" "hey, want to group?" 10 | grep tell | tee "$LOGS/tell.txt"
wait $bot
[ -n "$started" ] && kill "$started"
ok=1
ms=$(grep -o 'answered after [0-9]*' "$LOGS/tell.txt" | grep -o '[0-9]*$')
[ -n "$ms" ] && [ "$ms" -le 5000 ] || { echo "[FAIL] no answer to the tell within 5 s"; ok=0; }
lfg=$(grep -c 'action=Chat channel=ooc' "$LOGS/$name.log")
echo "lfg lines on ooc: $lfg in $SECS s"
[ "$lfg" -ge 1 ] && [ "$lfg" -le $(( SECS / 600 + 1 )) ] || { echo "[FAIL] lfg lines"; ok=0; }
[ $ok = 1 ] && echo "[ OK ] milestone 4 checks (templates)" || exit 1
