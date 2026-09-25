using System.Collections.Concurrent;
using EQClassic.Shared.Login;

namespace EQClassic.Server.Login;

/// <summary>
/// The worlds the login server lists, and the keys it hands out for them. In the legacy server a
/// world connects over TCP and receives ServerOP_LSClientAuth {account, key}; here World will ask
/// <see cref="TryRedeem"/> when the player arrives (milestone M2).
/// </summary>
public sealed class WorldDirectory
{
    private readonly ConcurrentDictionary<int, WorldServerInfo> _worlds = new();
    private readonly ConcurrentDictionary<(int AccountId, int WorldId), (string Key, DateTime Expires)> _pending = new();
    private readonly TimeSpan _keyLifetime;
    private readonly Func<DateTime> _now;

    public WorldDirectory(TimeSpan? keyLifetime = null, Func<DateTime>? now = null)
    {
        _keyLifetime = keyLifetime ?? TimeSpan.FromMinutes(1);
        _now = now ?? (() => DateTime.UtcNow);
    }

    public void Register(WorldServerInfo world) => _worlds[world.Id] = world;

    public IReadOnlyList<WorldServerInfo> List() => _worlds.Values.OrderBy(w => w.Id).ToList();

    /// <summary>Checks the world like Client::SendSessionKey / OP_RequestServerStatus, then issues a key.</summary>
    public PlayResponse RequestPlay(int accountId, int worldId)
    {
        if (!_worlds.TryGetValue(worldId, out var world))
            return PlayResponse.Refused(LoginMessages.WorldNotFound);
        if (world.Status == WorldStatus.Down)
            return PlayResponse.Refused(LoginMessages.WorldDown);
        if (world.Status == WorldStatus.Locked)
            return PlayResponse.Refused(LoginMessages.WorldLocked);

        var key = SessionKeys.New();
        _pending[(accountId, worldId)] = (key, _now() + _keyLifetime);
        return new PlayResponse(true, "", key, world.Address, world.Port);
    }

    /// <summary>World side: accepts a player once, with the key issued for this account and world.</summary>
    public bool TryRedeem(int accountId, int worldId, string key)
    {
        if (!_pending.TryGetValue((accountId, worldId), out var entry))
            return false;
        if (entry.Expires < _now() || !string.Equals(entry.Key, key, StringComparison.Ordinal))
            return false;
        return _pending.TryRemove(new KeyValuePair<(int, int), (string, DateTime)>((accountId, worldId), entry));
    }
}
