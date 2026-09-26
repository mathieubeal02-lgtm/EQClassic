using EQClassic.Server.Combat;
using static EQClassic.Server.Combat.CombatFormulas;

namespace EQClassic.Tests.Combat;

/// <summary>The expected values of the C++ server's tests/combat_test.cpp: both servers must agree.</summary>
public class CombatFormulasTests
{
    [Fact]
    public void Npc_offense_level_floor_plus_strength()
    {
        Assert.Equal(5, NpcOffense(1, 0, 0));
        Assert.Equal(57, NpcOffense(10, 0, 0));
        Assert.Equal(293, NpcOffense(48, 0, 0)); // 46-50 flattened to 45
        Assert.Equal(400, NpcOffense(60, 0, 0));
        Assert.Equal(57 + 20 + 5, NpcOffense(10, 30, 5));
    }

    [Fact]
    public void Client_offense_and_to_hit()
    {
        Assert.Equal(50, ClientOffense(50, 75, 0, Warrior, 10));
        Assert.Equal(50 + 10 + 20, ClientOffense(50, 105, 10, Warrior, 10));
        Assert.Equal(200 + 240 - 216, ClientOffense(200, 75, 0, Ranger, 60)); // ranger 55+
        Assert.Equal(37, ToHit(10, 20, 0, false, 0));
        Assert.Equal(39, ToHit(10, 20, 0, true, 1));
    }

    [Fact]
    public void Avoidance()
    {
        Assert.Equal(14, NpcAvoidance(1, 0, 0));
        Assert.Equal(400, NpcAvoidance(50, 0, 0));
        Assert.Equal(460, NpcAvoidance(60, 0, 0));
        Assert.Equal(1, ClientAvoidance(0, 50, 10));
        Assert.Equal(400, ClientAvoidance(225, 50, 50));
        Assert.Equal(53, ClientAvoidance(0, 200, 50));
        Assert.Equal(1, ClientAvoidance(0, 20, 50)); // negative AGI part, floored
    }

    [Fact]
    public void Mitigation()
    {
        Assert.Equal(5, NpcMitigation(1, 26, 0, 0)); // Quarm: level 1 rat AC 5
        Assert.Equal(30, NpcMitigation(10, 63, 0, 0));
        Assert.Equal(67, NpcMitigation(20, 0, 0, 0));
        Assert.Equal(200, NpcMitigation(60, 150, 0, 0));
        Assert.Equal(325, NpcMitigation(60, 325, 0, 0));
        Assert.Equal(53, ClientMitigation(10, Warrior, 1, 30, 0, 30, 75, 50)); // 40 + 10 + 3
        Assert.Equal(85, ClientMitigation(10, Warrior, 1, 300, 0, 0, 50, 50)); // anti-twink cap
        Assert.Equal(30 + 10 + 10, ClientMitigation(10, Wizard, 1, 30, 30, 20, 50, 50));
        Assert.Equal(430, ClientMitigation(60, Warrior, 1, 600, 0, 0, 50, 50)); // warrior cap above 50
        Assert.Equal(10, ClientMitigation(10, Warrior, RaceIksar, 0, 0, 0, 50, 50));
    }

    [Fact]
    public void Hit_chance_and_d20()
    {
        Assert.Equal(1.0 - 110.0 / (110 * 1.21 * 2), HitChance(100, 100), 4);
        Assert.Equal(20 * 1.21 / (410 * 2.0), HitChance(10, 400), 4);
        Assert.Equal(20, D20(9, 0, 5, 5)); // best attack roll (offense + 4)
        Assert.Equal(19, D20(4, 0, 5, 5));
        Assert.Equal(1, D20(0, 9, 5, 5));
        var random = new Random(1);
        for (int i = 0; i < 1000; i++)
            Assert.InRange(RollD20(random, 50, 30), 1, 20);
    }

    [Fact]
    public void Npc_damage_from_db_min_max()
    {
        Assert.Equal(2, NpcBaseDamage(1, 4)); // a level 1 rat hits 1..4
        Assert.Equal(0, NpcDamageBonus(1, 4));
        Assert.Equal(4, NpcDamageBonus(1, 4) + MeleeDamage(20, NpcBaseDamage(1, 4), 0));
        Assert.Equal(1, NpcDamageBonus(1, 4) + MeleeDamage(1, NpcBaseDamage(1, 4), 0));
        Assert.Equal(139, NpcDamageBonus(36, 139) + MeleeDamage(20, NpcBaseDamage(36, 139), 0)); // Fish Ranamer, Quarm 36-139
    }

    [Fact]
    public void Client_damage_bonus_and_multiplier()
    {
        Assert.Equal(0, ClientDamageBonus(27, Warrior, true, 40));
        Assert.Equal(1, ClientDamageBonus(28, Warrior, false, 30));
        Assert.Equal(9, ClientDamageBonus(40, Warrior, true, 40));
        Assert.Equal(0, ClientDamageBonus(40, Wizard, true, 40));
        var random = new Random(2);
        for (int i = 0; i < 1000; i++)
            Assert.InRange(ClientDamageMultiplier(random, 100, 300, 50, Warrior), 100, 210);
    }
}
