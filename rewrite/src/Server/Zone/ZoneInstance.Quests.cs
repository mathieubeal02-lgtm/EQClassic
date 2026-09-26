using EQClassic.Server.Quests;

namespace EQClassic.Server.Zone;

/// <summary>
/// What quest scripts do (the quest:: functions of Zone/Source/questmgr.cpp that the scripts use
/// most): NPC lines (say, emote, shout), items, money, experience and faction for the player, the
/// NPC going away, attacking or casting. And giving items to an NPC: a trade with it, which it
/// always accepts (the legacy "npcs always accept"), then its EVENT_ITEM.
/// </summary>
public sealed partial class ZoneInstance
{
    /// <summary>An NPC's line for the players around it (say, emote or shout).</summary>
    public sealed record NpcSpoke(int NpcId, string Channel, string Text) : ZoneEvent;
    /// <summary>A player handed items and money to an NPC: its script's EVENT_ITEM.</summary>
    public sealed record HandedIn(int PlayerId, int NpcId, IReadOnlyList<int> Items, Coins Coins) : ZoneEvent;

    /// <summary>The NPC trade: the player's offer goes to the NPC (items taken out), then EVENT_ITEM.</summary>
    private void FinishNpcTrade(Entity player, Entity npc)
    {
        var side = player.Trade!;
        var inventory = player.Inventory!;
        var items = side.Slots.Select(s => inventory.ItemAt(s)).Where(i => i != 0).Take(4).ToList(); // item1..item4
        if (inventory.Coins.Take((int)side.Coins.TotalCopper) is not { } left)
        {
            _events.Add(new Told(player.Id, "You do not have that much money."));
            return;
        }
        Remove(inventory, side.Slots.Take(4).ToList()); // a bag goes with what it holds
        inventory.Coins = left;
        player.Trade = null;
        _events.Add(new TradeChanged(player.Id));
        _events.Add(new InventoryChanged(player.Id));
        _events.Add(new HandedIn(player.Id, npc.Id, items, side.Coins));
    }

    /// <summary>Gives items back (an NPC with no use for them, or plugin::return_items).</summary>
    public void ReturnItems(int playerId, IEnumerable<int> items)
    {
        if (!_entities.TryGetValue(playerId, out var player))
            return;
        foreach (int item in items)
            SummonItem(player, item, 1);
    }

    /// <summary>Applies a script's quest:: calls for the NPC and the player of the event.</summary>
    public void ApplyQuest(int npcId, int playerId, IReadOnlyList<QuestAction> actions, Action<string>? log = null)
    {
        _entities.TryGetValue(npcId, out var npc);
        _entities.TryGetValue(playerId, out var player);
        foreach (var a in actions)
        {
            switch (a.Function)
            {
                case "say" when npc is not null:
                    _events.Add(new NpcSpoke(npcId, "say", a.Arg(0)));
                    break;
                case "emote" when npc is not null:
                    _events.Add(new NpcSpoke(npcId, "emote", a.Arg(0)));
                    break;
                case "shout" when npc is not null:
                    _events.Add(new NpcSpoke(npcId, "shout", a.Arg(0)));
                    break;
                case "me":
                    if (player is not null)
                        _events.Add(new Told(player.Id, a.Arg(0)));
                    break;
                case "summonitem" when player is not null:
                    SummonItem(player, a.Int(0), Math.Max(1, a.Int(1, 1)));
                    break;
                case "return_item" when player is not null:
                    for (int n = 0; n < Math.Max(1, a.Int(1, 1)); n++)
                        SummonItem(player, a.Int(0), 1);
                    break;
                case "givecash" when player?.Inventory is { } purse:
                    var cash = new Coins(a.Int(3), a.Int(2), a.Int(1), a.Int(0)); // copper, silver, gold, platinum
                    purse.Coins = purse.Coins.Add(cash);
                    _events.Add(new Told(player.Id, $"You receive {cash}."));
                    _events.Add(new InventoryChanged(player.Id));
                    break;
                case "exp" when player is not null:
                    SetExperience(player, (uint)Math.Max(0, player.Exp + (long)a.Int(0)));
                    break;
                case "faction" when player is not null && Factions is DatabaseFactions db:
                    if (db.Adjust(player, a.Int(0), a.Int(1), player.Deity) is { } message)
                        _events.Add(new Told(player.Id, message));
                    _events.Add(new FactionsChanged(player.Id));
                    break;
                case "depop" when npc is not null && !npc.IsPlayer:
                    Depop(npc);
                    break;
                case "attack" when npc is not null && player is not null:
                    npc.TargetId = player.Id;
                    _events.Add(new Engaged(npc.Id, player.Id));
                    break;
                case "castspell" when npc is not null && _entities.TryGetValue(a.Int(1, playerId), out var spellTarget):
                    NpcCast(npc, a.Int(0), spellTarget);
                    break;
                case "selfcast" when player is not null:
                    if (SpellById(a.Int(0)) is { } spell)
                        ApplyInstantEffects(player, player, spell);
                    break;
                case "ding":
                case "settimer":
                case "stoptimer":
                    break; // a sound; timers are not run yet
                case "error":
                    log?.Invoke($"quest error ({npc?.Name}): {a.Arg(0)}");
                    break;
                default:
                    log?.Invoke($"quest::{a.Function} is not supported yet ({npc?.Name})");
                    break;
            }
        }
    }

    /// <summary>quest::depop: the NPC goes (no corpse) and its spawn point comes back in its time.</summary>
    private void Depop(Entity npc)
    {
        if (!_entities.Remove(npc.Id))
            return;
        _events.Add(new Removed(npc.Id));
        if (npc.Spawn is { } spawn)
            _respawns.Add((spawn, _time + RespawnDelay(spawn)));
    }

    /// <summary>The variables a script reads (PerlembParser::ExportVar): the player, the NPC, the zone.</summary>
    public Dictionary<string, string> QuestVariables(int npcId, int playerId)
    {
        var vars = new Dictionary<string, string> { ["zonesn"] = ShortName };
        if (_entities.TryGetValue(playerId, out var p))
        {
            vars["name"] = p.Name;
            vars["race"] = RaceName(p.Race);
            vars["class"] = ClassName(p.Fighter.Class);
            vars["ulevel"] = p.Level.ToString();
            vars["userid"] = p.Id.ToString();
        }
        if (_entities.TryGetValue(npcId, out var n))
        {
            vars["mname"] = DisplayName(n.Name);
            vars["mobid"] = n.Id.ToString();
            vars["mlevel"] = n.Level.ToString();
            vars["x"] = n.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vars["y"] = n.Position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vars["z"] = n.Position.Z.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vars["h"] = n.Heading.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return vars;
    }

    private static string ClassName(int c) => c switch
    {
        1 => "Warrior", 2 => "Cleric", 3 => "Paladin", 4 => "Ranger", 5 => "Shadow Knight", 6 => "Druid", 7 => "Monk", 8 => "Bard",
        9 => "Rogue", 10 => "Shaman", 11 => "Necromancer", 12 => "Wizard", 13 => "Magician", 14 => "Enchanter", 15 => "Beastlord", _ => "Unknown",
    };

    private static string RaceName(int r) => r switch
    {
        1 => "Human", 2 => "Barbarian", 3 => "Erudite", 4 => "Wood Elf", 5 => "High Elf", 6 => "Dark Elf", 7 => "Half Elf", 8 => "Dwarf",
        9 => "Troll", 10 => "Ogre", 11 => "Halfling", 12 => "Gnome", 128 => "Iksar", _ => "Unknown",
    };
}
