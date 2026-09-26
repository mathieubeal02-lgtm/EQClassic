using EQClassic.Server.Quests;
using EQClassic.Shared.World;

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
    public sealed record NpcSpoke(int NpcId, string Name, Vec3 At, string Channel, string Text) : ZoneEvent;
    /// <summary>A script event of an NPC raised by the zone (death, timer, signal), with its variables taken at that moment.</summary>
    public sealed record QuestTriggered(int NpcId, int PlayerId, NpcTemplate Template, string Event, IReadOnlyDictionary<string, string> Variables) : ZoneEvent;

    private readonly Dictionary<(int NpcId, string Name), (double Every, double Next)> _questTimers = new();

    /// <summary>NPC types by id for quest::spawn (npc_types_without); null: scripts cannot spawn.</summary>
    public Func<int, NpcTemplate?>? NpcTypes { get; set; }

    /// <summary>QuestManager::spawn2 / unique_spawn: an NPC of that type at a place (the legacy zone never gave it its grid; here it walks it).</summary>
    private void QuestSpawn(QuestAction a, bool unique, Action<string>? log)
    {
        int type = a.Int(0);
        if (unique && _entities.Values.Any(e => e.Npc?.Id == type && !e.IsCorpse))
            return;
        if (NpcTypes?.Invoke(type) is not { } template)
        {
            log?.Invoke($"quest::spawn: no NPC type {type}");
            return;
        }
        float F(int i) => float.TryParse(a.Arg(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
        SpawnNpc(template, new Vec3(F(3), F(4), F(5)), F(6), a.Int(1), null);
    }

    /// <summary>quest::settimer: EVENT_TIMER every so many seconds until stopped or the NPC goes.</summary>
    private void CheckQuestTimers()
    {
        foreach (var (key, timer) in _questTimers.ToList())
        {
            if (!_entities.TryGetValue(key.NpcId, out var npc) || npc.Npc is not { } template)
            {
                _questTimers.Remove(key);
                continue;
            }
            if (_time < timer.Next)
                continue;
            _questTimers[key] = (timer.Every, _time + timer.Every);
            var vars = QuestVariables(npc.Id, 0);
            vars["timer"] = key.Name;
            _events.Add(new QuestTriggered(npc.Id, 0, template, "EVENT_TIMER", vars));
        }
    }

    /// <summary>quest::set_proximity's box: x, y and z ranges.</summary>
    internal readonly record struct ProximityBox(float MinX, float MaxX, float MinY, float MaxY, float MinZ, float MaxZ)
    {
        public bool Contains(Vec3 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY && p.Z >= MinZ && p.Z <= MaxZ;
    }

    /// <summary>The legacy timer: EVENT_ATTACK once, then again after this long without being attacked.</summary>
    public const double AttackEventReset = 12.0;

    /// <summary>
    /// EVENT_ENTER and EVENT_EXIT: a player coming into or leaving an NPC's quest::set_proximity box
    /// (the legacy zone stored the box but never checked it; this is what the scripts expect).
    /// </summary>
    private void CheckProximities()
    {
        foreach (var npc in _entities.Values.Where(e => e.Proximity is not null && !e.IsCorpse).ToList())
        {
            var box = npc.Proximity!.Value;
            var inside = npc.InProximity ??= new HashSet<int>();
            foreach (var player in _entities.Values.Where(e => e.IsPlayer).ToList())
            {
                bool now = player.Hp > 0 && box.Contains(player.Position);
                if (now && inside.Add(player.Id))
                    _events.Add(new QuestTriggered(npc.Id, player.Id, npc.Npc!, "EVENT_ENTER", QuestVariables(npc.Id, player.Id)));
                else if (!now && inside.Remove(player.Id) && _entities.ContainsKey(player.Id))
                    _events.Add(new QuestTriggered(npc.Id, player.Id, npc.Npc!, "EVENT_EXIT", QuestVariables(npc.Id, player.Id)));
            }
            inside.RemoveWhere(id => !_entities.ContainsKey(id)); // gone from the zone: no EVENT_EXIT
        }
    }

    /// <summary>quest::signalwith / signal: EVENT_SIGNAL on every NPC of that type in the zone.</summary>
    private void Signal(int npcTypeId, int signal)
    {
        foreach (var npc in _entities.Values.Where(e => e.Npc?.Id == npcTypeId && !e.IsCorpse).ToList())
        {
            var vars = QuestVariables(npc.Id, 0);
            vars["signal"] = signal.ToString();
            _events.Add(new QuestTriggered(npc.Id, 0, npc.Npc!, "EVENT_SIGNAL", vars));
        }
    }
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

    /// <summary>PerlembParser on EVENT_SAY and EVENT_ITEM: the NPC turns to the player.</summary>
    public void FaceTowards(int npcId, int playerId)
    {
        if (_entities.TryGetValue(npcId, out var npc) && !npc.IsPlayer && _entities.TryGetValue(playerId, out var player) && npc.TargetId is null)
        {
            npc.Heading = Heading(npc.Position, player.Position, npc.Heading);
            npc.Moved = true;
        }
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
    /// <param name="speaker">Who speaks when the NPC is gone (EVENT_DEATH): its name and where it was.</param>
    public void ApplyQuest(int npcId, int playerId, IReadOnlyList<QuestAction> actions, Action<string>? log = null, (string Name, Vec3 At)? speaker = null)
    {
        _entities.TryGetValue(npcId, out var npc);
        _entities.TryGetValue(playerId, out var player);
        var voice = npc is not null ? (DisplayName(npc.Name), npc.Position) : speaker;
        foreach (var a in actions)
        {
            switch (a.Function)
            {
                case "say" or "emote" or "shout" when voice is { } v:
                    _events.Add(new NpcSpoke(npcId, v.Item1, v.Item2, a.Function, a.Arg(0)));
                    break;
                case "settimer" when npc is not null && a.Int(1) > 0:
                    _questTimers[(npcId, a.Arg(0))] = (a.Int(1), _time + a.Int(1));
                    break;
                case "stoptimer":
                    _questTimers.Remove((npcId, a.Arg(0)));
                    break;
                case "signalwith":
                    Signal(a.Int(0), a.Int(1));
                    break;
                case "signal":
                    Signal(a.Int(0), 0);
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
                case "depop" when a.Int(0) != 0 && a.Int(0) != npc?.Npc?.Id: // another NPC of that type
                    if (_entities.Values.FirstOrDefault(e => e.Npc?.Id == a.Int(0) && !e.IsCorpse) is { } other)
                        Depop(other);
                    break;
                case "depop" when npc is not null && !npc.IsPlayer:
                    Depop(npc);
                    break;
                case "spawn" or "spawn2":
                    QuestSpawn(a, unique: false, log);
                    break;
                case "unique_spawn":
                    QuestSpawn(a, unique: true, log);
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
                case "set_proximity" when npc?.Npc is not null:
                {
                    float F(int i, float fallback) => float.TryParse(a.Arg(i), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : fallback;
                    npc.Proximity = new ProximityBox(F(0, 0), F(1, 0), F(2, 0), F(3, 0), F(4, -999999), F(5, 999999));
                    npc.InProximity = null;
                    break;
                }
                case "clear_proximity" when npc is not null:
                    npc.Proximity = null;
                    npc.InProximity = null;
                    break;
                case "client_Message" when player is not null:
                    _events.Add(new Told(player.Id, a.Arg(1)));
                    break;
                case "ding":
                    break; // a sound
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
            // $hasitem: the worn and general slots (0 to 29), as PerlembParser exports them.
            if (p.Inventory is { } inventory)
                for (int slot = 0; slot < PlayerInventory.Slots; slot++)
                    if (inventory.ItemAt(slot) is int item and not 0 and not -1)
                        vars[$"hasitem.{slot}"] = item.ToString();
        }
        if (_entities.TryGetValue(npcId, out var n))
        {
            vars["mname"] = DisplayName(n.Name);
            vars["mobid"] = n.Id.ToString();
            vars["mlevel"] = n.Level.ToString();
            vars["hpratio"] = n.HpPercent.ToString();
            if (n.TargetId is int targetId && _entities.TryGetValue(targetId, out var target))
            {
                vars["targetid"] = target.Id.ToString();
                vars["targetname"] = target.Name;
            }
            vars["x"] = n.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vars["y"] = n.Position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vars["z"] = n.Position.Z.ToString(System.Globalization.CultureInfo.InvariantCulture);
            vars["h"] = n.Heading.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return vars;
    }

    /// <summary>Client::GetCharacterFactionLevel: the player's value with a faction plus its modifiers.</summary>
    public int FactionLevel(int playerId, int factionId) =>
        _entities.TryGetValue(playerId, out var player) && Factions is DatabaseFactions db ? db.Level(player, factionId, player.Deity) : 0;

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
