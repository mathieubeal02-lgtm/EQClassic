using EQClassic.Server.Characters;
using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using Inventory = EQClassic.Server.Zone.ZoneInstance.PlayerInventory;

namespace EQClassic.Tests.Zone;

/// <summary>The bank: bankers, bank slots (2000+) and bank bags (2030+), money, the profile.</summary>
public class BankTests
{
    private const int Backpack = 17005, Whiskers = 13071;

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, ZoneInstance.Entity Banker, Inventory Inventory) Setup(float bankerAt = 10)
    {
        var items = new InMemoryItemSource();
        items.Items[Backpack] = new ItemStats(Backpack, "Backpack", 0, 0, 0, 0) { IsContainer = true, BagSlots = 8, BagSize = 3, Size = 3 };
        items.Items[Whiskers] = new ItemStats(Whiskers, "Rat Whiskers", 0, 0, 11, 0);
        var banker = new NpcTemplate(1, "Banker_Jon", 1, 0, 30, 6f) { Combat = new NpcCombatStats(40, 500, 1, 2) };
        var zone = new ZoneInstance(new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(bankerAt, 0, 0), 0, 0, [(banker, 100)], 600, 0)],
            new Dictionary<int, Grid>())) { Items = items };
        var inventory = new Inventory { Coins = new Coins(3, 2, 1, 0) };
        inventory.Items[22] = Backpack;
        inventory.BagItems[0] = Whiskers;
        inventory.Items[23] = Whiskers;
        var player = zone.AddPlayer("Qbot", 1, 0, 10, new Vec3(0, 0, 0), progress: new ZoneInstance.PlayerProgress(0, "", default, null, inventory));
        zone.DrainEvents();
        return (zone, player, zone.Entities.Single(e => !e.IsPlayer), inventory);
    }

    [Fact]
    public void The_bank_slots_are_reachable_only_at_a_banker()
    {
        var (zone, player, banker, inventory) = Setup();
        zone.MoveItem(player.Id, 23, 2000);
        Assert.Equal(Whiskers, inventory.Items[23]); // not open

        zone.OpenMerchant(player.Id, banker.Id);        // U on a banker opens the bank
        Assert.Equal(banker.Id, player.BankerId);
        zone.MoveItem(player.Id, 23, 2000);
        Assert.Equal(Whiskers, inventory.BankItems[0]);
        zone.MoveItem(player.Id, 22, 2003);             // the backpack and its contents
        Assert.Equal(Backpack, inventory.BankItems[3]);
        Assert.Equal(Whiskers, inventory.BankBagItems[30]);
        Assert.Equal(0, inventory.BagItems[0]);

        player.Position = new Vec3(200, 0, 0);
        zone.Tick(0.05f);
        Assert.Null(player.BankerId);                   // walked away
    }

    [Fact]
    public void Money_goes_in_and_out_coin_by_coin()
    {
        var (zone, player, banker, inventory) = Setup();
        zone.OpenBank(player.Id, banker.Id);
        zone.BankMoney(player.Id, new Coins(2, 0, 1, 0), Coins.None);
        Assert.Equal(new Coins(1, 2, 0, 0), inventory.Coins);
        Assert.Equal(new Coins(2, 0, 1, 0), inventory.BankCoins);
        zone.BankMoney(player.Id, Coins.None, new Coins(0, 1, 0, 0)); // no gold in the bank
        Assert.Contains(new ZoneInstance.Told(player.Id, "You do not have that much money."), zone.DrainEvents());
        zone.BankMoney(player.Id, Coins.None, new Coins(1, 0, 0, 0));
        Assert.Equal(new Coins(2, 2, 0, 0), inventory.Coins);
    }

    [Fact]
    public void The_bank_is_kept_in_the_profile()
    {
        var raw = EQClassic.Tests.Characters.ProfileBuilder.Build("Qbot", 1, 1, 10, "qeynos2");
        var bagItems = new int[80];
        bagItems[5] = Whiskers;
        ProfileTemplate.SetBank(raw, [Backpack, 0, 0, 0, 0, 0, 0, 0], new int[8], bagItems, new int[80], new Coins(7, 6, 5, 4));
        var profile = PlayerProfile.Read(raw)!;
        Assert.Equal(Backpack, profile.BankItems[0]);
        Assert.Equal(0, profile.BankItems[1]);
        Assert.Equal(Whiskers, profile.BankBagItems[5]);
        Assert.Equal(new Coins(7, 6, 5, 4), profile.BankCoins);
    }
}
