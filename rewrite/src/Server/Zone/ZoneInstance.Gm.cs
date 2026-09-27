using EQClassic.Server.Combat;
using EQClassic.Server.Spells;
using EQClassic.Shared.World;

namespace EQClassic.Server.Zone;

/// <summary>What the GM commands (Zone/Source/Client_Commands.cpp) do inside a zone.</summary>
public sealed partial class ZoneInstance
{
    /// <summary>#goto: to a place in this zone.</summary>
    public void GmTeleport(int playerId, Vec3 to)
    {
        if (_entities.TryGetValue(playerId, out var player) && player.IsPlayer)
            Teleport(player, ShortName, to, "teleport");
    }

    /// <summary>#zone: to another zone (at its safe point or the given place).</summary>
    public void GmZone(int playerId, string zone, Vec3 to)
    {
        if (_entities.TryGetValue(playerId, out var player) && player.IsPlayer)
            Teleport(player, zone, to, "teleport");
    }

    /// <summary>#summon: the target comes to the GM.</summary>
    public void GmSummon(int gmId, int targetId)
    {
        if (!_entities.TryGetValue(gmId, out var gm) || !_entities.TryGetValue(targetId, out var target) || target == gm)
            return;
        var at = gm.Position with { X = gm.Position.X + 3f };
        if (target.IsPlayer)
            Teleport(target, ShortName, at, "teleport");
        else
        {
            target.Position = at;
            target.Moved = true;
        }
    }

    /// <summary>#level: a player's level (through the experience of that level), or an NPC's.</summary>
    public void GmSetLevel(int targetId, int level)
    {
        if (!_entities.TryGetValue(targetId, out var e) || e.IsCorpse)
            return;
        level = Math.Clamp(level, 1, Experience.MaxLevel);
        if (e.IsPlayer)
            SetExperience(e, Experience.ForLevel(level, e.Fighter.Class, e.Race));
        else
            e.Level = level;
    }

    /// <summary>#setexp / #addexp.</summary>
    public void GmSetExperience(int playerId, long exp)
    {
        if (_entities.TryGetValue(playerId, out var player) && player.IsPlayer)
            SetExperience(player, (uint)Math.Clamp(exp, 0, uint.MaxValue));
    }

    /// <summary>#heal and #mana: full hit points and mana.</summary>
    public void GmHeal(int targetId, bool hp = true, bool mana = true)
    {
        if (!_entities.TryGetValue(targetId, out var e) || e.IsCorpse)
            return;
        if (hp)
        {
            e.Hp = e.Fighter.MaxHp;
            if (e.IsPlayer)
                _events.Add(new HealthChanged(e.Id, e.Hp, e.Fighter.MaxHp));
        }
        if (mana && e.MaxMana > 0)
            SetMana(e, e.MaxMana);
    }

    /// <summary>#damage and #kill: damage from the GM, with its consequences (death, experience, loot rights).</summary>
    public void GmDamage(int gmId, int targetId, int damage)
    {
        if (!_entities.TryGetValue(gmId, out var gm) || !_entities.TryGetValue(targetId, out var target) || target.IsCorpse)
            return; // the GM too (#kill self, #damage n self)
        target.Hp -= damage;
        AfterHarm(gm, target, damage);
    }

    /// <summary>#invul: no damage gets through.</summary>
    public void GmInvulnerable(int targetId, bool on)
    {
        if (_entities.TryGetValue(targetId, out var e))
            e.GmInvulnerable = on;
    }

    /// <summary>#flymode: the player may leave the ground (their client stops falling).</summary>
    public void GmFlying(int playerId, bool on)
    {
        if (_entities.TryGetValue(playerId, out var e) && e.IsPlayer)
        {
            e.GmFlying = on;
            _events.Add(new FlyingChanged(e.Id, on));
        }
    }

    /// <summary>#summonitem.</summary>
    public void GmSummonItem(int playerId, int itemId, int charges)
    {
        if (_entities.TryGetValue(playerId, out var player) && player.IsPlayer)
            SummonItem(player, itemId, Math.Max(1, charges));
    }

    /// <summary>#clearinventory: the general slots and their bags emptied (what is worn stays).</summary>
    public int GmClearInventory(int playerId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Inventory is not { } inventory)
            return 0;
        int cleared = 0;
        for (int slot = PlayerInventory.FirstGeneral; slot < PlayerInventory.Slots; slot++)
        {
            if (inventory.ItemAt(slot) != 0)
                cleared++;
            inventory.Set(slot, 0, 0);
            for (int cell = 0; cell < PlayerInventory.BagCells; cell++)
                inventory.Set(PlayerInventory.BagSlot(slot, cell), 0, 0);
        }
        _events.Add(new InventoryChanged(playerId));
        return cleared;
    }

    /// <summary>#givemoney.</summary>
    public void GmGiveMoney(int playerId, Coins coins)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Inventory is not { } inventory)
            return;
        inventory.Coins = inventory.Coins.Add(coins);
        _events.Add(new Told(playerId, $"You receive {coins}."));
        _events.Add(new InventoryChanged(playerId));
    }

    /// <summary>#repopzone: every spawn point waiting for its NPC gets it now.</summary>
    public int GmRepop()
    {
        var waiting = _respawns.ToList();
        _respawns.Clear();
        foreach (var (spawn, _) in waiting)
            SpawnAt(spawn);
        return waiting.Count;
    }

    /// <summary>#depopzone: every NPC goes (not pets); their spawn points count down as after a depop.</summary>
    public int GmDepop()
    {
        var npcs = _entities.Values.Where(e => !e.IsPlayer && !e.IsCorpse && e.OwnerId is null).ToList();
        foreach (var npc in npcs)
            Depop(npc);
        return npcs.Count;
    }

    /// <summary>#spawn: an NPC type where the GM stands.</summary>
    public string GmSpawn(int gmId, int npcTypeId)
    {
        if (!_entities.TryGetValue(gmId, out var gm))
            return "";
        if (NpcTypes?.Invoke(npcTypeId) is not { } template)
            return $"No NPC type {npcTypeId}.";
        var npc = SpawnNpc(template, gm.Position with { X = gm.Position.X + 5f }, gm.Heading, 0, null);
        return $"Spawned {DisplayName(npc.Name)} (entity {npc.Id}, level {npc.Level}).";
    }

    /// <summary>#castspell: the spell lands on the target at once (no cast time, no mana, no resist).</summary>
    public string GmCast(int gmId, int targetId, int spellId)
    {
        if (!_entities.TryGetValue(gmId, out var gm) || !_entities.TryGetValue(targetId, out var target))
            return "";
        if (SpellById(spellId) is not { } spell)
            return $"No spell {spellId}.";
        if (spell.IsBuff)
            AddBuff(gm, target, spell);
        ApplyInstantEffects(gm, target, spell);
        return $"{spell.Name} lands on {DisplayName(target.Name)}.";
    }

    /// <summary>#scribespells: every spell of the player's class up to that level, into the book.</summary>
    public int GmScribeSpells(int playerId, int maxLevel)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || Spells is null)
            return 0;
        var book = player.Book.Length >= Characters.PlayerProfile.SpellBookSlots ? player.Book.ToArray()
            : player.Book.Concat(Enumerable.Repeat(-1, Characters.PlayerProfile.SpellBookSlots - player.Book.Length)).ToArray();
        int added = 0;
        foreach (var spell in Spells.Where(s => s.IsValid && s.LevelFor(player.Fighter.Class) is int l && l <= maxLevel)
                     .OrderBy(s => s.LevelFor(player.Fighter.Class)))
        {
            if (book.Contains(spell.Id))
                continue;
            int page = Array.IndexOf(book, -1);
            if (page < 0)
                break;
            book[page] = spell.Id;
            added++;
        }
        player.Book = book;
        _events.Add(new GemsChanged(playerId));
        return added;
    }

    /// <summary>#unscribespells [spell id]: that spell, or every spell, out of the book (and off the gems).</summary>
    public int GmUnscribeSpells(int playerId, int? spellId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer)
            return 0;
        var book = player.Book.ToArray();
        int removed = 0;
        for (int page = 0; page < book.Length; page++)
            if (book[page] >= 0 && (spellId is null || book[page] == spellId))
            {
                book[page] = -1;
                removed++;
            }
        for (int gem = 0; gem < player.Gems.Length; gem++)
            if (player.Gems[gem] >= 0 && !book.Contains(player.Gems[gem]))
                player.Gems[gem] = -1;
        player.Book = book;
        _events.Add(new GemsChanged(playerId));
        return removed;
    }

    /// <summary>#npcstats.</summary>
    public string GmNpcStats(int targetId)
    {
        if (!_entities.TryGetValue(targetId, out var e) || e.Npc is not { } t)
            return "Target an NPC.";
        var f = e.Fighter;
        return $"{DisplayName(e.Name)} (type {t.Id}, entity {e.Id}): level {e.Level}, race {t.Race}, class {t.Combat.Class}, "
            + $"hp {e.Hp}/{f.MaxHp}, damage {t.Combat.MinDamage}-{t.Combat.MaxDamage}, AC {t.Combat.AC}, faction {t.PrimaryFaction}, "
            + $"loot {t.LoottableId}, merchant {t.MerchantId}, at ({e.Position.X:0.#}, {e.Position.Y:0.#}, {e.Position.Z:0.#})"
            + (e.Spawn is { } s ? $", spawn {s.Id} grid {s.GridId}" : ", no spawn point");
    }
}
