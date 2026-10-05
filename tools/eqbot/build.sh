#!/bin/sh
# Builds eqbot with g++ alone (no CMake), same sources and flags as CMakeLists.txt. Usage: build.sh [output]
set -e
cd "$(dirname "$0")"
OUT=${1:-../../build-linux/bin/eqbot}
mkdir -p "$(dirname "$OUT")"
C=../../Common/Source
g++ -O1 -w -include config.h -I../../Common/Include -I../../EQC/Include -o "$OUT" \
    eqbot.cpp EqSession.cpp $C/EQPacket.cpp $C/EQPacketManager.cpp $C/Fragment.cpp $C/FragmentGroup.cpp \
    $C/FragmentGroupList.cpp $C/timer.cpp $C/packet_dump.cpp -lcrypto -lz -pthread
echo "$OUT"
