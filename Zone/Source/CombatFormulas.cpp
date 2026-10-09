// Melee combat formulas, see Zone/Include/CombatFormulas.h for the model and its origin.
#include "../Include/CombatFormulas.h"

#include <cstdlib>

namespace
{
	// Sony class ids (Common/Include/classes.h), repeated here to keep this file self-contained.
	enum { WAR = 1, CLR, PAL, RNG, SHD, DRU, MNK, BRD, ROG, SHM, NEC, WIZ, MAG, ENC, BST };
	const int RACE_IKSAR = 128;

	int RandomInt(int low, int high)	// inclusive
	{
		if (high <= low)
			return low;
		return low + rand() % (high - low + 1);
	}

	bool IsPureCaster(int c) { return c == NEC || c == WIZ || c == MAG || c == ENC; }
}

namespace Combat
{
	bool IsMeleeClass(int c)
	{
		return c == WAR || c == PAL || c == RNG || c == SHD || c == MNK || c == BRD || c == ROG || c == BST;
	}

	// ---- attacker ------------------------------------------------------------------------------

	int NpcOffense(int level, int strBonus, int atk)
	{
		// NPC weapon skills plateau between 46 and 50, so does their offense.
		if (level > 45 && level < 51)
			level = 45;

		int base, str;
		if (level < 6)
		{
			base = level * 4;
			str = level;
		}
		else
		{
			base = level * 55 / 10 - 4;
			if (base > 320)
				base = 320;
			str = (level < 30) ? level / 2 + 1 : level * 2 - 40;
		}
		str += strBonus * 2 / 3;
		if (str < 0)
			str = 0;

		int offense = base + str + atk;
		return offense < 1 ? 1 : offense;
	}

	int ClientOffense(int weaponSkill, int str, int atkBonus, int playerClass, int level)
	{
		int offense = weaponSkill + atkBonus + (str >= 75 ? (2 * str - 150) / 3 : 0);
		if (offense < 1)
			offense = 1;
		if (playerClass == RNG && level > 54)
			offense += level * 4 - 216;
		return offense;
	}

	int ToHit(int offenseSkill, int weaponSkill, int accuracy, bool npc, int npcLevel)
	{
		if (npc && npcLevel < 3)
			accuracy += 2;	// the lowest level NPCs parse slightly more accurate
		return 7 + offenseSkill + weaponSkill + accuracy;
	}

	// ---- defender ------------------------------------------------------------------------------

	int NpcAvoidance(int level, int agiBonus, int bonusAvoidance)
	{
		int avoidance = level * 9 + 5;
		int cap = (level <= 50) ? 400 : 460;
		if (avoidance > cap)
			avoidance = cap;
		avoidance += agiBonus * 22 / 100 + bonusAvoidance;
		return avoidance < 1 ? 1 : avoidance;
	}

	int ClientAvoidance(int defenseSkill, int agi, int level)
	{
		int fromDefense = defenseSkill > 0 ? defenseSkill * 400 / 225 : 0;

		// AGI: -25..0 below 40, nothing from 40 to 59, then a level-dependent curve up to 200 AGI.
		int fromAgi = 0;
		if (agi < 40)
		{
			fromAgi = 25 * (agi - 40) / 40;
		}
		else if (agi >= 60)
		{
			int adj;
			if (agi <= 74)
				adj = 28;
			else if (level < 7)
				adj = 35;
			else if (level < 20)
				adj = 55;
			else if (level < 40)
				adj = 70;
			else
				adj = 80;
			fromAgi = agi < 200 ? 2 * (adj - (200 - agi) / 5) / 3 : 2 * adj / 3;
		}

		int avoidance = fromDefense + fromAgi;
		return avoidance < 1 ? 1 : avoidance;
	}

	int NpcMitigation(int level, int dbAC, int itemAC, int spellAC)
	{
		int mit;
		if (level < 15)
			mit = level * 3 + (level < 3 ? 2 : 0);
		else
			mit = level * 41 / 10 - 15;
		if (mit > 200)
			mit = 200;
		// Only tough NPCs (raid targets) are allowed above the curve, using their DB AC.
		if (mit == 200 && dbAC > 200)
			mit = dbAC;
		mit += 4 * itemAC / 3 + spellAC / 4;
		return mit < 1 ? 1 : mit;
	}

	namespace
	{
		// Monk weight limits (hard, soft) for the unencumbered AC bonus.
		void MonkWeightCaps(int level, int& hard, int& soft)
		{
			if (level < 15)       { hard = 30; soft = 14; }
			else if (level <= 29) { hard = 32; soft = 15; }
			else if (level <= 44) { hard = 34; soft = 16; }
			else if (level <= 50) { hard = 36; soft = 17; }
			else if (level <= 54) { hard = 38; soft = 18; }
			else if (level <= 59) { hard = 40; soft = 20; }
			else if (level <= 61) { hard = 45; soft = 24; }
			else if (level <= 63) { hard = 47; soft = 24; }
			else if (level == 64) { hard = 50; soft = 24; }
			else                  { hard = 53; soft = 24; }
		}

		// Agility-scaled AC bonus used by rogues and beastlords.
		int AgiScaledBonus(int levelScaler, int divisor, int agi, int cap)
		{
			int steps;
			if (agi < 80)       steps = 1;
			else if (agi < 85)  steps = 2;
			else if (agi < 90)  steps = 3;
			else if (agi < 100) steps = 4;
			else                steps = 5;
			int bonus = levelScaler * steps / divisor;
			return bonus > cap ? cap : bonus;
		}
	}

	int ClientMitigation(int level, int playerClass, int race, int itemAC, int spellAC,
	                     int defenseSkill, int agi, int weight)
	{
		int ac = itemAC;
		if (!IsPureCaster(playerClass))
			ac = 4 * ac / 3;
		// Low level characters cannot twink their way to high AC.
		if (level < 50 && ac > level * 6 + 25)
			ac = level * 6 + 25;

		if (playerClass == MNK)
		{
			int hard, soft;
			MonkWeightCaps(level, hard, soft);
			double bonus = level + 5.0;
			if (weight <= soft)
			{
				ac += (int)(bonus * 4.0 / 3.0);
			}
			else if (weight > hard + 1)
			{
				double scale = (weight - (hard - 10)) / 100.0;
				if (scale > 1.0)
					scale = 1.0;
				ac -= (int)(scale * 4.0 * bonus / 3.0);
			}
			else
			{
				double reduction = (weight - soft) * 6.66667;
				if (reduction > 100.0)
					reduction = 100.0;
				bonus *= (100.0 - reduction) / 100.0;
				if (bonus < 0.0)
					bonus = 0.0;
				ac += (int)(4.0 * bonus / 3.0);
			}
		}
		else if (playerClass == ROG)
		{
			if (level >= 30 && agi > 75)
				ac += AgiScaledBonus(level - 26, 4, agi, 12);
		}
		else if (playerClass == BST)
		{
			if (level > 10)
				ac += AgiScaledBonus(level - 6, 5, agi, 16);
		}

		if (race == RACE_IKSAR)
			ac += level < 10 ? 10 : (level > 35 ? 35 : level);
		if (ac < 0)
			ac = 0;

		if (defenseSkill > 0)
			ac += IsPureCaster(playerClass) ? defenseSkill / 2 : defenseSkill / 3;
		ac += spellAC / (IsPureCaster(playerClass) ? 3 : 4);
		if (agi > 70)
			ac += agi / 20;
		if (ac < 0)
			ac = 0;

		// Hard AC cap, raised per class above level 50 with Velious. Returns above the cap only
		// came with Luclin, which the Trilogy client predates.
		int cap = 350;
		if (level > 50)
		{
			switch (playerClass)
			{
				case WAR: cap = 430; break;
				case PAL: case SHD: case CLR: case BRD: cap = 403; break;
				case RNG: case SHM: cap = 375; break;
				default: cap = 350; break;
			}
		}
		return ac > cap ? cap : ac;
	}

	// ---- resolution ----------------------------------------------------------------------------

	double HitChance(int toHit, int avoidance)
	{
		double t = (toHit + 10) * 1.21;
		double a = avoidance + 10;
		return t > a ? 1.0 - a / (t * 2.0) : t / (a * 2.0);
	}

	int D20(int atkRoll, int defRoll, int offense, int mitigation)
	{
		int avg = (offense + mitigation + 10) / 2;
		if (avg < 1)
			avg = 1;
		int index = atkRoll - defRoll + avg / 2;
		if (index < 0)
			index = 0;
		index = index * 20 / avg;
		if (index > 19)
			index = 19;
		return index + 1;
	}

	int RollD20(int offense, int mitigation)
	{
		return D20(RandomInt(0, offense + 4), RandomInt(0, mitigation + 4), offense, mitigation);
	}

	int MeleeDamage(int d20, int baseDamage, int minHit)
	{
		int damage = (d20 * baseDamage + 5) / 10;
		if (damage < minHit)
			damage = minHit;
		return damage < 1 ? 1 : damage;
	}

	namespace
	{
		// (max - min) / 19, as thousandths rounded to a tenth: the DB stores min/max hits while
		// Sony's model is damage bonus + d20 x damage interval.
		int IntervalTimes1000(int minDmg, int maxDmg)
		{
			int di = (maxDmg - minDmg) * 1000 / 19;
			return (di + 50) / 100 * 100;
		}
	}

	int NpcBaseDamage(int minDmg, int maxDmg)
	{
		if (maxDmg <= minDmg)
			return 1;
		int base = IntervalTimes1000(minDmg, maxDmg) / 100;
		return base < 1 ? 1 : base;
	}

	int NpcDamageBonus(int minDmg, int maxDmg)
	{
		if (minDmg > maxDmg)
			return minDmg;
		return (maxDmg * 1000 - IntervalTimes1000(minDmg, maxDmg) * 20) / 1000;
	}

	int ClientDamageBonus(int level, int playerClass, bool twoHanded, int delay)
	{
		if (level < 28 || !IsMeleeClass(playerClass))
			return 0;
		int bonus = 1 + (level - 28) / 3;
		if (!twoHanded)
			return bonus;
		if (delay <= 27)
			return bonus + 1;
		if (level > 29)
		{
			int levelBonus = (level - 30) / 5 + 1;
			if (level > 50)
			{
				levelBonus++;
				int extra = level - 50;
				if (level > 67)      extra += 5;
				else if (level > 59) extra += 4;
				else if (level > 58) extra += 3;
				else if (level > 56) extra += 2;
				else if (level > 54) extra += 1;
				levelBonus += extra * delay / 40;
			}
			bonus += levelBonus;
		}
		if (delay >= 40)
		{
			int delayBonus = (delay - 40) / 3 + 1;
			if (delay >= 45)
				delayBonus += 2;
			else if (delay >= 43)
				delayBonus++;
			bonus += delayBonus;
		}
		return bonus;
	}

	void ClientMultiplierParams(int level, int playerClass, int& rollChance, int& maxExtra, int& minusFactor)
	{
		bool monk = playerClass == MNK;
		if (monk && level >= 65)                    { rollChance = 83; maxExtra = 300; minusFactor = 50; }
		else if (level >= 65 || (monk && level >= 63)) { rollChance = 81; maxExtra = 295; minusFactor = 55; }
		else if (level >= 63 || (monk && level >= 60)) { rollChance = 79; maxExtra = 290; minusFactor = 60; }
		else if (level >= 60 || (monk && level >= 56)) { rollChance = 77; maxExtra = 285; minusFactor = 65; }
		else if (level >= 56)                       { rollChance = 72; maxExtra = 265; minusFactor = 70; }
		else if (level >= 51 || monk)               { rollChance = 65; maxExtra = 245; minusFactor = 80; }
		else                                        { rollChance = 51; maxExtra = 210; minusFactor = 105; }
	}

	int ClientDamageMultiplier(int damage, int offense, int level, int playerClass)
	{
		int rollChance, maxExtra, minusFactor;
		ClientMultiplierParams(level, playerClass, rollChance, maxExtra, minusFactor);
		if (RandomInt(1, 100) > rollChance)
			return damage;
		int baseBonus = (offense - minusFactor) / 2;
		if (baseBonus < 10)
			baseBonus = 10;
		int multiplier = 100 + RandomInt(0, baseBonus);
		if (multiplier > maxExtra)
			multiplier = maxExtra;
		damage = damage * multiplier / 100;
		if (level >= 55 && damage > 1 && IsMeleeClass(playerClass))
			damage++;
		return damage;
	}

	float BoundingRadius(int race, float size)
	{
		float radius = 6.0f;
		if (size <= 0.0f)
			size = 5.0f;
		switch (race)
		{
		case 1: case 2: case 3: case 4: case 5: case 6:	// human, barbarian, erudite, wood/high/dark elf
		case 7: case 8: case 9: case 10: case 11: case 12:	// half elf, dwarf, troll, ogre, halfling, gnome
		case 128: case 130:									// iksar, vah shir
			size = 5.0f;
			radius = 5.0f;
			break;
		case 19:	// Trakanon
		case 192:	// clockwork dragon
			radius = 10.48f;
			break;
		case 184: case 195: case 196: case 198:	// Velious dragons
			radius = 9.48f;
			break;
		case 34:	// giant bat
			size = 5.0f;
			break;
		case 49:	// lava dragon
			size = 32.5f;
			break;
		case 158:	// wurm
			size = 16.0f;
			break;
		}
		return radius * size / 5.0f;
	}

	float MeleeRange(float radius1, float radius2)
	{
		float range = (radius1 + radius2) * 0.75f;
		if (range < 14.0f)
			range = 14.0f;
		range += 2.0f;
		if (range > 75.0f)
			range = 75.0f;
		return range;
	}

	int DeathExpLoss(int level, int levelExp)
	{
		if (level <= 5 || levelExp <= 0)
			return 0;
		long long loss = level >= 25 ? levelExp / 4 : (long long)levelExp * level / 100;
		loss /= 2;
		loss = loss * 9 / 10;
		if (loss > 6000000)
			loss = 6000000;
		return (int)loss;
	}

	int CastSuccessChance(int playerClass, int spellLevel, int fizzleAdjustment, int castingSkill,
	                      int primeStat, int randomPenalty, int specializeSkill)
	{
		const int BARD = 8;
		int chance = 95;
		if (fizzleAdjustment != 0)
		{
			// the adjustment does not apply to spells past level 55, nor past 40 for the hybrids
			if (spellLevel > 55)
				fizzleAdjustment = 0;
			if ((playerClass == 3 || playerClass == 4 || playerClass == 5 || playerClass == 15) && spellLevel > 40)
				fizzleAdjustment = 0;
			if (playerClass == BARD && fizzleAdjustment > 15)
				fizzleAdjustment = 15;
			int effectiveLevel = spellLevel - 1 > 50 ? 50 : spellLevel - 1;
			if (castingSkill < 0)
				castingSkill = 0;
			chance = castingSkill + 5 * (18 - effectiveLevel) + primeStat / 10 - randomPenalty - fizzleAdjustment;
			int low = playerClass == BARD ? 1 : 5;
			if (chance < low)
				chance = low;
			if (chance > 95)
				chance = 95;
		}
		if (specializeSkill > 0)
		{
			chance += specializeSkill / 10 + 1;
			if (chance > 98)
				chance = 98;
		}
		return chance;
	}

	int FizzleManaCost(int manaCost, int maxMana)
	{
		int cost = manaCost * 4 / 10;
		int cap = maxMana / 8;
		return cost > cap ? cap : cost;
	}

	int GroupExpBonusPercent(int membersInZone)
	{
		if (membersInZone < 2)
			return 0;
		if (membersInZone > 6)
			membersInZone = 6;
		return 2 * (membersInZone - 1);
	}

	int ClientHPRegen(int level, bool sitting, bool monkFeigned, bool famished, bool racialRegen)
	{
		int regen = 1;
		if (sitting)
			regen += 1 + (level >= 20 ? 1 : 0) + (level >= 50 ? 1 : 0);
		if (monkFeigned && level > 50)
			regen += 1;
		if (famished)
			regen = 0;
		regen += (level >= 51 ? 1 : 0) + (level >= 56 ? 1 : 0) + (level >= 60 ? 1 : 0);
		if (racialRegen)
		{
			regen += (level >= 51 ? 1 : 0) + (level >= 56 ? 1 : 0);
			regen *= 2;
		}
		return regen;
	}

	int ClientManaRegen(bool sitting, bool famished, bool bard, int meditateSkill)
	{
		if (famished)
			return 0;
		if (!sitting)
			return 1;
		if (bard || meditateSkill <= 0)
			return 2;
		return meditateSkill > 1 ? 4 + meditateSkill / 15 : 3;
	}

	float MerchantPriceMultiplier(int cha, bool amiableOrBetter, bool apprehensive)
	{
		if (amiableOrBetter)
			cha += 11;
		// the share of the price a merchant pays; it charges the inverse
		float pays;
		if (!apprehensive)
		{
			if (cha > 75)
				pays = 1.0f - (115 - cha) * 0.004f;
			else if (cha > 60)
				pays = 1.0f / 1.25f;
			else
				pays = 1.0f / (1.0f + (120 - cha) / 220.0f);
		}
		else
		{
			if (cha > 75)
				pays = (100.0f - (145 - cha) / 2.8f) / 100.0f;
			else if (cha > 60)
				pays = 1.0f / 1.4f;
			else
				pays = 1.0f / (1.0f + (143.574f - cha) / 196.434f);
		}
		float multiplier = 1.0f / pays;
		return multiplier < 1.05f || pays <= 0 ? 1.05f : multiplier;
	}

	int TrainingCost(int skill, float priceMultiplier)
	{
		int adjusted = skill - 10;
		if (adjusted <= 0)
			return 0;
		return (int)((double)adjusted * adjusted * adjusted * priceMultiplier * 0.0099999998);
	}
}
