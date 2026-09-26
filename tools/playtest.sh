#!/bin/bash
# Plays the game against a rewrite server with the scripted client (rewrite/src/PlayTest) and writes
# a report: login, chat, GM commands, consider, a quest hail, melee and loot, spells, food,
# equipment, merchant, bank, zoning, then a second player (bot2 / bot2, Qpartner) for grouping and
# trading. The first account must be a GM (account.status >= 80); see docs/test-protocol.md.
#   tools/playtest.sh [host] [port] [fingerprint] [user] [password] [character]
set -euo pipefail
REPO=$(cd "$(dirname "$0")/.." && pwd)
HOST=${1:-127.0.0.1}; PORT=${2:-6999}; FP=${3:--}; USER_=${4:-bot}; PASS=${5:-bot}; CHAR=${6:-Qbot}
DB=${EQC_DB:-"Server=127.0.0.1;User ID=eqc;Password=eqc;Database=eqclassic;SslMode=None"}
REPORT=${EQC_REPORT:-$REPO/build/playtest-$(date +%Y%m%d-%H%M).md}
mkdir -p "$(dirname "$REPORT")"
dotnet run --project "$REPO/rewrite/src/PlayTest" -- "$HOST" "$PORT" "$FP" "$USER_" "$PASS" "$CHAR" --db "$DB" --report "$REPORT"
echo "report: $REPORT"
