#!/bin/bash
# Dry run of a patch written by gen_zone_from_quarm.py: applies it twice to copies of the tables
# (prefix tzp_, in the same database: the server's user cannot create one) and checks that nothing
# is left dangling and that other zones did not move. Every table a patch writes must be in TABLES:
# one that is not would be written for real. Usage: test_zone_patch.sh <patch.sql> <zone> <zone id>
set -e
PATCH=$1; ZONE=$2; ZID=$3
M="mysql --skip-ssl -u${EQC_DB_USER:-eqc} -p${EQC_DB_PASS:-eqc} ${EQC_DB_NAME:-eqclassic}"
TABLES="spawn2 spawngroup spawnentry grid grid_entries npc_types npc_types_without loottable loottable_entries lootdrop lootdrop_entries npc_faction npc_faction_entries"
TMP=$(mktemp)
sed -E 's/\b(spawn2|spawngroup|spawnentry|grid_entries|grid|npc_types_without|npc_types|loottable_entries|loottable|lootdrop_entries|lootdrop|npc_faction_entries|npc_faction)(_before_era)?\b/tzp_\1\2/g' "$PATCH" > "$TMP"
# refuse to run if a statement would still write a real table
REAL=$(grep -oE "^(INSERT( IGNORE)? INTO|REPLACE INTO|DELETE FROM|UPDATE|CREATE TABLE IF NOT EXISTS|CREATE TEMPORARY TABLE|DROP TEMPORARY TABLE) +[A-Za-z_0-9]+" "$TMP" | awk '{print $NF}' | sort -u | grep -v '^tzp_' | grep -v '^era_groups$' || true)
if [ -n "$REAL" ]; then echo "not a dry run, the patch writes: $REAL (add to TABLES)"; rm -f "$TMP"; exit 1; fi
for t in $TABLES; do
  $M -e "DROP TABLE IF EXISTS tzp_$t, tzp_${t}_before_era; CREATE TABLE tzp_$t LIKE $t; INSERT INTO tzp_$t SELECT * FROM $t" 2>/dev/null
done
for run in 1 2; do
  $M < "$TMP" 2>&1 | grep -v Warning || true
  $M -N -e "
    SELECT 'points', COUNT(*) FROM tzp_spawn2 WHERE zone='$ZONE' UNION ALL
    SELECT 'points kept from before', COUNT(*) FROM tzp_spawn2 WHERE zone='$ZONE' AND id < 1000000 UNION ALL
    SELECT 'backup points', COUNT(*) FROM tzp_spawn2_before_era WHERE zone='$ZONE' UNION ALL
    SELECT 'grids', COUNT(*) FROM tzp_grid WHERE zoneid=$ZID UNION ALL
    SELECT 'entries without NPC', COUNT(*) FROM tzp_spawnentry e LEFT JOIN tzp_npc_types n ON n.id=e.npcID WHERE e.spawngroupID>=1000000 AND n.id IS NULL UNION ALL
    SELECT 'points without group', COUNT(*) FROM tzp_spawn2 s LEFT JOIN tzp_spawngroup g ON g.id=s.spawngroupID WHERE s.zone='$ZONE' AND g.id IS NULL UNION ALL
    SELECT 'groups without entry', COUNT(*) FROM tzp_spawn2 s LEFT JOIN tzp_spawnentry e ON e.spawngroupID=s.spawngroupID WHERE s.zone='$ZONE' AND e.npcID IS NULL UNION ALL
    SELECT 'points without grid', COUNT(*) FROM tzp_spawn2 s LEFT JOIN tzp_grid g ON g.zoneid=$ZID AND g.id=s.pathgrid WHERE s.zone='$ZONE' AND s.pathgrid<>0 AND g.id IS NULL UNION ALL
    SELECT 'imported NPCs without loot rows', COUNT(*) FROM tzp_npc_types n LEFT JOIN tzp_loottable l ON l.id=n.loottable_id WHERE n.loottable_id>=1000000 AND l.id IS NULL UNION ALL
    SELECT 'other zones moved', (SELECT COUNT(*) FROM tzp_spawn2 WHERE zone<>'$ZONE') - (SELECT COUNT(*) FROM spawn2 WHERE zone<>'$ZONE')" 2>/dev/null | tr '\t' '=' | paste -sd' ' | sed "s/^/run $run: /"
done
for t in $TABLES; do $M -e "DROP TABLE IF EXISTS tzp_$t, tzp_${t}_before_era" 2>/dev/null; done
rm -f "$TMP"
