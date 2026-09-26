using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Tests.Zone;

/// <summary>Merchants after Client::ProcessOP_ShopRequest / ShopPlayerBuy / ShopPlayerSell.</summary>
public class MerchantTests
{
    private const int Bread = 13003, Sword = 5019, Unknown = 99999;

    private sealed class FixedStanding : IFactionStandings
    {
        public FactionStanding Value = FactionStanding.Indifferent;
        public FactionStanding Standing(ZoneInstance.Entity player, NpcTemplate npc) => Value;
    }

    private static readonly NpcTemplate Merchant = new(1, "Merchant_Bill01", 1, 0, 20, 6f) { Combat = new NpcCombatStats(41, 500, 1, 2), MerchantId = 7 };

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, ZoneInstance.Entity Npc, FixedStanding Standing) Setup(Coins money, float distance = 10)
    {
        var items = new InMemoryItemSource();
        items.Items[Bread] = new ItemStats(Bread, "Bread", 0, 0, 14, 0) { Price = 10 };
        items.Items[Sword] = new ItemStats(Sword, "Rusty Short Sword", 5, 28, 0, 0) { Price = 1003 };
        var merchants = new InMemoryMerchantSource();
        merchants.Lists[7] = [Bread, Unknown, Sword];
        var standing = new FixedStanding();
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(distance, 0, 0), 0, 0, [(Merchant, 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data) { Items = items, Merchants = merchants, Factions = standing };
        var inventory = new ZoneInstance.PlayerInventory { Coins = money };
        var player = zone.AddPlayer("Qbot", 1, 0, 5, new Vec3(0, 0, 0), progress: new ZoneInstance.PlayerProgress(0, "", default, null, inventory));
        zone.DrainEvents();
        return (zone, player, zone.Entities.Single(e => !e.IsPlayer), standing);
    }

    [Theory]
    [InlineData(10, 25, 4)]
    [InlineData(1003, 2508, 401)] // 2507.5 and 401.2
    [InlineData(1, 3, 0)]         // 2.5 and 0.4
    public void Merchants_sell_at_two_and_a_half_times_the_value_and_buy_at_four_tenths(int price, int buy, int sell) =>
        Assert.Equal((buy, sell), (MerchantRules.BuyPrice(price), MerchantRules.SellPrice(price)));

    [Fact]
    public void Money_is_counted_again_in_the_biggest_coins()
    {
        Assert.Equal(new Coins(0, 9, 7, 5), new Coins(1, 0, 0, 0).Take(25));
        Assert.Null(new Coins(0, 0, 2, 4).Take(25));
        Assert.Equal(new Coins(1, 3, 3, 3), new Coins(0, 1, 1, 1).AddCopper(1222));
        Assert.Equal("2p 5s 8c", MerchantRules.Coins(2058));
    }

    [Fact]
    public void Opening_lists_the_goods_that_exist()
    {
        var (zone, player, npc, _) = Setup(new Coins(1, 0, 0, 0));
        zone.OpenMerchant(player.Id, npc.Id);
        Assert.Contains(new ZoneInstance.MerchantShown(player.Id, npc.Id, [Bread, Sword]), zone.DrainEvents(), new ShownComparer());
    }

    private sealed class ShownComparer : IEqualityComparer<ZoneInstance.ZoneEvent>
    {
        public bool Equals(ZoneInstance.ZoneEvent? a, ZoneInstance.ZoneEvent? b) =>
            a is ZoneInstance.MerchantShown x && b is ZoneInstance.MerchantShown y && (x.PlayerId, x.NpcId) == (y.PlayerId, y.NpcId) && x.Goods.SequenceEqual(y.Goods);
        public int GetHashCode(ZoneInstance.ZoneEvent e) => 0;
    }

    [Fact]
    public void Busy_hostile_or_distant_merchants_do_not_trade()
    {
        var (zone, player, npc, standing) = Setup(new Coins(1, 0, 0, 0));
        standing.Value = FactionStanding.Dubious;
        zone.OpenMerchant(player.Id, npc.Id);
        Assert.Contains(new ZoneInstance.Told(player.Id, "Merchant Bill says, 'Get out of here !'"), zone.DrainEvents());

        standing.Value = FactionStanding.Indifferent;
        npc.TargetId = player.Id;
        zone.OpenMerchant(player.Id, npc.Id);
        Assert.Contains(new ZoneInstance.Told(player.Id, "Merchant Bill says, 'Can't you see I am busy here?'"), zone.DrainEvents());

        var (far, farPlayer, farNpc, _) = Setup(new Coins(1, 0, 0, 0), distance: 60);
        far.OpenMerchant(farPlayer.Id, farNpc.Id);
        Assert.Null(farPlayer.MerchantId);
    }

    [Fact]
    public void Buying_takes_the_price_and_puts_the_item_in_a_general_slot()
    {
        var (zone, player, npc, _) = Setup(new Coins(0, 0, 3, 0));
        zone.OpenMerchant(player.Id, npc.Id);
        zone.Buy(player.Id, npc.Id, 0);
        Assert.Equal(Bread, player.Inventory!.Items[ZoneInstance.PlayerInventory.FirstGeneral]);
        Assert.Equal(new Coins(0, 0, 0, 5), player.Inventory.Coins);
        Assert.Contains(new ZoneInstance.Told(player.Id, "You bought a Bread for 2s 5c."), zone.DrainEvents());

        zone.Buy(player.Id, npc.Id, 1);
        Assert.Contains(new ZoneInstance.Told(player.Id, "You cannot afford the Rusty Short Sword."), zone.DrainEvents());
    }

    [Fact]
    public void Selling_pays_four_tenths_and_empties_the_slot()
    {
        var (zone, player, npc, _) = Setup(Coins.None);
        player.Inventory!.Items[25] = Sword;
        player.Inventory.Charges[25] = 1;
        zone.OpenMerchant(player.Id, npc.Id);
        zone.Sell(player.Id, npc.Id, 25);
        Assert.Equal(0, player.Inventory.Items[25]);
        Assert.Equal(new Coins(0, 4, 0, 1), player.Inventory.Coins);
    }
}
