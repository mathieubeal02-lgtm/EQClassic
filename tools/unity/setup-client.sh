#!/bin/bash
# Assemble the Unity client project (M4) in build/unity-client/ (not in git):
#   - LanternUnityTools (externals/, submodule): project settings, URP, EQ shaders, importers;
#   - our scripts: unity/Assets/EQClassic/;
#   - our libraries built for netstandard2.1: EQClassic.Shared, EQClassic.ClientCore, LiteNetLib;
#   - LanternExtractor exports (build/lantern-work/Exports, from tools/lantern/extract.sh) in Assets/EQAssets/.
# Then open build/unity-client with Unity 2021.3.18f1, run EQ > Assets > Import Zone / Import Characters,
# and press Play in an empty scene (docs/unity-client.md).
set -euo pipefail
REPO=$(cd "$(dirname "$0")/../.." && pwd)
OUT=${1:-$REPO/build/unity-client}

git -C "$REPO" submodule update --init externals/LanternUnityTools
mkdir -p "$OUT"
rsync -a --delete --exclude Library --exclude Temp --exclude Logs --exclude obj \
  --exclude Assets/EQClassic --exclude Assets/Plugins/EQClassic --exclude Assets/EQAssets \
  "$REPO/externals/LanternUnityTools/" "$OUT/"
rm -rf "$OUT/.git"

rsync -a --delete "$REPO/unity/Assets/EQClassic/" "$OUT/Assets/EQClassic/"

dotnet build "$REPO/rewrite/src/ClientCore/EQClassic.ClientCore.csproj" -c Release -f netstandard2.1 -nologo -v q
PLUGINS="$OUT/Assets/Plugins/EQClassic"
mkdir -p "$PLUGINS"
cp "$REPO/rewrite/src/ClientCore/bin/Release/netstandard2.1/EQClassic.ClientCore.dll" \
   "$REPO/rewrite/src/Shared/bin/Release/netstandard2.1/EQClassic.Shared.dll" "$PLUGINS/"
LITENET=$(ls -d "$HOME"/.nuget/packages/litenetlib/*/lib/netstandard2.1/LiteNetLib.dll | sort -V | tail -1)
cp "$LITENET" "$PLUGINS/"

if [ -d "$REPO/build/lantern-work/Exports" ]; then
  mkdir -p "$OUT/Assets/EQAssets"
  rsync -a "$REPO/build/lantern-work/Exports/" "$OUT/Assets/EQAssets/"
else
  echo "no Lantern exports: run tools/lantern/extract.sh <client dir> <zones> first" >&2
fi
echo "Unity project ready: $OUT"
