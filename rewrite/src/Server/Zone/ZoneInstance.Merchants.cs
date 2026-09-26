using EQClassic.Shared.Zone;
using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>What merchants sell (merchantlist: merchantid, slot, item, stack).</summary>
public interface IMerchantSource
{
    /// <summary>The items of a merchant list by slot; unknown lists are empty.</summary>
    IReadOnlyList<int> Goods(int merchantId);
}

public sealed class InMemoryMerchantSource : IMerchantSource
{
    public Dictionary<int, List<int>> Lists { get; } = new();
    public IReadOnlyList<int> Goods(int merchantId) => Lists.TryGetValue(merchantId, out var l) ? l : Array.Empty<int>();
}

/// <summary>
/// merchantlist, cached per list. Every stack of this database is 0 (unlimited), so goods never
/// run out and what players sell is not put on sale again, as in the legacy zone.
/// </summary>
public sealed class MySqlMerchantSource : IMerchantSource
{
    private readonly string _connectionString;
    private readonly Dictionary<int, IReadOnlyList<int>> _cache = new();
    public MySqlMerchantSource(string connectionString) => _connectionString = connectionString;

    public IReadOnlyList<int> Goods(int merchantId)
    {
        if (_cache.TryGetValue(merchantId, out var goods))
            return goods;
        var items = new List<int>();
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT item FROM merchantlist WHERE merchantid = @m ORDER BY slot";
        cmd.Parameters.AddWithValue("@m", merchantId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(Convert.ToInt32(r.GetValue(0)));
        return _cache[merchantId] = items;
    }
}

/// <summary>
/// Merchants after Client::ProcessOP_ShopRequest, ShopPlayerBuy and ShopPlayerSell
/// (Zone/Source/client_process.cpp) and Client::UpdateGoods: at most 30 goods, the items that
/// exist; one at a time; prices from <see cref="MerchantRules"/>.
/// </summary>
public sealed partial class ZoneInstance
{
    public const int MaxGoods = 30;
    public const float MerchantReach = 30f;

    public IMerchantSource? Merchants { get; init; }

    /// <summary>The merchant window opened: the goods (item ids) the player may buy.</summary>
    public sealed record MerchantShown(int PlayerId, int NpcId, IReadOnlyList<int> Goods) : ZoneEvent;
    public sealed record MerchantClosed(int PlayerId) : ZoneEvent;

    public void OpenMerchant(int playerId, int npcId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || !_entities.TryGetValue(npcId, out var npc)
            || npc.Npc is not { MerchantId: > 0 } template || npc.IsCorpse)
            return;
        string name = DisplayName(npc.Name);
        if (Distance2(player.Position, npc.Position) > MerchantReach * MerchantReach)
        {
            _events.Add(new Told(playerId, TooFarMessage));
            return;
        }
        if (npc.TargetId is not null)
        {
            _events.Add(new Told(playerId, $"{name} says, 'Can't you see I am busy here?'"));
            return;
        }
        if (player.Bonuses.Invisible)
            return; // the merchant does not see you
        var standing = Factions is DatabaseFactions db ? db.StandingFor(player, template, player.Deity) : Factions.Standing(player, template);
        if (standing is FactionStanding.Dubious or FactionStanding.Threatenly or FactionStanding.Scowls)
        {
            _events.Add(new Told(playerId, $"{name} says, 'Get out of here !'"));
            return;
        }
        var goods = (Merchants?.Goods(template.MerchantId) ?? Array.Empty<int>())
            .Where(id => Items?.Get(id) is not null).Take(MaxGoods).ToList();
        player.MerchantId = npcId;
        player.MerchantGoods = goods;
        _events.Add(new MerchantShown(playerId, npcId, goods));
    }

    public void CloseMerchant(int playerId)
    {
        if (_entities.TryGetValue(playerId, out var player) && player.MerchantId is not null)
        {
            player.MerchantId = null;
            player.MerchantGoods = Array.Empty<int>();
            _events.Add(new MerchantClosed(playerId));
        }
    }

    /// <summary>Buys one of the goods into the first free general slot; the money is taken in any coins (TakeMoneyFromPP).</summary>
    public void Buy(int playerId, int npcId, int index)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.MerchantId != npcId || player.Inventory is not { } inventory
            || index < 0 || index >= player.MerchantGoods.Count || Items?.Get(player.MerchantGoods[index]) is not { } item)
            return;
        if (!_entities.TryGetValue(npcId, out var npc) || Distance2(player.Position, npc.Position) > MerchantReach * MerchantReach)
        {
            CloseMerchant(playerId);
            return;
        }
        int slot = inventory.FreeGeneralSlot();
        if (slot < 0)
        {
            _events.Add(new Told(playerId, "Your inventory appears full now!"));
            return;
        }
        int price = MerchantRules.BuyPrice(item.Price);
        if (inventory.Coins.Take(price) is not { } left)
        {
            _events.Add(new Told(playerId, $"You cannot afford the {item.Name}."));
            return;
        }
        inventory.Coins = left;
        inventory.Items[slot] = item.Id;
        inventory.Charges[slot] = 1;
        _events.Add(new Told(playerId, $"You bought a {item.Name} for {MerchantRules.Coins(price)}."));
        _events.Add(new InventoryChanged(playerId));
    }

    /// <summary>Sells the item of an inventory slot for its sell price (AddMoneyToPP); a worn item changes the fighter.</summary>
    public void Sell(int playerId, int npcId, int slot)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.MerchantId != npcId || player.Inventory is not { } inventory
            || slot < 0 || slot >= PlayerInventory.Slots || inventory.Items[slot] == 0)
            return;
        var item = Items?.Get(inventory.Items[slot]);
        int price = MerchantRules.SellPrice(item?.Price ?? 0) * Math.Max(1, inventory.Charges[slot] > 1 ? inventory.Charges[slot] : 1);
        inventory.Coins = inventory.Coins.AddCopper(price);
        inventory.Items[slot] = 0;
        inventory.Charges[slot] = 0;
        _events.Add(new Told(playerId, $"You sold {item?.Name ?? "an item"} for {MerchantRules.Coins(price)}."));
        _events.Add(new InventoryChanged(playerId));
        if (slot < PlayerInventory.FirstGeneral)
            RebuildFighter(player);
    }

    /// <summary>NPC names as players read them: "Merchant_Bill01" → "Merchant Bill".</summary>
    private static string DisplayName(string name) => new string(name.Where(c => !char.IsAsciiDigit(c)).ToArray()).Replace('_', ' ').Trim();
}
