using EQClassic.Server.Spells;
using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>An npc_spells row: the spells of a caster class from a level on (−1: none).</summary>
public sealed record NpcSpellSet(int Class, int Level, int Buff1, int Buff2, int Heal, int Gate, IReadOnlyList<int> Debuffs)
{
    public const int None = 50000;
}

public interface INpcSpellSource
{
    /// <summary>Database::LoadNPCSpells: the row of the class with the highest level not above the NPC's, or null.</summary>
    NpcSpellSet? For(int npcClass, int level);
}

public sealed class InMemoryNpcSpellSource : INpcSpellSource
{
    public List<NpcSpellSet> Rows { get; } = new();
    public NpcSpellSet? For(int npcClass, int level) =>
        Rows.Where(r => r.Class == npcClass && r.Level <= level).OrderByDescending(r => r.Level).FirstOrDefault();
}

public sealed class MySqlNpcSpellSource : INpcSpellSource
{
    private readonly List<NpcSpellSet> _rows = new();

    public MySqlNpcSpellSource(string connectionString)
    {
        using var connection = new MySqlConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT class, level, buff1, buff2, heal, gate, debuff1, debuff2, debuff3, debuff4 FROM npc_spells";
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                int I(int i) => Convert.ToInt32(r.GetValue(i)) is var v && v != NpcSpellSet.None ? v : -1;
                _rows.Add(new NpcSpellSet(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), I(2), I(3), I(4), I(5), [I(6), I(7), I(8), I(9)]));
            }
        }
        catch (MySqlException)
        {
            // no npc_spells table: no NPC casts
        }
    }

    public NpcSpellSet? For(int npcClass, int level) =>
        _rows.Where(r => r.Class == npcClass && r.Level <= level).OrderByDescending(r => r.Level).FirstOrDefault();
}

/// <summary>
/// Caster NPCs after NPC::CheckMyOffenseCastStatus, CheckMyDefenseCastStatus, CheckMyDebuffRefundStatus
/// (Zone/Source/npc.cpp) and the low-health rescue in NPC::Damage (attack.cpp): an engaged caster tries a
/// debuff or nuke every 6 s, heals an ally of its faction at a quarter of its health every 3 s, and heals
/// itself when losing badly. Each spell spends one of its (level/10 + 3) credits, which come back slowly.
/// NPC fleeing and out-of-combat buffing are not ported yet; FLEE_RATIO and NPC_BUFF_RANGE, missing
/// from the legacy headers, are taken as 1.5 and 200 units.
/// </summary>
public sealed partial class ZoneInstance
{
    public const double OffenseCastSeconds = 6, DefenseCastSeconds = 3, RescueSeconds = 1.5, CreditSeconds = 6;
    public const float NpcSpellReach = 350f, NpcBuffRange = 200f, FleeRatio = 1.5f;

    public INpcSpellSource? NpcSpells { get; init; }

    /// <summary>NPC::NPC: whether it is a caster (spawns happen before the zone's sources are set, so this runs on its first tick).</summary>
    private void SetUpNpcCaster(Entity npc)
    {
        npc.SpellSetChecked = true;
        npc.SpellSet = npc.Npc is { } t ? NpcSpells?.For(t.Combat.Class, t.Level) : null;
        npc.SpellCredit = 0;
        npc.NextOffense = _time + OffenseCastSeconds;
        npc.NextDefense = _time + DefenseCastSeconds;
        npc.NextCredit = _time + CreditSeconds;
    }

    private int SpellCredits(Entity npc) => npc.Level / 10 + 3;

    /// <summary>The casting decisions of every caster NPC for this tick.</summary>
    private void NpcCasting()
    {
        foreach (var fresh in _entities.Values.Where(e => !e.IsPlayer && !e.IsCorpse && !e.SpellSetChecked).ToList())
            SetUpNpcCaster(fresh);
        foreach (var npc in _entities.Values.Where(e => e.SpellSet is not null && !e.IsCorpse).ToList())
        {
            var set = npc.SpellSet!;
            if (_time >= npc.NextCredit)
            {
                npc.NextCredit += CreditSeconds;
                if (npc.SpellCredit > 0 && _random.Next(1000) < (npc.TargetId is not null ? 75 : 150))
                    npc.SpellCredit--;
            }
            if (npc.TargetId is not int targetId || npc.Cast is not null || Incapacitated(npc) || !_entities.TryGetValue(targetId, out var victim))
                continue;
            if (_time >= npc.NextDefense)
            {
                npc.NextDefense += DefenseCastSeconds;
                if (set.Heal >= 0 && HealAlly(npc, set))
                    continue;
            }
            if (_time >= npc.NextOffense)
            {
                npc.NextOffense += OffenseCastSeconds;
                Offend(npc, victim, set);
            }
        }
    }

    /// <summary>NPC::Damage's rescue, once losing badly (see <see cref="Emergencies"/>): a heal on itself (the legacy gate did nothing for NPCs).</summary>
    private bool Rescue(Entity npc, Entity foe, NpcSpellSet set)
    {
        if (set.Heal < 0)
            return false;
        int left = SpellCredits(npc) - npc.SpellCredit;
        int chance = left <= 0 ? 700 : left * 1000 / SpellCredits(npc) + 700;
        if (_random.Next(1000) >= chance)
            return false;
        return NpcCast(npc, set.Heal, npc);
    }

    /// <summary>CheckMyDefenseCastStatus: an engaged NPC of the same faction at a quarter of its health or less, nearby.</summary>
    private bool HealAlly(Entity npc, NpcSpellSet set)
    {
        int chance = 1000 - 1000 * npc.SpellCredit / SpellCredits(npc) + 400;
        if (_random.Next(1000) >= chance)
            return false;
        var ally = _entities.Values.FirstOrDefault(a => a != npc && !a.IsPlayer && !a.IsCorpse && a.TargetId is not null && a.HpPercent <= 25
            && a.Npc?.PrimaryFaction == npc.Npc?.PrimaryFaction && Distance2D(a.Position, npc.Position) <= NpcBuffRange);
        return ally is not null && NpcCast(npc, set.Heal, ally);
    }

    /// <summary>CheckMyOffenseCastStatus: one of its four debuffs (up to 8 draws), not already on the victim, within 350 units and in sight.</summary>
    private void Offend(Entity npc, Entity victim, NpcSpellSet set)
    {
        int chance = (1000 - 1000 * npc.SpellCredit / SpellCredits(npc)) * (npc.SpellCredit == 0 ? 85 : 50) / 100;
        if (_random.Next(1000) >= chance || Distance2D(npc.Position, victim.Position) > NpcSpellReach
            || Mesh is not null && !Mesh.LineOfSight(Eye(npc.Position), Eye(victim.Position)))
            return;
        for (int draw = 0; draw < 8; draw++)
        {
            int id = set.Debuffs[_random.Next(set.Debuffs.Count)];
            if (id < 0 || SpellById(id) is not { } spell || victim.BuffList.Any(b => b.Spell.Id == id))
                continue;
            NpcCast(npc, id, victim);
            return;
        }
    }

    /// <summary>An NPC begins a spell (no fizzles for NPCs, no mana); it stands still and does not swing while casting.</summary>
    private bool NpcCast(Entity npc, int spellId, Entity target)
    {
        if (SpellById(spellId) is not { } spell || !IsSupported(spell))
            return false;
        npc.SpellCredit++;
        npc.Cast = new Casting(spell, target.Id, -1, npc.Position, _time + spell.CastTimeMs / 1000.0);
        _events.Add(new CastStarted(npc.Id, spell.Id, spell.Name, spell.CastTimeMs));
        if (spell.CastTimeMs <= 0)
            FinishCast(npc);
        return true;
    }
}
