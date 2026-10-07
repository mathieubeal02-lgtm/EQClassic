#!/bin/bash
# Writes paths/zonelines.tsv: where each zone line is, for bots that change zones (milestone 6).
# Columns: zone x y z range mode min max target_zone   (mode 0: a point and its range; 1: an X line,
# crossed at x >= or <= the trigger with y within min..max; 2: a Y line, the same with x; 0 = no bound)
# A database read, like setup-accounts.sh: the bots themselves never touch the database.
cd "$(dirname "$0")"
DB="mysql --skip-ssl -u${EQC_DB_USER:-eqc} -p${EQC_DB_PASS:-eqc} ${EQC_DB_NAME:-eqclassic}"
$DB -N -B -e "SELECT zone, x, y, z, Zrange, UseNewZoning, minvert, maxvert, target_zone FROM zone_points
  WHERE x < 9000 AND y < 9000 ORDER BY zone, id" > paths/zonelines.tsv
wc -l < paths/zonelines.tsv
