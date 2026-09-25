#!/bin/bash
# End-to-end smoke test: the Win32 servers under Wine + MariaDB, driven by eqbot.
#
#   tools/eqbot/ci-e2e.sh <server dir> <eqbot binary>
#
# <server dir> is a `cmake --install` layout (the CI artifact eqclassic-server-Release).
# Needs wine (32-bit), docker, and the sql/eqclassic_db submodule. Runs MariaDB in a container
# on 127.0.0.1:3306 (the old client library needs no TLS and no strict mode, see docs/RUNBOOK.md),
# imports the dump and sql/patches, creates a login account, starts login, world and one dynamic
# zone, then: eqbot login, create, play, and play again (the second session is refused with
# "Error 1018: active character" when the zone fails to log the first one out).
set -euo pipefail

SERVER=$(realpath "$1")
EQBOT=$(realpath "$2")
REPO=$(cd "$(dirname "$0")/../.." && pwd)
DB_CONTAINER=eqc-ci-db
LS_USER=ci
LS_PASS=ci
CHAR=Qcibot
export WINEDEBUG=-all WINEPREFIX=${WINEPREFIX:-$HOME/.wine-eqc-ci}

db() { docker exec -i "$DB_CONTAINER" mariadb --skip-ssl -uroot -proot "$@"; }

stop_servers() {
  wineserver -k 2>/dev/null || true
}

show_logs() {
  for f in "$SERVER"/logs/*.log; do
    echo "::group::$(basename "$f")"
    tail -n 80 "$f" || true
    echo "::endgroup::"
  done
}

trap 'status=$?; stop_servers; if [ $status -ne 0 ]; then show_logs; fi; exit $status' EXIT

echo "== MariaDB"
docker rm -f "$DB_CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$DB_CONTAINER" -p 127.0.0.1:3306:3306 -e MARIADB_ROOT_PASSWORD=root \
  mariadb:11.8 --skip-ssl --sql-mode=NO_ENGINE_SUBSTITUTION \
  --character-set-server=latin1 --collation-server=latin1_swedish_ci >/dev/null
# The image first runs a temporary server (port 0) to initialise, then restarts: wait for the
# real one, listening on 3306, or the first statements can hit the restart.
for _ in $(seq 90); do
  docker logs "$DB_CONTAINER" 2>&1 | grep -q 'ready for connections.*port: 3306' && break
  sleep 2
done
db -e "SELECT 1" >/dev/null
db -e "CREATE DATABASE eqclassic CHARACTER SET latin1;
       CREATE USER 'eqc'@'%' IDENTIFIED BY 'eqc';
       GRANT ALL ON eqclassic.* TO 'eqc'@'%';"
echo "importing the dump..."
cat "$REPO"/sql/eqclassic_db/sql/*.sql | db eqclassic
for p in "$REPO"/sql/patches/*.sql; do
  echo "patch $(basename "$p")"
  db eqclassic < "$p"
done
db eqclassic -e "INSERT INTO login_accounts (name, password, lsadmin, lsstatus, worldadmin, user_active)
                 VALUES ('$LS_USER', SHA1('$LS_PASS'), 0, 0, '0', '1');"

echo "== Server configuration"
cd "$SERVER"
mkdir -p logs
printf '[Database]\nhost=127.0.0.1\nuser=eqc\npass=eqc\ndata=eqclassic\n' > db.ini
printf '[LoginServer]\nloginserver=127.0.0.1\nworldname=EverQuest Classic\naccount=\npassword=\nlocked=false\nworldaddress=127.0.0.1\nloginport=5999\n\n[LoginConfig]\nServerMode=Standalone\nServerPort=5999\n' > LoginServer.ini

echo "== Wine prefix"
wineboot --init >/dev/null 2>&1 || true

echo "== Starting login, world, zone"
wine ./login.exe > logs/login.log 2>&1 < /dev/null &
sleep 5
wine ./world.exe > logs/world.log 2>&1 < /dev/null &
sleep 8
# Dynamic zone on UDP 7000 (ports below 1024 need root on Linux).
wine ./zone.exe . 127.0.0.1 7000 127.0.0.1 > logs/zone0.log 2>&1 < /dev/null &

# Ready when world is listed by the login server with a status answer.
ready=0
for _ in $(seq 30); do
  if "$EQBOT" login 127.0.0.1 "$LS_USER" "$LS_PASS" > logs/eqbot-wait.log 2>&1; then
    ready=1
    break
  fi
  sleep 4
done
if [ $ready -ne 1 ]; then
  echo "servers not ready:"
  cat logs/eqbot-wait.log
  exit 1
fi

echo "== eqbot"
"$EQBOT" login 127.0.0.1 "$LS_USER" "$LS_PASS"
"$EQBOT" create 127.0.0.1 "$LS_USER" "$LS_PASS" "$CHAR"
"$EQBOT" play 127.0.0.1 "$LS_USER" "$LS_PASS" "$CHAR"
sleep 5
echo "-- second session (the first one must have been logged out)"
"$EQBOT" play 127.0.0.1 "$LS_USER" "$LS_PASS" "$CHAR"
echo "== end-to-end OK"
