#!/bin/bash
# Assemble the Unity client project (M4) in build/unity-client/ (not in git):
#   - LanternUnityTools (externals/, submodule): project settings, URP, EQ shaders, importers;
#   - our scripts: unity/Assets/EQClassic/;
#   - our libraries built for netstandard2.1: EQClassic.Shared, EQClassic.ClientCore, LiteNetLib;
# The LanternExtractor exports (build/lantern-work/Exports, tools/lantern/extract.sh) are imported
# with tools/unity/import-zones.sh, a few zones at a time; then open build/unity-client with Unity
# 2021.3.18f1 and press Play in an empty scene (docs/unity-client.md). Rerunning this script keeps the imported assets (Assets/Content/AssetBundleContent).
set -euo pipefail
REPO=$(cd "$(dirname "$0")/../.." && pwd)
OUT=${1:-$REPO/build/unity-client}

git -C "$REPO" submodule update --init externals/LanternUnityTools
# LanternUnityTools keeps its DLLs (DryWetMidi...) in Git LFS: without git-lfs they are
# 130-byte pointer files and Lantern.EQ fails to compile ("Melanchall could not be found").
if ! git -C "$REPO/externals/LanternUnityTools" lfs pull; then
  echo "git-lfs is required (apt install git-lfs)" >&2
  exit 1
fi
mkdir -p "$OUT"
# The Trilogy client's interface art (the classic frame, buttons, spell gems, icons): its bmpwad*.s3d
# archives, read by the client at run time. EQC_CLIENT: the client install (default ~/eq-client).
CLIENT=${EQC_CLIENT:-$HOME/eq-client}
if ls "$CLIENT"/bmpwad*.s3d >/dev/null 2>&1; then
  mkdir -p "$REPO/build/lantern-work/Exports/ui"
  cp "$CLIENT"/bmpwad*.s3d "$REPO/build/lantern-work/Exports/ui/"
fi
rsync -a --delete --exclude Library --exclude Temp --exclude Logs --exclude obj \
  --exclude Assets/EQClassic --exclude Assets/Plugins/EQClassic --exclude Assets/EQAssets \
  --exclude 'Assets/Content/AssetBundleContent/*' \
  "$REPO/externals/LanternUnityTools/" "$OUT/"
rm -rf "$OUT/.git"
# LanternUnityTools' importers end with a blocking dialog: in this copy, route them through
# HeadlessDialog, which only logs when EQC_HEADLESS is set (tools/unity/import-zones.sh sets it).
cp "$REPO/tools/unity/overlay/HeadlessDialog.cs" "$OUT/Assets/Scripts/Lantern/EQ/Editor/HeadlessDialog.cs"
grep -rl 'EditorUtility.DisplayDialog(' "$OUT/Assets/Scripts/Lantern/EQ/Editor" --include=*.cs | grep -v HeadlessDialog.cs |
  xargs -r sed -i 's/EditorUtility\.DisplayDialog(/HeadlessDialog.Show(/g'

rsync -a --delete "$REPO/unity/Assets/EQClassic/" "$OUT/Assets/EQClassic/"

dotnet build "$REPO/rewrite/src/ClientCore/EQClassic.ClientCore.csproj" -c Release -f netstandard2.1 -nologo -v q
PLUGINS="$OUT/Assets/Plugins/EQClassic"
mkdir -p "$PLUGINS"
cp "$REPO/rewrite/src/ClientCore/bin/Release/netstandard2.1/EQClassic.ClientCore.dll" \
   "$REPO/rewrite/src/Shared/bin/Release/netstandard2.1/EQClassic.Shared.dll" "$PLUGINS/"
LITENET=$(ls -d "$HOME"/.nuget/packages/litenetlib/*/lib/netstandard2.1/LiteNetLib.dll | sort -V | tail -1)
cp "$LITENET" "$PLUGINS/"

# The Lantern exports stay in build/lantern-work/Exports (the client reads its collision meshes and
# object lists there): under Assets, Unity would scan all their files before anything else.
# tools/unity/import-zones.sh copies a few zones at a time into Assets/EQAssets for the importers.
rm -rf "$OUT/Assets/EQAssets" "$OUT/Assets/EQAssets.meta"
[ -d "$REPO/build/lantern-work/Exports" ] ||
  echo "no Lantern exports: run tools/lantern/extract.sh <client dir> <zones> first" >&2
echo "Unity project ready: $OUT"
