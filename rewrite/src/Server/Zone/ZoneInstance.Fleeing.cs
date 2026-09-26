using EQClassic.Shared.World;

namespace EQClassic.Server.Zone;

/// <summary>
/// NPCs running for their lives (NPC::Damage's emergency check, NPC::CheckMyFleeStatus, the flee
/// revert in NPC::Process): every 1.5 s an engaged NPC below its flee ratio and losing — its foe's
/// health over its own above 1.5 — heals itself if it is a caster, else flees, unless it is undead or
/// an ally of its faction stands close by. It runs to random places in sight (within 250 units, on
/// the ground, not more than 12 units up or down) until its health is back above a quarter.
/// NPC_ANTIFLEE_DISTANCE is not in the legacy headers; 100 units is assumed.
/// </summary>
public sealed partial class ZoneInstance
{
    public const float AntiFleeDistance = 100f;

    private void Emergencies()
    {
        foreach (var npc in _entities.Values.Where(e => !e.IsPlayer && !e.IsCorpse && e.TargetId is not null && e.OwnerId is null).ToList())
        {
            if (npc.Fleeing)
            {
                if (npc.HpPercent >= 26)
                {
                    npc.Fleeing = false; // revertFlee_timer
                    npc.FleeTo = null;
                }
                continue;
            }
            if (_time < npc.NextEmergency || npc.Cast is not null || Incapacitated(npc) || !_entities.TryGetValue(npc.TargetId!.Value, out var foe))
                continue;
            npc.NextEmergency = _time + RescueSeconds;
            int fleeRatio = Math.Clamp(20 - (npc.Level - foe.Level) * 2, 1, 20);
            if (npc.HpPercent > fleeRatio || (float)foe.HpPercent / Math.Max(1, npc.HpPercent) <= FleeRatio)
                continue;
            if (npc.SpellSet is { } set && Rescue(npc, foe, set))
                continue;
            if (npc.Npc is not { Undead: false } template)
                continue;
            bool ally = _entities.Values.Any(a => a != npc && !a.IsPlayer && !a.IsCorpse && template.PrimaryFaction > 0
                && a.Npc?.PrimaryFaction == template.PrimaryFaction && Distance2D(a.Position, npc.Position) < AntiFleeDistance);
            if (!ally)
                npc.Fleeing = true;
        }
    }

    /// <summary>One tick of running away: to the flee point, then another one.</summary>
    private void FleeStep(Entity npc, float seconds)
    {
        if (npc.FleeTo is not { } to || Distance2D(npc.Position, to) < 2f)
        {
            npc.FleeTo = FleePoint(npc);
            if (npc.FleeTo is null)
            {
                npc.Fleeing = false; // blockFlee: nowhere to go, it fights on
                return;
            }
            to = npc.FleeTo.Value;
        }
        float dx = to.X - npc.Position.X, dy = to.Y - npc.Position.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        float step = MathF.Min(npc.Npc!.RunUnitsPerSecond * seconds, distance);
        float x = npc.Position.X + dx / distance * step, y = npc.Position.Y + dy / distance * step;
        float z = Mesh?.GroundZ(x, y, npc.Position.Z, 5f) is float g && g >= npc.Position.Z - 15f ? g : npc.Position.Z + (to.Z - npc.Position.Z) * step / distance;
        var before = npc.Position;
        npc.Position = new Vec3(x, y, z);
        npc.Heading = Heading(before, npc.Position, npc.Heading);
        npc.Moved = true;
    }

    /// <summary>CheckMyFleeStatus's search: up to 100 random points, the range shrinking from 250.</summary>
    private Vec3? FleePoint(Entity npc)
    {
        var p = npc.Position;
        for (int loop = 0; loop < 100; loop++)
        {
            int range = 250 - loop * 2;
            float x = p.X + _random.Next(range) - _random.Next(range), y = p.Y + _random.Next(range) - _random.Next(range);
            if (Mesh is null)
                return new Vec3(x, y, p.Z);
            if (Mesh.GroundZ(x, y, p.Z + 12f, 24f) is not float z || MathF.Abs(z - p.Z) > 12f)
                continue;
            var to = new Vec3(x, y, z);
            if (Mesh.LineOfSight(Eye(p), Eye(to)))
                return to;
        }
        return null;
    }
}
