using EQClassic.Shared.Protocol;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;
using LiteNetLib;
using LiteNetLib.Utils;

namespace EQClassic.Server.Zone;

/// <summary>
/// Hosts every zone of the server in one process, on one UDP port: a player names the zone through
/// the ticket World issued, and the zone instance is booted on first use. <see cref="Tick"/> advances
/// every instance and sends each player what moved in their zone.
/// </summary>
public sealed class ZoneServer : IDisposable
{
    public const int DefaultPort = 7100; // the legacy zones use 7000+ under Wine
    public const string BadKey = "Your zone key has expired or is invalid. Please return to character select.";
    public const string UnknownZone = "That zone is unavailable.";

    private sealed class Player
    {
        public required ZoneInstance Instance;
        public required int EntityId;
        public required ZoneTicket Ticket;
    }

    private readonly ZoneKeys _keys;
    private readonly Func<string, ZoneInstance?> _boot;
    private readonly Dictionary<string, ZoneInstance> _instances = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<NetPeer, Player> _players = new();
    private readonly EventBasedNetListener _listener = new();
    private readonly NetManager _net;
    private readonly NetDataWriter _writer = new();

    public Action<string>? Log { get; set; }

    /// <summary>
    /// Players only receive the movement of entities this close (legacy NPC_UPDATE_RANGE is 400;
    /// a little more so entities do not pop at the edge of the view).
    /// </summary>
    public float UpdateRange { get; set; } = 600f;

    // type byte + tick (4) + count (2); each position is id + 4 floats.
    private const int PositionsHeaderSize = 7, PositionSize = 20;

    /// <summary>Where a player saves on leaving (camp, disconnect, zoning). Optional.</summary>
    public Characters.ICharacterStore? Characters { get; set; }

    /// <summary>Address clients reach this server at, sent in <see cref="ZoneChange"/>.</summary>
    public string PublicAddress { get; set; } = "127.0.0.1";

    /// <param name="boot">Creates a zone instance by short name, or null if the zone does not exist.</param>
    public ZoneServer(ZoneKeys keys, Func<string, ZoneInstance?> boot)
    {
        _keys = keys;
        _boot = boot;
        _net = new NetManager(_listener) { AutoRecycle = true };
        _listener.ConnectionRequestEvent += request => request.AcceptIfKey(ProtocolInfo.ConnectionKey);
        _listener.PeerDisconnectedEvent += (peer, _) => Leave(peer, null);
        _listener.NetworkReceiveEvent += OnReceive;
    }

    public void Start(int port = DefaultPort)
    {
        if (!_net.Start(port))
            throw new InvalidOperationException($"cannot listen on UDP port {port}");
    }

    public int Port => _net.LocalPort;
    public IReadOnlyCollection<ZoneInstance> Instances => _instances.Values;

    public void PollEvents() => _net.PollEvents();

    /// <summary>Advances every zone and broadcasts what happened: positions, spawns, deaths, zoning.</summary>
    public void Tick(float seconds)
    {
        foreach (var instance in _instances.Values.ToList())
        {
            var moved = instance.Tick(seconds);
            Broadcast(instance, instance.DrainEvents());
            if (moved.Count == 0)
                continue;
            float range2 = UpdateRange * UpdateRange;
            foreach (var (peer, player) in _players)
            {
                if (player.Instance != instance || instance.Get(player.EntityId) is not { } me)
                    continue;
                var positions = moved.Where(e => e.Id != player.EntityId && Distance2(e.Position, me.Position) <= range2)
                    .Select(e => new EntityPosition(e.Id, e.Position.X, e.Position.Y, e.Position.Z, e.Heading))
                    .ToList();
                // Unreliable, split to fit one datagram: a lost update is replaced by the next tick's,
                // and clients keep the newest tick per entity. (A whole busy zone in one message
                // exceeded LiteNetLib's 1020-byte unfragmented limit and threw.)
                int perPacket = Math.Max(1, (peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable) - PositionsHeaderSize) / PositionSize);
                for (int i = 0; i < positions.Count; i += perPacket)
                    Send(peer, new EntityPositions(instance.TickCount, positions.GetRange(i, Math.Min(perPacket, positions.Count - i))), DeliveryMethod.Unreliable);
            }
        }
    }

    public void Dispose() => _net.Stop();

    private static float Distance2(Vec3 a, Vec3 b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z);

    private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        IMessage message;
        try
        {
            message = MessageCodec.Read(reader);
        }
        catch (MessageFormatException)
        {
            peer.Disconnect();
            return;
        }

        switch (message)
        {
            case ZoneEnterRequest enter when !_players.ContainsKey(peer):
                Send(peer, Enter(peer, enter), DeliveryMethod.ReliableOrdered);
                break;

            case PlayerMove move when _players.TryGetValue(peer, out var player):
                var refused = player.Instance.MovePlayer(player.EntityId, new Vec3(move.X, move.Y, move.Z), move.Heading);
                if (refused is not null)
                {
                    var at = player.Instance.Get(player.EntityId)!.Position;
                    Log?.Invoke($"{peer.Address}: move refused ({refused})");
                    Send(peer, new MoveCorrection(at.X, at.Y, at.Z, refused), DeliveryMethod.ReliableOrdered);
                }
                else
                {
                    Broadcast(player.Instance, player.Instance.DrainEvents());
                }
                break;

            default:
                Log?.Invoke($"{peer.Address}: unexpected {message.Type}, disconnecting");
                peer.Disconnect();
                break;
        }
    }

    private ZoneEnterResponse Enter(NetPeer peer, ZoneEnterRequest enter)
    {
        var ticket = _keys.TryRedeem(enter.CharacterName, enter.ZoneKey);
        if (ticket is null)
            return ZoneEnterResponse.Refused(BadKey);
        if (!_instances.TryGetValue(ticket.Zone, out var instance))
        {
            instance = _boot(ticket.Zone);
            if (instance is null)
                return ZoneEnterResponse.Refused(UnknownZone);
            _instances[ticket.Zone] = instance;
            Log?.Invoke($"zone {ticket.Zone} booted: {instance.Entities.Count()} NPC(s)");
        }
        var entity = instance.AddPlayer(ticket.CharacterName, ticket.Race, ticket.Gender, ticket.Level, ticket.Position);
        _players[peer] = new Player { Instance = instance, EntityId = entity.Id, Ticket = ticket };
        foreach (var (other, p) in _players)
            if (other != peer && p.Instance == instance)
                Send(other, new EntitySpawned(entity.ToSpawn()), DeliveryMethod.ReliableOrdered);
        Log?.Invoke($"{peer.Address}: {ticket.CharacterName} entered {ticket.Zone} as entity {entity.Id}");
        return new ZoneEnterResponse(true, "", instance.ShortName, entity.Id, instance.Entities.Select(e => e.ToSpawn()).ToList());
    }

    private void Broadcast(ZoneInstance instance, IReadOnlyList<ZoneInstance.ZoneEvent> events)
    {
        foreach (var ev in events)
        {
            switch (ev)
            {
                case ZoneInstance.Spawned spawned:
                    SendToZone(instance, new EntitySpawned(spawned.Entity.ToSpawn()));
                    break;
                case ZoneInstance.Removed removed:
                    SendToZone(instance, new EntityRemoved(removed.EntityId));
                    break;
                case ZoneInstance.Engaged engaged:
                    Log?.Invoke($"{instance.ShortName}: {instance.Get(engaged.NpcId)?.Name} aggroes {instance.Get(engaged.PlayerId)?.Name}");
                    break;
                case ZoneInstance.CrossedZoneLine crossed:
                    ChangeZone(instance, crossed);
                    break;
            }
        }
    }

    private void ChangeZone(ZoneInstance instance, ZoneInstance.CrossedZoneLine crossed)
    {
        var (peer, player) = _players.FirstOrDefault(kv => kv.Value.Instance == instance && kv.Value.EntityId == crossed.PlayerId);
        if (peer is null)
            return;
        var d = crossed.Destination;
        var ticket = player.Ticket with { Zone = crossed.Line.TargetZone, Position = d };
        var key = _keys.Issue(ticket);
        Log?.Invoke($"{peer.Address}: {ticket.CharacterName} zones {instance.ShortName} -> {ticket.Zone}");
        Send(peer, new ZoneChange(ticket.Zone, PublicAddress, Port, key, d.X, d.Y, d.Z), DeliveryMethod.ReliableOrdered);
        Leave(peer, saveAs: (ticket.Zone, d));
    }

    private void SendToZone(ZoneInstance instance, IMessage message)
    {
        foreach (var (peer, player) in _players)
            if (player.Instance == instance)
                Send(peer, message, DeliveryMethod.ReliableOrdered);
    }

    private void Leave(NetPeer peer, (string Zone, Vec3 Position)? saveAs = null)
    {
        if (!_players.Remove(peer, out var player))
            return;
        var last = player.Instance.Get(player.EntityId)?.Position ?? player.Ticket.Position;
        var (zone, at) = saveAs ?? (player.Instance.ShortName, last);
        Characters?.SavePosition(player.Ticket.CharacterName, zone, at.X, at.Y, at.Z);
        player.Instance.RemovePlayer(player.EntityId);
        foreach (var (other, p) in _players)
            if (p.Instance == player.Instance)
                Send(other, new EntityRemoved(player.EntityId), DeliveryMethod.ReliableOrdered);
    }

    private void Send(NetPeer peer, IMessage message, DeliveryMethod method)
    {
        _writer.Reset();
        MessageCodec.Write(_writer, message);
        peer.Send(_writer, method);
    }
}
