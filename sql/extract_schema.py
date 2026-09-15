#!/usr/bin/env python3
"""Regenerate sql/schema.sql (CREATE TABLE statements only) from the eqclassic_db submodule dumps."""
import glob, os, re
root = os.path.dirname(os.path.abspath(__file__))
tables, out = [], []
for f in sorted(glob.glob(os.path.join(root, 'eqclassic_db', 'sql', '*.sql'))):
    s = open(f, encoding='utf-8', errors='replace').read()
    for m in re.finditer(r'(CREATE TABLE `(\w+)` \(.*?\) ENGINE=[^;]*;)', s, re.S):
        tables.append(m.group(2)); out.append(m.group(1))
hdr = (f"-- EQClassic world/zone/login schema ({len(tables)} tables), structure only.\n"
       "-- Extracted from the eqclassic_db dump (git submodule sql/eqclassic_db, commit 3fb126a, 2021-09-08,\n"
       "-- dumped from MariaDB 10.0.21). Regenerate with:  python3 sql/extract_schema.py\n"
       "-- Data is imported from sql/eqclassic_db/sql/*.sql (see sql/README.md).\n"
       "SET NAMES utf8;\nSET FOREIGN_KEY_CHECKS=0;\n\n")
open(os.path.join(root, 'schema.sql'), 'w').write(hdr + '\n\n'.join(out) + '\n')
print(f"{len(tables)} tables -> sql/schema.sql")
