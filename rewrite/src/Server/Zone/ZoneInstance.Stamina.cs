using EQClassic.Server.Combat;

namespace EQClassic.Server.Zone;

/// <summary>
/// Food, drink and fatigue (Mob::DoEnduRegen, Client::Process_ConsumeFoodDrink): every 5.76 s a
/// player gets 2 points hungrier and thirstier; starving or parched, they tire (fatigue up to 100)
/// and regain no mana; fed, fatigue falls by 10, except in water where swimming tires. Eating adds
/// 50 points per point of the item's casttime. The Trilogy client eats and drinks on its own when
/// hungry; here the server does it, from the general slots then the bags, when a level falls below
/// <see cref="AutoConsumeBelow"/> (the client's threshold is not known).
/// </summary>
public sealed partial class ZoneInstance
{
    public const double StaminaSeconds = 5.76;
    public const int FullStamina = 6000, MaxStamina = 32000, AutoConsumeBelow = 3000;

    /// <summary>The player's food, drink or fatigue changed.</summary>
    public sealed record StaminaChanged(int PlayerId) : ZoneEvent;

    private double _nextStamina = StaminaSeconds;

    private void TickStamina()
    {
        if (_time < _nextStamina)
            return;
        _nextStamina = _time + StaminaSeconds;
        foreach (var player in _entities.Values.Where(e => e.IsPlayer && !e.IsCorpse).ToList())
        {
            bool inWater = Regions?.InWater(player.Position) == true;
            if (player.Hunger == 0 || player.Thirst == 0)
                player.Fatigue = Math.Min(100, player.Fatigue + 1);
            else if (inWater)
                player.Fatigue = player.Fatigue >= 100 ? 90 : player.Fatigue + 1;
            else
                player.Fatigue = Math.Max(0, player.Fatigue - 10);
            bool wasFed = player.Hunger > 0, wasWatered = player.Thirst > 0;
            player.Hunger = Math.Max(0, player.Hunger - 2);
            player.Thirst = Math.Max(0, player.Thirst - 2);
            if (player.Hunger < AutoConsumeBelow)
                AutoConsume(player, ItemStats.Food);
            if (player.Thirst < AutoConsumeBelow)
                AutoConsume(player, ItemStats.Drink);
            if (wasFed && player.Hunger == 0)
                _events.Add(new Told(player.Id, "You are hungry."));
            if (wasWatered && player.Thirst == 0)
                _events.Add(new Told(player.Id, "You are thirsty."));
            _events.Add(new StaminaChanged(player.Id));
        }
    }

    /// <summary>The first food (or drink) the player carries, general slots first, then the bags.</summary>
    private void AutoConsume(Entity player, int itemType)
    {
        if (player.Inventory is not { } inventory)
            return;
        var slots = Enumerable.Range(PlayerInventory.FirstGeneral, PlayerInventory.Slots - PlayerInventory.FirstGeneral)
            .Concat(Enumerable.Range(PlayerInventory.BagSlotBase, PlayerInventory.BagSlotsTotal));
        foreach (int slot in slots)
            if (inventory.ItemAt(slot) is int id and not 0 && Items?.Get(id) is { } item && item.ItemType == itemType)
            {
                Consume(player, slot, item, silent: true);
                return;
            }
    }

    /// <summary>Eating or drinking an inventory item (right click in the Trilogy client).</summary>
    public void ConsumeItem(int playerId, int slot)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Inventory is not { } inventory
            || slot >= PlayerInventory.Slots && !PlayerInventory.IsBagSlot(slot) || Items?.Get(inventory.ItemAt(slot)) is not { } item)
            return;
        if (item.ItemType is not (ItemStats.Food or ItemStats.Drink))
        {
            _events.Add(new Told(playerId, "You cannot eat or drink that."));
            return;
        }
        int level = item.ItemType == ItemStats.Food ? player.Hunger : player.Thirst;
        if (level >= FullStamina)
        {
            _events.Add(new Told(playerId, item.ItemType == ItemStats.Food
                ? "You could not possibly eat any more, you would explode!" : "You could not possibly drink any more, you would explode!"));
            return;
        }
        Consume(player, slot, item, silent: false);
        _events.Add(new StaminaChanged(playerId));
    }

    private void Consume(Entity player, int slot, ItemStats item, bool silent)
    {
        var inventory = player.Inventory!;
        int gain = 50 * Math.Max(1, item.FoodDuration);
        if (item.ItemType == ItemStats.Food)
            player.Hunger = Math.Min(MaxStamina, player.Hunger + gain);
        else
            player.Thirst = Math.Min(MaxStamina, player.Thirst + gain);
        int charges = inventory.ChargesAt(slot);
        if (charges > 1)
            inventory.Set(slot, item.Id, charges - 1);
        else
            inventory.Set(slot, 0, 0);
        _events.Add(new InventoryChanged(player.Id));
        if (!silent)
            _events.Add(new Told(player.Id, item.ItemType == ItemStats.Food
                ? $"Chomp, chomp, chomp...  You eat the {item.Name}." : $"Glug, glug, glug...  You drink the {item.Name}."));
    }
}
