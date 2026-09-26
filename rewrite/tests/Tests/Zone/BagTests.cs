using EQClassic.Server.Characters;
using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using Inventory = EQClassic.Server.Zone.ZoneInstance.PlayerInventory;

namespace EQClassic.Tests.Zone;

/// <summary>Bags in the general slots (containerinv, slots 250 + bag × 10 + cell).</summary>
public class BagTests
{
    private const int Backpack = 17005, SmallBag = 17003, Whiskers = 13071, Sword = 5013, Bread = 13003;

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, Inventory Inventory) Setup()
    {
        var items = new InMemoryItemSource();
        items.Items[Backpack] = new ItemStats(Backpack, "Backpack", 0, 0, 0, 0) { IsContainer = true, BagSlots = 8, BagSize = 3, Size = 3 };
        items.Items[SmallBag] = new ItemStats(SmallBag, "Small Bag", 0, 0, 0, 0) { IsContainer = true, BagSlots = 4, BagSize = 1, Size = 0 };
        items.Items[Whiskers] = new ItemStats(Whiskers, "Rat Whiskers", 0, 0, 11, 0) { Size = 0, Price = 10 };
        items.Items[Sword] = new ItemStats(Sword, "Rusty Short Sword", 5, 28, 0, 0, Slots: 1 << 13) { Size = 2 };
        items.Items[Bread] = new ItemStats(Bread, "Bread", 0, 0, 14, 0) { Size = 1 };
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>())) { Items = items };
        var inventory = new Inventory();
        inventory.Items[22] = Backpack;
        inventory.Items[23] = SmallBag;
        var player = zone.AddPlayer("Qbot", 1, 0, 10, new Vec3(0, 0, 0), progress: new ZoneInstance.PlayerProgress(0, "", default, null, inventory));
        zone.DrainEvents();
        return (zone, player, inventory);
    }

    [Fact]
    public void Items_go_into_bags_when_the_general_slots_are_full()
    {
        var (zone, _, inventory) = Setup();
        for (int g = 24; g < Inventory.Slots; g++)
            inventory.Items[g] = Bread;
        Assert.Equal(250, inventory.FreeSlotFor(zone.Items!.Get(Whiskers), zone.Items.Get));
        Assert.Equal(-1, inventory.FreeSlotFor(zone.Items.Get(Backpack), zone.Items.Get)); // no bag in a bag
    }

    [Fact]
    public void Moving_into_a_bag_checks_the_size_and_refuses_bags()
    {
        var (zone, player, inventory) = Setup();
        inventory.Items[24] = Sword;
        inventory.Items[25] = Whiskers;
        zone.MoveItem(player.Id, 24, 260); // the small bag (slot 23 → cells 260-263) takes size 1: the sword is 2
        Assert.Equal(Sword, inventory.Items[24]);
        Assert.Contains(new ZoneInstance.Told(player.Id, "That item is too large for the bag."), zone.DrainEvents());

        zone.MoveItem(player.Id, 24, 250); // the backpack takes it
        Assert.Equal(Sword, inventory.BagItems[0]);
        zone.MoveItem(player.Id, 25, 261);
        Assert.Equal(Whiskers, inventory.BagItems[11]);
        zone.MoveItem(player.Id, 22, 262); // the backpack into the small bag
        Assert.Equal(Backpack, inventory.Items[22]);
        zone.MoveItem(player.Id, 25, 268); // cell 8 of the small bag: it has 4
        Assert.Equal(0, inventory.BagItems[18]);
    }

    [Fact]
    public void A_bag_carries_its_contents_between_general_slots_but_not_elsewhere()
    {
        var (zone, player, inventory) = Setup();
        inventory.BagItems[0] = Whiskers;
        zone.MoveItem(player.Id, 22, 27);
        Assert.Equal(Backpack, inventory.Items[27]);
        Assert.Equal(Whiskers, inventory.BagItems[50]); // bag 5 (slot 27), cell 0
        Assert.Equal(0, inventory.BagItems[0]);

        zone.MoveItem(player.Id, 27, 13); // a full bag in the main hand
        Assert.Equal(Backpack, inventory.Items[27]);
    }

    [Fact]
    public void Things_in_bags_can_be_sold_and_are_kept_in_the_profile()
    {
        var (zone, player, inventory) = Setup();
        inventory.BagItems[3] = Whiskers;
        inventory.BagCharges[3] = 1;
        var profile = EQClassic.Tests.Characters.ProfileBuilder.Build("Qbot", 1, 1, 10, "qeynos2");
        InMemoryCharacterStore.WriteInventory(profile, inventory.Items, inventory.Charges, inventory.Coins, inventory.BagItems, inventory.BagCharges);
        var read = PlayerProfile.Read(profile)!;
        Assert.Equal(Whiskers, read.BagItems[3]);
        Assert.Equal(0, read.BagItems[4]);
        Assert.Equal(Backpack, read.Inventory[22]);

        player.MerchantId = 99; // as if the merchant window were open
        zone.Sell(player.Id, 99, 253);
        Assert.Equal(0, inventory.BagItems[3]);
        Assert.Equal(4, inventory.Coins.TotalCopper);
    }
}
