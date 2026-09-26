using EQClassic.Server.Spells;
using EQClassic.Shared.World;

namespace EQClassic.Server.Zone;

/// <summary>
/// Spell casting after Mob::CastSpell / SpellFinished / SpellOnTarget (Zone/Source/spells.cpp):
/// memorised gems, mana, fizzles, cast time, interruption by moving or by damage (channeling),
/// resists, and the instant hit point effects (direct damage and heals). Buffs, damage over time
/// and the other effects come later: such spells are refused before any mana is spent.
/// </summary>
public sealed partial class ZoneInstance
{
    public const int GemCount = 8;
    public const string FizzleMessage = "Your spell fizzles!";
    public const string InterruptedMessage = "Your spell is interrupted.";
    public const string NoManaMessage = "Insufficient mana to cast this spell!";
    public const string OutOfRangeMessage = "Your target is out of range, get closer!";
    public const string AlreadyCastingMessage = "You are already casting a spell!";
    public const string NeedTargetMessage = "You must first select a target for this spell!";
    public const string NotYetMessage = "That spell is not available yet.";

    /// <summary>What a player brings for casting: stats, skills, spell book, gems and saved mana.</summary>
    public sealed record PlayerMagic(int Wis, int Int, IReadOnlyList<int> Skills, IReadOnlyList<int> Book, IReadOnlyList<int> Gems, int? Mana = null)
    {
        public int Skill(int id) => id < Skills.Count ? Skills[id] : 0;
    }

    public sealed class Casting
    {
        internal Casting(Spell spell, int targetId, int gem, Vec3 from, double endsAt) =>
            (Spell, TargetId, Gem, From, EndsAt) = (spell, targetId, gem, from, endsAt);
        public Spell Spell { get; }
        public int TargetId { get; }
        public int Gem { get; }
        internal Vec3 From { get; }
        internal double EndsAt { get; }
    }

    public enum CastOutcome { Finished, Fizzled, Interrupted, Resisted }

    public sealed record CastStarted(int CasterId, int SpellId, string SpellName, int CastMs) : ZoneEvent;
    public sealed record CastEnded(int CasterId, int SpellId, CastOutcome Outcome) : ZoneEvent;
    /// <summary>A spell took effect on <paramref name="TargetId"/>; Amount is the hit point change (negative: damage).</summary>
    public sealed record SpellLanded(int CasterId, int TargetId, int SpellId, int Amount) : ZoneEvent;
    public sealed record ManaChanged(int PlayerId, int Mana, int MaxMana) : ZoneEvent;
    public sealed record GemsChanged(int PlayerId) : ZoneEvent;

    /// <summary>The spells of spdat.eff by id; null: nobody can cast.</summary>
    public IReadOnlyList<Spell>? Spells { get; init; }

    public Spell? SpellById(int id) => Spells is { } s && id >= 0 && id < s.Count && s[id].IsValid ? s[id] : null;

    /// <summary>Whether the rewrite can apply every effect of the spell (instant hit points on one target so far).</summary>
    public static bool IsSupported(Spell spell) =>
        !spell.IsBuff
        && spell.TargetType is not (SpellTarget.AECaster or SpellTarget.AETarget or SpellTarget.GroupV1 or SpellTarget.GroupV2)
        && spell.Effect.All(e => e is SpellEffect.Blank or SpellEffect.CurrentHp or SpellEffect.CurrentHpOnce);

    private void SetUpMagic(Entity player, PlayerMagic? magic)
    {
        player.Magic = magic;
        player.Book = magic?.Book.ToArray() ?? Array.Empty<int>();
        player.Gems = Enumerable.Range(0, GemCount).Select(i => magic is not null && i < magic.Gems.Count ? magic.Gems[i] : -1).ToArray();
        player.MaxMana = magic is null ? 0 : SpellRules.MaxMana(player.Fighter.Class, player.Level, magic.Wis, magic.Int);
        player.Mana = magic?.Mana is int m && m >= 0 && m <= player.MaxMana ? m : player.MaxMana;
    }

    /// <summary>Memorises a spell of the book in a gem (the client's scribing time is not enforced).</summary>
    public void MemorizeSpell(int playerId, int gem, int spellId)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || gem is < 0 or >= GemCount)
            return;
        if (player.Cast is not null)
        {
            _events.Add(new Told(playerId, AlreadyCastingMessage));
            return;
        }
        if (spellId < 0)
            player.Gems[gem] = -1;
        else if (!player.Book.Contains(spellId) || SpellById(spellId) is not { } spell)
            return;
        else if (spell.LevelFor(player.Fighter.Class) is not int level || level > player.Level)
        {
            _events.Add(new Told(playerId, "You are not experienced enough to memorize that spell."));
            return;
        }
        else
            player.Gems[gem] = spellId;
        _events.Add(new GemsChanged(playerId));
    }

    /// <summary>
    /// Scribes the spell scroll of an inventory slot into the first free page of the book (legacy
    /// OP_MemorizeSpell with scribing 0): the scroll is used up. The class must get the spell, at
    /// the player's level or below.
    /// </summary>
    public void ScribeScroll(int playerId, int slot)
    {
        if (!_entities.TryGetValue(playerId, out var player) || player.Inventory is not { } inventory
            || slot is < 0 or >= PlayerInventory.Slots || inventory.Items[slot] == 0)
            return;
        if (Items?.Get(inventory.Items[slot]) is not { ItemType: Combat.ItemStats.SpellScroll } scroll || SpellById(scroll.ScrollSpell) is not { } spell)
        {
            _events.Add(new Told(playerId, "That is not a spell scroll."));
            return;
        }
        if (spell.LevelFor(player.Fighter.Class) is not int level)
        {
            _events.Add(new Told(playerId, "Your class cannot learn this spell."));
            return;
        }
        if (level > player.Level)
        {
            _events.Add(new Told(playerId, "You are not experienced enough to scribe that spell."));
            return;
        }
        if (player.Book.Contains(spell.Id))
        {
            _events.Add(new Told(playerId, "You already have this spell scribed."));
            return;
        }
        var book = player.Book.Length >= Characters.PlayerProfile.SpellBookSlots ? player.Book
            : player.Book.Concat(Enumerable.Repeat(-1, Characters.PlayerProfile.SpellBookSlots - player.Book.Length)).ToArray();
        int page = Array.IndexOf(book, -1);
        if (page < 0)
        {
            _events.Add(new Told(playerId, "Your spell book is full."));
            return;
        }
        book[page] = spell.Id;
        player.Book = book;
        inventory.Items[slot] = 0;
        inventory.Charges[slot] = 0;
        _events.Add(new Told(playerId, $"You have finished scribing {spell.Name}."));
        _events.Add(new InventoryChanged(playerId));
        _events.Add(new GemsChanged(playerId));
    }

    /// <summary>
    /// Casts the spell in <paramref name="gem"/> at the player's target (or themselves when the spell
    /// only targets its caster, or when a helpful spell has no target).
    /// </summary>
    public void CastSpell(int playerId, int gem)
    {
        if (!_entities.TryGetValue(playerId, out var caster) || !caster.IsPlayer || gem is < 0 or >= GemCount)
            return;
        if (caster.Cast is not null)
        {
            _events.Add(new Told(playerId, AlreadyCastingMessage));
            return;
        }
        if (SpellById(caster.Gems[gem]) is not { } spell)
            return;
        if (!IsSupported(spell))
        {
            _events.Add(new Told(playerId, NotYetMessage));
            return;
        }
        var target = SpellTargetOf(caster, spell);
        if (target is null)
            return;
        if (spell.Mana > caster.Mana)
        {
            _events.Add(new Told(playerId, NoManaMessage));
            return;
        }
        if (target != caster && Distance2D(caster.Position, target.Position) > spell.Range)
        {
            _events.Add(new Told(playerId, OutOfRangeMessage));
            return;
        }
        if (caster.Sitting)
            SetSitting(playerId, false);
        int spellLevel = spell.LevelFor(caster.Fighter.Class) ?? caster.Level;
        var magic = caster.Magic;
        int chance = SpellRules.FizzleChance(spellLevel, spell.BaseDifficulty, magic?.Skill(spell.Skill) ?? 0, caster.Level, magic?.Wis ?? 75, magic?.Int ?? 75);
        if (SpellRules.Fizzles(chance, _random))
        {
            SetMana(caster, caster.Mana - spell.Mana); // the legacy zone takes the whole cost
            _events.Add(new Told(playerId, FizzleMessage));
            _events.Add(new CastEnded(playerId, spell.Id, CastOutcome.Fizzled));
            return;
        }
        caster.Cast = new Casting(spell, target.Id, gem, caster.Position, _time + spell.CastTimeMs / 1000.0);
        _events.Add(new CastStarted(playerId, spell.Id, spell.Name, spell.CastTimeMs));
        if (spell.CastTimeMs <= 0)
            FinishCast(caster);
    }

    /// <summary>The entity a spell goes to, or null after telling the caster why not.</summary>
    private Entity? SpellTargetOf(Entity caster, Spell spell)
    {
        if (spell.TargetType is SpellTarget.Self or SpellTarget.AECaster or SpellTarget.GroupV1 or SpellTarget.GroupV2)
            return caster;
        Entity? target = caster.PlayerTargetId is int t ? _entities.GetValueOrDefault(t) : null;
        if (target is null)
        {
            if (!spell.Beneficial)
            {
                _events.Add(new Told(caster.Id, NeedTargetMessage));
                return null;
            }
            return caster; // CastSpell: no target means yourself
        }
        if (target.IsCorpse)
        {
            _events.Add(new Told(caster.Id, "You may not target a corpse with this spell."));
            return null;
        }
        if (!spell.Beneficial && target.IsPlayer)
        {
            _events.Add(new Told(caster.Id, "You cannot cast that on another player."));
            return null;
        }
        if (spell.TargetType == SpellTarget.Undead && target.Npc?.Undead != true)
        {
            _events.Add(new Told(caster.Id, "This spell only works on the undead."));
            return null;
        }
        return target;
    }

    /// <summary>Casts in progress: moving more than 3 units interrupts; the cast time up, the spell lands.</summary>
    private void AdvanceCasting()
    {
        foreach (var caster in _entities.Values.Where(e => e.Cast is not null).ToList())
        {
            var cast = caster.Cast!;
            if (Distance2D(caster.Position, cast.From) > SpellRules.InterruptDistance)
                Interrupt(caster);
            else if (_time >= cast.EndsAt - 1e-6)
                FinishCast(caster);
        }
    }

    private void Interrupt(Entity caster)
    {
        var cast = caster.Cast!;
        caster.Cast = null;
        _events.Add(new Told(caster.Id, InterruptedMessage));
        _events.Add(new CastEnded(caster.Id, cast.Spell.Id, CastOutcome.Interrupted));
    }

    /// <summary>
    /// Damage while casting (Mob::Damage): the spell goes on with a chance of 30 + channeling/4 percent,
    /// else it is interrupted.
    /// </summary>
    private void CheckChanneling(Entity player)
    {
        if (player.Cast is null)
            return;
        int chance = 30 + (player.Magic?.Skill(SpellRules.Channeling) ?? 0) / 4;
        if (_random.NextDouble() * 100 > chance)
            Interrupt(player);
    }

    /// <summary>SpellFinished + SpellOnTarget + SpellEffect for the instant hit point effects.</summary>
    private void FinishCast(Entity caster)
    {
        var cast = caster.Cast!;
        var spell = cast.Spell;
        caster.Cast = null;
        if (!_entities.TryGetValue(cast.TargetId, out var target) || target.IsCorpse)
        {
            _events.Add(new Told(caster.Id, InterruptedMessage));
            _events.Add(new CastEnded(caster.Id, spell.Id, CastOutcome.Interrupted));
            return;
        }
        if (target != caster && Distance2D(caster.Position, target.Position) > spell.Range)
        {
            _events.Add(new Told(caster.Id, OutOfRangeMessage));
            _events.Add(new CastEnded(caster.Id, spell.Id, CastOutcome.Interrupted));
            return;
        }
        SetMana(caster, caster.Mana - spell.Mana);
        bool hostile = !spell.Beneficial && target != caster;
        if (hostile)
            caster.LastCombatTime = target.LastCombatTime = _time;
        if (hostile && target.Npc is { } npc
            && SpellRules.Resists(SpellRules.ResistChance(spell.ResistType, SpellRules.NpcResist(spell.ResistType, npc.Combat), target.Level, caster.Level), _random))
        {
            _events.Add(new Told(caster.Id, $"Your target resisted the {spell.Name} spell."));
            _events.Add(new CastEnded(caster.Id, spell.Id, CastOutcome.Resisted));
            AfterHarm(caster, target, 0); // a resisted spell still angers
            return;
        }
        _events.Add(new CastEnded(caster.Id, spell.Id, CastOutcome.Finished));
        int change = 0;
        for (int i = 0; i < Spell.EffectCount; i++)
            if (spell.Effect[i] is SpellEffect.CurrentHp or SpellEffect.CurrentHpOnce)
                change += spell.Value(i, caster.Level);
        int before = target.Hp;
        target.Hp = Math.Min(target.Fighter.MaxHp, target.Hp + change);
        _events.Add(new SpellLanded(caster.Id, target.Id, spell.Id, target.Hp - before));
        if (hostile)
            AfterHarm(caster, target, before - target.Hp);
        else if (target.IsPlayer && target.Hp != before)
            _events.Add(new HealthChanged(target.Id, target.Hp, target.Fighter.MaxHp));
    }

    private void SetMana(Entity player, int mana)
    {
        int clamped = Math.Clamp(mana, 0, player.MaxMana);
        if (clamped == player.Mana)
            return;
        player.Mana = clamped;
        _events.Add(new ManaChanged(player.Id, player.Mana, player.MaxMana));
    }

    /// <summary>Mob::DoManaRegen, once per tic for every caster below their maximum.</summary>
    private void RegenerateMana(Entity player)
    {
        if (player.MaxMana <= 0 || player.Mana >= player.MaxMana)
            return;
        SetMana(player, player.Mana + SpellRules.ManaRegen(player.Level, player.Sitting, player.Magic?.Skill(SpellRules.Meditate) ?? 0, player.MaxMana));
    }

    /// <summary>After a level change (Client::SetLevel): a new pool (CalcMaxMana), filled.</summary>
    private void RecalculateMana(Entity player)
    {
        if (player.Magic is not { } magic)
            return;
        player.MaxMana = SpellRules.MaxMana(player.Fighter.Class, player.Level, magic.Wis, magic.Int);
        player.Mana = player.MaxMana;
        _events.Add(new ManaChanged(player.Id, player.Mana, player.MaxMana));
    }

    private static float Distance2D(Vec3 a, Vec3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
