using EQClassic.Server.Characters;
using EQClassic.Server.Zone;
using static EQClassic.Server.Combat.CombatFormulas;

namespace EQClassic.Server.Combat;

/// <summary>
/// One fighter's melee numbers, computed once from its stats (Mob::CombatToHit / CombatAvoidance /
/// CombatOffense / CombatMitigation in Zone/Source/attack.cpp). Buffs and item stat bonuses are
/// not modelled yet (0), nor dual wield, double attack, ripostes and the like.
/// </summary>
public sealed record Combatant(bool IsPlayer, int Level, int Class, int MaxHp, int Offense, int ToHit, int Avoidance, int Mitigation,
    int DamageBonus, int BaseDamage, float DelaySeconds)
{
    /// <summary>An NPC: skills at their class caps, damage from its DB min/max hit (NPC::Attack).</summary>
    public static Combatant ForNpc(NpcTemplate npc)
    {
        var c = npc.Combat;
        int offenseSkill = SkillCaps.Cap(SkillCaps.Offense, c.Class, npc.Level);
        int weaponSkill = SkillCaps.NpcMelee(c.Class, npc.Level);
        int strBonus = 0; // item and spell STR: none yet
        return new Combatant(false, npc.Level, c.Class, c.Hp,
            Offense: NpcOffense(npc.Level, strBonus, c.Atk),
            ToHit: CombatFormulas.ToHit(offenseSkill, weaponSkill, c.Accuracy, true, npc.Level),
            Avoidance: NpcAvoidance(npc.Level, 0, c.Avoidance),
            Mitigation: NpcMitigation(npc.Level, c.AC, 0, 0),
            DamageBonus: NpcDamageBonus(c.MinDamage, c.MaxDamage),
            BaseDamage: NpcBaseDamage(c.MinDamage, c.MaxDamage),
            DelaySeconds: c.DelaySeconds);
    }

    /// <summary>
    /// A player from their profile: skills and stats as saved, the main-hand weapon (slot 13) or bare
    /// hands, and the AC of the worn items (slots 0-21). Max HP: Client::CalcBaseHP (no item HP yet).
    /// </summary>
    public static Combatant ForPlayer(PlayerProfile p, IItemSource? items)
    {
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
        int offense = ClientOffense(weaponSkill, p.Str, 0, p.Class, p.Level);
        return new Combatant(true, p.Level, p.Class, ClientBaseHp(p.Level, p.Class, p.Sta),
            Offense: offense,
            ToHit: CombatFormulas.ToHit(p.Skill(SkillCaps.Offense), weaponSkill, 0, false, 0),
            Avoidance: ClientAvoidance(defense, p.Agi, p.Level),
            Mitigation: ClientMitigation(p.Level, p.Class, p.Race, itemAC, 0, defense, p.Agi, 0),
            DamageBonus: ClientDamageBonus(p.Level, p.Class, twoHanded, delay),
            BaseDamage: damage,
            DelaySeconds: delay / 10f);
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
