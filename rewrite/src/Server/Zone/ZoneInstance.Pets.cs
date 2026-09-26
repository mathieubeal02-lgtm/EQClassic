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
