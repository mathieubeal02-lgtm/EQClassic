#!/bin/bash
# Milestone 6: a bot travels North Qeynos -> Qeynos Hills -> Blackburrow -> Qeynos Hills -> North Qeynos.
# Usage: travel-test.sh [bot index, default 55] [route]   (setup places a human warrior in qeynos2)
cd "$(dirname "$0")"
BOT=${1:-55}; ROUTE=${2:-qeytoqrg,blackburrow,qeytoqrg,qeynos2}
EQBOT=$(readlink -f "${EQBOT:-../../build-linux/bin/eqbot}")
n=$((BOT - 1))
name="Bot$(printf "\\x$(printf %x $((97 + n / 26 % 26)))")$(printf "\\x$(printf %x $((97 + n % 26)))")"
[ -s paths/zonelines.tsv ] || ./zonelines.sh > /dev/null
BOTD_FIRST=$BOT EQBOT_CLASS=warrior ./setup-accounts.sh 1 qeynos2 paths/qeynos2-grid2.txt > /dev/null
"$EQBOT" travel 127.0.0.1 "$(printf 'bot_%03d' "$BOT")" botpass "$name" "$ROUTE" paths/zonelines.tsv | grep -E "action=(TravelTo|ZoneLine|Zoned|Travel|ZoneChange|Done)"
