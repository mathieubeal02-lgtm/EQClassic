using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.Protocol;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;
using Inventory = EQClassic.Server.Zone.ZoneInstance.PlayerInventory;

namespace EQClassic.Tests.Zone;

/// <summary>Food, drink and fatigue (Mob::DoEnduRegen, Client::Process_ConsumeFoodDrink).</summary>
public class StaminaTests
{
    private const int Muffin = 13014, Flask = 13006, Sword = 5013;

    private static (ZoneInstance Zone, ZoneInstance.Entity Player) Setup(int hunger, int thirst)
    {
        var items = new InMemoryItemSource();
        items.Items[Muffin] = new ItemStats(Muffin, "Muffin", 0, 0, ItemStats.Food, 0) { FoodDuration = 4 };
        items.Items[Flask] = new ItemStats(Flask, "Water Flask", 0, 0, ItemStats.Drink, 0) { FoodDuration = 15 };
        items.Items[Sword] = new ItemStats(Sword, "Rusty Short Sword", 5, 28, 0, 0);
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>())) { Items = items };
        var player = zone.AddPlayer("Ann", 1, 0, 10, new Vec3(0, 0, 0),
            progress: new ZoneInstance.PlayerProgress(0, "", default, null, new Inventory()) { Hunger = hunger, Thirst = thirst });
        zone.DrainEvents();
        return (zone, player);
    }

    [Fact]
    public void Players_grow_hungry_and_thirsty_by_two_every_5_76_seconds()
    {
        var (zone, ann) = Setup(5000, 4000);
        zone.Tick(5);
        Assert.Equal((5000, 4000), (ann.Hunger, ann.Thirst));
        zone.Tick(1);
        Assert.Equal((4998, 3998), (ann.Hunger, ann.Thirst));
        Assert.Contains(new ZoneInstance.StaminaChanged(ann.Id), zone.DrainEvents());
    }

    [Fact]
    public void Starving_players_tire_and_the_server_feeds_them_from_their_packs()
    {
        var (zone, ann) = Setup(2, 6000);
        zone.Tick(6);
        Assert.Equal(0, ann.Hunger);
        Assert.Contains(new ZoneInstance.Told(ann.Id, "You are hungry."), zone.DrainEvents());
        zone.Tick(6);
        Assert.Equal(1, ann.Fatigue);

        ann.Inventory!.Set(23, Muffin, 2);
        zone.Tick(6);
        Assert.Equal(200, ann.Hunger);               // 50 × casttime 4, eaten below 3000
        Assert.Equal(1, ann.Inventory.ChargesAt(23)); // one of the stack
        zone.Tick(6);
        Assert.Equal(0, ann.Fatigue);                // fed again: fatigue falls by 10
    }

    [Fact]
    public void Eating_by_hand_until_full()
    {
        var (zone, ann) = Setup(5900, 5000);
        ann.Inventory!.Set(23, Muffin, 1);
        ann.Inventory.Set(24, Flask, 1);
        ann.Inventory.Set(25, Sword, 0);
        zone.ConsumeItem(ann.Id, 23);
        Assert.Equal(6100, ann.Hunger);
        Assert.Equal(0, ann.Inventory.ItemAt(23));
        zone.ConsumeItem(ann.Id, 24);
        Assert.Equal(5750, ann.Thirst);
        var events = zone.DrainEvents();
        Assert.Contains(new ZoneInstance.Told(ann.Id, "Chomp, chomp, chomp...  You eat the Muffin."), events);
        Assert.Contains(new ZoneInstance.Told(ann.Id, "Glug, glug, glug...  You drink the Water Flask."), events);

        ann.Inventory.Set(23, Muffin, 1);
        zone.ConsumeItem(ann.Id, 23);                // 6100: full
        zone.ConsumeItem(ann.Id, 25);
        Assert.Equal(Muffin, ann.Inventory.ItemAt(23));
        events = zone.DrainEvents();
        Assert.Contains(new ZoneInstance.Told(ann.Id, "You could not possibly eat any more, you would explode!"), events);
        Assert.Contains(new ZoneInstance.Told(ann.Id, "You cannot eat or drink that."), events);
    }

    [Fact]
    public void No_mana_comes_back_while_parched()
    {
        var (zone, ann) = Setup(6000, 0);
        typeof(ZoneInstance.Entity).GetProperty("MaxMana")!.SetValue(ann, 100);
        typeof(ZoneInstance.Entity).GetProperty("Mana")!.SetValue(ann, 10);
        zone.Tick(7);
        Assert.Equal(10, ann.Mana);
    }

    [Fact]
    public void Stamina_messages_round_trip()
    {
        Assert.Equal(new PlayerStamina(32000, 0, 100), MessageCodec.Decode(MessageCodec.Encode(new PlayerStamina(32000, 0, 100))));
        Assert.Equal(new ConsumeItem(251), MessageCodec.Decode(MessageCodec.Encode(new ConsumeItem(251))));
    }
}
