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
