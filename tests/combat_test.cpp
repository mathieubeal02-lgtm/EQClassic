// Unit tests for Zone/Source/CombatFormulas.cpp. Expected values are worked out by hand from the
// formulas (see CombatFormulas.h), and cross-checked against Quarm DB data where possible.
#include "../Zone/Include/CombatFormulas.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>

static int failures = 0;
#define EXPECT_EQ(a, b) do { long _a = (a), _b = (b); if (_a != _b) { printf("FAIL line %d: %s = %ld, want %ld\n", __LINE__, #a, _a, _b); failures++; } } while (0)
#define EXPECT_NEAR(a, b) do { double _a = (a), _b = (b); if (std::fabs(_a - _b) > 0.0005) { printf("FAIL line %d: %s = %f, want %f\n", __LINE__, #a, _a, _b); failures++; } } while (0)

int main()
{
	using namespace Combat;

	// NPC offense: level floor + strength, levels 46-50 flattened to 45, base capped at 320
	EXPECT_EQ(NpcOffense(1, 0, 0), 5);
	EXPECT_EQ(NpcOffense(10, 0, 0), 57);
	EXPECT_EQ(NpcOffense(48, 0, 0), 293);
	EXPECT_EQ(NpcOffense(60, 0, 0), 400);
	EXPECT_EQ(NpcOffense(10, 30, 5), 57 + 20 + 5);

	// Client offense: skill + ATK + (2*STR-150)/3 above 75 STR
	EXPECT_EQ(ClientOffense(50, 75, 0, 1, 10), 50);
	EXPECT_EQ(ClientOffense(50, 105, 10, 1, 10), 50 + 10 + 20);
	EXPECT_EQ(ClientOffense(200, 75, 0, 4, 60), 200 + 240 - 216);	// ranger 55+

	EXPECT_EQ(ToHit(10, 20, 0, false, 0), 37);
	EXPECT_EQ(ToHit(10, 20, 0, true, 1), 39);

	// NPC avoidance: level * 9 + 5, capped 400 up to 50 then 460
	EXPECT_EQ(NpcAvoidance(1, 0, 0), 14);
	EXPECT_EQ(NpcAvoidance(50, 0, 0), 400);
	EXPECT_EQ(NpcAvoidance(60, 0, 0), 460);

	// Client avoidance: Defense skill and AGI curve
	EXPECT_EQ(ClientAvoidance(0, 50, 10), 1);
	EXPECT_EQ(ClientAvoidance(225, 50, 50), 400);
	EXPECT_EQ(ClientAvoidance(0, 200, 50), 53);
	EXPECT_EQ(ClientAvoidance(0, 20, 50), 1);	// negative AGI part, floored

	// NPC mitigation: level curve, capped 200; DB AC only counts above 200 (Quarm: level 1 rat AC 5)
	EXPECT_EQ(NpcMitigation(1, 26, 0, 0), 5);
	EXPECT_EQ(NpcMitigation(10, 63, 0, 0), 30);
	EXPECT_EQ(NpcMitigation(20, 0, 0, 0), 67);
	EXPECT_EQ(NpcMitigation(60, 150, 0, 0), 200);
	EXPECT_EQ(NpcMitigation(60, 325, 0, 0), 325);

	// Client mitigation
	EXPECT_EQ(ClientMitigation(10, 1, 1, 30, 0, 30, 75, 50), 53);	// warrior: 40 + 10 + 3
	EXPECT_EQ(ClientMitigation(10, 1, 1, 300, 0, 0, 50, 50), 85);	// anti-twink cap
	EXPECT_EQ(ClientMitigation(10, 12, 1, 30, 30, 20, 50, 50), 30 + 10 + 10);	// wizard
	EXPECT_EQ(ClientMitigation(60, 1, 1, 600, 0, 0, 50, 50), 430);	// warrior cap above 50
	EXPECT_EQ(ClientMitigation(10, 1, 128, 0, 0, 0, 50, 50), 10);	// iksar

	// Hit chance
	EXPECT_NEAR(HitChance(100, 100), 1.0 - 110.0 / (110 * 1.21 * 2));
	EXPECT_NEAR(HitChance(10, 400), 20 * 1.21 / (410 * 2.0));

	// d20
	EXPECT_EQ(D20(9, 0, 5, 5), 20);	// best attack roll (offense + 4)
	EXPECT_EQ(D20(4, 0, 5, 5), 19);
	EXPECT_EQ(D20(0, 9, 5, 5), 1);
	for (int i = 0; i < 1000; i++)
	{
		int r = RollD20(50, 30);
		if (r < 1 || r > 20) { printf("FAIL RollD20 out of range: %d\n", r); failures++; break; }
	}

	// NPC damage from DB min/max: a level 1 rat (1-4) hits 1..4
	EXPECT_EQ(NpcBaseDamage(1, 4), 2);
	EXPECT_EQ(NpcDamageBonus(1, 4), 0);
	EXPECT_EQ(NpcDamageBonus(1, 4) + MeleeDamage(20, NpcBaseDamage(1, 4), 0), 4);
	EXPECT_EQ(NpcDamageBonus(1, 4) + MeleeDamage(1, NpcBaseDamage(1, 4), 0), 1);
	// Fish Ranamer (Quarm: 36-139): max d20 roll gives the DB max
	EXPECT_EQ(NpcDamageBonus(36, 139) + MeleeDamage(20, NpcBaseDamage(36, 139), 0), 139);

	// Client damage bonus
	EXPECT_EQ(ClientDamageBonus(27, 1, true, 40), 0);
	EXPECT_EQ(ClientDamageBonus(28, 1, false, 30), 1);
	EXPECT_EQ(ClientDamageBonus(40, 1, true, 40), 9);
	EXPECT_EQ(ClientDamageBonus(40, 12, true, 40), 0);	// wizard

	// Client multiplier never lowers damage and respects the cap
	for (int i = 0; i < 1000; i++)
	{
		int d = ClientDamageMultiplier(100, 300, 50, 1);
		if (d < 100 || d > 210) { printf("FAIL multiplier out of range: %d\n", d); failures++; break; }
	}

	// Reach: playable races are 5 whatever their size; others 6 x size / 5
	EXPECT_NEAR(BoundingRadius(1, 0.0f), 5.0);
	EXPECT_NEAR(BoundingRadius(9, 8.0f), 5.0);	// a troll's size does not change it
	EXPECT_NEAR(BoundingRadius(60, 0.0f), 6.0);	// skeleton, default size
	EXPECT_NEAR(BoundingRadius(18, 20.0f), 24.0);	// giant
	EXPECT_NEAR(BoundingRadius(49, 20.0f), 39.0);	// lava dragon: fixed size 32.5
	EXPECT_NEAR(BoundingRadius(184, 20.0f), 37.92);
	EXPECT_NEAR(MeleeRange(5.0f, 5.0f), 16.0);	// two players: floor 14 + 2
	EXPECT_NEAR(MeleeRange(5.0f, 24.0f), 23.75);	// player and giant
	EXPECT_NEAR(MeleeRange(5.0f, 39.0f), 35.0);	// player and Nagafen
	EXPECT_NEAR(MeleeRange(60.0f, 60.0f), 75.0);	// cap

	// Death: no loss up to level 5, level% of the level's exp below 25, a quarter from 25; halved, x 0.9
	EXPECT_EQ(DeathExpLoss(5, 40000), 0);
	EXPECT_EQ(DeathExpLoss(6, 54900), 1482);		// human warrior: 54900 x 6% / 2 x 0.9
	EXPECT_EQ(DeathExpLoss(20, 924300), 83187);
	EXPECT_EQ(DeathExpLoss(30, 2193300), 246745);
	EXPECT_EQ(DeathExpLoss(60, 100000000), 6000000);	// cap

	// Casting: 95 without adjustment, 98 with a maxed specialization
	EXPECT_EQ(CastSuccessChance(12, 10, 0, 50, 150, 5, 0), 95);
	EXPECT_EQ(CastSuccessChance(12, 10, 0, 50, 150, 5, 200), 98);
	// with an adjustment: skill 100 + 5 x (18 - 9) + 150/10 - 5 - 20 = 135 -> 95
	EXPECT_EQ(CastSuccessChance(12, 10, 20, 100, 150, 5, 0), 95);
	// a level 49 spell at skill 150: 150 + 5 x (18 - 48) + 20 - 10 - 30 = -20 -> 5
	EXPECT_EQ(CastSuccessChance(12, 49, 30, 150, 200, 10, 0), 5);
	// skill 235, level 39 spell, adjustment 30: 235 - 105 + 20 - 0 - 30 = 120 -> 95
	EXPECT_EQ(CastSuccessChance(6, 39, 30, 235, 200, 0, 0), 95);
	// skill 200, level 49 spell, adjustment 25: 200 - 150 + 20 - 10 - 25 = 35, + 50 spec / 10 + 1 = 41
	EXPECT_EQ(CastSuccessChance(6, 49, 25, 200, 200, 10, 50), 41);
	// level 56+ spells ignore the adjustment: 235 - 160 + 25 - 0 = 100 -> 95
	EXPECT_EQ(CastSuccessChance(12, 60, 40, 235, 255, 0, 0), 95);

	EXPECT_EQ(GroupExpBonusPercent(1), 0);
	EXPECT_EQ(GroupExpBonusPercent(2), 2);
	EXPECT_EQ(GroupExpBonusPercent(6), 10);
	EXPECT_EQ(FizzleManaCost(100, 2000), 40);
	EXPECT_EQ(FizzleManaCost(400, 800), 100);	// capped at an eighth of the pool

	if (failures == 0)
		printf("combat_test: all tests passed\n");
	return failures == 0 ? 0 : 1;
}
