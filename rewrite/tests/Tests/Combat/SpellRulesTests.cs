using EQClassic.Server.Spells;

namespace EQClassic.Tests.Combat;

/// <summary>spdat.eff and the legacy spell formulas (SpellsHandler.cpp, mob.cpp, spells.cpp).</summary>
public class SpellRulesTests
{
    private static IReadOnlyList<Spell>? _file;

    /// <summary>runtime/spdat.eff of the repository (the servers read it from their working directory).</summary>
    internal static IReadOnlyList<Spell> File() =>
        _file ??= Spell.ReadFile(System.IO.File.ReadAllBytes(Path.Combine(Characters.PlayerProfileTests.RepoRoot(), "runtime", "spdat.eff")));

    [Fact]
    public void The_spell_file_holds_3000_records_with_their_messages_and_numbers()
    {
        var spells = File();
        Assert.Equal(3000, spells.Count);

        var heal = spells[200];
        Assert.Equal("Minor Healing", heal.Name);
        Assert.Equal((100f, 1000, 10, SpellTarget.Single, true), (heal.Range, heal.CastTimeMs, heal.Mana, heal.TargetType, heal.Beneficial));
        Assert.Equal("You feel a little better.", heal.CastOnYou);
        Assert.Equal(" feels a little better.", heal.CastOnOther);
        Assert.Equal(1, heal.LevelFor(2)); // cleric
        Assert.Null(heal.LevelFor(1));    // warrior (61 in the file)

        var burst = spells[93];
        Assert.Equal(("Burst of Flame", 7, 2), (burst.Name, burst.Mana, burst.ResistType));
        Assert.False(burst.Beneficial);
        Assert.Equal(1, burst.LevelFor(13)); // magician
        Assert.Null(burst.LevelFor(12));      // not a wizard spell
    }

    [Fact]
    public void Cha_with_base_zero_is_a_spacer_and_reads_as_no_effect()
    {
        var heal = File()[200];
        Assert.Equal(SpellEffect.CurrentHp, heal.Effect[0]);
        Assert.All(heal.Effect.Skip(1), e => Assert.Equal(SpellEffect.Blank, e));
        Assert.True(heal.IsValid);
        Assert.False(heal.IsBuff);
    }

    [Theory]
    [InlineData(93, 1, -3)]  // Burst of Flame: 3 + level/2, at most 5
    [InlineData(93, 4, -5)]
    [InlineData(93, 20, -5)]
    [InlineData(200, 1, 10)] // Minor Healing: flat 10
    [InlineData(17, 9, 28)]  // Light Healing: 24 + level/2, at most 33
    [InlineData(17, 30, 33)]
    public void Spell_values_follow_the_formula_and_stop_at_max(int spellId, int level, int expected) =>
        Assert.Equal(expected, File()[spellId].Value(0, level));

    [Theory]
    [InlineData(12, 10, 75, 100, 220)] // wizard: INT
    [InlineData(2, 10, 100, 75, 220)]  // cleric: WIS
    [InlineData(5, 20, 75, 85, 380)]   // shadow knight: INT
    [InlineData(1, 50, 200, 200, 0)]   // warrior
    public void Max_mana_is_the_casting_stat_over_five_plus_two_per_level(int classId, int level, int wis, int @int, int expected) =>
        Assert.Equal(expected, SpellRules.MaxMana(classId, level, wis, @int));

    [Fact]
    public void Mana_regenerates_faster_sitting_with_meditate()
    {
        Assert.Equal(4, SpellRules.ManaRegen(10, false, 50, 220));
        Assert.Equal(7, SpellRules.ManaRegen(10, true, 50, 220));  // (50/10 + 10 - 2)/4 + 4
        Assert.Equal(4, SpellRules.ManaRegen(10, true, 0, 220));   // no meditate: standing rate
        Assert.Equal(4, SpellRules.ManaRegen(10, true, 50, 30));   // more than a sixth of the pool: standing rate
    }

    [Fact]
    public void Fizzles_depend_on_the_spell_level_against_skill_and_level()
    {
        Assert.Equal(10, SpellRules.FizzleChance(1, 0, 0, 1, 75, 100));
        Assert.Equal(5, SpellRules.FizzleChance(1, 0, 50, 10, 75, 100));
        Assert.Equal(69, SpellRules.FizzleChance(60, 0, 0, 1, 75, 75)); // (235 + 60 − 1 + 2)/5 + 10
    }

    [Fact]
    public void Resists_grow_with_the_victims_save_and_level()
    {
        Assert.Equal(0, SpellRules.ResistChance(0, 200, 60, 1));   // unresistable
        Assert.Equal(20, SpellRules.ResistChance(2, 15, 5, 5));    // save on par
        Assert.Equal(82, SpellRules.ResistChance(2, 21, 7, 1));    // 20 × 1.1^12 = 62, + 20 for being higher
        Assert.Equal(6, SpellRules.ResistChance(1, 0, 1, 10));     // 20 × 0.9^10
        Assert.Equal(3, SpellRules.ResistChance(1, 0, 1, 50));     // at least 3%
    }
}
