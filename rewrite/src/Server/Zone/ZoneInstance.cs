using EQClassic.Server.Combat;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Server.Zone;

/// <summary>
/// One running zone: its NPCs and players, advanced by <see cref="Tick"/>. No static or global
/// state, so any number of instances live in one process (the legacy zone processes crashed when
/// reused for another zone, on stale globals). Spawns, deaths and zone line crossings are queued
/// as events for the server to broadcast (<see cref="DrainEvents"/>).
/// </summary>
public sealed class ZoneInstance
{
    /// <summary>Fastest a player may move, units per second (EverQuest run speed with haste and SoW stays well under).</summary>
    public const float MaxPlayerSpeed = 70f;
    /// <summary>An engaged NPC stops this close to its target (melee reach in the rewrite until combat, M5).</summary>
    public const float MeleeRange = 10f;
    private const float MoveTolerance = 5f;
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

        public EntitySpawn ToSpawn() => new(Id, Name, IsPlayer, Race, Gender, Level, Size, Position.X, Position.Y, Position.Z, Heading);
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
    public sealed record ExperienceChanged(int PlayerId, uint Exp, int Level) : ZoneEvent;

    private const int BankerClass = 40, MerchantClass = 41;

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
    public ZoneCollisionMesh? Mesh { get; }
    public IFactionStandings Factions { get; set; } = new IndifferentFactions();
    /// <summary>The legacy zone header (cfg/&lt;zone&gt;.cfg), when known: sent to clients, and its underworld depth applies.</summary>
    public ZoneInfo? Info { get; init; }
    public const string UnderworldReason = "underworld";
    public uint TickCount { get; private set; }
    public IEnumerable<Entity> Entities => _entities.Values;
    public IEnumerable<(Door Door, bool Open)> Doors => _doors.Values.Select(d => (d.Data, d.Open));

    public ZoneInstance(ZoneData data, ZoneCollisionMesh? mesh = null, int seed = 0)
    {
        ShortName = data.ShortName;
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
        player.Fighter = fighter ?? progress?.FighterAt?.Invoke(level) ?? DefaultPlayer(level);
        player.Hp = hp is int h && h > 0 && h <= player.Fighter.MaxHp ? h : player.Fighter.MaxHp;
        player.EntryPosition = position;
        return player;
    }

    /// <summary>What a player brings besides position: experience, bind point, and their fighter at any level.</summary>
    public sealed record PlayerProgress(uint Exp, string BindZone, Vec3 Bind, Func<int, Combatant>? FighterAt);

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
            || !_entities.TryGetValue(entityId, out var other))
            return;
        var standing = Standing.Indifferent;
        if (other.Npc is { } npc)
        {
            standing = (Standing)(int)Factions.Standing(player, npc);
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
        if (on && (player.PlayerTargetId is not int t || !_entities.TryGetValue(t, out var target) || target.IsPlayer))
        {
            _events.Add(new Told(playerId, "You must first select a target for this command!"));
            return;
        }
        player.AutoAttack = on;
        player.SwingIn = 0; // the first swing goes out as soon as the target is in reach
        _events.Add(new Told(playerId, on ? "Auto attack is on." : "Auto attack is off."));
    }

    public bool RemovePlayer(int id)
    {
        if (!_entities.TryGetValue(id, out var e) || !e.IsPlayer)
            return false;
        _entities.Remove(id);
        foreach (var npc in _entities.Values.Where(n => n.TargetId == id))
            Disengage(npc);
        foreach (var other in _entities.Values.Where(p => p.PlayerTargetId == id))
            other.PlayerTargetId = null;
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
    /// since the last accepted move (teleport hacks, lag spikes are corrected, not trusted).
    /// Returns null when accepted, otherwise the reason (the position stays the last valid one).
    /// An accepted move onto a zone line queues <see cref="CrossedZoneLine"/>.
    /// </summary>
    public string? MovePlayer(int id, Vec3 to, float heading)
    {
        if (!_entities.TryGetValue(id, out var player) || !player.IsPlayer)
            return "not in zone";
        double elapsed = Math.Max(_time - player.LastMoveTime, 0.05);
        float distance = MathF.Sqrt(Distance2(player.Position, to));
        if (distance > MaxPlayerSpeed * elapsed + MoveTolerance)
            return $"moved {distance:0} units in {elapsed:0.00} s";
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
    public bool Kill(int npcId)
    {
        if (!_entities.TryGetValue(npcId, out var npc) || npc.IsPlayer)
            return false;
        _entities.Remove(npcId);
        _events.Add(new Removed(npcId));
        if (npc.Spawn is { } spawn)
            _respawns.Add((spawn, _time + RespawnDelay(spawn)));
        return true;
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
            if (e.IsPlayer)
                continue;
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

        Fight(seconds);
        Regenerate();

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
            if (!_entities.ContainsKey(e.Id)) // slain earlier in this tick
                continue;
            int? targetId = e.IsPlayer ? (e.AutoAttack ? e.PlayerTargetId : null) : e.TargetId;
            if (targetId is not int tid)
            {
                e.SwingIn = Math.Max(0, e.SwingIn - seconds);
                continue;
            }
            if (!_entities.TryGetValue(tid, out var target))
            {
                if (e.IsPlayer)
                    SetTarget(e.Id, null);
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
        {
            defender.Hp -= result.Damage;
            if (attacker.IsPlayer && !defender.IsPlayer)
            {
                defender.DamageBy ??= new Dictionary<int, int>();
                defender.DamageBy[attacker.Id] = defender.DamageBy.GetValueOrDefault(attacker.Id) + result.Damage;
            }
        }
        _events.Add(new Swung(attacker.Id, defender.Id, result.Hit ? result.Damage : 0, defender.HpPercent));
        if (!defender.IsPlayer && defender.TargetId is null)
        {
            defender.TargetId = attacker.Id; // hit by a player: it fights back
            _events.Add(new Engaged(defender.Id, attacker.Id));
        }
        if (defender.IsPlayer && result.Hit)
            _events.Add(new HealthChanged(defender.Id, Math.Max(defender.Hp, 0), defender.Fighter.MaxHp));
        if (defender.Hp > 0)
            return;
        _events.Add(new Slain(defender.Id, defender.Name, attacker.Id, attacker.Name));
        if (defender.IsPlayer)
        {
            PlayerDied(defender);
        }
        else
        {
            RewardKill(defender);
            Kill(defender.Id);
        }
    }

    /// <summary>
    /// NPC::Death: the player who did the most damage gets NPC level² × 75 experience (capped at a
    /// tenth of their level), unless the NPC cons green to them or is a merchant or banker.
    /// Groups and pets are not modelled yet.
    /// </summary>
    private void RewardKill(Entity npc)
    {
        if (npc.DamageBy is null || npc.Npc is null || npc.Npc.Combat.Class is BankerClass or MerchantClass)
            return;
        var (killerId, _) = npc.DamageBy.OrderByDescending(kv => kv.Value).First();
        if (!_entities.TryGetValue(killerId, out var player) || !player.IsPlayer)
            return;
        if (ConsiderRules.LevelCon(player.Level, npc.Level) == ConColor.Green)
            return;
        uint gain = Experience.Capped(Experience.ForKill(npc.Level), player.Level, player.Fighter.Class, player.Race);
        if (gain > 0)
            SetExperience(player, player.Exp + gain);
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
            if (player.Progress?.FighterAt is { } rebuild)
            {
                player.Fighter = rebuild(level);
                player.Hp = Math.Min(player.Hp, player.Fighter.MaxHp);
                _events.Add(new HealthChanged(player.Id, player.Hp, player.Fighter.MaxHp));
            }
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
        uint loss = Experience.DeathLoss(player.Level, player.Exp);
        if (loss > 0)
            SetExperience(player, player.Exp - loss);
        player.Hp = player.Fighter.MaxHp;
        _events.Add(new HealthChanged(player.Id, player.Hp, player.Fighter.MaxHp));
        var bind = player.Progress is { BindZone: { Length: > 0 } zone } p ? (Zone: zone, Position: p.Bind) : (Zone: ShortName, Position: player.EntryPosition);
        if (!string.Equals(bind.Zone, ShortName, StringComparison.OrdinalIgnoreCase))
        {
            _events.Add(new CrossedZoneLine(player.Id, new ZoneLine(0, player.Position, 0, bind.Zone, bind.Position), bind.Position));
            return;
        }
        player.Position = bind.Position;
        player.LastMoveTime = _time;
        player.Moved = true;
        _events.Add(new Teleported(player.Id, bind.Position, DeathReason));
    }

    /// <summary>
    /// Every 6 s, players out of combat for a tic regain hit points: 1 per 10 levels (at least 1),
    /// twice that sitting. A simplification of Client::CalcHPRegen, without items or race bonuses.
    /// </summary>
    private void Regenerate()
    {
        if (_time < _nextRegen)
            return;
        _nextRegen += RegenSeconds;
        foreach (var p in _entities.Values)
        {
            if (!p.IsPlayer || p.Hp >= p.Fighter.MaxHp || _time - p.LastCombatTime < RegenSeconds)
                continue;
            int amount = Math.Max(1, p.Level / 10) * (p.Sitting ? 2 : 1);
            p.Hp = Math.Min(p.Fighter.MaxHp, p.Hp + amount);
            _events.Add(new HealthChanged(p.Id, p.Hp, p.Fighter.MaxHp));
        }
    }

    private Entity? FindAggroTarget(Entity npc)
    {
        Entity? best = null;
        float bestD2 = float.MaxValue;
        foreach (var p in _entities.Values)
        {
            if (!p.IsPlayer)
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

    private void Chase(Entity npc, int targetId, float seconds)
    {
        if (!_entities.TryGetValue(targetId, out var target))
        {
            Disengage(npc);
            return;
        }
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
