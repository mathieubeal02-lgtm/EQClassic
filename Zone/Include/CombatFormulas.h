#ifndef EQC_COMBATFORMULAS_H
#define EQC_COMBATFORMULAS_H

// Melee combat model for the Classic -> Velious era, as reconstructed by the TAKP/Quarm project
// (EQMacEmu, zone/attack.cpp) from client decompiles, Sony developer posts and parses.
// Reimplemented here as pure functions of plain numbers (no Mob/Client), so they can be unit
// tested (tests/combat_test.cpp). EQMacEmu is GPLv3 and this tree is GPLv2: the maths is
// reproduced, the code is not.
//
// One swing:
//   1. hit or miss:  HitChance(ToHit(attacker), Avoidance(defender))
//   2. damage:       DamageBonus + MeleeDamage(D20(Offense(attacker), Mitigation(defender)), BaseDamage)
//                    (clients then get ClientDamageMultiplier)
//
// Class ids are Sony's (WARRIOR = 1 ... BEASTLORD = 15), race IKSAR = 128.

namespace Combat
{
	// ---- attacker ------------------------------------------------------------------------------

	// Offense of an NPC (level-based floor + strength), plus its ATK (DB and buffs).
	int NpcOffense(int level, int strBonus, int atk);
	// Offense of a player: weapon skill + ATK + strength above 75 (half of the client's ATK display).
	int ClientOffense(int weaponSkill, int str, int atkBonus, int playerClass, int level);
	// To-hit rating: offense skill + weapon skill + accuracy (NPC: DB accuracy).
	int ToHit(int offenseSkill, int weaponSkill, int accuracy, bool npc, int npcLevel);

	// ---- defender ------------------------------------------------------------------------------

	// Avoidance of an NPC: level-based, plus AGI from buffs/items and the DB avoidance bonus.
	int NpcAvoidance(int level, int agiBonus, int bonusAvoidance);
	// Avoidance of a player: Defense skill and AGI.
	int ClientAvoidance(int defenseSkill, int agi, int level);
	// Mitigation (AC) of an NPC. Below ~level 50 the DB AC is ignored: parses show uniform values.
	int NpcMitigation(int level, int dbAC, int itemAC, int spellAC);
	// Mitigation (AC) of a player: item AC with class bonuses, Defense skill, spell AC, AGI, caps.
	// weight is the carried weight (used by monks).
	int ClientMitigation(int level, int playerClass, int race, int itemAC, int spellAC,
	                     int defenseSkill, int agi, int weight);

	// ---- resolution ----------------------------------------------------------------------------

	// Probability (0..1) that a swing with this to-hit lands against this avoidance.
	double HitChance(int toHit, int avoidance);
	// The d20 damage roll (1..20) from an attack roll in [0, offense+5) and a defense roll in
	// [0, mitigation+5). RollD20() draws them with rand().
	int D20(int atkRoll, int defRoll, int offense, int mitigation);
	int RollD20(int offense, int mitigation);
	// Damage of one hit: roll x base damage / 10, at least minHit and 1.
	int MeleeDamage(int d20, int baseDamage, int minHit);

	// NPC DB min/max damage -> Sony's base damage (damage interval x 10) and damage bonus.
	int NpcBaseDamage(int minDmg, int maxDmg);
	int NpcDamageBonus(int minDmg, int maxDmg);

	// Player main-hand damage bonus (melee classes from level 28; 2-handers get more).
	int ClientDamageBonus(int level, int playerClass, bool twoHanded, int delay);
	// Player damage multiplier: with probability rollChance(level, class) percent, damage is
	// multiplied by (100 + extra) / 100 where extra is drawn in [0, baseBonus].
	void ClientMultiplierParams(int level, int playerClass, int& rollChance, int& maxExtra, int& minusFactor);
	int ClientDamageMultiplier(int damage, int offense, int level, int playerClass);

	bool IsMeleeClass(int playerClass);
}

#endif
