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

	// ---- reach ---------------------------------------------------------------------------------
	// The client decides a swing is in range from both models' bounding radii (Harakiri's rev. 872;
	// TAKP's CalcBoundingRadius / CombatRange): the server must agree, or a player hits the air (a
	// dragon) or is hit from too far.

	// Bounding radius of a model: 6 x size / 5 by default; playable races and a few big models
	// have their own radius or size. size <= 0 counts as 5.
	float BoundingRadius(int race, float size);
	// Melee range between two models: (radius1 + radius2) x 0.75, at least 14, plus 2, at most 75.
	float MeleeRange(float radius1, float radius2);

	// ---- death ---------------------------------------------------------------------------------
	// Experience lost on death (as TAKP has it from Sony's history): a share of the experience the
	// current level took (levelExp = exp(level) - exp(level - 1)), level% of it below 25 and a
	// quarter from 25, halved (May 24 1999) and x 0.9, at most 6,000,000. Nothing up to level 5.
	int DeathExpLoss(int level, int levelExp);
	// Experience bonus of a group kill, in percent: 2% per member past the first (2, 4, 6, 8, 10).
	int GroupExpBonusPercent(int membersInZone);

	// ---- casting -------------------------------------------------------------------------------
	// Chance (percent) that a player's spell does not fizzle, as the client computes it (TAKP's
	// CheckFizzle): 95 unless the spell has a fizzle adjustment; then casting skill + 5 x (18 - spell
	// level, at most 50) + prime stat / 10 - a 0..10 random penalty - the adjustment, within 5..95
	// (bards 1..95). Specialization adds skill / 10 + 1, up to 98. spellLevel is the level this class
	// gets the spell at; primeStat WIS or INT (bards (CHA + DEX) / 2).
	int CastSuccessChance(int playerClass, int spellLevel, int fizzleAdjustment, int castingSkill,
	                      int primeStat, int randomPenalty, int specializeSkill);
	// Mana a fizzle costs: 40% of the spell's, at most an eighth of the caster's pool.
	int FizzleManaCost(int manaCost, int maxMana);
}

#endif
