using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

public class LootTests
{
    private static InMemoryLootSource RatLoot()
    {
        var loot = new InMemoryLootSource();
        loot.Tables[137] = new LootTable(137, 10, 10, 0, [(1, 1, 100), (2, 2, 100)]); // always 10 copper; drop 1 once, drop 2 twice
        loot.Drops[1] = [(13071, 1, 1)];                  // rat whiskers
        loot.Drops[2] = [(13073, 0, 3), (13074, 5, 1)];   // mostly bone chips, some fur
        return loot;
    }

    [Fact]
    public void Rolls_follow_the_legacy_tables()
    {
        var (items, coins) = RatLoot().Roll(137, new Random(1));
        Assert.Equal(new Coins(0, 0, 1, 0), coins); // 10 copper: 1 silver
        Assert.Equal(3, items.Count);
        Assert.Equal(13071, items[0].ItemId);
        Assert.All(items.Skip(1), i => Assert.Contains(i.ItemId, new[] { 13073, 13074 }));
        Assert.Equal("3 platinum, 2 gold and 5 copper", new Coins(3, 2, 0, 5).ToString());
    }

    [Fact]
    public void Average_coin_splits_cash_with_the_legacy_quirk()
    {
        var table = new LootTable(1, 5000, 5000, 4, []);
        var (_, coins) = LootRoller.Roll(table, _ => [], new Random(3));
        // copper, silver and gold each 3..4 (avg 4 ± 25 %), the rest in platinum/gold/silver/copper;
        // gold is taken off the cash at ×10 (sic), so the total is not exactly 5000 copper.
        Assert.InRange(coins.Platinum, 4, 5);
        Assert.InRange(coins.Copper, 3, 13);
    }

    private static readonly NpcTemplate Rat = new(1, "a_rat", 36, 2, 1, 2f) { Combat = new NpcCombatStats(1, 16, 1, 4), LoottableId = 137 };

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, int CorpseId) KillRat(Vec3? playerAt = null)
    {
        var zone = new ZoneInstance(new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(5, 0, 0), 0, 0, [(Rat, 100)], 600, 0)], new Dictionary<int, Grid>()), seed: 2)
        {
            Loot = RatLoot(),
        };
        var player = zone.AddPlayer("Qbot", 9, 0, 1, playerAt ?? new Vec3(0, 0, 0));
        var rat = zone.Entities.Single(e => !e.IsPlayer);
        zone.Kill(rat.Id, player.Id);
        zone.DrainEvents();
        return (zone, player, zone.Entities.Single(e => e.IsCorpse).Id);
    }

    [Fact]
    public void The_killer_opens_the_corpse_gets_the_coins_and_takes_items()
    {
        var (zone, player, corpse) = KillRat();
        zone.OpenLoot(player.Id, corpse);
        var events = zone.DrainEvents();
        Assert.Contains(new ZoneInstance.Told(player.Id, "You receive 1 silver from the corpse."), events);
        Assert.Equal(new Coins(0, 0, 1, 0), player.Inventory!.Coins);
        var shown = events.OfType<ZoneInstance.LootShown>().Single();
        Assert.Equal(3, shown.Items.Count);

        zone.TakeLoot(player.Id, corpse, 0);
        Assert.Equal((13071, 1), (player.Inventory.Items[22], player.Inventory.Charges[22]));
        Assert.Contains(new ZoneInstance.Told(player.Id, "You have looted a item #13071."), zone.DrainEvents());
        Assert.Equal(2, zone.CorpseItems(corpse)!.Count);

        zone.TakeLoot(player.Id, corpse, 0);
        zone.TakeLoot(player.Id, corpse, 0);
        zone.CloseLoot(player.Id, corpse);
        Assert.DoesNotContain(zone.Entities, e => e.Id == corpse); // empty: gone
    }

    [Fact]
    public void Others_wait_for_free_for_all_and_must_be_close()
    {
        var (zone, player, corpse) = KillRat();
        var other = zone.AddPlayer("Qother", 9, 0, 1, new Vec3(3, 0, 0));
        var far = zone.AddPlayer("Qfar", 9, 0, 1, new Vec3(200, 0, 0));
        zone.OpenLoot(other.Id, corpse);
        zone.OpenLoot(far.Id, corpse);
        var events = zone.DrainEvents();
        Assert.Contains(new ZoneInstance.Told(other.Id, "You may not loot this corpse at this time."), events);
        Assert.Contains(new ZoneInstance.Told(far.Id, "You are too far away to loot that corpse."), events);

        for (int i = 0; i < 3400; i++)
            zone.Tick(0.05f); // 170 s: free for all
        zone.OpenLoot(other.Id, corpse);
        Assert.Contains(zone.DrainEvents(), e => e is ZoneInstance.LootShown s && s.PlayerId == other.Id);
        zone.OpenLoot(player.Id, corpse);
        Assert.Contains(new ZoneInstance.Told(player.Id, "Someone is already looting this corpse."), zone.DrainEvents());
    }

    [Fact]
    public void Full_inventories_keep_the_item_on_the_corpse_and_corpses_rot()
    {
        var (zone, player, corpse) = KillRat();
        for (int slot = 22; slot < 30; slot++)
            player.Inventory!.Items[slot] = 1001;
        zone.OpenLoot(player.Id, corpse);
        zone.TakeLoot(player.Id, corpse, 0);
        Assert.Contains(new ZoneInstance.Told(player.Id, "There is no room in your inventory for that item."), zone.DrainEvents());
        Assert.Equal(3, zone.CorpseItems(corpse)!.Count);

        for (int i = 0; i < 20 * 481; i++)
            zone.Tick(0.05f); // 8 minutes
        Assert.DoesNotContain(zone.Entities, e => e.Id == corpse);
    }

    [Fact]
    public void Corpses_cannot_be_attacked_or_considered()
    {
        var (zone, player, corpse) = KillRat();
        zone.SetTarget(player.Id, corpse);
        zone.SetAutoAttack(player.Id, true);
        Assert.False(player.AutoAttack);
        zone.Consider(player.Id, corpse);
        Assert.DoesNotContain(zone.DrainEvents(), e => e is ZoneInstance.Considered);
    }
}
