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
        /// <summary>Hit while casting and not interrupted.</summary>
        internal bool Channeled { get; set; }
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
    /// <summary>A player's bind point changed (bind affinity): the server saves it.</summary>
    public sealed record BindChanged(int PlayerId) : ZoneEvent;

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
        SpellEffect.Invisibility, SpellEffect.InvisVsUndead, SpellEffect.Stun, SpellEffect.BindAffinity, SpellEffect.Gate, SpellEffect.Mez,
        SpellEffect.SummonItem, SpellEffect.Levitate, SpellEffect.Teleport, SpellEffect.Root, WipeHateList,
    ];

    private const int WipeHateList = 63;

    /// <summary>
    /// Whether the rewrite applies every effect of the spell: hit points now or over time, stat and
    /// resist buffs and debuffs, haste and slow, movement speed, mana over time, invisibility, root,
    /// mez, stun, levitation, bind affinity, gate, teleports, summoned items, and the effects the
    /// client shows by itself (vision, blindness). Group spells land on the group members in range.
    /// </summary>
    public static bool IsSupported(Spell spell) => spell.Effect.All(e => SupportedEffects.Contains(e));

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
        int chance = SpellRules.FizzleChance(spellLevel, spell.BaseDifficulty, SkillOf(caster, spell.Skill), caster.Level, magic?.Wis ?? 75, magic?.Int ?? 75);
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
        int chance = 30 + SkillOf(player, SpellRules.Channeling) / 4;
        if (_random.NextDouble() * 100 > chance)
            Interrupt(player);
        else
            player.Cast.Channeled = true; // "You regain your concentration" at the end, and a channeling skill-up
    }

    /// <summary>SpellFinished: range, the zone's rules, mana, then the spell on each of its targets.</summary>
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
        SetMana(caster, caster.Mana - spell.Mana); // a refusal by the zone's rules spends it too (InterruptSpell(false, true))
        if (cast.Channeled)
        {
            _events.Add(new Told(caster.Id, "You regain your concentration and continue your casting!"));
            CheckAddSkill(caster, SpellRules.Channeling);
        }
        // The legacy zone never raised the casting skills (only channeling and meditate); EQMacEmu does,
        // on each cast that is not a fizzle: without it a caster's fizzle rate never improved.
        CheckAddSkill(caster, spell.Skill);
        if (ZoneRefusal(caster, target, spell) is { } refusal)
        {
            _events.Add(new Told(caster.Id, refusal));
            _events.Add(new CastEnded(caster.Id, spell.Id, CastOutcome.Interrupted));
            return;
        }
        if (!spell.Beneficial)
        {
            BreakInvisibility(caster);
            caster.Hidden = false;
        }
        var targets = spell.TargetType switch
        {
            // Group::CastGroupSpell: every member of the caster's group in range.
            SpellTarget.GroupV1 or SpellTarget.GroupV2 => GroupHere(caster)
                .Where(m => m == caster || Distance2D(m.Position, caster.Position) <= Math.Max(spell.Range, spell.AoeRange)).ToList(),
            SpellTarget.AECaster => InArea(caster.Position, spell, except: caster),
            SpellTarget.AETarget => InArea(target.Position, spell, except: spell.Beneficial ? null : caster),
            _ => [target],
        };
        var outcome = CastOutcome.Finished;
        foreach (var t in targets)
            if (_entities.ContainsKey(t.Id) && !t.IsCorpse)
                outcome = SpellOnTarget(caster, t, spell);
        _events.Add(new CastEnded(caster.Id, spell.Id, targets.Count == 1 ? outcome : CastOutcome.Finished));
    }

    /// <summary>Area spells: NPCs (hostile spells) or players (helpful ones) within the spell's area range.</summary>
    private List<Entity> InArea(Vec3 centre, Spell spell, Entity? except)
    {
        float r2 = spell.AoeRange * spell.AoeRange;
        return _entities.Values.Where(e => e != except && !e.IsCorpse && e.IsPlayer == spell.Beneficial && Distance2(e.Position, centre) <= r2).ToList();
    }

    /// <summary>SpellFinished's checks of the zone's rules (zone_rules): binding, levitation, outdoor spells.</summary>
    private string? ZoneRefusal(Entity caster, Entity target, Spell spell)
    {
        var rules = Rules;
        if (spell.Effect.Contains((byte)SpellEffect.BindAffinity))
        {
            // The legacy zone lets others be bound only in can_bind 2 zones and within a group; no groups yet.
            bool permit = target == caster ? rules.CanBind >= 1 : rules.CanBind == 2;
            if (!permit)
                return target == caster ? "You may not bind here." : "Your target may not be bound here.";
        }
        if (spell.Effect.Contains((byte)SpellEffect.Levitate) && !rules.CanLevitate)
            return "You can't levitate in this zone.";
        if (IsOutdoorSpell(spell) && !rules.Outdoor)
            return "You can't cast this spell indoors.";
        return null;
    }

    /// <summary>Spell::IsOutDoorSpell: faster movement, levitation, harmony.</summary>
    private static bool IsOutdoorSpell(Spell spell)
    {
        for (int i = 0; i < Spell.EffectCount; i++)
            if (spell.Effect[i] == SpellEffect.MovementSpeed && spell.Base[i] > 0 || spell.Effect[i] is SpellEffect.Levitate or Harmony)
                return true;
        return false;
    }

    private const int Harmony = 86;

    /// <summary>SpellOnTarget + SpellEffect: resist, buff, then the instant effects.</summary>
    private CastOutcome SpellOnTarget(Entity caster, Entity target, Spell spell)
    {
        bool hostile = !spell.Beneficial && target != caster;
        if (hostile)
            caster.LastCombatTime = target.LastCombatTime = _time;
        int? resist = !hostile ? null : target.Npc is { } npc ? NpcResist(target, npc, spell.ResistType)
            : BonusResist(target, spell.ResistType); // players: no base resists in the legacy zone (Client's BaseStats are 0), only buffs
        if (resist is int save && SpellRules.Resists(SpellRules.ResistChance(spell.ResistType, save, target.Level, caster.Level), _random))
        {
            _events.Add(new Told(caster.Id, $"Your target resisted the {spell.Name} spell."));
            AfterHarm(caster, target, 0); // a resisted spell still angers
            return CastOutcome.Resisted;
        }
        if (spell.IsBuff && !AddBuff(caster, target, spell))
        {
            _events.Add(new Told(caster.Id, DidNotTakeHoldMessage));
            if (hostile)
                AfterHarm(caster, target, 0);
            return CastOutcome.Resisted;
        }
        int before = target.Hp;
        ApplyInstantEffects(caster, target, spell);
        if (!_entities.ContainsKey(target.Id))
            return CastOutcome.Finished;
        _events.Add(new SpellLanded(caster.Id, target.Id, spell.Id, target.Hp - before));
        if (hostile)
            AfterHarm(caster, target, before - target.Hp);
        else if (target.IsPlayer && target.Hp != before)
            _events.Add(new HealthChanged(target.Id, target.Hp, target.Fighter.MaxHp));
        if (spell.Effect.Contains((byte)WipeHateList) && !target.IsPlayer && _entities.ContainsKey(target.Id) && !target.IsCorpse)
        {
            Disengage(target); // NPC::WhipeHateList, after the spell's own anger: it forgets everyone
            foreach (var p in _entities.Values.Where(p => p.IsPlayer && Distance2(p.Position, target.Position) <= 200f * 200f))
                _events.Add(new Told(p.Id, "My mind fogs. Who are my friends? Who are my enemies?... it was all so clear a moment ago..."));
        }
        return CastOutcome.Finished;
    }

    private static int NpcResist(Entity target, NpcTemplate npc, int resistType) =>
        SpellRules.NpcResist(resistType, npc.Combat) + BonusResist(target, resistType);

    private static int BonusResist(Entity target, int resistType) => resistType switch
    {
        1 => target.Bonuses.MR, 2 => target.Bonuses.FR, 3 => target.Bonuses.CR, 4 => target.Bonuses.PR, 5 => target.Bonuses.DR, _ => 0,
    };

    /// <summary>
    /// SpellEffect's single-time part: hit points (for buffs too, then every tic), mana, stun, bind
    /// affinity, gate, teleport, summoned items.
    /// </summary>
    private void ApplyInstantEffects(Entity caster, Entity target, Spell spell)
    {
        int change = 0;
        for (int i = 0; i < Spell.EffectCount; i++)
        {
            int v = spell.Value(i, caster.Level);
            switch (spell.Effect[i])
            {
                case SpellEffect.CurrentHp:
                case SpellEffect.CurrentHpOnce:
                    change += v;
                    break;
                case SpellEffect.CurrentMana when target.MaxMana > 0:
                    SetMana(target, target.Mana + v);
                    break;
                case SpellEffect.Stun when !target.IsPlayer:
                    target.StunnedUntil = Math.Max(target.StunnedUntil, _time + spell.Base[i] / 1000.0);
                    break;
                case SpellEffect.BindAffinity when target.IsPlayer:
                    target.BindZone = ShortName;
                    target.Bind = target.Position;
                    _events.Add(new Told(target.Id, "You feel yourself bind to the area."));
                    _events.Add(new BindChanged(target.Id));
                    break;
                case SpellEffect.Gate when target.IsPlayer:
                    SendToBind(target, "gate");
                    return;
                case SpellEffect.Teleport when target.IsPlayer && spell.TeleportZone.Length > 0:
                    Teleport(target, spell.TeleportZone, new Vec3(spell.Base[1], spell.Base[0], spell.Base[2]), "teleport");
                    return;
                case SpellEffect.SummonItem when target.IsPlayer:
                    SummonItem(target, spell.Base[i], Math.Clamp(v, 1, 20));
                    break;
            }
        }
        target.Hp = Math.Min(target.Fighter.MaxHp, target.Hp + change);
    }

    /// <summary>Client::SummonItem: into the first free general slot (there is no cursor yet).</summary>
    private void SummonItem(Entity player, int itemId, int charges)
    {
        if (player.Inventory is not { } inventory || itemId <= 0)
            return;
        int slot = inventory.FreeGeneralSlot();
        if (slot < 0)
        {
            _events.Add(new Told(player.Id, "You have no room to hold the summoned item."));
            return;
        }
        inventory.Items[slot] = itemId;
        inventory.Charges[slot] = charges;
        _events.Add(new InventoryChanged(player.Id));
    }

    /// <summary>To a place in this zone, or across zones through the zone server (like a zone line).</summary>
    private void Teleport(Entity player, string zone, Vec3 to, string reason)
    {
        if (!string.Equals(zone, ShortName, StringComparison.OrdinalIgnoreCase))
        {
            _events.Add(new CrossedZoneLine(player.Id, new ZoneLine(0, player.Position, 0, zone, to), to));
            return;
        }
        player.Position = to;
        player.LastMoveTime = _time;
        player.Moved = true;
        _events.Add(new Teleported(player.Id, to, reason));
    }

    /// <summary>Gate and death: the bind point, in this zone or another; where the player entered when it is unknown.</summary>
    private void SendToBind(Entity player, string reason)
    {
        if (player.BindZone.Length == 0)
            Teleport(player, ShortName, player.EntryPosition, reason);
        else
            Teleport(player, player.BindZone, player.Bind, reason);
    }

    /// <summary>Invisibility ends when its owner attacks or casts a hostile spell.</summary>
    private void BreakInvisibility(Entity e)
    {
        var invisible = e.BuffList.Where(b => b.Spell.Effect.Contains((byte)SpellEffect.Invisibility) || b.Spell.Effect.Contains((byte)SpellEffect.InvisVsUndead)).ToList();
        RemoveBuffs(e, invisible);
    }

    /// <summary>Takes buffs off (they fade with their message) and updates the bonuses.</summary>
    private void RemoveBuffs(Entity e, List<Buff> buffs)
    {
        if (buffs.Count == 0)
            return;
        foreach (var b in buffs)
        {
            e.BuffList.Remove(b);
            _events.Add(new BuffFaded(e.Id, b.Spell.Id));
        }
        UpdateBonuses(e);
    }

    /// <summary>Damage wakes a mesmerized NPC or player.</summary>
    private void BreakMez(Entity e) =>
        RemoveBuffs(e, e.BuffList.Where(b => b.Spell.Effect.Contains((byte)SpellEffect.Mez)).ToList());

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
        int meditate = SkillOf(player, SpellRules.Meditate);
        SetMana(player, player.Mana + SpellRules.ManaRegen(player.Level, player.Sitting, meditate, player.MaxMana));
        if (player.Sitting && meditate > 0)
            CheckAddSkill(player, SpellRules.Meditate); // DoManaRegen
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
