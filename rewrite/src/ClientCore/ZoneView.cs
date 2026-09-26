using System;
using System.Collections.Generic;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.ClientCore
{
    /// <summary>
    /// What the client knows about the zone: every entity, with a short history of positions so it
    /// can be drawn smoothly at the frame rate from 20 Hz server updates (entity interpolation,
    /// rendered <see cref="InterpolationDelay"/> seconds in the past).
    /// </summary>
    public sealed class ZoneView
    {
        public const double InterpolationDelay = 0.1;
        private const int MaxSnapshots = 20;

        public sealed class EntityView
        {
            internal EntityView(EntitySpawn spawn, double now)
            {
                Spawn = spawn;
                Snapshots.Add((now, new Vec3(spawn.X, spawn.Y, spawn.Z), spawn.Heading));
            }

            public EntitySpawn Spawn { get; }
            public int Id => Spawn.Id;
            public string ModelCode => ModelCodes.For(Spawn.Race, Spawn.Gender);
            /// <summary>Name as the Trilogy client shows it ("a_rat01" → "a rat").</summary>
            public string DisplayName => CombatText.DisplayName(Spawn.Name);
            /// <summary>Hit points in percent, from the last combat event about it.</summary>
            public int HpPercent { get; internal set; } = 100;
            public bool Sitting { get; internal set; }
            /// <summary>Colour from the last consider, null before (name plates use it).</summary>
            public ConColor? Con { get; internal set; }
            internal readonly List<(double Time, Vec3 Position, float Heading)> Snapshots = new List<(double, Vec3, float)>();
            internal uint LastTick;

            public Vec3 Latest => Snapshots[Snapshots.Count - 1].Position;
        }

        private readonly Dictionary<int, EntityView> _entities = new Dictionary<int, EntityView>();

        public ZoneView(ZoneEnterResponse entered, double now)
        {
            Zone = entered.Zone;
            YourEntityId = entered.YourEntityId;
            foreach (var e in entered.Entities)
                _entities[e.Id] = new EntityView(e, now);
        }

        public string Zone { get; }
        public int YourEntityId { get; }
        public IEnumerable<EntityView> Entities => _entities.Values;
        public int Count => _entities.Count;

        public event Action<EntityView>? Added;
        public event Action<int>? Removed;
        /// <summary>The door list arrived (just after entering the zone).</summary>
        public event Action<IReadOnlyCollection<DoorInfo>>? DoorsLoaded;
        /// <summary>A door opened or closed; the argument is its new state.</summary>
        public event Action<DoorInfo>? DoorChanged;

        private readonly Dictionary<int, DoorInfo> _doors = new Dictionary<int, DoorInfo>();
        public IReadOnlyCollection<DoorInfo> Doors => _doors.Values;

        public event Action<EntityView>? AppearanceChanged;

        public void Apply(EntityAppearance appearance)
        {
            if (!_entities.TryGetValue(appearance.EntityId, out var e) || e.Sitting == appearance.Sitting)
                return;
            e.Sitting = appearance.Sitting;
            AppearanceChanged?.Invoke(e);
        }

        /// <summary>
        /// An illusion: the entity is drawn again as another race, where it stands (its view is
        /// replaced: <see cref="Removed"/> then <see cref="Added"/>).
        /// </summary>
        public void Apply(EntityIllusion illusion, double now)
        {
            if (!_entities.TryGetValue(illusion.EntityId, out var old) || old.Spawn.Race == illusion.Race && old.Spawn.Gender == illusion.Gender)
                return;
            Redraw(old, old.Spawn with { Race = illusion.Race, Gender = illusion.Gender }, now);
        }

        /// <summary>New armour or helmet (OP_WearChange): the entity is drawn again with them, where it stands.</summary>
        public void Apply(EntityLooks looks, double now)
        {
            if (!_entities.TryGetValue(looks.EntityId, out var old) || old.Spawn.Texture == looks.Texture && old.Spawn.Helm == looks.Helm)
                return;
            Redraw(old, old.Spawn with { Texture = looks.Texture, Helm = looks.Helm }, now);
        }

        private void Redraw(EntityView old, EntitySpawn spawn, double now)
        {
            var at = old.Latest;
            var last = old.Snapshots[old.Snapshots.Count - 1];
            var view = new EntityView(spawn with { X = at.X, Y = at.Y, Z = at.Z, Heading = last.Heading }, now)
            {
                HpPercent = old.HpPercent, Sitting = old.Sitting, Con = old.Con, LastTick = old.LastTick,
            };
            _entities[view.Id] = view;
            Removed?.Invoke(view.Id);
            Added?.Invoke(view);
        }

        /// <summary>A swing: remembers the defender's health for the target window.</summary>
        public void Apply(CombatEvent swing)
        {
            if (_entities.TryGetValue(swing.DefenderId, out var defender))
                defender.HpPercent = swing.DefenderHpPercent;
        }

        /// <summary>
        /// Tab targeting: the NPCs within <paramref name="reach"/>, nearest first; the one after
        /// <paramref name="current"/> in that order (so repeated Tabs cycle), or the nearest. Null when none is near.
        /// </summary>
        public EntityView? NextNpc(Vec3 position, float reach, int? current = null)
        {
            var near = new List<(float Distance, EntityView Entity)>();
            foreach (var e in _entities.Values)
            {
                if (e.Spawn.IsPlayer || e.Spawn.IsCorpse || e.Id == YourEntityId)
                    continue;
                var p = e.Latest;
                float dx = p.X - position.X, dy = p.Y - position.Y, dz = p.Z - position.Z;
                float distance = dx * dx + dy * dy + dz * dz;
                if (distance <= reach * reach)
                    near.Add((distance, e));
            }
            if (near.Count == 0)
                return null;
            near.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            int index = near.FindIndex(n => n.Entity.Id == current);
            return near[(index + 1) % near.Count].Entity;
        }

        /// <summary>The closest corpse within <paramref name="reach"/>, or null.</summary>
        public EntityView? NearestCorpse(Vec3 position, float reach)
        {
            EntityView? best = null;
            float bestDistance = reach * reach;
            foreach (var e in _entities.Values)
            {
                if (!e.Spawn.IsCorpse)
                    continue;
                var p = e.Latest;
                float dx = p.X - position.X, dy = p.Y - position.Y, dz = p.Z - position.Z;
                float distance = dx * dx + dy * dy + dz * dz;
                if (distance <= bestDistance)
                {
                    best = e;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public void Apply(ZoneDoors doors)
        {
            _doors.Clear();
            foreach (var d in doors.Doors)
                _doors[d.Id] = d;
            DoorsLoaded?.Invoke(_doors.Values);
        }

        public void Apply(DoorState state)
        {
            if (!_doors.TryGetValue(state.DoorId, out var door) || door.Open == state.Open)
                return;
            _doors[state.DoorId] = door = door with { Open = state.Open };
            DoorChanged?.Invoke(door);
        }

        /// <summary>The closest door within <paramref name="reach"/> units of a position (invisible click spots included), or null.</summary>
        public DoorInfo? NearestDoor(Vec3 position, float reach)
        {
            DoorInfo? best = null;
            float bestDistance = reach * reach;
            foreach (var d in _doors.Values)
            {
                float dx = d.X - position.X, dy = d.Y - position.Y, dz = d.Z - position.Z;
                float distance = dx * dx + dy * dy + dz * dz;
                if (distance <= bestDistance)
                {
                    best = d;
                    bestDistance = distance;
                }
            }
            return best;
        }

        public EntityView? Get(int id) => _entities.TryGetValue(id, out var e) ? e : null;

        public void Apply(EntitySpawned spawned, double now)
        {
            var view = new EntityView(spawned.Entity, now);
            _entities[view.Id] = view;
            Added?.Invoke(view);
        }

        public void Apply(EntityRemoved removed)
        {
            if (_entities.Remove(removed.Id))
                Removed?.Invoke(removed.Id);
        }

        /// <summary>Updates are unreliable and may arrive out of order: older ticks than the last applied are dropped.</summary>
        public void Apply(EntityPositions update, double now)
        {
            foreach (var p in update.Positions)
            {
                if (!_entities.TryGetValue(p.Id, out var view) || update.Tick < view.LastTick)
                    continue;
                view.LastTick = update.Tick;
                view.Snapshots.Add((now, new Vec3(p.X, p.Y, p.Z), p.Heading));
                if (view.Snapshots.Count > MaxSnapshots)
                    view.Snapshots.RemoveAt(0);
            }
        }

        /// <summary>Where to draw an entity at <paramref name="now"/>: between the two snapshots around now - delay.</summary>
        public (Vec3 Position, float Heading) Interpolated(int id, double now)
        {
            if (!_entities.TryGetValue(id, out var view))
                return (default, 0);
            var s = view.Snapshots;
            double t = now - InterpolationDelay;
            if (t <= s[0].Time)
                return (s[0].Position, s[0].Heading);
            for (int i = s.Count - 1; i > 0; i--)
            {
                if (s[i - 1].Time <= t)
                {
                    if (t >= s[i].Time)
                        return (s[i].Position, s[i].Heading);
                    float k = (float)((t - s[i - 1].Time) / (s[i].Time - s[i - 1].Time));
                    var a = s[i - 1].Position;
                    var b = s[i].Position;
                    return (new Vec3(a.X + (b.X - a.X) * k, a.Y + (b.Y - a.Y) * k, a.Z + (b.Z - a.Z) * k), LerpAngle(s[i - 1].Heading, s[i].Heading, k));
                }
            }
            return (s[s.Count - 1].Position, s[s.Count - 1].Heading);
        }

        private static float LerpAngle(float a, float b, float k)
        {
            float d = ((b - a) % 360f + 540f) % 360f - 180f;
            return a + d * k;
        }
    }
}
