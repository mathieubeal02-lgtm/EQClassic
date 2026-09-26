using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Tests.Combat;

namespace EQClassic.Tests.Zone;

/// <summary>Pets (Mob::MakePet, the pet AI and commands).</summary>
public class PetTests
{
    private const int ElementalkinEarth = 58;

    private static (ZoneInstance Zone, ZoneInstance.Entity Mage, ZoneInstance.Entity Rat) Setup()
    {
        var pets = new InMemoryPetSource();
        pets.Pets[74] = new PetType(74, 95, 6, 12, 75, 1, 6, 0);
        var rat = new NpcTemplate(1, "a_rat", 36, 2, 5, 2f) { Combat = new NpcCombatStats(1, 40, 1, 2) };
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(40, 0, 0), 0, 0, [(rat, 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data, seed: 4) { Spells = SpellRulesTests.File(), Pets = pets };
        var magic = new ZoneInstance.PlayerMagic(75, 150, Enumerable.Repeat(200, 74).ToArray(), [ElementalkinEarth], [ElementalkinEarth]);
        var mage = zone.AddPlayer("Qmage", 1, 0, 8, new Vec3(0, 0, 0), fighter: new Combatant(true, 8, CombatFormulas.Magician, 200, 1, 1, 1, 1, 0, 1, 3f),
            progress: new ZoneInstance.PlayerProgress(0, "", default, null, null, magic));
        zone.DrainEvents();
        return (zone, mage, zone.Entities.Single(e => !e.IsPlayer));
    }

    private static List<ZoneInstance.ZoneEvent> Run(ZoneInstance zone, float seconds)
    {
        var events = new List<ZoneInstance.ZoneEvent>();
        for (float t = 0; t < seconds; t += 0.05f)
        {
            zone.Tick(0.05f);
            events.AddRange(zone.DrainEvents());
        }
        return events;
    }

    private static ZoneInstance.Entity Summon(ZoneInstance zone, ZoneInstance.Entity mage)
    {
        for (int i = 0; i < 20 && mage.PetId is null; i++)
        {
            mage.Mana = mage.MaxMana;
            zone.CastSpell(mage.Id, 0);
            Run(zone, 11f);
        }
        return zone.Get(mage.PetId!.Value)!;
    }

    [Theory]
    [InlineData("SumEarthR2", 74)]
    [InlineData("SpiritWolf224", 40)]
    [InlineData("Skeleton217", 27)]
    [InlineData("DruidPet", 11)]
    public void Spell_pet_names_pick_the_legacy_pets_rows(string name, int id) =>
        Assert.Equal(id, PetRules.TypeFor(name));

    [Fact]
    public void A_summoned_pet_follows_its_owner_and_there_is_only_one()
    {
        var (zone, mage, _) = Setup();
        var pet = Summon(zone, mage);
        Assert.Contains(pet.Name, PetRules.Names);
        Assert.Equal((6, 95, mage.Id), (pet.Level, pet.Fighter.MaxHp, pet.OwnerId!.Value));

        mage.Position = new Vec3(0, 100, 0);
        Run(zone, 5f);
        Assert.InRange(MathF.Sqrt(MathF.Pow(pet.Position.X - 0, 2) + MathF.Pow(pet.Position.Y - 100, 2)), 0, ZoneInstance.PetFollowDistance + 1);

        mage.Mana = mage.MaxMana;
        zone.CastSpell(mage.Id, 0);
        var events = Run(zone, 11f);
        if (!events.Contains(new ZoneInstance.Told(mage.Id, ZoneInstance.FizzleMessage)))
            Assert.Contains(new ZoneInstance.Told(mage.Id, ZoneInstance.AlreadyHavePetMessage), events);
    }

    [Fact]
    public void Told_to_attack_the_pet_kills_for_its_owner()
    {
        var (zone, mage, rat) = Setup();
        var pet = Summon(zone, mage);
        zone.SetTarget(mage.Id, rat.Id);
        zone.CommandPet(mage.Id, ZoneInstance.PetOrder.Attack);
        Assert.Equal(rat.Id, pet.TargetId);
        var events = Run(zone, 60f);
        Assert.Contains(events, e => e is ZoneInstance.Slain s && s.VictimId == rat.Id && s.KillerId == pet.Id);
        Assert.True(mage.Exp > 0);
    }

    [Fact]
    public void The_pet_defends_its_owner_and_goes_when_dismissed()
    {
        var (zone, mage, rat) = Setup();
        var pet = Summon(zone, mage);
        rat.TargetId = mage.Id;
        rat.Position = new Vec3(5, 0, 0);
        Run(zone, 5f);
        Assert.Equal(rat.Id, pet.TargetId);

        zone.CommandPet(mage.Id, ZoneInstance.PetOrder.GetLost);
        Assert.Null(mage.PetId);
        Assert.DoesNotContain(zone.Entities, e => e.Id == pet.Id);
    }
}
