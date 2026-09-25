# Map tools

`check_grids.py` checks the NPC waypoint grids (`grid_entries`) against the zone geometry in
`Maps/Maps/<zone>.map`. NPCs walk their grids point by point, so a grid that crosses a wall makes
the NPC walk through it (seen with a rat in the Qeynos sewers).

```sh
pip install numpy pymysql
EQC_DB_USER=eqc EQC_DB_PASS=eqc tools/maps/check_grids.py --maps ~/eqc-server/Maps/Maps [--zone qeynos2] [--csv problems.csv] [--start <zone>]
```

For each zone it counts:

| Column | Meaning |
|---|---|
| `no_floor` | No geometry under the waypoint: under the world or off the mesh |
| `floating` | More than 40 units above the floor below it. Flying NPCs are expected here, e.g. in airplane. |
| `walls` | The segment to the next waypoint crosses steep geometry at body height and at head height. The NPC walks through a wall. |
| `steps` | Steep geometry at body height only: a ledge or low obstacle. It is cosmetic. |
| `floors` | The segment only dips through flat geometry, e.g. a hill or stairs between two points. It is cosmetic. |

A truncated `.map` file is reported instead of checked. The zone cannot load it either.
The CSV lists every problem with grid id, waypoint number and coordinates, so a grid can be looked up in `spawn2.pathgrid`.
