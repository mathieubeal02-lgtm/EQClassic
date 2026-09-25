#!/bin/bash
# Drive the EverQuest client running under Wine on Xvfb :99.
#   eqctl.sh start                 launch EQW in the virtual desktop
#   eqctl.sh shot NAME             screenshot -> $SHOTS/NAME.png
#   eqctl.sh aclick X Y            absolute click (menus drawn by EQW/Windows: EULA, login, server list)
#   eqctl.sh gclick X Y            in-game click (DirectInput: cursor homed top-left, then relative move)
#   eqctl.sh key KEY... / type TEXT / say TEXT (opens chat, types, Enter)
#   eqctl.sh stop
export DISPLAY=:99 WINEPREFIX=$HOME/.wine-eqclient WINEDEBUG=-all
SHOTS=${SHOTS:-/tmp/eqshots}; mkdir -p "$SHOTS"
UI_X=192; UI_Y=144           # top-left of the 640x480 game UI on the 1024x768 screen (full-screen mode)
focus() { xdotool windowfocus "$(xdotool search --name 'Wine Desktop' | head -1)" 2>/dev/null; }
slowclick() { xdotool mousedown 1; sleep 0.3; xdotool mouseup 1; }
case "$1" in
  start) cd "$(dirname "$0")" && setsid nohup wine eqw.exe > /tmp/eqclient.log 2>&1 < /dev/null & ;;
  stop)  /usr/lib/i386-linux-gnu/wine/wineserver -k ;;
  shot)  import -display :99 -window root "$SHOTS/$2.png" && echo "$SHOTS/$2.png" ;;
  aclick) xdotool mousemove "$2" "$3"; sleep 0.2; slowclick ;;
  gclick) focus
          for i in 1 2 3 4 5 6; do xdotool mousemove_relative -- -200 -200; sleep 0.05; done
          dx=$(( $2 - UI_X )); dy=$(( $3 - UI_Y ))
          while [ $dx -gt 0 ] || [ $dy -gt 0 ]; do
            sx=$(( dx > 40 ? 40 : dx )); sy=$(( dy > 40 ? 40 : dy ))
            xdotool mousemove_relative -- $sx $sy; dx=$((dx-sx)); dy=$((dy-sy)); sleep 0.05
          done; sleep 0.3; slowclick ;;
  key)   focus; shift; for k in "$@"; do xdotool key "$k"; sleep 0.3; done ;;
  type)  focus; xdotool type --delay 120 "$2" ;;
  say)   focus; xdotool key Return; sleep 0.8; xdotool type --delay 120 "$2"; sleep 0.3; xdotool key Return ;;
esac
