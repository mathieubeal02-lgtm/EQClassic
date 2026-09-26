using EQClassic.Server.Characters;
using EQClassic.Server.Combat;
using EQClassic.Server.Zone;

namespace EQClassic.Tests.Combat;

public class MeleeTests
{
    // Quarm / live DB: a level 1 rat, 16 HP, hits 1-4, AC 5.
    private static readonly NpcTemplate Rat = new(1, "a_rat", 36, 2, 1, 2f) { Combat = new NpcCombatStats(1, 16, 1, 4, AC: 5) };

    // The level 1 troll shaman of the client's creation packet: STR 108, STA 119, AGI 83, untrained skills, nothing worn.
    private static PlayerProfile Shaman(params (int Slot, int Item)[] items)
    {
        var inventory = new int[30];
        foreach (var (slot, item) in items)
            inventory[slot] = item;
        return new PlayerProfile("Qbot", 0, 0, 9, 10, 1, 0, 0, 0, 0, "grobb")
        {
            CurHp = 25, Str = 108, Sta = 119, Agi = 83, Dex = 75, Skills = new int[74], Inventory = inventory,
        };
    }

    [Fact]
    public void Npc_numbers_come_from_its_level_class_and_db_columns()
    {
        var rat = Combatant.ForNpc(Rat);
        // Offense 1×4 + 1 STR; to-hit 7 + offense skill 10 + melee skill 10 + 2 (NPCs under level 3).
        Assert.Equal((5, 29, 14, 5), (rat.Offense, rat.ToHit, rat.Avoidance, rat.Mitigation));
        Assert.Equal((0, 2, 2f, 16), (rat.DamageBonus, rat.BaseDamage, rat.DelaySeconds, rat.MaxHp));
    }

    [Fact]
    public void Player_numbers_come_from_the_profile_and_bare_hands()
    {
        var shaman = Combatant.ForPlayer(Shaman(), new InMemoryItemSource());
        // Offense (2×108-150)/3 = 22; to-hit 7; AGI 83 at level 1: 2×(35 - 117/5)/3 = 8; AC 83/20 = 4.
        Assert.Equal((22, 7, 8, 4), (shaman.Offense, shaman.ToHit, shaman.Avoidance, shaman.Mitigation));
        // Fists 2 / 3.6 s; HP 5 + 15 + 15×119/300 = 25 (shaman multiplier 15).
        Assert.Equal((2, 3.6f, 25), (shaman.BaseDamage, shaman.DelaySeconds, shaman.MaxHp));
    }

    [Fact]
    public void Weapons_and_worn_armour_change_the_numbers()
    {
        var items = new InMemoryItemSource();
        items.Items[5019] = new ItemStats(5019, "Rusty Two Handed Sword", 12, 50, ItemStats.TwoHandSlash, 0);
        items.Items[2001] = new ItemStats(2001, "Cloth Cap", 0, 0, 10, 1);
        var armed = Combatant.ForPlayer(Shaman((13, 5019), (2, 2001)), items);
        Assert.Equal((12, 5f), (armed.BaseDamage, armed.DelaySeconds));
        Assert.Equal(4 + 1, armed.Mitigation); // 4/3 × 1 item AC (non-caster), + AGI part
    }

    [Fact]
    public void A_rat_hits_a_level_one_shaman_about_four_times_in_five_for_one_to_four()
    {
        var rat = Combatant.ForNpc(Rat);
        var shaman = Combatant.ForPlayer(Shaman(), null);
        var random = new Random(7);
        int hits = 0;
        for (int i = 0; i < 10000; i++)
        {
            var swing = Melee.Swing(rat, shaman, random);
            if (!swing.Hit)
                continue;
            hits++;
            Assert.InRange(swing.Damage, 1, 4);
        }
        // HitChance(29, 8) = 1 - 18 / (39 × 1.21 × 2) = 0.809, rounded to 81 %.
        Assert.InRange(hits, 7900, 8300);
    }

    [Fact]
    public void Sitting_defenders_take_the_best_roll()
    {
        var rat = Combatant.ForNpc(Rat);
        var shaman = Combatant.ForPlayer(Shaman(), null);
        var swing = Melee.Swing(rat, shaman, new Random(1), defenderSitting: true);
        Assert.Equal(new SwingResult(true, 4), swing);
    }

    [Fact]
    public void Skill_caps_follow_level_and_class()
    {
        Assert.Equal(10, SkillCaps.Cap(SkillCaps.Offense, CombatFormulas.Warrior, 1));
        Assert.Equal(210, SkillCaps.Cap(SkillCaps.Offense, CombatFormulas.Warrior, 50));   // capped
        Assert.Equal(140, SkillCaps.Cap(SkillCaps.Offense, CombatFormulas.Wizard, 40));    // 205 → wizard cap
        Assert.Equal(235, SkillCaps.Cap(SkillCaps.Offense, CombatFormulas.Warrior, 55));   // gap 42 ≤ 50: 210 + 5 per level
        Assert.Equal(140, SkillCaps.Cap(SkillCaps.Offense, CombatFormulas.Wizard, 55));    // wizard: 140 up to 50 and after
        Assert.Equal(252, SkillCaps.Cap(SkillCaps.Offense, CombatFormulas.Warrior, 60));
        Assert.Equal(230, SkillCaps.NpcMelee(CombatFormulas.Monk, 45));                    // level cap 230 < blunt caps 240
    }
}
