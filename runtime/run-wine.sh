#!/bin/bash
# Start/stop the EQClassic servers under Wine. Usage: ./run-wine.sh start|stop|status [zones]
cd "$(dirname "$0")"
export WINEPREFIX=$HOME/.wine-eqc WINEDEBUG=-all
IP=$(ip -4 route get 1.1.1.1 | awk '{for(i=1;i<=NF;i++) if($i=="src") print $(i+1)}')
ZONES=${2:-5}
# Zone UDP ports start at 7000: on Linux, ports below 1024 need root (the .bat files use 1000).
case "$1" in
  start)
    mkdir -p logs
    setsid nohup wine ./login.exe > logs/login.log 2>&1 < /dev/null &
    sleep 5
    setsid nohup wine ./world.exe > logs/world.log 2>&1 < /dev/null &
    sleep 8
    for i in $(seq 0 $((ZONES-1))); do
      setsid nohup wine ./zone.exe . "$IP" $((7000+i)) 127.0.0.1 > logs/zone$i.log 2>&1 < /dev/null &
      sleep 3
    done ;;
  stop)
    /usr/lib/i386-linux-gnu/wine/wineserver -k 2>/dev/null
    # match the program name exactly, never a shell whose command line merely mentions it
    ps -eo pid,args | awk '$2 ~ /^[.]\/(login|world|zone)[.]exe$/ {print $1}' | xargs -r kill ;;
  status)
    ps -eo pid,args | awk '$2 ~ /^[.]\/(login|world|zone)[.]exe$/'
    ss -lntu | awk 'NR==1 || /:(5999|9000|700[0-9]) /' ;;
esac
