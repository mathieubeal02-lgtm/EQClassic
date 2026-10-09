#!/bin/bash
# Merchant purchase check against the legacy servers: the bot (Qbottwo, North Qeynos) buys 5 of the
# cheapest stack of Balhallia (EQBOT_SHOP_NPC), twice, and the database tells what it paid and got:
#   1. with one copper short of 5 items: refused, money unchanged;
#   2. with exactly the price of 5: bought, money 0, 5 in the stack.
# Usage: merchant-test.sh [unit price in copper]  (the default 8 was Balhallia's 13106 at the old fixed x2.5;
# the price now follows charisma and standing, Combat::MerchantPriceMultiplier: pass what Qbottwo pays)
# Needs the database credentials of runtime/db.ini (eqc/eqc here) and a GM bot account (bot/bot).
cd "$(dirname "$0")"
UNIT=${1:-8}
M="mysql --skip-ssl -ueqc -peqc eqclassic"
setcopper() { $M -e "update character_ set profile=concat(substring(profile,1,2460), unhex('00000000000000000000000000000000'), substring(profile,2477)) where name='Qbottwo'; update character_ set profile=concat(substring(profile,1,2472), unhex(lpad(hex(reverse(unhex(lpad(hex($1),8,'0')))),8,'0')), substring(profile,2477)) where name='Qbottwo'"; }
copper() { $M -N -e "select conv(hex(reverse(substring(profile,2461,4))),16,10)*1000 + conv(hex(reverse(substring(profile,2465,4))),16,10)*100 + conv(hex(reverse(substring(profile,2469,4))),16,10)*10 + conv(hex(reverse(substring(profile,2473,4))),16,10) from character_ where name='Qbottwo'"; }
clearpacks() { $M -e "update character_ set profile=concat(substring(profile,1,212), unhex(repeat('FFFF',8)), substring(profile,229)) where name='Qbottwo'"; }
fail=0
check() { if [ "$2" = "$3" ]; then echo "[ OK ] $1: $2"; else echo "[FAIL] $1: $2, want $3"; fail=1; fi; }

clearpacks; setcopper $((UNIT * 5 - 1))
out=$(EQBOT_HAIL=- EQBOT_SHOP=5 EQBOT_SHOP_NPC=Balhallia ./legacy-test.sh 2>&1 | grep "merchant buy"); echo "$out"
sleep 3
check "short of money: copper left" "$(copper)" "$((UNIT * 5 - 1))"

clearpacks; setcopper $((UNIT * 5))
out=$(EQBOT_HAIL=- EQBOT_SHOP=5 EQBOT_SHOP_NPC=Balhallia ./legacy-test.sh 2>&1 | grep "merchant buy"); echo "$out"
sleep 3
check "exact money: copper left" "$(copper)" "0"
# the packs were empty: the stack lands in the first pack slot (22), its charges at 348 + 22 x 10 + 2
check "exact money: stack bought" "$($M -N -e "select ord(substring(profile,571,1)) from character_ where name='Qbottwo'")" "5"
exit $fail
