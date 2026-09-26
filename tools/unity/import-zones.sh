#!/bin/bash
# Import Lantern exports into the Unity client project, a few at a time:
#
#   tools/unity/import-zones.sh [--characters] <zone|all> [zone...]
#
# For each batch the zones' exports (build/lantern-work/Exports/<zone>) are copied into
# Assets/EQAssets, the LanternUnityTools importers run in Unity (EQClassicBatch), and the copies are
# removed: the imported prefabs, meshes and textures stay in Assets/Content/AssetBundleContent.
# Keeping every export under Assets makes Unity scan 125,000 files before doing anything.
# --characters also imports the character models (the "characters" export).
# Needs the Unity setup of docs/unity-client.md (UNITY, LD_LIBRARY_PATH for the compat libraries).
set -euo pipefail
REPO=$(cd "$(dirname "$0")/../.." && pwd)
PROJECT=$REPO/build/unity-client
EXPORTS=$REPO/build/lantern-work/Exports
UNITY=${UNITY:-$HOME/Unity/Hub/Editor/2021.3.18f1/Editor/Unity}
BATCH=${EQC_IMPORT_BATCH:-8}
LOGS=$REPO/build/unity-import-logs
mkdir -p "$LOGS"

characters=0
if [ "${1:-}" = "--characters" ]; then characters=1; shift; fi
if [ "${1:-}" = "all" ]; then
  mapfile -t zones < <(ls "$EXPORTS" | grep -v '^characters$')
else
  zones=("$@")
fi

run_unity() { # <method> <log> [zones]
  rm -f "$PROJECT/Temp/UnityLockfile"
  (ulimit -n 4096; EQC_ZONES="${3:-}" "$UNITY" -projectPath "$PROJECT" -executeMethod "EQClassic.Unity.Editor.EQClassicBatch.$1" \
     -quit -logFile "$2" >/dev/null 2>&1) || true # Unity 2021 on Linux often segfaults while quitting, after the work is saved
  grep -E 'EQClassicBatch: |threw exception' "$2" || true
}

mkdir -p "$PROJECT/Assets/EQAssets"
if [ $characters -eq 1 ]; then
  rsync -a "$EXPORTS/characters/" "$PROJECT/Assets/EQAssets/characters/"
  run_unity ImportCharacters "$LOGS/characters.log"
  rm -rf "$PROJECT/Assets/EQAssets/characters" "$PROJECT/Assets/EQAssets/characters.meta"
fi

for ((i = 0; i < ${#zones[@]}; i += BATCH)); do
  batch=("${zones[@]:i:BATCH}")
  for z in "${batch[@]}"; do rsync -a "$EXPORTS/$z/" "$PROJECT/Assets/EQAssets/$z/"; done
  echo "== zones $((i + 1))-$((i + ${#batch[@]})) of ${#zones[@]}: ${batch[*]}"
  run_unity ImportZones "$LOGS/zones-$i.log" "$(IFS=';'; echo "${batch[*]}")"
  for z in "${batch[@]}"; do rm -rf "$PROJECT/Assets/EQAssets/$z" "$PROJECT/Assets/EQAssets/$z.meta"; done
done
rmdir "$PROJECT/Assets/EQAssets" 2>/dev/null && rm -f "$PROJECT/Assets/EQAssets.meta" || true
echo "imported; logs in $LOGS"
