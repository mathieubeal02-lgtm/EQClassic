using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using Inventory = EQClassic.Server.Zone.ZoneInstance.PlayerInventory;

namespace EQClassic.Tests.Zone;

/// <summary>Trades between players (ProcessOP_TradeRequest, TradeAccepted, CancelTrade, FinishTrade).</summary>
public class TradeTests
{
    private const int Backpack = 17005, Whiskers = 13071, Sword = 5013;

    private static (ZoneInstance Zone, ZoneInstance.Entity Ann, ZoneInstance.Entity Bob) Setup()
    {
        var items = new InMemoryItemSource();
        items.Items[Backpack] = new ItemStats(Backpack, "Backpack", 0, 0, 0, 0) { IsContainer = true, BagSlots = 8, BagSize = 3, Size = 3 };
        items.Items[Whiskers] = new ItemStats(Whiskers, "Rat Whiskers", 0, 0, 11, 0);
        items.Items[Sword] = new ItemStats(Sword, "Rusty Short Sword", 5, 28, 0, 0) { Size = 2 };
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>())) { Items = items };
        ZoneInstance.Entity Add(string name, Vec3 at, Coins coins) =>
            zone.AddPlayer(name, 1, 0, 10, at, progress: new ZoneInstance.PlayerProgress(0, "", default, null, new Inventory { Coins = coins }));
        var ann = Add("Ann", new Vec3(0, 0, 0), new Coins(1, 0, 0, 0));
        var bob = Add("Bob", new Vec3(5, 0, 0), Coins.None);
        ann.Inventory!.Items[22] = Backpack;
        ann.Inventory.BagItems[0] = Whiskers;
        bob.Inventory!.Items[24] = Sword;
        zone.DrainEvents();
        return (zone, ann, bob);
    }

    [Fact]
    public void Both_accept_and_the_items_and_money_change_hands()
    {
        var (zone, ann, bob) = Setup();
        zone.RequestTrade(ann.Id, bob.Id);
        zone.OfferItem(ann.Id, 22);            // the backpack, with its whiskers
        zone.OfferCoins(ann.Id, new Coins(0, 5, 0, 0));
        zone.OfferItem(bob.Id, 24);
        zone.AcceptTrade(ann.Id);
        Assert.NotEqual(Backpack, bob.Inventory!.Items[22]); // one acceptance is not enough
        zone.AcceptTrade(bob.Id);

        Assert.Equal(Backpack, bob.Inventory.Items[22]);
        Assert.Equal(Whiskers, bob.Inventory.BagItems[0]);
        Assert.Equal(Sword, ann.Inventory!.Items[22]);
        Assert.Equal(0, ann.Inventory.BagItems[0]);
        Assert.Equal(500, bob.Inventory.Coins.TotalCopper);
        Assert.Equal(500, ann.Inventory.Coins.TotalCopper);
        Assert.Null(ann.Trade);
        Assert.Contains(new ZoneInstance.Told(bob.Id, "You have completed the trade."), zone.DrainEvents());
    }

    [Fact]
    public void Changing_an_offer_takes_the_acceptances_back()
    {
        var (zone, ann, bob) = Setup();
        zone.RequestTrade(ann.Id, bob.Id);
        zone.OfferItem(bob.Id, 24);
        zone.AcceptTrade(ann.Id);
        zone.OfferCoins(bob.Id, Coins.None);
        Assert.False(ann.Trade!.Accepted);
        zone.AcceptTrade(bob.Id);
        Assert.Equal(Sword, bob.Inventory!.Items[24]); // Ann has not accepted again
    }

    [Fact]
    public void Walking_away_or_offering_money_you_do_not_have_does_not_work()
    {
        var (zone, ann, bob) = Setup();
        zone.RequestTrade(ann.Id, bob.Id);
        zone.OfferCoins(bob.Id, new Coins(1, 0, 0, 0)); // Bob has nothing
        Assert.Equal(0, bob.Trade!.Coins.TotalCopper);
        for (int i = 0; i < 20; i++)
            zone.Tick(0.05f);
        bob.Position = new Vec3(200, 0, 0);
        zone.Tick(0.05f);
        Assert.Null(ann.Trade);
        Assert.Null(bob.Trade);
    }

    [Fact]
    public void Offered_items_cannot_be_moved()
    {
        var (zone, ann, bob) = Setup();
        zone.RequestTrade(ann.Id, bob.Id);
        zone.OfferItem(bob.Id, 24);
        zone.MoveItem(bob.Id, 24, 25);
        Assert.Equal(Sword, bob.Inventory!.Items[24]);
    }
}
