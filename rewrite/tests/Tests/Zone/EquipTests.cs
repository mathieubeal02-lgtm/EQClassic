using EQClassic.Server.Combat;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;

namespace EQClassic.Tests.Zone;

/// <summary>Moving and equipping items (items_axclassic masks: Cloth Cap, Club, Rusty Two Handed Sword, Small Leather Tunic).</summary>
public class EquipTests
{
    private const int Troll = 9, Shaman = 10, Warrior = 1;
    private static readonly InMemoryItemSource Items = new()
    {
        Items =
        {
            [1001] = new ItemStats(1001, "Cloth Cap", 0, 0, 10, 2, Slots: 4),
            [6001] = new ItemStats(6001, "Club", 4, 27, ItemStats.OneHandBlunt, 0, Slots: 24576, Classes: 17407),
            [5023] = new ItemStats(5023, "Rusty Two Handed Sword", 9, 50, ItemStats.TwoHandSlash, 0, Slots: 8192, Classes: 32797),
            [2016] = new ItemStats(2016, "Small Leather Tunic", 0, 0, 10, 8, Slots: 131072, Classes: 33791, Races: 19640),
        },
    };

    private static (ZoneInstance Zone, ZoneInstance.Entity Player) Zone(int @class, params (int Slot, int Item)[] carried)
    {
        var zone = new ZoneInstance(new ZoneData("qeynos2", [], new Dictionary<int, Grid>())) { Items = Items };
        var inventory = new ZoneInstance.PlayerInventory();
        foreach (var (slot, item) in carried)
            inventory.Items[slot] = item;
        // The fighter as the server builds it: AC of the worn items and the primary weapon, from the inventory as it is.
        Combatant At(int level) => Combatant.ForPlayer(new EQClassic.Server.Characters.PlayerProfile("Qbot", 0, 0, Troll, @class, level, 0, 0, 0, 0, "qeynos2")
        {
            Sta = 100, Agi = 75, Str = 100, Skills = new int[74], Inventory = inventory.Items.ToArray(),
        }, Items);
        var player = zone.AddPlayer("Qbot", Troll, 0, 10, new Vec3(0, 0, 0),
            progress: new ZoneInstance.PlayerProgress(0, "", default, At, inventory));
        zone.DrainEvents();
        return (zone, player);
    }

    [Fact]
    public void A_cap_goes_on_the_head_and_raises_the_armour_class()
    {
        var (zone, player) = Zone(Shaman, (22, 1001));
        int before = player.Fighter.Mitigation;
        zone.MoveItem(player.Id, 22, 2);
        Assert.Equal((1001, 0), (player.Inventory!.Items[2], player.Inventory.Items[22]));
        Assert.True(player.Fighter.Mitigation > before);
        Assert.Contains(new ZoneInstance.InventoryChanged(player.Id), zone.DrainEvents());
    }

    [Fact]
    public void A_club_in_hand_changes_the_weapon_and_swaps_back()
    {
        var (zone, player) = Zone(Shaman, (22, 6001));
        zone.MoveItem(player.Id, 22, 13);
        Assert.Equal((4, 2.7f), (player.Fighter.BaseDamage, player.Fighter.DelaySeconds));
        zone.MoveItem(player.Id, 13, 22);
        Assert.Equal(2, player.Fighter.BaseDamage); // fists again
    }

    [Theory]
    [InlineData(Shaman, 6001, 2, "You cannot equip that there.")]            // a club on the head
    [InlineData(Shaman, 5023, 13, "Your class cannot use that item.")]       // shamans cannot use two-handed swords
    [InlineData(Warrior, 2016, 17, "Your race cannot use that item.")]       // no small tunic for a troll
    public void Refusals_leave_the_item_where_it_was(int @class, int item, int slot, string message)
    {
        var (zone, player) = Zone(@class, (22, item));
        zone.MoveItem(player.Id, 22, slot);
        Assert.Equal(item, player.Inventory!.Items[22]);
        Assert.Contains(new ZoneInstance.Told(player.Id, message), zone.DrainEvents());
    }

    [Fact]
    public void No_two_handed_weapon_with_something_in_the_off_hand()
    {
        var (zone, player) = Zone(Warrior, (22, 5023), (14, 6001));
        zone.MoveItem(player.Id, 22, 13);
        Assert.Equal(5023, player.Inventory!.Items[22]);
        Assert.Contains(new ZoneInstance.Told(player.Id, "You cannot use a two-handed weapon with something in your off hand."), zone.DrainEvents());
        zone.MoveItem(player.Id, 14, 23); // off hand emptied
        zone.MoveItem(player.Id, 22, 13);
        Assert.Equal(5023, player.Inventory.Items[13]);
    }
}
