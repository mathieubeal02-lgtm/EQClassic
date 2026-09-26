using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using Inventory = EQClassic.Server.Zone.ZoneInstance.PlayerInventory;

namespace EQClassic.Tests.Zone;

/// <summary>Player corpses (Client::Death / MakeCorpse, PlayerCorpse.cpp, player_corpses).</summary>
public class PlayerCorpseTests
{
    private const int Backpack = 17005, Whiskers = 13071, Sword = 5013;

    private static (ZoneInstance Zone, ZoneInstance.Entity Player, InMemoryPlayerCorpseStore Store) Setup(InMemoryPlayerCorpseStore? store = null)
    {
        var items = new InMemoryItemSource();
        items.Items[Backpack] = new ItemStats(Backpack, "Backpack", 0, 0, 0, 0) { IsContainer = true, BagSlots = 8, BagSize = 3, Size = 3 };
        items.Items[Whiskers] = new ItemStats(Whiskers, "Rat Whiskers", 0, 0, 11, 0);
        items.Items[Sword] = new ItemStats(Sword, "Rusty Short Sword", 5, 28, 0, 0, Slots: 1 << 13) { Size = 2 };
        store ??= new InMemoryPlayerCorpseStore();
        var brute = new NpcTemplate(1, "a_brute", 54, 0, 40, 6f) { Combat = new NpcCombatStats(1, 5000, 500, 900) };
        var data = new ZoneData("qeynos2", [new SpawnPoint(1, new Vec3(30, 0, 0), 0, 0, [(brute, 100)], 600, 0)], new Dictionary<int, Grid>());
        var zone = new ZoneInstance(data, seed: 1) { Items = items, PlayerCorpses = store };
        var inventory = new Inventory { Coins = new Coins(2, 3, 4, 5) };
        inventory.Items[13] = Sword;
        inventory.Items[22] = Backpack;
        inventory.BagItems[0] = Whiskers;
        var magic = new ZoneInstance.PlayerMagic(75, 75, new int[74], [200], [200]);
        var player = zone.AddPlayer("Qbot", 1, 0, 10, new Vec3(0, 0, 0),
            progress: new ZoneInstance.PlayerProgress(0, "qeynos2", new Vec3(-100, 0, 0), null, inventory, magic));
        zone.DrainEvents();
        return (zone, player, store);
    }

    private static ZoneInstance.Entity Die(ZoneInstance zone, ZoneInstance.Entity player)
    {
        var brute = zone.Entities.Single(e => e.Name == "a_brute");
        brute.TargetId = player.Id;
        brute.Position = new Vec3(5, 0, 0);
        for (int i = 0; i < 400 && !zone.Entities.Any(e => e.Name == "Qbot's_corpse"); i++)
            zone.Tick(0.05f);
        return zone.Entities.Single(e => e.Name == "Qbot's_corpse");
    }

    [Fact]
    public void Dying_leaves_everything_on_a_corpse_where_the_player_fell()
    {
        var (zone, player, store) = Setup();
        var corpse = Die(zone, player);
        Assert.True(corpse.IsCorpse);
        Assert.Equal(-100, player.Position.X);                          // back at the bind point
        Assert.All(player.Inventory!.Items, i => Assert.Equal(0, i));
        Assert.All(player.Inventory.BagItems, i => Assert.Equal(0, i));
        Assert.True(player.Inventory.Coins.IsZero);
        Assert.Equal(-1, player.Gems[0]);                              // memorised spells forgotten
        var stored = Assert.Single(store.Corpses.Values);
        Assert.Equal(new Coins(2, 3, 4, 5), stored.Coins);
        Assert.Contains((Whiskers, 250, 0), stored.Items);
        Assert.Contains((Sword, 13, 0), stored.Items);
    }

    [Fact]
    public void Only_the_owner_loots_and_items_go_back_where_they_were()
    {
        var (zone, player, store) = Setup();
        var corpse = Die(zone, player);
        var other = zone.AddPlayer("Qthief", 1, 0, 10, corpse.Position);
        zone.OpenLoot(other.Id, corpse.Id);
        Assert.Contains(new ZoneInstance.Told(other.Id, "You may not loot this corpse."), zone.DrainEvents());

        player.Position = corpse.Position;
        zone.OpenLoot(player.Id, corpse.Id);
        Assert.Equal(new Coins(2, 3, 4, 5), player.Inventory!.Coins);
        foreach (var itemId in new[] { Backpack, Sword, Whiskers })
            zone.TakeLoot(player.Id, corpse.Id, zone.CorpseItems(corpse.Id)!.ToList().FindIndex(i => i.ItemId == itemId));
        Assert.Equal(Sword, player.Inventory.Items[13]);
        Assert.Equal(Backpack, player.Inventory.Items[22]);
        Assert.Equal(Whiskers, player.Inventory.BagItems[0]);
        zone.CloseLoot(player.Id, corpse.Id);
        Assert.Empty(store.Corpses);
        Assert.DoesNotContain(zone.Entities, e => e.Id == corpse.Id);
    }

    [Fact]
    public void Stored_corpses_come_back_when_the_zone_starts()
    {
        var store = new InMemoryPlayerCorpseStore();
        store.Create(new StoredCorpse(0, "Qbot", "qeynos2", new Vec3(10, 10, 0), 0, 1, 0, 10, 1, 140, new Coins(1, 0, 0, 0), [(Sword, 13, 0)], 3600));
        var (zone, _, _) = Setup(store);
        zone.Tick(0.05f);
        var corpse = Assert.Single(zone.Entities, e => e.Name == "Qbot's_corpse");
        Assert.Equal(Sword, Assert.Single(zone.CorpseItems(corpse.Id)!).ItemId);
    }

    [Fact]
    public void The_legacy_blob_is_read_and_written()
    {
        // Qbottwo's corpse in everfrost, as the legacy zone stored it.
        var blob = Convert.FromHexString("03000000055A20FF2F0900038CFFFF61000000580000004A00000028000000DB301600010000005D2E1700010000007B2D180001000000");
        var (_, race, _, _, deity, coins, items) = MySqlPlayerCorpseStore.Decode(blob);
        Assert.Equal((9, 140), (race, deity));
        Assert.Equal(new Coins(40, 74, 88, 97), coins);
        Assert.Equal([(12507, 22, 1), (11869, 23, 1), (11643, 24, 1)], items);

        var written = MySqlPlayerCorpseStore.Encode(new StoredCorpse(0, "Qbottwo", "everfrost", default, 0, 9, 0, 1, 3, 140, coins,
            items.Select(i => (i.Item1, i.Item2, i.Item3)).ToList(), 60));
        var again = MySqlPlayerCorpseStore.Decode(written);
        Assert.Equal(coins, again.Coins);
        Assert.Equal(items, again.Items);
    }
}
