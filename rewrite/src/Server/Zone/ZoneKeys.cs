using System.Collections.Concurrent;
using EQClassic.Server.Login;
using EQClassic.Shared.World;

namespace EQClassic.Server.Zone;

/// <summary>What World tells the zone about an arriving character (legacy: World and zone both read the profile).</summary>
/// <param name="Profile">The character's profile as World read it (class, stats, skills, items, HP for combat); null in some tests.</param>
public sealed record ZoneTicket(string CharacterName, string Zone, int WorldAccountId, int Race, int Gender, int Level, Vec3 Position,
    EQClassic.Server.Characters.PlayerProfile? Profile = null);

/// <summary>World-to-zone hand-off: a single-use key per character, valid for a short time.</summary>
public sealed class ZoneKeys
{
    private readonly ConcurrentDictionary<string, (string Key, ZoneTicket Ticket, DateTime Expires)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _lifetime;
    private readonly Func<DateTime> _now;

    public ZoneKeys(TimeSpan? lifetime = null, Func<DateTime>? now = null)
    {
        _lifetime = lifetime ?? TimeSpan.FromMinutes(1);
        _now = now ?? (() => DateTime.UtcNow);
    }

    public string Issue(ZoneTicket ticket)
    {
        var key = WorldKeys.New();
        _pending[ticket.CharacterName] = (key, ticket, _now() + _lifetime);
        return key;
    }

    public ZoneTicket? TryRedeem(string characterName, string key)
    {
        if (!_pending.TryGetValue(characterName, out var entry) || entry.Expires < _now()
            || !string.Equals(entry.Key, key, StringComparison.Ordinal))
            return null;
        return _pending.TryRemove(new KeyValuePair<string, (string, ZoneTicket, DateTime)>(characterName, entry)) ? entry.Ticket : null;
    }
}
