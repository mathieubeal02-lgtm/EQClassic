using EQClassic.Server.Characters;
using EQClassic.Shared.Characters;

namespace EQClassic.Tests.Characters;

public class CharacterCreationTests
{
    private static readonly CharacterStats TrollShamanStats = new(108, 119, 45, 75, 52, 83, 95);

    private static CreateCharacterRequest Request(string name = "Qrewrite", int race = 9, int @class = 10, string zone = "grobb") =>
        new(name, race, @class, Gender: 1, Deity: 203, Face: 3, zone, TrollShamanStats);

    private static (CharacterCreation Creation, InMemoryCharacterStore Store, InMemoryCreationData Data) Setup()
    {
        var store = new InMemoryCharacterStore();
        var data = new InMemoryCreationData();
        data.FilteredPatterns.Add("drizzt");
        data.FilteredPatterns.Add("bad%");
        data.StartPositions[("grobb", 9, 10)] = (0, -100, 4);
        data.Items.AddRange([(0, 0, 9990), (0, 0, 9991), (0, 10, 9999), (9, 10, 18791), (1, 1, 12345)]);
        return (new CharacterCreation(store, data), store, data);
    }

    [Fact]
    public void Creates_a_profile_the_legacy_servers_can_read()
    {
        var (creation, store, _) = Setup();

        Assert.Null(creation.Create(24, Request()));

        var c = Assert.Single(store.ListForAccount(24));
        var p = c.Profile;
        Assert.Equal(("Qrewrite", 9, 10, 1, 1, "grobb"), (p.Name, p.Race, p.Class, p.Level, p.Gender, p.Zone));
        Assert.Equal((0f, -100f, 4f), (p.X, p.Y, p.Z));
        var raw = store.Profiles["Qrewrite"];
        Assert.Equal(ProfileTemplate.ProfileLength, raw.Length);
        Assert.Equal(203 & 0xFF, raw[55]);
        Assert.Equal(3, raw[72]);
        Assert.Equal(new byte[] { 108, 119, 45, 75, 52, 83, 95 }, raw[123..130]);
    }

    [Fact]
    public void Starting_items_go_to_the_general_slots_with_food_and_drink_charges()
    {
        var (creation, store, _) = Setup();
        creation.Create(24, Request());
        var raw = store.Profiles["Qrewrite"];

        Assert.Equal([9990, 9991, 9999, 18791], Enumerable.Range(22, 4).Select(s => (int)ProfileTemplate.GetItem(raw, s)));
        Assert.Equal(ProfileTemplate.NoItem, ProfileTemplate.GetItem(raw, 26));
        Assert.Equal(ProfileTemplate.NoItem, ProfileTemplate.GetItem(raw, 0)); // nothing on the cursor
        Assert.Equal(5, ProfileTemplate.GetCharges(raw, 22));
        Assert.Equal(5, ProfileTemplate.GetCharges(raw, 23));
    }

    [Fact]
    public void Without_a_start_position_the_template_position_is_kept()
    {
        var (creation, store, _) = Setup();
        creation.Create(24, Request(zone: "innothule"));
        var p = store.ListForAccount(24)[0].Profile;
        Assert.Equal("innothule", p.Zone);
        Assert.Equal((-46f, 126f, 4f), (p.X, p.Y, p.Z)); // what the captured client packet carries
    }

    [Theory]
    [InlineData("")]
    [InlineData("Averyveryverylong")] // 17 > 15
    [InlineData("Qu'rt")]
    [InlineData("Drizzt")]            // name_filter, case-insensitive like MySQL LIKE
    [InlineData("Badboy")]            // name_filter pattern bad%
    public void Refused_names(string name)
    {
        var (creation, store, _) = Setup();
        Assert.Equal(CharacterCreation.NameRefused, creation.Create(24, Request(name)));
        Assert.Empty(store.ListForAccount(24));
    }

    [Fact]
    public void Name_taken_by_any_account_is_refused()
    {
        var (creation, _, _) = Setup();
        Assert.Null(creation.Create(1, Request("Qrewrite")));
        Assert.Equal(CharacterCreation.NameTaken, creation.Create(24, Request("QREWRITE")));
    }

    [Theory]
    [InlineData(13, 1)]  // not a Trilogy race
    [InlineData(130, 1)] // Vah Shir: post-Trilogy, crashes the client (patch 006)
    [InlineData(1, 15)]  // no class 15
    [InlineData(1, 0)]
    public void Invalid_race_or_class_is_refused(int race, int @class)
    {
        var (creation, _, _) = Setup();
        Assert.Equal(CharacterCreation.InvalidChoice, creation.Create(24, Request(race: race, @class: @class)));
    }

    [Fact]
    public void Ten_characters_per_account()
    {
        var (creation, _, _) = Setup();
        for (int i = 0; i < 10; i++)
            Assert.Null(creation.Create(24, Request("Qalt" + (char)('a' + i))));
        Assert.Equal(CharacterCreation.TooManyCharacters, creation.Create(24, Request("Qeleventh")));
    }
}
