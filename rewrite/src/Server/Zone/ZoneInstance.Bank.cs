namespace EQClassic.Server.Zone;

/// <summary>
/// The bank (a banker, class 40): while its window is open, the 8 bank slots and the bags in them
/// are reachable with the usual item moves, and money goes in and out coin by coin. The window
/// closes when the player walks away.
/// </summary>
public sealed partial class ZoneInstance
{
    /// <summary>The bank window of a player opened, changed or closed.</summary>
    public sealed record BankChanged(int PlayerId) : ZoneEvent;

    public void OpenBank(int playerId, int npcId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || !_entities.TryGetValue(npcId, out var npc)
            || npc.Npc is not { } template || template.Combat.Class != BankerClass || npc.IsCorpse)
            return;
        if (Distance2(player.Position, npc.Position) > MerchantReach * MerchantReach)
        {
            _events.Add(new Told(playerId, TooFarMessage));
            return;
        }
        if (npc.TargetId is not null)
        {
            _events.Add(new Told(playerId, $"{DisplayName(npc.Name)} says, 'Can't you see I am busy here?'"));
            return;
        }
        player.BankerId = npcId;
        _events.Add(new BankChanged(playerId));
    }

    public void CloseBank(int playerId)
    {
        if (_entities.TryGetValue(playerId, out var player) && player.BankerId is not null)
        {
            player.BankerId = null;
            _events.Add(new BankChanged(playerId));
        }
    }

    /// <summary>Moves money between the purse and the bank, each coin kind on its own (positive: into the bank).</summary>
    public void BankMoney(int playerId, Coins deposit, Coins withdraw)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.BankerId is null || player.Inventory is not { } inventory)
            return;
        static bool Covers(Coins have, Coins want) => want.Platinum >= 0 && want.Gold >= 0 && want.Silver >= 0 && want.Copper >= 0
            && have.Platinum >= want.Platinum && have.Gold >= want.Gold && have.Silver >= want.Silver && have.Copper >= want.Copper;
        static Coins Minus(Coins a, Coins b) => new(a.Platinum - b.Platinum, a.Gold - b.Gold, a.Silver - b.Silver, a.Copper - b.Copper);
        if (!Covers(inventory.Coins, deposit) || !Covers(inventory.BankCoins, withdraw))
        {
            _events.Add(new Told(playerId, "You do not have that much money."));
            return;
        }
        inventory.Coins = Minus(inventory.Coins, deposit).Add(withdraw);
        inventory.BankCoins = Minus(inventory.BankCoins, withdraw).Add(deposit);
        _events.Add(new InventoryChanged(playerId));
        _events.Add(new BankChanged(playerId));
    }

    /// <summary>The bank window closes when its player walks away from the banker.</summary>
    private void CheckBanks()
    {
        foreach (var p in _entities.Values.Where(e => e.BankerId is not null).ToList())
            if (!_entities.TryGetValue(p.BankerId!.Value, out var banker) || Distance2(p.Position, banker.Position) > MerchantReach * MerchantReach * 4)
                CloseBank(p.Id);
    }
}
