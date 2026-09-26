using EQClassic.Server.Combat;
using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>What foraging finds in a zone (forage: zoneid, Itemid, level).</summary>
public interface IForageSource
{
    /// <summary>The zone's forage items whose level is under the skill.</summary>
    IReadOnlyList<int> Items(string zone, int skill);
}

public sealed class InMemoryForageSource : IForageSource
{
    public List<(string Zone, int Item, int Level)> Rows { get; } = new();
    public IReadOnlyList<int> Items(string zone, int skill) =>
        Rows.Where(r => string.Equals(r.Zone, zone, StringComparison.OrdinalIgnoreCase) && r.Level < skill).Select(r => r.Item).ToList();
}

public sealed class MySqlForageSource : IForageSource
{
    private readonly string _connectionString;
    public MySqlForageSource(string connectionString) => _connectionString = connectionString;

    /// <summary>Database::GetZoneForage.</summary>
    public IReadOnlyList<int> Items(string zone, int skill)
    {
        var items = new List<int>();
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT f.Itemid FROM forage f JOIN zone_ids z ON z.zoneidnumber = f.zoneid WHERE z.short_name = @zone AND f.level < @skill";
        cmd.Parameters.AddWithValue("@zone", zone);
        cmd.Parameters.AddWithValue("@skill", skill);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            items.Add(Convert.ToInt32(r.GetValue(0)));
        return items;
    }
}

/// <summary>
/// Skills used from the abilities window (Client::ProcessOP_CombatAbility, ProcessOP_Taunt,
/// ProcessOP_Mend, ProcessOP_Hide, ProcessOP_Sneak, ProcessOP_Forage): kick, bash, taunt, mend,
/// hide, sneak and forage, each after its reuse time.
/// </summary>
public sealed partial class ZoneInstance
{
    public const int KickSkill = 30, BashSkill = 10, TauntSkill = 73, MendSkill = 32, HideSkill = 29, SneakSkill = 42, ForageSkill = 27;

    /// <summary>Seconds before an ability can be used again (the Trilogy client's timers; the legacy zone trusted the client).</summary>
    public static readonly IReadOnlyDictionary<int, double> ReuseSeconds = new Dictionary<int, double>
    {
        [KickSkill] = 8, [BashSkill] = 8, [TauntSkill] = 6, [MendSkill] = 360, [HideSkill] = 10, [SneakSkill] = 6, [ForageSkill] = 100,
    };

    /// <summary>The abilities a player may use: those their class learns.</summary>
    public static bool CanUse(int skill, int playerClass) =>
        ReuseSeconds.ContainsKey(skill) && SkillCaps.Cap(skill, playerClass, 60) > 0;

    public IForageSource? Forage { get; init; }

    private static readonly int[] CommonFoods = [13046, 13045, 13419, 13048, 13047, 13044, 13106]; // fruit, berries, vegetables, rabbit meat, roots, water, grubs

    public void UseAbility(int playerId, int skill)
    {
        if (!_entities.TryGetValue(playerId, out var player) || !player.IsPlayer || !CanUse(skill, player.Fighter.Class))
            return;
        if (player.AbilityReadyAt.TryGetValue(skill, out double ready) && _time < ready)
        {
            _events.Add(new Told(playerId, "You can't use that ability again yet."));
            return;
        }
        bool used = skill switch
        {
            KickSkill or BashSkill => Strike(player, skill),
            TauntSkill => Taunt(player),
            MendSkill => Mend(player),
            HideSkill => Hide(player),
            SneakSkill => Sneak(player),
            ForageSkill => ForageOnce(player),
            _ => false,
        };
        if (used)
            player.AbilityReadyAt[skill] = _time + ReuseSeconds[skill];
    }

    /// <summary>The target of a combat ability in melee reach, or null after telling why not.</summary>
    private Entity? MeleeTarget(Entity player)
    {
        if (player.PlayerTargetId is not int t || !_entities.TryGetValue(t, out var target) || target.IsPlayer)
        {
            _events.Add(new Told(player.Id, "You must first select a target for this command!"));
            return null;
        }
        if (target.IsCorpse)
        {
            _events.Add(new Told(player.Id, "You cannot attack a corpse."));
            return null;
        }
        if (Distance2(player.Position, target.Position) > PlayerReach * PlayerReach)
        {
            _events.Add(new Told(player.Id, TooFarMessage));
            return null;
        }
        return target;
    }

    /// <summary>
    /// Kick: ((kick + STR) / 25 × 4 + level) × a random fraction for warriors, × 3 for rangers,
    /// paladins and shadow knights. Bash (the legacy "slam"): level/10 × 3 × (bash + STR + level) /
    /// (700 − bash); the legacy zone divided the level by 10 in integers, so it did nothing below
    /// level 10 — the rewrite keeps the fraction.
    /// </summary>
    private bool Strike(Entity player, int skill)
    {
        if (MeleeTarget(player) is not { } target)
            return false;
        BreakInvisibility(player);
        player.Hidden = false;
        int str = player.Progress?.Str ?? 75;
        int value = SkillOf(player, skill);
        int damage = skill == KickSkill
            ? (int)(((value + str) / 25 * (player.Fighter.Class == CombatFormulas.Warrior ? 4 : 3) + player.Level) * _random.NextDouble())
            : (int)(player.Level / 10f * 3 * (value + str + player.Level) / (700 - value));
        player.LastCombatTime = target.LastCombatTime = _time;
        target.Hp -= damage;
        _events.Add(new Swung(player.Id, target.Id, damage, target.HpPercent));
        CheckAddSkill(player, skill);
        AfterHarm(player, target, damage);
        return true;
    }

    /// <summary>Taunt: the NPC turns on the taunter (the legacy chance was disabled: it always works).</summary>
    private bool Taunt(Entity player)
    {
        if (MeleeTarget(player) is not { } npc)
            return false;
        BreakInvisibility(player);
        CheckAddSkill(player, TauntSkill, -10);
        if (npc.TargetId != player.Id)
        {
            npc.TargetId = player.Id;
            _events.Add(new Engaged(npc.Id, player.Id));
        }
        return true;
    }

    /// <summary>Client::ProcessOP_Mend: a quarter of the hit points with a skill-percent chance; a bad failure hurts as much.</summary>
    private bool Mend(Entity player)
    {
        int amount = player.Fighter.MaxHp / 4;
        int noAdvance = _random.Next(200);
        if (_random.Next(100) <= SkillOf(player, MendSkill))
        {
            player.Hp = Math.Min(player.Fighter.MaxHp, player.Hp + amount);
            _events.Add(new Told(player.Id, "You mend your wounds and heal some damage"));
        }
        else if (noAdvance > 175)
        {
            player.Hp = player.Hp > amount ? player.Hp - amount : 1;
            _events.Add(new Told(player.Id, "You fail to mend your wounds and damage yourself!"));
        }
        else
            _events.Add(new Told(player.Id, "You fail to mend your wounds"));
        _events.Add(new HealthChanged(player.Id, player.Hp, player.Fighter.MaxHp));
        CheckAddSkill(player, MendSkill);
        return true;
    }

    /// <summary>Hide: works with a chance of hide/300 + 25%; hidden players are not seen by NPCs until they move (unless sneaking) or attack.</summary>
    private bool Hide(Entity player)
    {
        bool success = _random.NextDouble() < SkillOf(player, HideSkill) / 300f + 0.25f;
        CheckAddSkill(player, HideSkill);
        player.Hidden = success;
        player.HiddenAt = player.Position;
        _events.Add(new Told(player.Id, success ? "You have hidden yourself from view." : "You failed to hide yourself."));
        return true;
    }

    /// <summary>Sneak: toggled; starts with a chance of sneak/300 + 25%.</summary>
    private bool Sneak(Entity player)
    {
        if (player.Sneaking)
            player.Sneaking = false;
        else if (_random.Next(100) < (int)((SkillOf(player, SneakSkill) / 300f + 0.25f) * 100))
        {
            player.Sneaking = true;
            _events.Add(new Told(player.Id, "You are as quiet as a cat stalking its prey."));
        }
        else
            _events.Add(new Told(player.Id, "You are as quiet as a herd of stampeding elephants."));
        CheckAddSkill(player, SneakSkill);
        return true;
    }

    /// <summary>
    /// ForageItem: rand(240) under the skill finds something — a common food three times in four
    /// (or when the zone has nothing), else one of the zone's forage items. The legacy zone only
    /// tried a skill-up when nothing was found.
    /// </summary>
    private bool ForageOnce(Entity player)
    {
        int skill = SkillOf(player, ForageSkill);
        int found = 0;
        if (_random.Next(240) < skill)
        {
            var zoneItems = Forage?.Items(ShortName, skill) ?? Array.Empty<int>();
            found = _random.Next(100) < 75 || zoneItems.Count == 0 ? CommonFoods[_random.Next(CommonFoods.Length)] : zoneItems[_random.Next(zoneItems.Count)];
        }
        if (found != 0 && Items?.Get(found) is { } item)
        {
            _events.Add(new Told(player.Id, $"You forage a {item.Name}"));
            SummonItem(player, found, 1);
            return true;
        }
        _events.Add(new Told(player.Id, "You fail to find anything to forage."));
        int wis = player.Magic?.Wis ?? 75;
        CheckAddSkill(player, ForageSkill, (int)(wis > 200 ? 20 + (wis - 200) * 0.05 : wis * 0.1));
        return true;
    }
}
