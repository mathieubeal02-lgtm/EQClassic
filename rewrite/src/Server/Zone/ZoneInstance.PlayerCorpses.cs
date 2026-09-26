namespace EQClassic.Server.Zone;

/// <summary>
/// Player corpses after Client::Death / MakeCorpse and Corpse (Zone/Source/attack.cpp, PlayerCorpse.cpp):
/// a dying player leaves everything they carry — worn items, the general slots, the bags' contents
/// and their money — on a corpse where they fell, and forget their memorised spells. Only they may
/// loot it; each item goes back to the slot it came from when that slot is free. Corpses are kept in
/// player_corpses and rot after a day (the legacy PC_CORPSE_* timers are not in the tree; the stored
/// corpses had about a day left).
/// </summary>
public sealed partial class ZoneInstance
{
    public const double PlayerCorpseRotSeconds = 24 * 3600;

    public IPlayerCorpseStore? PlayerCorpses { get; init; }
    private bool _playerCorpsesLoaded;

    /// <summary>The zone's stored player corpses, on its first tick (the store is set after construction).</summary>
    private void LoadPlayerCorpses()
    {
        if (_playerCorpsesLoaded)
            return;
        _playerCorpsesLoaded = true;
        foreach (var stored in PlayerCorpses?.InZone(ShortName) ?? Array.Empty<StoredCorpse>())
            AddPlayerCorpse(stored, announce: false);
    }

    private Entity AddPlayerCorpse(StoredCorpse stored, bool announce)
    {
        var body = Add(stored.Owner + "'s_corpse", false, stored.Race, stored.Gender, stored.Level, RaceSizes.Default(stored.Race), stored.Position, stored.Heading);
        body.Corpse = new CorpseData
        {
            Items = stored.Items.Select(i => new LootDrop(i.ItemId, i.Charges, i.Slot)).ToList(),
            Coins = stored.Coins,
            DecayAt = _time + stored.SecondsLeft,
            FreeForAllAt = double.PositiveInfinity,
            Owner = stored.Owner,
            DbId = stored.Id,
            Class = stored.Class,
            Deity = stored.Deity,
        };
        body.Fighter = new Combat.Combatant(false, stored.Level, stored.Class, 1, 0, 0, 0, 0, 0, 0, 1f);
        body.Hp = 0;
        if (announce)
            _events.Add(new Spawned(body));
        return body;
    }

    /// <summary>Client::MakeCorpse: everything the player carries goes onto a corpse where they fell.</summary>
    private void MakePlayerCorpse(Entity player)
    {
        var inventory = player.Inventory;
        var items = new List<(int ItemId, int Slot, int Charges)>();
        if (inventory is not null)
        {
            for (int s = 0; s < PlayerInventory.Slots; s++)
                if (inventory.Items[s] != 0)
                    items.Add((inventory.Items[s], s, inventory.Charges[s]));
            for (int c = 0; c < PlayerInventory.BagSlotsTotal; c++)
                if (inventory.BagItems[c] != 0)
                    items.Add((inventory.BagItems[c], PlayerInventory.BagSlotBase + c, inventory.BagCharges[c]));
        }
        var coins = inventory?.Coins ?? Coins.None;
        for (int g = 0; g < player.Gems.Length; g++)
            player.Gems[g] = -1; // Client::Death: spell_memory cleared
        _events.Add(new GemsChanged(player.Id));
        if (items.Count == 0 && coins.IsZero)
            return;
        Array.Clear(inventory!.Items);
        Array.Clear(inventory.Charges);
        Array.Clear(inventory.BagItems);
        Array.Clear(inventory.BagCharges);
        inventory.Coins = Coins.None;
        _events.Add(new InventoryChanged(player.Id));
        var stored = new StoredCorpse(0, player.Name, ShortName, player.Position, player.Heading, player.Race, player.Gender, player.Level,
            player.Fighter.Class, player.Deity, coins, items, PlayerCorpseRotSeconds);
        int id = PlayerCorpses?.Create(stored) ?? 0;
        AddPlayerCorpse(stored with { Id = id }, announce: true);
        RebuildFighter(player); // no armour, bare hands
    }

    /// <summary>Saves what is left on a player corpse, or deletes it when it is empty.</summary>
    private void SavePlayerCorpse(Entity body)
    {
        if (body.Corpse is not { Owner: { } owner } corpse || PlayerCorpses is null || corpse.DbId == 0)
            return;
        if (corpse.Items.Count == 0 && corpse.Coins.IsZero)
        {
            PlayerCorpses.Delete(corpse.DbId);
            return;
        }
        PlayerCorpses.Update(new StoredCorpse(corpse.DbId, owner, ShortName, body.Position, body.Heading, body.Race, body.Gender, body.Level,
            corpse.Class, corpse.Deity, corpse.Coins, corpse.Items.Select(i => (i.ItemId, i.Slot, i.Charges)).ToList(), Math.Max(0, corpse.DecayAt - _time)));
    }
}
