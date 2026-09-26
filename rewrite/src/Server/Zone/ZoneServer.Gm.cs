using System.Globalization;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;
using LiteNetLib;

namespace EQClassic.Server.Zone;

/// <summary>
/// GM commands (Zone/Source/Client_Commands.cpp): a say line starting with # is a command, never
/// heard by others. The account's status (account.status) must reach the command's level; #help
/// and #loc are for everyone. The rest asks for <see cref="GmStatus"/> (the legacy levels ran
/// from 5, its alpha testers, to 255).
/// </summary>
public sealed partial class ZoneServer
{
    public const int GmStatus = 80;

    /// <summary>account.status by world account id; null: nobody is a GM.</summary>
    public Func<int, int>? AccountStatus { get; set; }
    /// <summary>A zone's safe point (its cfg header), or null for an unknown zone: #zone.</summary>
    public Func<string, Vec3?>? SafePointOf { get; set; }

    private sealed record GmCommand(string Name, int Status, string Usage, Action<NetPeer, Player, string[]> Run);

    private IReadOnlyList<GmCommand>? _gmCommands;

    private IReadOnlyList<GmCommand> GmCommands => _gmCommands ??=
    [
        new("help", 0, "[text] - the commands you may use", (peer, p, a) =>
        {
            int status = StatusOf(p);
            foreach (var c in GmCommands.Where(c => c.Status <= status && (a.Length < 2 || c.Name.Contains(a[1], StringComparison.OrdinalIgnoreCase))))
                Tell(peer, $"#{c.Name} {c.Usage}");
        }),
        new("loc", 0, "- your location", (peer, p, _) =>
        {
            if (p.Instance.Get(p.EntityId) is { } me)
                Tell(peer, $"Your Location is {me.Position.Y:0.00}, {me.Position.X:0.00}, {me.Position.Z:0.00}");
        }),
        new("zone", GmStatus, "<zone> [x y z] - to a zone's safe point, or a place in it", (peer, p, a) =>
        {
            if (a.Length < 2)
            {
                Tell(peer, "Usage: #zone <zonename> [x y z]");
                return;
            }
            string zone = a[1].ToLowerInvariant();
            Vec3? to = a.Length >= 5 && F(a[2]) is float x && F(a[3]) is float y && F(a[4]) is float z ? new Vec3(x, y, z) : SafePointOf?.Invoke(zone);
            if (to is null)
            {
                Tell(peer, $"That zone ('{zone}') is not known.");
                return;
            }
            if (string.Equals(zone, p.Instance.ShortName, StringComparison.OrdinalIgnoreCase))
                p.Instance.GmTeleport(p.EntityId, to.Value);
            else
                p.Instance.GmZone(p.EntityId, zone, to.Value);
        }),
        new("goto", GmStatus, "[x y z | player] - to a place, a player, or your target", (peer, p, a) =>
        {
            if (a.Length >= 4 && F(a[1]) is float x && F(a[2]) is float y && F(a[3]) is float z)
            {
                p.Instance.GmTeleport(p.EntityId, new Vec3(x, y, z));
                return;
            }
            if (a.Length >= 2)
            {
                var (_, other) = _players.FirstOrDefault(kv => string.Equals(kv.Value.Ticket.CharacterName, a[1], StringComparison.OrdinalIgnoreCase));
                if (other is null || other.Instance.Get(other.EntityId) is not { } them)
                    Tell(peer, $"{a[1]} is not online.");
                else if (other.Instance != p.Instance)
                    p.Instance.GmZone(p.EntityId, other.Instance.ShortName, them.Position);
                else
                    p.Instance.GmTeleport(p.EntityId, them.Position with { X = them.Position.X + 3f });
                return;
            }
            if (Target(p) is { } target)
                p.Instance.GmTeleport(p.EntityId, target.Position with { X = target.Position.X + 3f });
            else
                Tell(peer, "Usage: #goto [x y z | player], or target someone.");
        }),
        new("summon", GmStatus, "[player] - brings your target (or a player of this zone) to you", (peer, p, a) =>
        {
            var target = a.Length >= 2 ? p.Instance.Entities.FirstOrDefault(e => e.IsPlayer && string.Equals(e.Name, a[1], StringComparison.OrdinalIgnoreCase)) : Target(p);
            if (target is null)
                Tell(peer, "Usage: #summon [player of this zone], or target someone.");
            else
                p.Instance.GmSummon(p.EntityId, target.Id);
        }),
        new("level", GmStatus, "<level> - your target's level (or yours)", (peer, p, a) =>
        {
            if (a.Length < 2 || !int.TryParse(a[1], out int level))
                Tell(peer, "Usage: #level <1-60>");
            else
                p.Instance.GmSetLevel((Target(p) ?? Me(p))!.Id, level);
        }),
        new("setexp", GmStatus, "<value> - your experience", (peer, p, a) =>
        {
            if (a.Length >= 2 && long.TryParse(a[1], out long v))
                p.Instance.GmSetExperience(p.EntityId, v);
        }),
        new("addexp", GmStatus, "<value> - adds to your experience", (peer, p, a) =>
        {
            if (a.Length >= 2 && long.TryParse(a[1], out long v) && Me(p) is { } me)
                p.Instance.GmSetExperience(p.EntityId, me.Exp + v);
        }),
        new("heal", GmStatus, "- heals your target (or you) completely", (peer, p, _) => p.Instance.GmHeal((Target(p) ?? Me(p))!.Id)),
        new("mana", GmStatus, "- refills your target's (or your) mana", (peer, p, _) => p.Instance.GmHeal((Target(p) ?? Me(p))!.Id, hp: false)),
        new("damage", GmStatus, "<amount> - damages your target", (peer, p, a) =>
        {
            if (a.Length >= 2 && int.TryParse(a[1], out int d) && Target(p) is { } t)
                p.Instance.GmDamage(p.EntityId, t.Id, d);
            else
                Tell(peer, "Usage: #damage <amount>, with a target.");
        }),
        new("kill", GmStatus, "- kills your target", (peer, p, _) =>
        {
            if (Target(p) is { } t)
                p.Instance.GmDamage(p.EntityId, t.Id, t.Hp + 1);
            else
                Tell(peer, "You need a target.");
        }),
        new("invul", GmStatus, "[on|off] - your target (or you) cannot be hurt (also #invulnerable)", InvulCommand),
        new("invulnerable", GmStatus, "[on|off]", InvulCommand),
        new("summonitem", GmStatus, "<item id> [charges] - an item into your inventory (also #si)", SummonItemCommand),
        new("si", GmStatus, "<item id> [charges]", SummonItemCommand),
        new("clearinventory", GmStatus, "- empties the general slots and bags of your target player (or yours)", (peer, p, _) =>
        {
            var t = Target(p) is { IsPlayer: true } player ? player : Me(p)!;
            Tell(peer, $"{p.Instance.GmClearInventory(t.Id)} item(s) removed from {t.Name}'s packs.");
        }),
        new("givemoney", GmStatus, "<copper> [silver] [gold] [platinum] - money for you", (peer, p, a) =>
        {
            int I(int i) => a.Length > i && int.TryParse(a[i], out int v) ? Math.Max(0, v) : 0;
            p.Instance.GmGiveMoney(p.EntityId, new Coins(I(4), I(3), I(2), I(1)));
        }),
        new("spawn", GmStatus, "<npc type id> - an NPC next to you", (peer, p, a) =>
        {
            if (a.Length >= 2 && int.TryParse(a[1], out int type))
                Tell(peer, p.Instance.GmSpawn(p.EntityId, type));
            else
                Tell(peer, "Usage: #spawn <npc type id>");
        }),
        new("repopzone", GmStatus, "- every NPC waiting to respawn comes back now", (peer, p, _) => Tell(peer, $"{p.Instance.GmRepop()} spawn point(s) repopulated.")),
        new("depopzone", GmStatus, "- removes every NPC of the zone (they respawn in their time)", (peer, p, _) => Tell(peer, $"{p.Instance.GmDepop()} NPC(s) removed.")),
        new("castspell", GmStatus, "<spell id> - the spell lands on your target (or you) at once (also #cast)", CastCommand),
        new("cast", GmStatus, "<spell id>", CastCommand),
        new("scribespells", GmStatus, "[level] - every spell of your class up to that level (default yours)", (peer, p, a) =>
        {
            int level = a.Length >= 2 && int.TryParse(a[1], out int l) ? l : Me(p)?.Level ?? 1;
            Tell(peer, $"{p.Instance.GmScribeSpells(p.EntityId, level)} spell(s) scribed.");
        }),
        new("npcstats", GmStatus, "- about your target NPC", (peer, p, _) => Tell(peer, Target(p) is { } t ? p.Instance.GmNpcStats(t.Id) : "Target an NPC.")),
    ];

    private void InvulCommand(NetPeer peer, Player p, string[] a)
    {
        var t = (Target(p) ?? Me(p))!;
        bool on = a.Length < 2 ? !t.GmInvulnerable : a[1] is "on" or "1";
        p.Instance.GmInvulnerable(t.Id, on);
        Tell(peer, $"{DisplayName(t.Name)} is {(on ? "now" : "no longer")} invulnerable.");
    }

    private void SummonItemCommand(NetPeer peer, Player p, string[] a)
    {
        if (a.Length >= 2 && int.TryParse(a[1], out int item))
            p.Instance.GmSummonItem(p.EntityId, item, a.Length >= 3 && int.TryParse(a[2], out int c) ? c : 1);
        else
            Tell(peer, "Usage: #summonitem <item id> [charges]");
    }

    private void CastCommand(NetPeer peer, Player p, string[] a)
    {
        if (a.Length >= 2 && int.TryParse(a[1], out int spell))
            Tell(peer, p.Instance.GmCast(p.EntityId, (Target(p) ?? Me(p))!.Id, spell));
        else
            Tell(peer, "Usage: #castspell <spell id>");
    }

    /// <summary>Read at each command: a status changed in the database applies without logging out.</summary>
    private int StatusOf(Player p) => AccountStatus?.Invoke(p.Ticket.WorldAccountId) ?? 0;

    private static ZoneInstance.Entity? Me(Player p) => p.Instance.Get(p.EntityId);

    private static ZoneInstance.Entity? Target(Player p) =>
        Me(p) is { PlayerTargetId: int t } && t != p.EntityId ? p.Instance.Get(t) : null;

    private static float? F(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : null;

    /// <summary>A # line: the command, if the account may use it.</summary>
    private void GmLine(NetPeer peer, Player player, string text)
    {
        var args = text[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0)
            return;
        var command = GmCommands.FirstOrDefault(c => string.Equals(c.Name, args[0], StringComparison.OrdinalIgnoreCase));
        if (command is null)
        {
            Tell(peer, $"Unknown command '#{args[0]}'. #help lists yours.");
            return;
        }
        int status = command.Status > 0 ? StatusOf(player) : 0;
        if (command.Status > status)
        {
            Tell(peer, "Your access level is not high enough to use this command.");
            return;
        }
        if (command.Status > 0)
            Log?.Invoke($"{player.Ticket.CharacterName} (status {status}): {text}");
        command.Run(peer, player, args);
    }
}
