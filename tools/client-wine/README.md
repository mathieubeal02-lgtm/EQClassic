# Driving the real Trilogy client on Linux (Wine + Xvfb)

Used to reproduce gameplay bugs without a Windows PC: the client runs next to the servers,
is driven with `xdotool`, and screenshots are read back. Slow (software rendering), so it is a
debugging tool, not a CI test.

## Setup (once)
- `sudo apt install xvfb xdotool imagemagick`; a Wine prefix for the client: `WINEPREFIX=~/.wine-eqclient`.
- Copy an installed, working game folder (with `eqw.exe`, `eqhost.txt`) to `~/eq-client`, plus `eqctl.sh`.
- In `eqw.ini`: `EQExe=Z:\home\<user>\eq-client\eqgame.exe` and `640by480X=50`, `640by480Y=50`
  (a position saved on a bigger monitor puts the game window off-screen: nothing renders).
- Enable Wine's virtual desktop, otherwise the game windows stay unmapped:
  `wine reg add 'HKCU\Software\Wine\Explorer' /v Desktop /d Default /f` and
  `wine reg add 'HKCU\Software\Wine\Explorer\Desktops' /v Default /d 1024x768 /f`.
- `Xvfb :99 -screen 0 1024x768x24 +extension GLX &` (llvmpipe OpenGL is enough).

## Use
```
eqctl.sh start; eqctl.sh aclick 647 297     # EQW "Start EQ"; then EULA "I Accept" at 423 591
eqctl.sh aclick 567 306                      # CONNECT -> login screen (password field 495 455, CONNECT 177 376)
eqctl.sh gclick 285 587                      # in-game UI (DirectInput): ENTER WORLD on character select
eqctl.sh say '#zone qeynos'                  # chat / GM commands
eqctl.sh shot name                           # screenshot to $SHOTS
inv.sh Qbot                                  # saved inventory of a character, from the DB
```
Menus drawn by EQW/Windows take absolute clicks (`aclick`). Once the game is full screen it reads
the mouse through DirectInput: `gclick` homes the cursor to the top-left of the 640x480 UI and moves
it relatively; clicks need a slow press/release. The server saves a character when it zones, so
`#zone` is the quickest way to get the DB in sync before `inv.sh`.

Test account: create it with `user_active=1` (see docs/RUNBOOK.md), and a character by cloning a
profile in `character_` if the scenario needs a known inventory.
