#!/bin/bash
# Print a character's saved inventory (worn + general slots) with item names.
M="mariadb --skip-ssl -h 127.0.0.1 -u eqc -peqc eqclassic"
HEX=$($M -N -e "SELECT HEX(profile) FROM character_ WHERE name='$1'")
python3 - "$HEX" <<'PY'
import struct,sys,subprocess
b=bytes.fromhex(sys.argv[1]); inv=struct.unpack_from('<30H',b,168)
for slot,i in enumerate(inv):
    if i not in (0,0xFFFF):
        n=subprocess.run(['mariadb','--skip-ssl','-h','127.0.0.1','-u','eqc','-peqc','eqclassic','-N','-e',f"SELECT name FROM items_axclassic WHERE id={i}"],capture_output=True,text=True).stdout.strip()
        print(f"  slot {slot:2d}: {i:5d} {n}")
PY
