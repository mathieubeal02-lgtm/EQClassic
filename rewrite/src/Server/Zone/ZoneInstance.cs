using EQClassic.Server.Combat;
using EQClassic.Server.Spells;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Server.Zone;

/// <summary>
/// One running zone: its NPCs and players, advanced by <see cref="Tick"/>. No static or global
/// state, so any number of instances live in one process (the legacy zone processes crashed when
/// reused for another zone, on stale globals). Spawns, deaths and zone line crossings are queued
/// as events for the server to broadcast (<see cref="DrainEvents"/>).
/// </summary>
public sealed partial class ZoneInstance
{
    /// <summary>Fastest a player may move, units per second (EverQuest run speed with haste and SoW stays well under).</summary>
    public const float MaxPlayerSpeed = 70f;
    /// <summary>An engaged NPC stops this close to its target (melee reach in the rewrite until combat, M5).</summary>
    public const float MeleeRange = 10f;
    private const float MoveTolerance = 5f;
    /// <summary>Upward speed a jump or a slope allows (units per second), plus a step.</summary>
    public const float MaxClimbSpeed = 40f;
    private const float ClimbTolerance = 8f;
    /// <summary>A door untouched this long is closed again (legacy: "not touched in twelve seconds").</summary>
    public const double DoorCloseSeconds = 12;
    /// <summary>Farthest a player may be from a door to use it. Not in the legacy handler: a rewrite sanity check.</summary>
    public const float DoorReach = 40f;
    public const string NoKeyMessage = "You do not have the required key in hand to open this door";
    /// <summary>A player swings at a target this close (melee reach; the legacy client decides by model size).</summary>
    public const float PlayerReach = 14f;
    /// <summary>Seconds between regeneration ticks (the legacy 6-second tic).</summary>
    public const double RegenSeconds = 6;
    public const string TooFarMessage = "Your target is too far away, get closer!";
    /// <summary>Reason of the move that brings a slain player back: clients drop their target and stop attacking.</summary>
    public const string DeathReason = "death";

    public sealed class Entity
    {
        internal Entity(int id, string name, bool isPlayer, int race, int gender, int level, float size, Vec3 position, float heading)
        {
            Id = id; Name = name; IsPlayer = isPlayer; Race = race; Gender = gender; Level = level; Size = size; Position = position; Heading = heading;
        }

        public int Id { get; }
        public string Name { get; }
        public bool IsPlayer { get; }
        public int Race { get; }
        public int Gender { get; }
        public int Level { get; internal set; }
        public float Size { get; }
        public Vec3 Position { get; internal set; }
        public float Heading { get; internal set; }
        public bool Sitting { get; set; }
        /// <summary>For NPCs: the player they are after, or null.</summary>
        public int? TargetId { get; internal set; }
        internal NpcTemplate? Npc;
        internal SpawnPoint? Spawn;
        internal WaypointWalker? Walker;
        internal Grid? Grid;
        internal float PauseLeft;
        internal float Speed;
        internal float ScanIn;
        internal bool Moved;
        internal double LastMoveTime;
        /// <summary>Melee numbers (NPC from its type, player from the profile).</summary>
        public Combatant Fighter { get; internal set; } = null!;
        public int Hp { get; internal set; }
        /// <summary>Players: the entity they target, and whether they auto-attack it.</summary>
        public int? PlayerTargetId { get; internal set; }
        public bool AutoAttack { get; internal set; }
        internal float SwingIn;
        internal double TooFarToldAt = double.NegativeInfinity;
        /// <summary>Players: where they entered the zone (death returns them there until binding exists).</summary>
        internal Vec3 EntryPosition;
        internal double LastCombatTime = double.NegativeInfinity;
        /// <summary>Players: experience, and how to rebuild their fighter at another level.</summary>
        public uint Exp { get; internal set; }
        internal PlayerProgress? Progress;
        /// <summary>NPCs: damage taken from each player (the most damage earns the experience).</summary>
        internal Dictionary<int, int>? DamageBy;

        public int HpPercent => Fighter.MaxHp <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(100.0 * Hp / Fighter.MaxHp), 0, 100);

        public EntitySpawn ToSpawn() => new(Id, Name, IsPlayer, Race, Gender, Level, Size, Position.X, Position.Y, Position.Z, Heading, IsCorpse);

        /// <summary>A dead NPC's body, with what it carried until looted or rotten.</summary>
        public bool IsCorpse => Corpse is not null;
        internal CorpseData? Corpse;
        /// <summary>Players: what they carry (from the profile; saved when they leave).</summary>
        public PlayerInventory? Inventory { get; internal set; }
        /// <summary>Players: mana, spell book (spell ids), memorised gems (−1: empty), and the spell being cast.</summary>
        public int Mana { get; internal set; }
        public int MaxMana { get; internal set; }
        public int[] Book { get; internal set; } = Array.Empty<int>();
        public int[] Gems { get; internal set; } = Array.Empty<int>();
        public Casting? Cast { get; internal set; }
        internal PlayerMagic? Magic;
        /// <summary>Spells lasting on the entity (at most 15), and what they add up to.</summary>
        public IReadOnlyList<Buff> Buffs => BuffList;
        internal readonly List<Buff> BuffList = new();
        public StatBonuses Bonuses { get; internal set; } = StatBonuses.None;
        /// <summary>NPCs: stunned (no moving, no fighting) until then.</summary>
        internal double StunnedUntil = double.NegativeInfinity;
        /// <summary>Players: where death and gate send them (bind affinity changes it); empty zone when unknown.</summary>
        public string BindZone { get; internal set; } = "";
        public Vec3 Bind { get; internal set; }
        /// <summary>Players: deity (faction modifiers) and their values with each faction (faction_values).</summary>
        public int Deity { get; internal set; } = FactionRules.AgnosticDeity;
        internal readonly Dictionary<int, int> FactionValues = new();
        public int FactionValue(int factionId) => FactionValues.GetValueOrDefault(factionId);
        internal void SetFactionValue(int factionId, int value) => FactionValues[factionId] = value;
        /// <summary>NPC casters: their spells (npc_spells), spell credits spent, and when they next consider casting.</summary>
        /// <summary>NPCs: running away at low health, where to, and when the next emergency check is.</summary>
        public bool Fleeing { get; internal set; }
        internal Vec3? FleeTo;
        internal double NextEmergency;
        internal NpcSpellSet? SpellSet;
        internal bool SpellSetChecked;
        internal int SpellCredit;
        internal double NextOffense, NextDefense, NextCredit;
        /// <summary>Players: skill values by skill id (the fighter builder reads the same array).</summary>
        public int[] Skills { get; internal set; } = new int[SkillCaps.SkillCount];
        /// <summary>Players: when each ability can be used again; hidden (and where), sneaking.</summary>
        internal readonly Dictionary<int, double> AbilityReadyAt = new();
        public bool Hidden { get; internal set; }
        internal Vec3 HiddenAt;
        public bool Sneaking { get; internal set; }
        /// <summary>Players: their side of a trade in progress, or null.</summary>
        public TradeSide? Trade { get; internal set; }
        /// <summary>Players: the merchant whose window is open, and its goods.</summary>
        public int? MerchantId { get; internal set; }
        internal IReadOnlyList<int> MerchantGoods = Array.Empty<int>();
    }

    public abstract record ZoneEvent;
    public sealed record Spawned(Entity Entity) : ZoneEvent;
    public sealed record Removed(int EntityId) : ZoneEvent;
    public sealed record Engaged(int NpcId, int PlayerId) : ZoneEvent;
    public sealed record CrossedZoneLine(int PlayerId, ZoneLine Line, Vec3 Destination) : ZoneEvent;
    public sealed record DoorChanged(int DoorId, bool Open) : ZoneEvent;
    /// <summary>A player moved by the server within the zone (teleport door): their client must follow.</summary>
    public sealed record Teleported(int PlayerId, Vec3 Destination, string Reason = "teleport") : ZoneEvent;
    /// <summary>A line of text for one player.</summary>
    public sealed record Told(int PlayerId, string Text) : ZoneEvent;
    /// <summary>A melee swing: Damage 0 is a miss. DefenderHpPercent after the hit.</summary>
    public sealed record Swung(int AttackerId, int DefenderId, int Damage, int DefenderHpPercent) : ZoneEvent;
    /// <summary>A player's hit points changed (hit, regeneration, death).</summary>
    public sealed record HealthChanged(int PlayerId, int Hp, int MaxHp) : ZoneEvent;
    public sealed record Slain(int VictimId, string VictimName, int KillerId, string KillerName) : ZoneEvent;
    public sealed record Considered(int PlayerId, int EntityId, Standing Standing, ConColor Con) : ZoneEvent;
    public sealed record AppearanceChanged(int EntityId, bool Sitting) : ZoneEvent;
    /// <summary>A player's loot window: the corpse and what is left on it (in order).</summary>
    public sealed record LootShown(int PlayerId, int CorpseId, IReadOnlyList<LootDrop> Items) : ZoneEvent;
    public sealed record InventoryChanged(int PlayerId) : ZoneEvent;
    public sealed record ExperienceChanged(int PlayerId, uint Exp, int Level) : ZoneEvent;
    /// <summary>A kill moved the player's faction values: the server saves them.</summary>
    public sealed record FactionsChanged(int PlayerId) : ZoneEvent;

    internal const int BankerClass = 40, MerchantClass = 41;

    internal sealed class CorpseData
    {
        public List<LootDrop> Items = new();
        public Coins Coins;
        public double DecayAt;
        public double FreeForAllAt;
        public HashSet<int> Rights = new();
        public int? Looter;
        /// <summary>Player corpses: whose it is (only they loot it), its player_corpses id, class and deity for saving.</summary>
        public string? Owner;
        public int DbId;
        public int Class, Deity;
    }

    /// <summary>A player's inventory: item id and charges for each of the 30 profile slots, and money.</summary>
    /// <summary>
    /// A player's items: 30 slots (0-21 worn, 22-29 general) and the contents of the bags in the
    /// general slots, up to 10 each (the profile's containerinv[80]), addressed as the legacy zone
    /// did: 250 + bag × 10 + cell, bag 0 being the one in slot 22.
    /// </summary>
    public sealed class PlayerInventory
    {
        public const int Slots = 30, FirstGeneral = 22, BagSlotBase = 250, BagCells = 10, BagCount = 8, BagSlotsTotal = BagCount * BagCells;
        public int[] Items { get; } = new int[Slots];
        public int[] Charges { get; } = new int[Slots];
        public int[] BagItems { get; } = new int[BagSlotsTotal];
        public int[] BagCharges { get; } = new int[BagSlotsTotal];
        public Coins Coins { get; set; }

        public static bool IsBagSlot(int slot) => slot >= BagSlotBase && slot < BagSlotBase + BagSlotsTotal;
        /// <summary>The general slot of the bag holding a bag slot.</summary>
        public static int BagOf(int bagSlot) => FirstGeneral + (bagSlot - BagSlotBase) / BagCells;
        public static int CellOf(int bagSlot) => (bagSlot - BagSlotBase) % BagCells;
        public static int BagSlot(int generalSlot, int cell) => BagSlotBase + (generalSlot - FirstGeneral) * BagCells + cell;
        public static bool IsGeneral(int slot) => slot >= FirstGeneral && slot < Slots;

        public int ItemAt(int slot) => slot is >= 0 and < Slots ? Items[slot] : IsBagSlot(slot) ? BagItems[slot - BagSlotBase] : 0;
        public int ChargesAt(int slot) => slot is >= 0 and < Slots ? Charges[slot] : IsBagSlot(slot) ? BagCharges[slot - BagSlotBase] : 0;

        public void Set(int slot, int item, int charges)
        {
            if (slot is >= 0 and < Slots)
                (Items[slot], Charges[slot]) = (item, charges);
            else if (IsBagSlot(slot))
                (BagItems[slot - BagSlotBase], BagCharges[slot - BagSlotBase]) = (item, charges);
        }

        /// <summary>Whether the bag in a general slot holds anything.</summary>
        public bool BagHasItems(int generalSlot) =>
            IsGeneral(generalSlot) && Enumerable.Range(0, BagCells).Any(c => BagItems[(generalSlot - FirstGeneral) * BagCells + c] != 0);

        public static PlayerInventory From(IReadOnlyList<int> items, IReadOnlyList<int> charges, Coins coins,
            IReadOnlyList<int>? bagItems = null, IReadOnlyList<int>? bagCharges = null)
        {
            var inventory = new PlayerInventory { Coins = coins };
            for (int i = 0; i < Slots; i++)
            {
                inventory.Items[i] = i < items.Count ? items[i] : 0;
                inventory.Charges[i] = i < charges.Count ? charges[i] : 0;
            }
            for (int i = 0; bagItems is not null && i < BagSlotsTotal && i < bagItems.Count; i++)
            {
                inventory.BagItems[i] = bagItems[i];
                inventory.BagCharges[i] = bagCharges is not null && i < bagCharges.Count ? bagCharges[i] : 0;
            }
            return inventory;
        }

        /// <summary>The first empty general slot (22-29), or -1.</summary>
        public int FreeGeneralSlot()
        {
            for (int i = FirstGeneral; i < Slots; i++)
                if (Items[i] == 0)
                    return i;
            return -1;
        }

        /// <summary>
        /// Client::AutoPutItemInInventory: the first empty general slot, else the first empty cell of a
        /// bag the item fits in (no bag in a bag, the item's size within the bag's); -1 when full.
        /// </summary>
        public int FreeSlotFor(ItemStats? item, Func<int, ItemStats?> itemById)
        {
            int general = FreeGeneralSlot();
            if (general >= 0)
                return general;
            if (item is { IsContainer: true })
                return -1;
            for (int g = FirstGeneral; g < Slots; g++)
                if (itemById(Items[g]) is { IsContainer: true } bag && (item is null || item.Size <= bag.BagSize))
                    for (int c = 0; c < Math.Min(bag.BagSlots, BagCells); c++)
                        if (BagItems[(g - FirstGeneral) * BagCells + c] == 0)
                            return BagSlot(g, c);
            return -1;
        }
    }

    // Legacy corpse timers (Common/Include/config.h): 8 minutes, 45 seconds when empty, 30 minutes
    // from level 55; loot rights to the killer, then free for all after 165 seconds.
    public const double CorpseRotSeconds = 480, EmptyCorpseRotSeconds = 45, ExtendedCorpseRotSeconds = 1800, FreeForAllSeconds = 165;
    public const float LootReach = 20f;

    private sealed class DoorSlot
    {
        public DoorSlot(Door data) => Data = data;
        public Door Data { get; }
        public bool Open;
        public double LastClick = double.NegativeInfinity;
    }

    private readonly Dictionary<int, Entity> _entities = new();
    private readonly List<ZoneEvent> _events = new();
    private readonly List<(SpawnPoint Spawn, double At)> _respawns = new();
    private readonly IReadOnlyDictionary<int, Grid> _grids;
    private readonly IReadOnlyList<ZoneLine> _lines;
    private readonly Dictionary<int, DoorSlot> _doors = new();
    private readonly Random _random;
    private int _nextId = 1;
    private double _time;

    public string ShortName { get; }
    /// <summary>Who is grouped with whom (set by the zone server); null: nobody.</summary>
    public IGroups? Groups { get; set; }

    /// <summary>The players of this zone in <paramref name="player"/>'s group (the player included).</summary>
    internal List<Entity> GroupHere(Entity player)
    {
        var names = Groups?.MembersOf(player.Name) ?? Array.Empty<string>();
        if (names.Count == 0)
            return [player];
        return _entities.Values.Where(e => e.IsPlayer && names.Contains(e.Name, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Water, lava and zone line regions (the zone's BSP tree), when the Lantern export has it.</summary>
    public ZoneRegions? Regions { get; init; }
    /// <summary>zone_rules: binding, levitation, outdoor spells.</summary>
    public ZoneRules Rules { get; }
    public ZoneCollisionMesh? Mesh { get; }
    public IFactionStandings Factions { get; set; } = new IndifferentFactions();
    /// <summary>The legacy zone header (cfg/&lt;zone&gt;.cfg), when known: sent to clients, and its underworld depth applies.</summary>
    public ZoneInfo? Info { get; init; }
    /// <summary>Loot tables for NPC corpses; none: corpses are empty.</summary>
    public ILootSource? Loot { get; init; }
    /// <summary>Item names for the loot messages.</summary>
    public IItemSource? Items { get; init; }
    public const string UnderworldReason = "underworld";
    public uint TickCount { get; private set; }
    public IEnumerable<Entity> Entities => _entities.Values;
    public IEnumerable<(Door Door, bool Open)> Doors => _doors.Values.Select(d => (d.Data, d.Open));

    public ZoneInstance(ZoneData data, ZoneCollisionMesh? mesh = null, int seed = 0)
    {
        ShortName = data.ShortName;
        Rules = data.Rules;
        WeatherType = data.Weather;
        Mesh = mesh;
        _grids = data.Grids;
        _lines = data.Lines;
        foreach (var door in data.Doors)
            _doors[door.Id] = new DoorSlot(door); // the table has duplicate ids in a few zones: the last wins
        _random = new Random(seed);
        foreach (var spawn in data.Spawns)
            SpawnAt(spawn);
        _events.Clear(); // the initial population is the entity list, not news
    }

    /// <param name="fighter">Melee numbers from the profile; null gives a plain level-based fighter (tests).</param>
    /// <param name="hp">Saved hit points; null or out of range gives full health.</param>
    public Entity AddPlayer(string name, int race, int gender, int level, Vec3 position, float heading = 0, Combatant? fighter = null, int? hp = null,
        PlayerProgress? progress = null)
    {
        var player = Add(name, true, race, gender, level, 6f, position, heading);
        player.LastMoveTime = _time;
        player.Progress = progress;
        player.Exp = progress?.Exp ?? 0;
        player.Inventory = progress?.Inventory ?? new PlayerInventory();
        player.Fighter = fighter ?? progress?.FighterAt?.Invoke(level, StatBonuses.None) ?? DefaultPlayer(level);
        player.Hp = hp is int h && h > 0 && h <= player.Fighter.MaxHp ? h : player.Fighter.MaxHp;
        player.EntryPosition = position;
        player.Skills = progress?.Skills ?? (progress?.Magic?.Skills is { Count: > 0 } s ? s.ToArray() : new int[SkillCaps.SkillCount]);
        player.BindZone = progress?.BindZone ?? "";
        player.Deity = progress?.Deity ?? FactionRules.AgnosticDeity;
        foreach (var (id, value) in progress?.Factions ?? new Dictionary<int, int>())
            player.FactionValues[id] = value;
        player.Bind = progress?.Bind ?? default;
        SetUpMagic(player, progress?.Magic);
        return player;
    }

    /// <summary>What a player brings besides position: experience, bind point, and their fighter at any level.</summary>
    /// <param name="FighterAt">The player's fighter at a level with the bonuses of their buffs (from the profile and items).</param>
    public sealed record PlayerProgress(uint Exp, string BindZone, Vec3 Bind, Func<int, StatBonuses, Combatant>? FighterAt, PlayerInventory? Inventory = null,
        PlayerMagic? Magic = null)
    {
        public int Deity { get; init; } = FactionRules.AgnosticDeity;
        /// <summary>The character's skills; skill-ups change this array, which <see cref="FighterAt"/> may read.</summary>
        public int[]? Skills { get; init; }
        /// <summary>STR, for kicks and bashes.</summary>
        public int Str { get; init; } = 75;
        /// <summary>faction_values of the character.</summary>
        public IReadOnlyDictionary<int, int>? Factions { get; init; }
    }

    private static Combatant DefaultPlayer(int level) =>
        new(true, level, CombatFormulas.Warrior, CombatFormulas.ClientBaseHp(level, CombatFormulas.Warrior, 75),
            Offense: level * 5 + 5, ToHit: 7 + 2 * (level * 5 + 5), Avoidance: level * 5 + 5, Mitigation: level * 3 + 5,
            DamageBonus: 0, BaseDamage: 2, DelaySeconds: 3.6f);

    /// <summary>
    /// Consider (Client::ProcessOP_Consider): the NPC's faction standing towards the player (merchants
    /// and bankers never worse than dubious) and the colour by level. Players are indifferent.
    /// </summary>
    public void Consider(int playerId, int entityId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || entityId == playerId
            || !_entities.TryGetValue(entityId, out var other) || other.IsCorpse)
            return;
        var standing = Standing.Indifferent;
        if (other.Npc is { } npc)
        {
            // Consider asks with the player's deity, aggro with an agnostic one (client_process.cpp, NpcAI.cpp).
            standing = (Standing)(int)(Factions is DatabaseFactions db ? db.StandingFor(player, npc, player.Deity) : Factions.Standing(player, npc));
            if (npc.Combat.Class is BankerClass or MerchantClass && standing is Standing.Scowls or Standing.Threatenly)
                standing = Standing.Dubious;
        }
        _events.Add(new Considered(playerId, entityId, standing, ConsiderRules.LevelCon(player.Level, other.Level)));
    }

    /// <summary>A player sits down or stands up (legacy OP_SpawnAppearance); sitting doubles regeneration and gets you always hit.</summary>
    public void SetSitting(int playerId, bool sitting)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || player.Sitting == sitting)
            return;
        player.Sitting = sitting;
        _events.Add(new AppearanceChanged(playerId, sitting));
    }

    /// <summary>A player targets an entity (null clears the target, and stops auto-attack).</summary>
    public void SetTarget(int playerId, int? targetId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer)
            return;
        player.PlayerTargetId = targetId is int t && t != playerId && _entities.ContainsKey(t) ? t : null;
        if (player.PlayerTargetId is null && player.AutoAttack)
            SetAutoAttack(playerId, false);
    }

    /// <summary>Auto-attack on or off (legacy OP_AutoAttack); players do not attack each other yet.</summary>
    public void SetAutoAttack(int playerId, bool on)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || player.AutoAttack == on)
            return;
        if (on && player.PlayerTargetId is int c && _entities.TryGetValue(c, out var dead) && dead.IsCorpse)
        {
            _events.Add(new Told(playerId, "You cannot attack a corpse."));
            return;
        }
        if (on && (player.PlayerTargetId is not int t || !_entities.TryGetValue(t, out var target) || target.IsPlayer))
        {
            _events.Add(new Told(playerId, "You must first select a target for this command!"));
            return;
        }
        player.AutoAttack = on;
        if (on)
        {
            BreakInvisibility(player);
            player.Hidden = false;
        }
        player.SwingIn = 0; // the first swing goes out as soon as the target is in reach
        _events.Add(new Told(playerId, on ? "Auto attack is on." : "Auto attack is off."));
    }

    public bool RemovePlayer(int id)
    {
        if (!_entities.TryGetValue(id, out var e) || !e.IsPlayer)
            return false;
        CancelTrade(id);
        _entities.Remove(id);
        foreach (var npc in _entities.Values.Where(n => n.TargetId == id))
            Disengage(npc);
        foreach (var other in _entities.Values.Where(p => p.PlayerTargetId == id))
            other.PlayerTargetId = null;
        foreach (var body in _entities.Values.Where(b => b.Corpse?.Looter == id))
            body.Corpse!.Looter = null;
        return true;
    }

    public Entity? Get(int id) => _entities.GetValueOrDefault(id);

    public bool IsDoorOpen(int doorId) => _doors.TryGetValue(doorId, out var d) && d.Open;

    /// <summary>
    /// A player uses a door, after Client::ProcessOP_ClickDoor: a door untouched for
    /// <see cref="DoorCloseSeconds"/> counts as closed; a locked door needs its key in hand (no
    /// inventory yet: always refused); a door with a destination teleports the player, within the
    /// zone or to another one; any other door toggles, with its trigger door.
    /// </summary>
    public void ClickDoor(int playerId, int doorId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || !_doors.TryGetValue(doorId, out var door))
            return;
        if (Distance2(player.Position, door.Data.Position) > DoorReach * DoorReach)
        {
            _events.Add(new Told(playerId, "You are too far away to use that."));
            return;
        }
        if (_time - door.LastClick >= DoorCloseSeconds)
            door.Open = false;
        door.LastClick = _time;

        if (door.Data.Locked)
        {
            _events.Add(new Told(playerId, NoKeyMessage));
            return;
        }
        if (door.Data.Teleports)
        {
            var to = door.Data.Destination;
            if (string.Equals(door.Data.DestZone, ShortName, StringComparison.OrdinalIgnoreCase))
            {
                player.Position = to;
                player.LastMoveTime = _time;
                player.Moved = true;
                _events.Add(new Teleported(playerId, to));
            }
            else
            {
                _events.Add(new CrossedZoneLine(playerId, new ZoneLine(-door.Data.Id, door.Data.Position, 0, door.Data.DestZone!, to), to));
            }
            return;
        }
        SetDoor(door, !door.Open);
        if (door.Data.TriggerDoor > 0 && door.Data.TriggerDoor != doorId && _doors.TryGetValue(door.Data.TriggerDoor, out var linked))
        {
            linked.LastClick = _time;
            SetDoor(linked, door.Open);
        }
    }

    private void SetDoor(DoorSlot door, bool open)
    {
        if (door.Open == open)
            return;
        door.Open = open;
        _events.Add(new DoorChanged(door.Data.Id, open));
    }

    /// <summary>
    /// Validates a player's reported position: refused when faster than <see cref="MaxPlayerSpeed"/>
    /// on the ground plane or climbing faster than <see cref="MaxClimbSpeed"/> since the last accepted
    /// move (teleport hacks, lag spikes are corrected, not trusted); falls are free.
    /// Returns null when accepted, otherwise the reason (the position stays the last valid one).
    /// An accepted move onto a zone line queues <see cref="CrossedZoneLine"/>.
    /// </summary>
    public string? MovePlayer(int id, Vec3 to, float heading)
    {
        if (!_entities.TryGetValue(id, out var player) || !player.IsPlayer)
            return "not in zone";
        double elapsed = Math.Max(_time - player.LastMoveTime, 0.05);
        float moveX = to.X - player.Position.X, moveY = to.Y - player.Position.Y;
        float distance = MathF.Sqrt(moveX * moveX + moveY * moveY);
        float speed = MaxPlayerSpeed * Math.Max(1f, (100 + player.Bonuses.MovementSpeed) / 100f); // spirit of wolf and the like
        if (distance > speed * elapsed + MoveTolerance)
            return $"moved {distance:0} units in {elapsed:0.00} s";
        // Falling is free (the legacy server checked nothing); climbing is limited to jumps and steps, except in water.
        float climb = to.Z - player.Position.Z;
        bool swimming = Regions is { } regions && (regions.InWater(to) || regions.InWater(player.Position));
        if (!swimming && climb > MaxClimbSpeed * elapsed + ClimbTolerance)
            return $"climbed {climb:0} units in {elapsed:0.00} s";
        if (Info is { } info && to.Z < info.Underworld)
        {
            // Fell through the world: back to the zone's safe point, as the Trilogy client did.
            player.Position = new Vec3(info.SafeX, info.SafeY, info.SafeZ);
            player.LastMoveTime = _time;
            player.Moved = true;
            return UnderworldReason;
        }
        if (player.Sitting && Distance2(player.Position, to) > 0.01f)
            SetSitting(id, false); // walking stands you up
        if (player.Hidden && !player.Sneaking && Distance2(player.HiddenAt, to) > 1f)
        {
            player.Hidden = false;
            _events.Add(new Told(id, "You are no longer hidden."));
        }
        player.Position = to;
        player.Heading = heading;
        player.LastMoveTime = _time;
        player.Moved = true;
        foreach (var line in _lines)
        {
            float dx = to.X - line.Position.X, dy = to.Y - line.Position.Y;
            if (dx * dx + dy * dy <= line.Range * line.Range && MathF.Abs(to.Z - line.Position.Z) <= Math.Max(line.Range, 20f))
            {
                var destination = new Vec3(line.KeepX ? to.X : line.Target.X, line.KeepY ? to.Y : line.Target.Y, line.Target.Z);
                _events.Add(new CrossedZoneLine(id, line, destination));
                break;
            }
        }
        return null;
    }

    /// <summary>
    /// An NPC dies (combat is M5; GM commands and tests call this). Its spawn point respawns after
    /// spawn2.respawntime shortened by up to `variance` percent (Spawn2::resetTimer; the legacy
    /// direction roll, rand()%50 &lt; 50, always shortens, and the rewrite keeps it).
    /// </summary>
    /// <summary>
    /// An NPC dies: it is replaced by its corpse (what its loot table gives, loot rights to
    /// <paramref name="looterId"/>), and its spawn point counts down to the next one.
    /// </summary>
    public bool Kill(int npcId, int? looterId = null)
    {
        if (!_entities.TryGetValue(npcId, out var npc) || npc.IsPlayer || npc.IsCorpse)
            return false;
        _entities.Remove(npcId);
        _events.Add(new Removed(npcId));
        if (npc.Spawn is { } spawn)
            _respawns.Add((spawn, _time + RespawnDelay(spawn)));
        if (npc.Npc is { } template)
            AddCorpse(npc, template, looterId);
        return true;
    }

    private void AddCorpse(Entity npc, NpcTemplate template, int? looterId)
    {
        var (items, coins) = Loot?.Roll(template.LoottableId, _random) ?? (new List<LootDrop>(), Coins.None);
        var corpse = Add(npc.Name + "'s_corpse", false, npc.Race, npc.Gender, npc.Level, npc.Size, npc.Position, npc.Heading);
        bool empty = items.Count == 0 && coins.IsZero;
        corpse.Corpse = new CorpseData
        {
            Items = items,
            Coins = coins,
            DecayAt = _time + (empty ? EmptyCorpseRotSeconds : npc.Level >= 55 ? ExtendedCorpseRotSeconds : CorpseRotSeconds),
            FreeForAllAt = _time + FreeForAllSeconds,
        };
        if (looterId is int id)
            corpse.Corpse.Rights.Add(id);
        corpse.Fighter = npc.Fighter;
        corpse.Hp = 0;
        _events.Add(new Spawned(corpse));
    }

    public IReadOnlyList<LootDrop>? CorpseItems(int corpseId) => _entities.GetValueOrDefault(corpseId)?.Corpse?.Items;

    /// <summary>
    /// Opens a corpse (legacy OP_LootRequest): in reach, with the loot rights (or free for all), one
    /// looter at a time. The coins go to the player at once, as in the legacy client.
    /// </summary>
    public void OpenLoot(int playerId, int corpseId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || !_entities.TryGetValue(corpseId, out var body) || body.Corpse is not { } corpse)
            return;
        if (Distance2(player.Position, body.Position) > LootReach * LootReach)
        {
            _events.Add(new Told(playerId, "You are too far away to loot that corpse."));
            return;
        }
        if (corpse.Owner is { } owner && !string.Equals(owner, player.Name, StringComparison.OrdinalIgnoreCase))
        {
            _events.Add(new Told(playerId, "You may not loot this corpse."));
            return;
        }
        if (_time < corpse.FreeForAllAt && corpse.Rights.Count > 0 && !corpse.Rights.Contains(playerId))
        {
            _events.Add(new Told(playerId, "You may not loot this corpse at this time."));
            return;
        }
        if (corpse.Looter is int other && other != playerId && _entities.ContainsKey(other))
        {
            _events.Add(new Told(playerId, "Someone is already looting this corpse."));
            return;
        }
        corpse.Looter = playerId;
        if (!corpse.Coins.IsZero && player.Inventory is { } inventory)
        {
            inventory.Coins = inventory.Coins.Add(corpse.Coins);
            _events.Add(new Told(playerId, $"You receive {corpse.Coins} from the corpse."));
            corpse.Coins = Coins.None;
            _events.Add(new InventoryChanged(playerId));
            SavePlayerCorpse(body);
        }
        _events.Add(new LootShown(playerId, corpseId, corpse.Items.ToList()));
    }

    /// <summary>
    /// Moves or swaps the items of two inventory slots (legacy OP_MoveItem, which the Trilogy client
    /// validated itself): an item going to a worn slot must fit it (slots bitmask) and the player's
    /// class and race; no two-handed weapon with something in the off hand. A change to the worn
    /// slots rebuilds the fighter (armour class, weapon).
    /// </summary>
    public void MoveItem(int playerId, int from, int to)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Inventory is not { } inventory
            || !ValidSlot(inventory, from) || !ValidSlot(inventory, to) || from == to || inventory.ItemAt(from) == 0)
            return;
        if (player.Trade is { } trade && (trade.Slots.Contains(from) || trade.Slots.Contains(to)))
        {
            _events.Add(new Told(playerId, "You cannot move an item you are trading."));
            _events.Add(new InventoryChanged(playerId));
            return;
        }
        int moving = inventory.ItemAt(from), swapped = inventory.ItemAt(to);
        string? refusal = CanWear(player, moving, to) ?? CanWear(player, swapped, from)
            ?? CanBag(inventory, moving, to) ?? CanBag(inventory, swapped, from);
        // A bag that holds items only moves between general slots (its contents follow it).
        if (refusal is null && (inventory.BagHasItems(from) && !PlayerInventory.IsGeneral(to) || inventory.BagHasItems(to) && !PlayerInventory.IsGeneral(from)))
            refusal = "You cannot move a bag that has items in it there.";
        if (refusal is null && WouldClashTwoHanded(inventory, from, to))
            refusal = "You cannot use a two-handed weapon with something in your off hand.";
        if (refusal is not null)
        {
            _events.Add(new Told(playerId, refusal));
            _events.Add(new InventoryChanged(playerId)); // puts the client's view back
            return;
        }
        int movingCharges = inventory.ChargesAt(from), swappedCharges = inventory.ChargesAt(to);
        inventory.Set(to, moving, movingCharges);
        inventory.Set(from, swapped, swappedCharges);
        if (PlayerInventory.IsGeneral(from) && PlayerInventory.IsGeneral(to))
            for (int c = 0; c < PlayerInventory.BagCells; c++)
            {
                int a = PlayerInventory.BagSlot(from, c), b = PlayerInventory.BagSlot(to, c);
                int ai = inventory.ItemAt(a), ac = inventory.ChargesAt(a);
                inventory.Set(a, inventory.ItemAt(b), inventory.ChargesAt(b));
                inventory.Set(b, ai, ac);
            }
        _events.Add(new InventoryChanged(playerId));
        if (from < PlayerInventory.FirstGeneral || to < PlayerInventory.FirstGeneral)
            RebuildFighter(player);
    }

    /// <summary>A worn or general slot, or a cell of a bag that is there and has that many cells.</summary>
    private bool ValidSlot(PlayerInventory inventory, int slot) =>
        slot is >= 0 and < PlayerInventory.Slots
        || PlayerInventory.IsBagSlot(slot) && Items?.Get(inventory.Items[PlayerInventory.BagOf(slot)]) is { IsContainer: true } bag
           && PlayerInventory.CellOf(slot) < bag.BagSlots;

    /// <summary>Why this item cannot go into that bag cell (no bag in a bag, sizes), or null.</summary>
    private string? CanBag(PlayerInventory inventory, int itemId, int slot)
    {
        if (itemId == 0 || !PlayerInventory.IsBagSlot(slot))
            return null;
        var item = Items?.Get(itemId);
        if (item is { IsContainer: true })
            return "You cannot put a bag in a bag.";
        var bag = Items?.Get(inventory.Items[PlayerInventory.BagOf(slot)]);
        return item is not null && bag is not null && item.Size > bag.BagSize ? "That item is too large for the bag." : null;
    }

    private const int PrimarySlot = 13, SecondarySlot = 14;

    /// <summary>Why this item cannot go to that slot, or null (general slots take anything).</summary>
    private string? CanWear(Entity player, int itemId, int slot)
    {
        if (itemId == 0 || slot >= PlayerInventory.FirstGeneral)
            return null;
        if (Items?.Get(itemId) is not { } item)
            return "You cannot equip that there.";
        if (!item.FitsSlot(slot))
            return "You cannot equip that there.";
        if (!item.UsableByClass(player.Fighter.Class))
            return "Your class cannot use that item.";
        if (!item.UsableByRace(player.Race))
            return "Your race cannot use that item.";
        return null;
    }

    private bool WouldClashTwoHanded(PlayerInventory inventory, int from, int to)
    {
        int Slot(int i) => i == from ? inventory.Items[to] : i == to ? inventory.Items[from] : inventory.Items[i];
        return Items?.Get(Slot(PrimarySlot)) is { TwoHanded: true } && Slot(SecondarySlot) != 0;
    }

    /// <summary>Takes one item off the corpse being looted, into the first free general slot.</summary>
    public void TakeLoot(int playerId, int corpseId, int index)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Inventory is not { } inventory
            || !_entities.TryGetValue(corpseId, out var body) || body.Corpse is not { } corpse
            || corpse.Looter != playerId || index < 0 || index >= corpse.Items.Count)
            return;
        var item = corpse.Items[index];
        // A player's own item goes back where it was, when that slot is free (a bag cell needs its bag).
        int slot = item.Slot >= 0 && inventory.ItemAt(item.Slot) == 0 && ValidSlot(inventory, item.Slot)
            ? item.Slot : inventory.FreeSlotFor(Items?.Get(item.ItemId), id => Items?.Get(id));
        if (slot < 0)
        {
            _events.Add(new Told(playerId, "There is no room in your inventory for that item."));
            return;
        }
        corpse.Items.RemoveAt(index);
        inventory.Set(slot, item.ItemId, item.Charges);
        _events.Add(new Told(playerId, $"You have looted a {Items?.Get(item.ItemId)?.Name ?? "item #" + item.ItemId}."));
        _events.Add(new InventoryChanged(playerId));
        SavePlayerCorpse(body);
        if (slot is >= 0 and < PlayerInventory.FirstGeneral)
            RebuildFighter(player);
        _events.Add(new LootShown(playerId, corpseId, corpse.Items.ToList()));
    }

    /// <summary>Closes the loot window; a corpse left empty goes away.</summary>
    public void CloseLoot(int playerId, int corpseId)
    {
        if (!_entities.TryGetValue(corpseId, out var body) || body.Corpse is not { } corpse || corpse.Looter != playerId)
            return;
        corpse.Looter = null;
        if (corpse.Items.Count == 0 && corpse.Coins.IsZero)
        {
            SavePlayerCorpse(body); // deletes a stored player corpse
            _entities.Remove(corpseId);
            _events.Add(new Removed(corpseId));
        }
    }

    private void RotCorpses()
    {
        foreach (var body in _entities.Values.Where(e => e.Corpse is { } c && _time >= c.DecayAt).ToList())
        {
            if (body.Corpse!.Owner is not null && body.Corpse.DbId != 0)
                PlayerCorpses?.Delete(body.Corpse.DbId);
            _entities.Remove(body.Id);
            _events.Add(new Removed(body.Id));
        }
    }

    public double RespawnDelay(SpawnPoint spawn)
    {
        double seconds = spawn.RespawnSeconds;
        if (spawn.Variance > 0)
            seconds -= seconds * _random.Next(spawn.Variance) / 100.0;
        return seconds;
    }

    /// <summary>Spawns, deaths, aggro and zone line crossings since the last call.</summary>
    public IReadOnlyList<ZoneEvent> DrainEvents()
    {
        var events = _events.ToList();
        _events.Clear();
        return events;
    }

    /// <summary>Advances the zone by <paramref name="seconds"/> and returns what moved since the last tick.</summary>
    public IReadOnlyList<Entity> Tick(float seconds)
    {
        _time += seconds;
        TickCount++;
        LoadPlayerCorpses();

        foreach (var door in _doors.Values)
            if (door.Open && _time - door.LastClick >= DoorCloseSeconds)
                SetDoor(door, false);

        for (int i = _respawns.Count - 1; i >= 0; i--)
            if (_respawns[i].At <= _time)
            {
                var spawn = _respawns[i].Spawn;
                _respawns.RemoveAt(i);
                SpawnAt(spawn);
            }

        foreach (var e in _entities.Values.ToList())
        {
            if (e.IsPlayer || e.IsCorpse || Incapacitated(e))
                continue;
            if (e.Fleeing)
            {
                FleeStep(e, seconds);
                continue;
            }
            if (e.TargetId is int targetId)
            {
                Chase(e, targetId, seconds);
                continue;
            }
            e.ScanIn -= seconds;
            if (e.ScanIn <= 0)
            {
                e.ScanIn += AggroRules.ScanSeconds;
                if (FindAggroTarget(e) is { } target)
                {
                    e.TargetId = target.Id;
                    _events.Add(new Engaged(e.Id, target.Id));
                    continue;
                }
            }
            if (e.Walker is null)
                continue;
            if (e.PauseLeft > 0)
            {
                e.PauseLeft -= seconds;
                continue;
            }
            var before = e.Position;
            int waypoint = e.Walker.TargetIndex;
            bool arrived = e.Walker.Step(e.Speed * seconds);
            e.Position = e.Walker.Position;
            e.Heading = Heading(before, e.Position, e.Heading);
            e.Moved = true;
            if (arrived && e.Grid is not null)
                e.PauseLeft = Math.Max(e.Grid.Waypoints[waypoint].PauseSeconds, 0);
        }

        AdvanceCasting();
        Fight(seconds);
        NpcCasting();
        Emergencies();
        AdvanceWeather();
        CheckTrades();
        if (_time >= _nextRegen)
            TickBuffs(); // before the regeneration below moves _nextRegen: same 6 s tic
        Regenerate();
        RotCorpses();

        var moved = _entities.Values.Where(e => e.Moved).ToList();
        foreach (var e in moved)
            e.Moved = false;
        return moved;
    }

    private double _nextRegen = RegenSeconds;

    /// <summary>
    /// Melee for this tick: players auto-attacking their target within <see cref="PlayerReach"/>,
    /// engaged NPCs within <see cref="MeleeRange"/> of theirs, each at its own weapon delay.
    /// </summary>
    private void Fight(float seconds)
    {
        foreach (var e in _entities.Values.ToList())
        {
            if (!_entities.ContainsKey(e.Id) || Incapacitated(e) || !e.IsPlayer && (e.Cast is not null || e.Fleeing)) // slain earlier in this tick, stunned, mesmerized, casting, fleeing
                continue;
            int? targetId = e.IsPlayer ? (e.AutoAttack ? e.PlayerTargetId : null) : e.TargetId;
            if (targetId is not int tid)
            {
                e.SwingIn = Math.Max(0, e.SwingIn - seconds);
                continue;
            }
            if (!_entities.TryGetValue(tid, out var target) || target.IsCorpse)
            {
                if (e.IsPlayer)
                    e.AutoAttack = false;
                else
                    Disengage(e);
                continue;
            }
            e.SwingIn -= seconds;
            if (e.SwingIn > 0)
                continue;
            float reach = e.IsPlayer ? PlayerReach : MeleeRange + 2f;
            if (Distance2(e.Position, target.Position) > reach * reach)
            {
                e.SwingIn = 0;
                if (e.IsPlayer && _time - e.TooFarToldAt >= e.Fighter.DelaySeconds)
                {
                    e.TooFarToldAt = _time;
                    _events.Add(new Told(e.Id, TooFarMessage));
                }
                continue;
            }
            e.SwingIn += e.Fighter.DelaySeconds;
            Swing(e, target);
        }
    }

    private void Swing(Entity attacker, Entity defender)
    {
        var result = Melee.Swing(attacker.Fighter, defender.Fighter, _random, defender.Sitting);
        attacker.LastCombatTime = defender.LastCombatTime = _time;
        if (result.Hit)
            defender.Hp -= result.Damage;
        _events.Add(new Swung(attacker.Id, defender.Id, result.Hit ? result.Damage : 0, defender.HpPercent));
        if (attacker.IsPlayer)
        {
            // Client::GetWeaponSkill: every swing may raise the weapon's skill and offense.
            CheckAddSkill(attacker, attacker.Fighter.WeaponSkill, -10);
            CheckAddSkill(attacker, SkillCaps.Offense, -10);
        }
        if (defender.IsPlayer && result.Hit && result.Damage > 0)
            CheckAddSkill(defender, SkillCaps.Defense, -10); // Client::Damage: melee damage only
        AfterHarm(attacker, defender, result.Hit ? result.Damage : 0);
    }

    /// <summary>
    /// What follows damage (or a hostile spell) from <paramref name="attacker"/>: an NPC counts the
    /// damage and fights back, a player sees their health and may lose their spell, and whoever
    /// reaches 0 hit points dies.
    /// </summary>
    private void AfterHarm(Entity attacker, Entity defender, int damage)
    {
        if (damage > 0 && attacker.IsPlayer && !defender.IsPlayer)
        {
            defender.DamageBy ??= new Dictionary<int, int>();
            defender.DamageBy[attacker.Id] = defender.DamageBy.GetValueOrDefault(attacker.Id) + damage;
        }
        if (!defender.IsPlayer && defender.TargetId is null && attacker.IsPlayer)
        {
            defender.TargetId = attacker.Id; // hit by a player: it fights back
            _events.Add(new Engaged(defender.Id, attacker.Id));
        }
        if (damage > 0 && defender.Bonuses.Mezzed)
            BreakMez(defender);
        if (defender.IsPlayer && damage > 0)
        {
            _events.Add(new HealthChanged(defender.Id, Math.Max(defender.Hp, 0), defender.Fighter.MaxHp));
            if (defender.Hp > 0)
                CheckChanneling(defender);
        }
        if (defender.Hp > 0)
            return;
        _events.Add(new Slain(defender.Id, defender.Name, attacker.Id, attacker.Name));
        if (defender.IsPlayer)
        {
            PlayerDied(defender);
        }
        else
        {
            int? looter = RewardKill(defender);
            Kill(defender.Id, looter);
        }
    }

    /// <summary>
    /// NPC::Death: the player who did the most damage gets NPC level² × 75 experience (capped at a
    /// tenth of their level), unless the NPC cons green to them or is a merchant or banker, and the
    /// faction hits of the NPC's faction list.
    /// Groups and pets are not modelled yet.
    /// </summary>
    /// <returns>The player who earned the kill (and the loot rights), if any.</returns>
    private int? RewardKill(Entity npc)
    {
        if (npc.DamageBy is null || npc.DamageBy.Count == 0 || npc.Npc is null)
            return null;
        var (killerId, _) = npc.DamageBy.OrderByDescending(kv => kv.Value).First();
        if (!_entities.TryGetValue(killerId, out var player) || !player.IsPlayer)
            return null;
        if (Factions is DatabaseFactions db)
        {
            // HateList: the top hater's faction moves (SetFactionLevel), with their deity.
            foreach (var message in db.Kill(player, npc.Npc, player.Deity))
                _events.Add(new Told(player.Id, message));
            _events.Add(new FactionsChanged(player.Id));
        }
        if (ConsiderRules.LevelCon(player.Level, npc.Level) != ConColor.Green && npc.Npc.Combat.Class is not (BankerClass or MerchantClass))
        {
            // Group::SplitExp: the group members in the zone share it by level, (level + 5) / (sum of levels + 5 × members).
            var group = GroupHere(player);
            long total = group.Sum(m => (long)m.Level) + 5L * group.Count;
            foreach (var member in group)
            {
                uint share = group.Count == 1 ? Experience.ForKill(npc.Level) : (uint)(Experience.ForKill(npc.Level) * (long)(member.Level + 5) / total);
                uint gain = Experience.Capped(share, member.Level, member.Fighter.Class, member.Race);
                if (gain > 0)
                    SetExperience(member, member.Exp + gain);
            }
        }
        return killerId;
    }

    /// <summary>Client::SetEXP: new total, level from it, the legacy messages, fighter rebuilt on a level change.</summary>
    private void SetExperience(Entity player, uint exp)
    {
        if (exp == player.Exp)
            return;
        int level = Math.Clamp(Experience.LevelFor(exp, player.Fighter.Class, player.Race), 1, Experience.MaxLevel);
        _events.Add(new Told(player.Id, exp > player.Exp ? "You gain experience!!" : "You have lost experience."));
        player.Exp = exp;
        if (level != player.Level)
        {
            _events.Add(new Told(player.Id, level > player.Level
                ? $"You have gained a level! Welcome to level {level}!" : $"You have lost a level! Welcome to level {level}!"));
            player.Level = level;
            RebuildFighter(player);
            RecalculateMana(player);
        }
        _events.Add(new ExperienceChanged(player.Id, player.Exp, player.Level));
    }

    /// <summary>
    /// A slain player (Client::Death): every NPC after them lets go, they lose experience from level
    /// 6 (ExpLost), and come back at full health at their bind point, in this zone or another; where
    /// they entered the zone when the bind point is unknown. No corpse yet (it needs the inventory).
    /// </summary>
    private void PlayerDied(Entity player)
    {
        foreach (var npc in _entities.Values.Where(n => n.TargetId == player.Id).ToList())
            Disengage(npc);
        player.AutoAttack = false;
        player.PlayerTargetId = null;
        player.Sitting = false;
        player.Cast = null;
        CancelTrade(player.Id);
        if (player.BuffList.Count > 0)
        {
            player.BuffList.Clear(); // death takes every buff away
            UpdateBonuses(player);
        }
        uint loss = Experience.DeathLoss(player.Level, player.Exp);
        if (loss > 0)
            SetExperience(player, player.Exp - loss);
        MakePlayerCorpse(player);
        player.Hp = player.Fighter.MaxHp;
        _events.Add(new HealthChanged(player.Id, player.Hp, player.Fighter.MaxHp));
        SendToBind(player, DeathReason);
    }

    /// <summary>
    /// Every 6 s, players out of combat for a tic regain hit points: 1 per 10 levels (at least 1),
    /// twice that sitting. A simplification of Client::CalcHPRegen, without items or race bonuses.
    /// Mana comes back every tic, in combat or not (Mob::DoManaRegen).
    /// </summary>
    private void Regenerate()
    {
        if (_time < _nextRegen)
            return;
        _nextRegen += RegenSeconds;
        foreach (var e in _entities.Values.Where(e => !e.IsCorpse).ToList())
        {
            if (e.IsPlayer)
                RegenerateMana(e);
            if (e.Hp >= e.Fighter.MaxHp || e.Hp <= 0)
                continue;
            // Mob::DoHPRegen: every tic, in combat or not; an NPC's hp_regen_rate when it is higher.
            int amount = CombatFormulas.LevelRegen(e.Level, e.Race, e.Sitting);
            if (e.Npc is { } npc)
                amount = Math.Max(amount, npc.RegenRate);
            e.Hp = Math.Min(e.Fighter.MaxHp, e.Hp + amount);
            if (e.IsPlayer)
                _events.Add(new HealthChanged(e.Id, e.Hp, e.Fighter.MaxHp));
        }
    }

    private Entity? FindAggroTarget(Entity npc)
    {
        Entity? best = null;
        float bestD2 = float.MaxValue;
        foreach (var p in _entities.Values)
        {
            if (!p.IsPlayer || p.Hidden || p.Bonuses.Invisible || p.Bonuses.InvisibleToUndead && npc.Npc!.Undead)
                continue;
            var r2 = AggroRules.RadiusSquared(Factions.Standing(p, npc.Npc!), p.Level, npc.Level, p.Sitting, npc.Npc!.Undead);
            float d2 = Distance2(p.Position, npc.Position);
            if (r2 is null || d2 > r2 || d2 >= bestD2)
                continue;
            if (Mesh is not null && !Mesh.LineOfSight(Eye(npc.Position), Eye(p.Position)))
                continue;
            best = p;
            bestD2 = d2;
        }
        return best;
    }

    /// <summary>Stunned or mesmerized: no moving, no fighting, no noticing anyone.</summary>
    private bool Incapacitated(Entity e) => e.StunnedUntil > _time || e.Bonuses.Mezzed;

    private void Chase(Entity npc, int targetId, float seconds)
    {
        if (!_entities.TryGetValue(targetId, out var target))
        {
            Disengage(npc);
            return;
        }
        if (npc.Bonuses.Rooted || npc.Cast is not null)
            return; // rooted: fights whoever is in reach, goes nowhere; casting: stands still
        float dx = target.Position.X - npc.Position.X, dy = target.Position.Y - npc.Position.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= MeleeRange)
            return;
        float step = MathF.Min(npc.Npc!.RunUnitsPerSecond * seconds, distance - MeleeRange);
        float x = npc.Position.X + dx / distance * step, y = npc.Position.Y + dy / distance * step;
        float z = Mesh?.GroundZ(x, y, npc.Position.Z, 5f) is float g && g >= npc.Position.Z - 15f ? g : npc.Position.Z;
        var before = npc.Position;
        npc.Position = new Vec3(x, y, z);
        npc.Heading = Heading(before, npc.Position, npc.Heading);
        npc.Moved = true;
    }

    private void Disengage(Entity npc)
    {
        npc.TargetId = null;
        npc.Fleeing = false;
        npc.FleeTo = null;
        npc.SpellCredit = 0; // NPC::CheckMyLosStatus / leash: a fresh start
        // Back to the grid from where the chase ended (the legacy NPC walks home, then resumes).
        if (npc.Grid is not null)
        {
            npc.Walker = new WaypointWalker(npc.Grid.Waypoints.Select(w => w.Position).ToList(), Mesh, npc.Grid.Type, ClosestWaypoint(npc.Grid, npc.Position));
            npc.Moved = true;
        }
    }

    private void SpawnAt(SpawnPoint spawn)
    {
        if (Pick(spawn.Candidates, _random) is not { } npc)
            return;
        var start = spawn.Position;
        if (Mesh?.GroundZ(start.X, start.Y, start.Z, 5f) is float ground)
            start = start with { Z = ground };
        var entity = Add(npc.Name, false, npc.Race, npc.Gender, npc.Level, npc.Size, start, spawn.Heading);
        entity.Npc = npc;
        entity.Spawn = spawn;
        entity.Fighter = Combatant.ForNpc(npc);
        entity.Hp = entity.Fighter.MaxHp;
        entity.Speed = npc.WalkUnitsPerSecond;
        entity.ScanIn = (float)_random.NextDouble() * AggroRules.ScanSeconds; // legacy: random first scan
        if (spawn.GridId > 0 && _grids.TryGetValue(spawn.GridId, out var grid) && grid.Waypoints.Count >= 2)
        {
            entity.Grid = grid;
            entity.Walker = new WaypointWalker(grid.Waypoints.Select(w => w.Position).ToList(), Mesh, grid.Type, ClosestWaypoint(grid, start));
            entity.Position = entity.Walker.Position;
        }
        _events.Add(new Spawned(entity));
    }

    private Entity Add(string name, bool isPlayer, int race, int gender, int level, float size, Vec3 position, float heading)
    {
        var e = new Entity(_nextId++, name, isPlayer, race, gender, level, size, position, heading);
        _entities[e.Id] = e;
        return e;
    }

    /// <summary>Weighted by spawnentry.chance; a group whose chances are all 0 spawns its first NPC.</summary>
    private static NpcTemplate? Pick(IReadOnlyList<(NpcTemplate Npc, int Chance)> candidates, Random random)
    {
        if (candidates.Count == 0)
            return null;
        int total = candidates.Sum(c => Math.Max(c.Chance, 0));
        if (total == 0)
            return candidates[0].Npc;
        int roll = random.Next(total);
        foreach (var (npc, chance) in candidates)
        {
            roll -= Math.Max(chance, 0);
            if (roll < 0)
                return npc;
        }
        return candidates[^1].Npc;
    }

    private static int ClosestWaypoint(Grid grid, Vec3 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < grid.Waypoints.Count; i++)
        {
            float d = Distance2(grid.Waypoints[i].Position with { Z = p.Z }, p);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }
        return best;
    }

    private static Vec3 Eye(Vec3 p) => p with { Z = p.Z + 5f };

    private static float Distance2(Vec3 a, Vec3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);

    // Degrees; 0 faces +Y.
    private static float Heading(Vec3 from, Vec3 to, float previous)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        return dx * dx + dy * dy < 1e-6f ? previous : MathF.Atan2(dx, dy) * 180f / MathF.PI;
    }
}
