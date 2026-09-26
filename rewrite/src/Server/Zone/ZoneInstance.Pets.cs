using EQClassic.Server.Combat;
using EQClassic.Server.Spells;
using EQClassic.Shared.World;

namespace EQClassic.Server.Zone;

/// <summary>
/// Pets (Mob::MakePet, the pet AI and commands): a summoning spell makes one pet from the pets table,
/// with a legacy name; it follows its owner, fights back, defends its owner when an NPC hits them,
/// attacks the owner's target when told, backs off or goes away when told. Its kills are its
/// owner's. It goes when its owner dies, leaves the zone or dismisses it.
/// </summary>
public sealed partial class ZoneInstance
{
    public const float PetFollowDistance = 15f;
    public const string AlreadyHavePetMessage = "You've already got a pet.";

    public IPetSource? Pets { get; init; }

    public enum PetOrder { Attack, BackOff, GetLost }

    private void SummonPet(Entity owner, Spell spell)
    {
        if (owner.PetId is int existing && _entities.ContainsKey(existing))
        {
            _events.Add(new Told(owner.Id, AlreadyHavePetMessage));
            return;
        }
        if (PetRules.TypeFor(spell.TeleportZone) is not int typeId || Pets?.Get(typeId) is not { } type)
            return;
        string name = PetRules.Names[_random.Next(PetRules.Names.Length)];
        var template = new NpcTemplate(0, name, type.Race, 2, type.Level, type.Size > 0 ? type.Size : 6f)
        {
            Combat = new NpcCombatStats(type.Class, Math.Max(1, type.MaxHp), type.MinDamage, type.MaxDamage),
        };
        var at = owner.Position with { X = owner.Position.X + 5f };
        var pet = Add(name, false, type.Race, 2, type.Level, template.Size, at, owner.Heading);
        pet.Npc = template;
        pet.Fighter = Combatant.ForNpc(template);
        pet.Hp = pet.Fighter.MaxHp;
        pet.OwnerId = owner.Id;
        pet.SpellSetChecked = true; // pets do not cast
        owner.PetId = pet.Id;
        _events.Add(new Spawned(pet));
    }

    /// <summary>SE_Charm: the NPC becomes the caster's pet, forgetting its foes, for as long as the spell lasts.</summary>
    private void Charm(Entity caster, Entity npc)
    {
        Disengage(npc);
        foreach (var other in _entities.Values.Where(n => n.TargetId == npc.Id && !n.IsPlayer).ToList())
            Disengage(other);
        npc.OwnerId = caster.Id;
        caster.PetId = npc.Id;
    }

    /// <summary>The charm fades (BuffFadeBySlot SE_Charm): the NPC is itself again and turns on its charmer.</summary>
    private void ReleaseCharm(Entity npc)
    {
        if (npc.OwnerId is not int ownerId)
            return;
        npc.OwnerId = null;
        if (_entities.TryGetValue(ownerId, out var owner))
        {
            if (owner.PetId == npc.Id)
                owner.PetId = null;
            if (!owner.IsCorpse && owner.IsPlayer)
            {
                npc.TargetId = owner.Id;
                _events.Add(new Engaged(npc.Id, owner.Id));
                return;
            }
        }
        Disengage(npc);
    }

    /// <summary>Breaks a charm by taking the spell off (dismissed, the charmer gone or dead).</summary>
    private void BreakCharm(Entity npc) =>
        RemoveBuffs(npc, npc.BuffList.Where(b => b.Spell.Effect.Contains((byte)SpellEffect.Charm)).ToList());

    /// <summary>/pet attack, back off, get lost.</summary>
    public void CommandPet(int ownerId, PetOrder order)
    {
        if (!_entities.TryGetValue(ownerId, out var owner) || owner.PetId is not int petId || !_entities.TryGetValue(petId, out var pet))
            return;
        switch (order)
        {
            case PetOrder.Attack:
                if (owner.PlayerTargetId is int t && t != petId && _entities.TryGetValue(t, out var target) && !target.IsPlayer && !target.IsCorpse)
                {
                    pet.TargetId = t;
                    _events.Add(new Told(ownerId, $"{pet.Name} says, 'Attacking {DisplayName(target.Name)} Master.'"));
                }
                break;
            case PetOrder.BackOff:
                pet.TargetId = null;
                _events.Add(new Told(ownerId, $"{pet.Name} says, 'Sorry, Master..calming down.'"));
                break;
            case PetOrder.GetLost:
                RemovePet(owner);
                break;
        }
    }

    private void RemovePet(Entity owner)
    {
        if (owner.PetId is int charmedId && _entities.TryGetValue(charmedId, out var charmed) && charmed.Bonuses.Charmed)
        {
            BreakCharm(charmed); // a charmed NPC is let go, not unmade
            owner.PetId = null;
            return;
        }
        if (owner.PetId is int petId && _entities.Remove(petId))
        {
            _events.Add(new Removed(petId));
            foreach (var npc in _entities.Values.Where(n => n.TargetId == petId).ToList())
                Disengage(npc);
        }
        owner.PetId = null;
    }

    /// <summary>A pet's tick: fight its target, else stay close to its owner. False when it is gone.</summary>
    private void PetStep(Entity pet, float seconds)
    {
        if (!_entities.TryGetValue(pet.OwnerId!.Value, out var owner) || owner.IsCorpse)
        {
            if (pet.Bonuses.Charmed)
            {
                BreakCharm(pet);
                return;
            }
            _entities.Remove(pet.Id);
            _events.Add(new Removed(pet.Id));
            return;
        }
        if (pet.TargetId is int t)
        {
            if (_entities.TryGetValue(t, out var target) && !target.IsCorpse && target != owner)
            {
                Chase(pet, t, seconds);
                return;
            }
            pet.TargetId = null;
        }
        float dx = owner.Position.X - pet.Position.X, dy = owner.Position.Y - pet.Position.Y;
        float distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance <= PetFollowDistance)
            return;
        float step = MathF.Min(pet.Npc!.RunUnitsPerSecond * seconds, distance - PetFollowDistance + 1f);
        float x = pet.Position.X + dx / distance * step, y = pet.Position.Y + dy / distance * step;
        float z = Mesh?.GroundZ(x, y, pet.Position.Z, 5f) is float g && g >= pet.Position.Z - 15f ? g : owner.Position.Z;
        var before = pet.Position;
        pet.Position = new Vec3(x, y, z);
        pet.Heading = Heading(before, pet.Position, pet.Heading);
        pet.Moved = true;
    }
}
