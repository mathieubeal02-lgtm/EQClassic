#!/bin/bash
# Build LanternExtractor (externals/LanternExtractor, a git submodule) and export zones from a
# Trilogy client install. Output: build/lantern-work/Exports/<zone>/ (not in git: it is client data).
#
#   tools/lantern/extract.sh <everquest dir> <zone|all> [zone...]
#   e.g. tools/lantern/extract.sh ~/eq-client permafrost qeynos2
#
# Needs the .NET SDK (8+; the project targets net6.0 and is built for net10.0 here) and, on Linux,
# libgdiplus for the texture conversion (System.Drawing): sudo apt install libgdiplus.
set -euo pipefail
REPO=$(cd "$(dirname "$0")/../.." && pwd)
EQDIR=$(realpath "$1"); shift
[ $# -ge 1 ] || { echo "usage: $0 <everquest dir> <zone|all> [zone...]"; exit 2; }
TFM=${LANTERN_TFM:-net10.0}
PROJ=$REPO/externals/LanternExtractor/LanternExtractor/LanternExtractor.csproj
OUT=$REPO/build/lantern-extractor
WORK=$REPO/build/lantern-work

[ -f "$PROJ" ] || git -C "$REPO" submodule update --init externals/LanternExtractor
if [ ! -f "$OUT/LanternExtractor.dll" ]; then
  dotnet restore "$PROJ" -p:TargetFramework="$TFM"
  dotnet build "$PROJ" -c Release --no-restore -p:TargetFramework="$TFM" -o "$OUT"
fi

mkdir -p "$WORK"
# The extractor reads settings.txt and writes Exports/ in its working directory.
sed "s#^EverQuestDirectory = .*#EverQuestDirectory = $EQDIR/#" \
  "$REPO/externals/LanternExtractor/LanternExtractor/settings.txt" > "$WORK/settings.txt"
cd "$WORK"
for zone in "$@"; do
  # Invariant culture: under a French (or any comma-decimal) locale the extractor writes
  # "285,875" in its comma-separated files, which corrupts every coordinate.
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet "$OUT/LanternExtractor.dll" "$zone"
done
if grep -q 'Exception' log.txt 2>/dev/null; then
  echo "warning: errors in $WORK/log.txt (missing libgdiplus gives no textures)" >&2
fi
echo "exports: $WORK/Exports"
