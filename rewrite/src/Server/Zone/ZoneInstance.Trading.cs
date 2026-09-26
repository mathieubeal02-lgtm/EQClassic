namespace EQClassic.Server.Zone;

/// <summary>
/// Trading between players (Client::ProcessOP_TradeRequest, TradeAccepted, CancelTrade and
/// FinishTrade): each side puts up to 8 items (a bag goes with its contents) and money; when both
/// have accepted, the items and money change hands; any change to an offer takes the acceptances
/// back. Walking away, zoning, dying or cancelling ends the trade.
/// </summary>
public sealed partial class ZoneInstance
{
    public const int TradeSlots = 8;
    public const float TradeReach = 20f;

    /// <summary>A player's side of a trade: their partner, the slots they put up, their money, whether they accepted.</summary>
    public sealed class TradeSide
    {
        public int PartnerId { get; internal set; }
        public List<int> Slots { get; } = new();
        public Coins Coins { get; internal set; }
        public bool Accepted { get; internal set; }
    }

    /// <summary>The trade window of a player changed (or closed when they have no trade any more).</summary>
    public sealed record TradeChanged(int PlayerId) : ZoneEvent;

    public void RequestTrade(int playerId, int targetId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || !_entities.TryGetValue(targetId, out var target)
            || !target.IsPlayer || target == player)
            return;
        if (player.Trade is not null || target.Trade is not null)
        {
            _events.Add(new Told(playerId, target.Trade is not null ? $"{target.Name} is busy trading." : "You are already trading."));
            return;
        }
        if (Distance2(player.Position, target.Position) > TradeReach * TradeReach)
        {
            _events.Add(new Told(playerId, TooFarMessage));
            return;
        }
        player.Trade = new TradeSide { PartnerId = targetId };
        target.Trade = new TradeSide { PartnerId = playerId };
        _events.Add(new Told(targetId, $"{player.Name} wants to trade with you."));
        _events.Add(new TradeChanged(playerId));
        _events.Add(new TradeChanged(targetId));
    }

    /// <summary>Puts the item of an inventory slot up for trade, or takes it back.</summary>
    public void OfferItem(int playerId, int slot)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Trade is not { } side || player.Inventory is not { } inventory
            || inventory.ItemAt(slot) == 0 || slot >= PlayerInventory.Slots && !PlayerInventory.IsBagSlot(slot) // not from the bank
            || PlayerInventory.IsBagSlot(slot) && side.Slots.Contains(PlayerInventory.BagOf(slot)))
            return;
        if (!side.Slots.Remove(slot))
        {
            if (side.Slots.Count >= TradeSlots)
            {
                _events.Add(new Told(playerId, "The trade window is full."));
                return;
            }
            if (PlayerInventory.IsGeneral(slot))
                side.Slots.RemoveAll(s => PlayerInventory.IsBagSlot(s) && PlayerInventory.BagOf(s) == slot); // the bag brings them
            side.Slots.Add(slot);
        }
        TradeTouched(player);
    }

    public void OfferCoins(int playerId, Coins coins)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Trade is not { } side || player.Inventory is not { } inventory
            || coins.Platinum < 0 || coins.Gold < 0 || coins.Silver < 0 || coins.Copper < 0 || coins.TotalCopper > inventory.Coins.TotalCopper)
            return;
        side.Coins = coins;
        TradeTouched(player);
    }

    /// <summary>An offer changed: both acceptances go, both windows are updated.</summary>
    private void TradeTouched(Entity player)
    {
        var side = player.Trade!;
        side.Accepted = false;
        if (_entities.TryGetValue(side.PartnerId, out var partner) && partner.Trade is { } other)
            other.Accepted = false;
        _events.Add(new TradeChanged(player.Id));
        _events.Add(new TradeChanged(side.PartnerId));
    }

    public void AcceptTrade(int playerId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Trade is not { } side
            || !_entities.TryGetValue(side.PartnerId, out var partner) || partner.Trade is not { } other)
            return;
        side.Accepted = true;
        _events.Add(new TradeChanged(playerId));
        _events.Add(new TradeChanged(partner.Id));
        if (other.Accepted)
            FinishTrade(player, partner);
    }

    public void CancelTrade(int playerId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Trade is not { } side)
            return;
        player.Trade = null;
        _events.Add(new TradeChanged(playerId));
        _events.Add(new Told(playerId, "The trade has been cancelled."));
        if (_entities.TryGetValue(side.PartnerId, out var partner) && partner.Trade?.PartnerId == playerId)
        {
            partner.Trade = null;
            _events.Add(new TradeChanged(partner.Id));
            _events.Add(new Told(partner.Id, "The trade has been cancelled."));
        }
    }

    /// <summary>Trades end when the partners are too far apart.</summary>
    private void CheckTrades()
    {
        foreach (var p in _entities.Values.Where(e => e.Trade is not null).ToList())
            if (p.Trade is { } side && (!_entities.TryGetValue(side.PartnerId, out var partner)
                || Distance2(p.Position, partner.Position) > TradeReach * TradeReach * 4))
                CancelTrade(p.Id);
    }

    /// <summary>What a side hands over: each offered slot's item and charges, and a bag's contents.</summary>
    private static List<(int Item, int Charges, (int Item, int Charges)[] Contents)> Offered(PlayerInventory inventory, TradeSide side) =>
        side.Slots.Select(slot => (inventory.ItemAt(slot), inventory.ChargesAt(slot),
            PlayerInventory.IsGeneral(slot)
                ? Enumerable.Range(0, PlayerInventory.BagCells).Select(c => (inventory.ItemAt(PlayerInventory.BagSlot(slot, c)), inventory.ChargesAt(PlayerInventory.BagSlot(slot, c)))).ToArray()
                : Array.Empty<(int, int)>()))
            .Where(o => o.Item1 != 0).ToList();

    /// <summary>Client::FinishTrade both ways, when everything fits; otherwise nobody gives anything.</summary>
    private void FinishTrade(Entity a, Entity b)
    {
        var ia = a.Inventory!;
        var ib = b.Inventory!;
        var fromA = Offered(ia, a.Trade!);
        var fromB = Offered(ib, b.Trade!);
        var coinsA = a.Trade!.Coins;
        var coinsB = b.Trade!.Coins;
        var slotsA = a.Trade.Slots.ToList();
        var slotsB = b.Trade.Slots.ToList();
        // Take the offers out first (they free room), then check both sides can hold what they get.
        var backupA = Snapshot(ia);
        var backupB = Snapshot(ib);
        Remove(ia, slotsA);
        Remove(ib, slotsB);
        if (!Receive(ib, fromA) || !Receive(ia, fromB) || ia.Coins.Take((int)coinsA.TotalCopper) is not { } leftA || ib.Coins.Take((int)coinsB.TotalCopper) is not { } leftB)
        {
            Restore(ia, backupA);
            Restore(ib, backupB);
            a.Trade.Accepted = b.Trade.Accepted = false;
            foreach (var p in new[] { a, b })
            {
                _events.Add(new Told(p.Id, "The trade could not be completed: not enough room or money."));
                _events.Add(new TradeChanged(p.Id));
            }
            return;
        }
        ia.Coins = leftA.AddCopper((int)coinsB.TotalCopper);
        ib.Coins = leftB.AddCopper((int)coinsA.TotalCopper);
        foreach (var p in new[] { a, b })
        {
            p.Trade = null;
            _events.Add(new TradeChanged(p.Id));
            _events.Add(new InventoryChanged(p.Id));
            _events.Add(new Told(p.Id, "You have completed the trade."));
            if (slotsA.Concat(slotsB).Any(s => s is >= 0 and < PlayerInventory.FirstGeneral))
                RebuildFighter(p);
        }
    }

    private static void Remove(PlayerInventory inventory, List<int> slots)
    {
        foreach (int slot in slots)
        {
            inventory.Set(slot, 0, 0);
            if (PlayerInventory.IsGeneral(slot))
                for (int c = 0; c < PlayerInventory.BagCells; c++)
                    inventory.Set(PlayerInventory.BagSlot(slot, c), 0, 0);
        }
    }

    /// <summary>AutoPutItemInInventory for each item; a bag takes a general slot and keeps its contents.</summary>
    private bool Receive(PlayerInventory inventory, List<(int Item, int Charges, (int Item, int Charges)[] Contents)> items)
    {
        foreach (var (item, charges, contents) in items)
        {
            bool bagWithItems = contents.Any(c => c.Item != 0);
            int slot = bagWithItems ? inventory.FreeGeneralSlot() : inventory.FreeSlotFor(Items?.Get(item), id => Items?.Get(id));
            if (slot < 0)
                return false;
            inventory.Set(slot, item, charges);
            for (int c = 0; bagWithItems && c < contents.Length; c++)
                inventory.Set(PlayerInventory.BagSlot(slot, c), contents[c].Item, contents[c].Charges);
        }
        return true;
    }

    private static (int[] Items, int[] Charges, int[] BagItems, int[] BagCharges, Coins Coins) Snapshot(PlayerInventory i) =>
        (i.Items.ToArray(), i.Charges.ToArray(), i.BagItems.ToArray(), i.BagCharges.ToArray(), i.Coins);

    private static void Restore(PlayerInventory i, (int[] Items, int[] Charges, int[] BagItems, int[] BagCharges, Coins Coins) s)
    {
        s.Items.CopyTo(i.Items, 0);
        s.Charges.CopyTo(i.Charges, 0);
        s.BagItems.CopyTo(i.BagItems, 0);
        s.BagCharges.CopyTo(i.BagCharges, 0);
        i.Coins = s.Coins;
    }
}
