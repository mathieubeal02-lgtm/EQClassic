using EQClassic.Server.Spells;
using EQClassic.Shared.World;

namespace EQClassic.Server.Zone;

/// <summary>
/// Spell casting after Mob::CastSpell / SpellFinished / SpellOnTarget (Zone/Source/spells.cpp):
/// memorised gems, mana, fizzles, cast time, interruption by moving or by damage (channeling),
/// resists, the instant hit point effects (direct damage and heals), and buffs (Mob::AddBuff,
/// ApplySpellsBonuses, the tic processing): stats, AC, ATK, hit points, resists, haste and slow,
/// movement speed, damage and heals over time, mana over time. Spells with other effects (roots,
/// mez, charm, pets, teleports...) are refused before any mana is spent.
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

    /// <summary>What a player brings for casting: stats, skills, spell book, gems, saved mana and buffs.</summary>
    public sealed record PlayerMagic(int Wis, int Int, IReadOnlyList<int> Skills, IReadOnlyList<int> Book, IReadOnlyList<int> Gems, int? Mana = null,
        IReadOnlyList<SavedBuff>? Buffs = null)
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

    /// <summary>A buff as the profile keeps it (SpellBuff_Struct): spell, caster level, tics left.</summary>
    public readonly record struct SavedBuff(int SpellId, int CasterLevel, int TicsLeft);

    public enum CastOutcome { Finished, Fizzled, Interrupted, Resisted }

    public sealed record CastStarted(int CasterId, int SpellId, string SpellName, int CastMs) : ZoneEvent;
    public sealed record CastEnded(int CasterId, int SpellId, CastOutcome Outcome) : ZoneEvent;
    /// <summary>A spell took effect on <paramref name="TargetId"/>; Amount is the hit point change (negative: damage).</summary>
    public sealed record SpellLanded(int CasterId, int TargetId, int SpellId, int Amount) : ZoneEvent;
    public sealed record ManaChanged(int PlayerId, int Mana, int MaxMana) : ZoneEvent;
    public sealed record GemsChanged(int PlayerId) : ZoneEvent;
    /// <summary>The buffs on an entity changed (added, faded, a tic went by).</summary>
    public sealed record BuffsChanged(int EntityId) : ZoneEvent;
    public sealed record BuffFaded(int EntityId, int SpellId) : ZoneEvent;
    public const string DidNotTakeHoldMessage = "Your spell did not take hold.";

    /// <summary>The spells of spdat.eff by id; null: nobody can cast.</summary>
    public IReadOnlyList<Spell>? Spells { get; init; }

    public Spell? SpellById(int id) => Spells is { } s && id >= 0 && id < s.Count && s[id].IsValid ? s[id] : null;

    private static readonly HashSet<int> SupportedEffects =
    [
        SpellEffect.Blank, SpellEffect.CurrentHp, SpellEffect.CurrentHpOnce, SpellEffect.ArmorClass, SpellEffect.Atk, SpellEffect.MovementSpeed,
        SpellEffect.Str, SpellEffect.Dex, SpellEffect.Agi, SpellEffect.Sta, SpellEffect.Int, SpellEffect.Wis, SpellEffect.Cha,
        SpellEffect.AttackSpeed, SpellEffect.SeeInvis, SpellEffect.WaterBreathing, SpellEffect.CurrentMana, SpellEffect.Blind,
        SpellEffect.ResistFire, SpellEffect.ResistCold, SpellEffect.ResistPoison, SpellEffect.ResistDisease, SpellEffect.ResistMagic,
        SpellEffect.InfraVision, SpellEffect.UltraVision, SpellEffect.TotalHp, SpellEffect.MagnifyVision, SpellEffect.HealOverTime,
        BuffRules.StackingBlock, BuffRules.StackingOverwrite,
        BuffRules.DiseaseCounter, BuffRules.PoisonCounter, BuffRules.CurseCounter, // what cures count down: nothing to do until cures exist
    ];

    /// <summary>
    /// Whether the rewrite applies every effect of the spell: hit points now or over time, stat and
    /// resist buffs and debuffs, haste and slow, movement speed, mana over time, and the effects the
    /// client shows by itself (vision, blindness), on one target.
    /// </summary>
    public static bool IsSupported(Spell spell) =>
        spell.TargetType is not (SpellTarget.AECaster or SpellTarget.AETarget or SpellTarget.GroupV1 or SpellTarget.GroupV2)
        && spell.Effect.All(e => SupportedEffects.Contains(e));

    private void SetUpMagic(Entity player, PlayerMagic? magic)
    {
        player.Magic = magic;
        player.Book = magic?.Book.ToArray() ?? Array.Empty<int>();
        player.Gems = Enumerable.Range(0, GemCount).Select(i => magic is not null && i < magic.Gems.Count ? magic.Gems[i] : -1).ToArray();
        player.MaxMana = magic is null ? 0 : SpellRules.MaxMana(player.Fighter.Class, player.Level, magic.Wis, magic.Int);
        player.Mana = magic?.Mana is int m && m >= 0 && m <= player.MaxMana ? m : player.MaxMana;
        foreach (var saved in magic?.Buffs ?? Array.Empty<SavedBuff>())
            if (SpellById(saved.SpellId) is { IsBuff: true } spell && saved.TicsLeft > 0 && player.BuffList.Count < BuffRules.Slots)
                player.BuffList.Add(new Buff(spell, 0, saved.CasterLevel, saved.TicsLeft));
        if (player.BuffList.Count > 0)
            UpdateBonuses(player);
    }

    /// <summary>The buffs as the profile keeps them.</summary>
    public static IReadOnlyList<SavedBuff> SaveBuffs(Entity e) =>
        e.BuffList.Select(b => new SavedBuff(b.Spell.Id, b.CasterLevel, b.TicsLeft)).ToList();

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
        if (spell.IsBuff && !AddBuff(caster, target, spell))
        {
            _events.Add(new Told(caster.Id, DidNotTakeHoldMessage));
            _events.Add(new CastEnded(caster.Id, spell.Id, CastOutcome.Resisted));
            if (hostile)
                AfterHarm(caster, target, 0);
            return;
        }
        _events.Add(new CastEnded(caster.Id, spell.Id, CastOutcome.Finished));
        // SpellEffect: the hit point effects apply at once, for buffs too (then every tic).
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

    /// <summary>Mob::AddBuff + HandleBuffSpellEffects: false when stacking keeps it out.</summary>
    private bool AddBuff(Entity caster, Entity target, Spell spell)
    {
        int tics = BuffRules.Tics(spell, caster.Level);
        if (tics <= 0)
            return true; // not a lasting spell after all: only its instant part
        var replaced = BuffRules.Add(target.BuffList, new Buff(spell, caster.Id, caster.Level, tics), onNpc: !target.IsPlayer);
        if (replaced is null)
            return false;
        UpdateBonuses(target);
        return true;
    }

    /// <summary>Mob::CalcBonuses after a buff comes or goes: the fighter, hit points and mana follow.</summary>
    private void UpdateBonuses(Entity e)
    {
        e.Bonuses = StatBonuses.From(e.BuffList);
        RebuildFighter(e);
        if (e.Magic is { } magic)
        {
            e.MaxMana = SpellRules.MaxMana(e.Fighter.Class, e.Level, magic.Wis + e.Bonuses.Wis, magic.Int + e.Bonuses.Int);
            if (e.Mana > e.MaxMana)
                e.Mana = e.MaxMana;
            _events.Add(new ManaChanged(e.Id, e.Mana, e.MaxMana));
        }
        _events.Add(new BuffsChanged(e.Id));
    }

    /// <summary>A new fighter from the level, the equipment and the buffs; hit points stay within the maximum.</summary>
    private void RebuildFighter(Entity e)
    {
        if (e.IsPlayer)
        {
            if (e.Progress?.FighterAt is not { } rebuild)
                return;
            e.Fighter = rebuild(e.Level, e.Bonuses);
        }
        else if (e.Npc is { } npc)
        {
            e.Fighter = Combat.Combatant.ForNpc(npc, e.Bonuses);
        }
        e.Hp = Math.Min(e.Hp, e.Fighter.MaxHp);
        if (e.IsPlayer)
            _events.Add(new HealthChanged(e.Id, e.Hp, e.Fighter.MaxHp));
    }

    /// <summary>
    /// Every tic: damage and heals over time (the caster of a damage spell gets the credit and the
    /// kill), mana over time, then one tic less on every buff; those that run out fade.
    /// </summary>
    private void TickBuffs()
    {
        foreach (var e in _entities.Values.Where(x => x.BuffList.Count > 0 && !x.IsCorpse).ToList())
        {
            if (!_entities.ContainsKey(e.Id))
                continue;
            var b = e.Bonuses;
            if (b.HpPerTic < 0)
            {
                var dot = e.BuffList.FirstOrDefault(x => !x.Spell.Beneficial && x.Spell.Effect.Contains((byte)SpellEffect.CurrentHp));
                var caster = dot is null ? null : _entities.GetValueOrDefault(dot.CasterId);
                int damage = Math.Min(-b.HpPerTic, Math.Max(e.Hp, 0) + 1);
                e.Hp -= damage;
                if (caster is not null && caster != e)
                {
                    caster.LastCombatTime = e.LastCombatTime = _time;
                    AfterHarm(caster, e, damage);
                }
                else if (e.Hp <= 0)
                {
                    _events.Add(new Slain(e.Id, e.Name, 0, ""));
                    if (e.IsPlayer) PlayerDied(e); else Kill(e.Id);
                }
                else if (e.IsPlayer)
                    _events.Add(new HealthChanged(e.Id, e.Hp, e.Fighter.MaxHp));
                if (!_entities.TryGetValue(e.Id, out var still) || still.IsCorpse || e.BuffList.Count == 0)
                    continue;
            }
            else if (b.HpPerTic > 0 && e.Hp < e.Fighter.MaxHp)
            {
                e.Hp = Math.Min(e.Fighter.MaxHp, e.Hp + b.HpPerTic);
                if (e.IsPlayer)
                    _events.Add(new HealthChanged(e.Id, e.Hp, e.Fighter.MaxHp));
            }
            if (b.ManaPerTic != 0 && e.MaxMana > 0)
                SetMana(e, e.Mana + b.ManaPerTic);
            var faded = new List<Buff>();
            foreach (var buff in e.BuffList)
                if (buff.TicsLeft < 32767 && --buff.TicsLeft <= 0)
                    faded.Add(buff);
            foreach (var buff in faded)
            {
                e.BuffList.Remove(buff);
                _events.Add(new BuffFaded(e.Id, buff.Spell.Id));
            }
            if (faded.Count > 0)
                UpdateBonuses(e);
            else
                _events.Add(new BuffsChanged(e.Id));
        }
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
