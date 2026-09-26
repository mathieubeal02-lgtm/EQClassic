using EQClassic.Server.Characters;
using EQClassic.Server.Zone;
using static EQClassic.Server.Combat.CombatFormulas;

namespace EQClassic.Server.Combat;

/// <summary>
/// One fighter's melee numbers, computed once from its stats (Mob::CombatToHit / CombatAvoidance /
/// CombatOffense / CombatMitigation in Zone/Source/attack.cpp), with the bonuses of the buffs on
/// them (AC, ATK, STR, STA, AGI, hit points, haste and slow). Item stat bonuses are not modelled
/// yet, nor dual wield, double attack, ripostes and the like.
/// </summary>
public sealed record Combatant(bool IsPlayer, int Level, int Class, int MaxHp, int Offense, int ToHit, int Avoidance, int Mitigation,
    int DamageBonus, int BaseDamage, float DelaySeconds)
{
    /// <summary>An NPC: skills at their class caps, damage from its DB min/max hit (NPC::Attack).</summary>
    public static Combatant ForNpc(NpcTemplate npc, Spells.StatBonuses? bonuses = null)
    {
        var b = bonuses ?? Spells.StatBonuses.None;
        var c = npc.Combat;
        int offenseSkill = SkillCaps.Cap(SkillCaps.Offense, c.Class, npc.Level);
        int weaponSkill = SkillCaps.NpcMelee(c.Class, npc.Level);
        return new Combatant(false, npc.Level, c.Class, Math.Max(1, c.Hp + b.Hp),
            Offense: NpcOffense(npc.Level, b.Str, c.Atk + b.Atk),
            ToHit: CombatFormulas.ToHit(offenseSkill, weaponSkill, c.Accuracy, true, npc.Level),
            Avoidance: NpcAvoidance(npc.Level, b.Agi, c.Avoidance),
            Mitigation: NpcMitigation(npc.Level, c.AC, 0, b.AC),
            DamageBonus: NpcDamageBonus(c.MinDamage, c.MaxDamage),
            BaseDamage: NpcBaseDamage(c.MinDamage, c.MaxDamage),
            DelaySeconds: WithAttackSpeed(c.DelaySeconds, b));
    }

    /// <summary>Haste shortens the delay between swings, slow lengthens it (percentages).</summary>
    public static float WithAttackSpeed(float delaySeconds, Spells.StatBonuses b) =>
        b.Haste == 0 && b.Slow == 0 ? delaySeconds : delaySeconds * (100f + b.Slow) / (100f + b.Haste);

    /// <summary>
    /// A player from their profile: skills and stats as saved, the main-hand weapon (slot 13) or bare
    /// hands, and the AC of the worn items (slots 0-21). Max HP: Client::CalcBaseHP (no item HP yet).
    /// </summary>
    public static Combatant ForPlayer(PlayerProfile p, IItemSource? items, Spells.StatBonuses? bonuses = null)
    {
        var b = bonuses ?? Spells.StatBonuses.None;
        p = p with { Str = p.Str + b.Str, Sta = p.Sta + b.Sta, Agi = p.Agi + b.Agi, Dex = p.Dex + b.Dex };
        var worn = Enumerable.Range(0, Math.Min(22, p.Inventory.Count))
            .Select(slot => items?.Get(p.Inventory[slot])).Where(i => i is not null).ToList();
        int itemAC = worn.Sum(i => i!.AC);
        var weapon = p.Inventory.Count > 13 ? items?.Get(p.Inventory[13]) : null;
        int weaponSkillId, damage, delay;
        bool twoHanded;
        if (weapon is { IsWeapon: true })
        {
            (weaponSkillId, damage, delay, twoHanded) = (weapon.Skill, Math.Max(1, weapon.Damage), weapon.Delay, weapon.TwoHanded);
        }
        else
        {
            (damage, delay) = Fists(p.Level, p.Class, p.Race);
            (weaponSkillId, twoHanded) = (SkillCaps.HandToHand, false);
        }
        int weaponSkill = p.Skill(weaponSkillId), defense = p.Skill(SkillCaps.Defense);
        int offense = ClientOffense(weaponSkill, p.Str, b.Atk, p.Class, p.Level);
        return new Combatant(true, p.Level, p.Class, ClientBaseHp(p.Level, p.Class, p.Sta) + b.Hp,
            Offense: offense,
            ToHit: CombatFormulas.ToHit(p.Skill(SkillCaps.Offense), weaponSkill, 0, false, 0),
            Avoidance: ClientAvoidance(defense, p.Agi, p.Level),
            Mitigation: ClientMitigation(p.Level, p.Class, p.Race, itemAC, b.AC, defense, p.Agi, 0),
            DamageBonus: ClientDamageBonus(p.Level, p.Class, twoHanded, delay),
            BaseDamage: damage,
            DelaySeconds: WithAttackSpeed(delay / 10f, b));
    }
}

public readonly record struct SwingResult(bool Hit, int Damage);

public static class Melee
{
    /// <summary>
    /// One main-hand swing (Mob::GetHitChance, NPC::Attack, Client::CalculateAttackDamage): a sitting
    /// defender is always hit, for the best d20 roll; players' damage gets the class multiplier.
    /// </summary>
    public static SwingResult Swing(Combatant attacker, Combatant defender, Random random, bool defenderSitting = false)
    {
        if (!defenderSitting)
        {
            // Legacy: rand()%100 <= round(chance×100) - 1, that is round(chance×100) times in 100.
            int percent = (int)(HitChance(attacker.ToHit, defender.Avoidance) * 100.0 + 0.5);
            if (random.Next(100) >= percent)
                return new SwingResult(false, 0);
        }
        int d20 = defenderSitting ? 20 : RollD20(random, attacker.Offense, defender.Mitigation);
        int damage = attacker.DamageBonus + MeleeDamage(d20, attacker.BaseDamage, 0);
        if (attacker.IsPlayer)
            damage = ClientDamageMultiplier(random, damage, attacker.Offense, attacker.Level, attacker.Class);
        return new SwingResult(true, damage);
    }
}
