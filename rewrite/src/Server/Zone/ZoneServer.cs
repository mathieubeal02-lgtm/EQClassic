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
    /// <summary>Items for the players' weapons and armour (melee); null: bare hands, no AC.</summary>
    public EQClassic.Server.Combat.IItemSource? Items { get; set; }
    /// <summary>Combat events reach the players this close to the fight.</summary>
    public float CombatHearingRange { get; set; } = 200f;

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
                var response = Enter(peer, enter);
                Send(peer, response, DeliveryMethod.ReliableOrdered);
                if (response.Accepted && _players.TryGetValue(peer, out var entered))
                {
                    Send(peer, DoorsOf(entered.Instance), DeliveryMethod.ReliableOrdered); // after the entry: same channel, in order
                    if (entered.Instance.Get(entered.EntityId) is { } me)
                    {
                        Send(peer, new PlayerHealth(me.Hp, me.Fighter.MaxHp), DeliveryMethod.ReliableOrdered);
                        Send(peer, ExperienceOf(me), DeliveryMethod.ReliableOrdered);
                    }
                }
                break;

            case SetTarget target when _players.TryGetValue(peer, out var targeter):
                targeter.Instance.SetTarget(targeter.EntityId, target.EntityId == 0 ? null : target.EntityId);
                Broadcast(targeter.Instance, targeter.Instance.DrainEvents());
                break;

            case ChatSend chat when _players.TryGetValue(peer, out var speaker) && chat.Text.Length is > 0 and <= MaxChatLength:
                Chat(peer, speaker, chat);
                break;

            case WhoRequest when _players.TryGetValue(peer, out var asker):
                Who(peer, asker);
                break;

            case ConsiderRequest consider when _players.TryGetValue(peer, out var considerer):
                considerer.Instance.Consider(considerer.EntityId, consider.EntityId);
                Broadcast(considerer.Instance, considerer.Instance.DrainEvents());
                break;

            case SetSitting sit when _players.TryGetValue(peer, out var sitter):
                sitter.Instance.SetSitting(sitter.EntityId, sit.Sitting);
                Broadcast(sitter.Instance, sitter.Instance.DrainEvents());
                break;

            case AutoAttack attack when _players.TryGetValue(peer, out var attacker):
                attacker.Instance.SetAutoAttack(attacker.EntityId, attack.On);
                Broadcast(attacker.Instance, attacker.Instance.DrainEvents());
                break;

            case ClickDoor click when _players.TryGetValue(peer, out var clicker):
                clicker.Instance.ClickDoor(clicker.EntityId, click.DoorId);
                Broadcast(clicker.Instance, clicker.Instance.DrainEvents());
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
        var profile = ticket.Profile;
        var progress = profile is null ? null : new ZoneInstance.PlayerProgress(profile.Exp, profile.BindZone,
            new Vec3(profile.BindX, profile.BindY, profile.BindZ),
            level => EQClassic.Server.Combat.Combatant.ForPlayer(profile with { Level = level }, Items));
        var entity = instance.AddPlayer(ticket.CharacterName, ticket.Race, ticket.Gender, profile?.Level ?? ticket.Level, ticket.Position,
            profile?.Heading ?? 0, null, profile?.CurHp, progress);
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
                case ZoneInstance.DoorChanged door:
                    SendToZone(instance, new DoorState(door.DoorId, door.Open));
                    break;
                case ZoneInstance.Teleported teleported when PeerOf(instance, teleported.PlayerId) is { } moved:
                    var to = teleported.Destination;
                    Send(moved, new MoveCorrection(to.X, to.Y, to.Z, teleported.Reason), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.Told told when PeerOf(instance, told.PlayerId) is { } listener:
                    Send(listener, new ZoneMessage(told.Text), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.Swung swung when instance.Get(swung.DefenderId) is { } defender:
                    SendNear(instance, defender.Position, new CombatEvent(swung.AttackerId, swung.DefenderId, swung.Damage, swung.DefenderHpPercent));
                    break;
                case ZoneInstance.HealthChanged health when PeerOf(instance, health.PlayerId) is { } hurt:
                    Send(hurt, new PlayerHealth(health.Hp, health.MaxHp), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.Considered considered when PeerOf(instance, considered.PlayerId) is { } asker:
                    Send(asker, new ConsiderResult(considered.EntityId, considered.Standing, considered.Con), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.ExperienceChanged xp when PeerOf(instance, xp.PlayerId) is { } learner && instance.Get(xp.PlayerId) is { } who:
                    Send(learner, ExperienceOf(who), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.AppearanceChanged appearance:
                    SendToZone(instance, new EntityAppearance(appearance.EntityId, appearance.Sitting));
                    break;
                case ZoneInstance.Slain slain:
                    AnnounceDeath(instance, slain);
                    break;
            }
        }
    }

    public const int MaxChatLength = 512;
    /// <summary>Say and emotes reach this far (units).</summary>
    public const float SayRange = 200f;

    /// <summary>
    /// Say and emotes to the players near the speaker, shout / OOC / auction to the whole zone,
    /// tells to the named character in any zone of this server; the speaker gets the echo.
    /// </summary>
    private void Chat(NetPeer peer, Player speaker, ChatSend chat)
    {
        string from = speaker.Ticket.CharacterName;
        if (chat.Channel == ChatChannel.Tell)
        {
            var (to, recipient) = _players.FirstOrDefault(kv => string.Equals(kv.Value.Ticket.CharacterName, chat.To, StringComparison.OrdinalIgnoreCase));
            if (to is null)
            {
                Send(peer, new ZoneMessage($"{chat.To} is not online at this time."), DeliveryMethod.ReliableOrdered);
                return;
            }
            var tell = new ChatMessage(ChatChannel.Tell, from, recipient.Ticket.CharacterName, chat.Text);
            Send(to, tell, DeliveryMethod.ReliableOrdered);
            if (to != peer)
                Send(peer, tell, DeliveryMethod.ReliableOrdered);
            return;
        }
        var message = new ChatMessage(chat.Channel, from, "", chat.Text);
        if (chat.Channel is ChatChannel.Say or ChatChannel.Emote)
        {
            var at = speaker.Instance.Get(speaker.EntityId)?.Position ?? speaker.Ticket.Position;
            foreach (var (other, p) in _players)
                if (p.Instance == speaker.Instance && p.Instance.Get(p.EntityId) is { } e && Distance2(e.Position, at) <= SayRange * SayRange)
                    Send(other, message, DeliveryMethod.ReliableOrdered);
        }
        else
        {
            SendToZone(speaker.Instance, message);
        }
    }

    private static readonly string[] ClassNames = ["", "Warrior", "Cleric", "Paladin", "Ranger", "Shadow Knight", "Druid", "Monk", "Bard",
        "Rogue", "Shaman", "Necromancer", "Wizard", "Magician", "Enchanter", "Beastlord"];

    private static string RaceName(int race) => race switch
    {
        1 => "Human", 2 => "Barbarian", 3 => "Erudite", 4 => "Wood Elf", 5 => "High Elf", 6 => "Dark Elf", 7 => "Half Elf",
        8 => "Dwarf", 9 => "Troll", 10 => "Ogre", 11 => "Halfling", 12 => "Gnome", 128 => "Iksar", _ => "Unknown",
    };

    /// <summary>/who: the players in the asker's zone, "[level class] name (race)".</summary>
    private void Who(NetPeer peer, Player asker)
    {
        var here = _players.Values.Where(p => p.Instance == asker.Instance)
            .Select(p => p.Instance.Get(p.EntityId)).Where(e => e is not null).OrderBy(e => e!.Name).ToList();
        Send(peer, new ZoneMessage("Players in EverQuest:"), DeliveryMethod.ReliableOrdered);
        Send(peer, new ZoneMessage("---------------------------"), DeliveryMethod.ReliableOrdered);
        foreach (var e in here)
        {
            int c = e!.Fighter.Class;
            Send(peer, new ZoneMessage($"[{e.Level} {(c > 0 && c < ClassNames.Length ? ClassNames[c] : "Unknown")}] {e.Name} ({RaceName(e.Race)})"), DeliveryMethod.ReliableOrdered);
        }
        Send(peer, new ZoneMessage(here.Count == 1 ? $"There is 1 player in {asker.Instance.ShortName}."
            : $"There are {here.Count} players in {asker.Instance.ShortName}."), DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Legacy death messages: the killer, the victim, then everyone near.</summary>
    private void AnnounceDeath(ZoneInstance instance, ZoneInstance.Slain slain)
    {
        string victim = DisplayName(slain.VictimName), killer = DisplayName(slain.KillerName);
        foreach (var (peer, player) in _players)
        {
            if (player.Instance != instance)
                continue;
            string? text = player.EntityId == slain.KillerId ? $"You have slain {victim}!"
                : player.EntityId == slain.VictimId ? $"You have been slain by {killer}!"
                : instance.Get(player.EntityId) is { } p && instance.Get(slain.KillerId) is { } k && Distance2(p.Position, k.Position) <= CombatHearingRange * CombatHearingRange
                    ? $"{victim} has been slain by {killer}!" : null;
            if (text is not null)
                Send(peer, new ZoneMessage(text), DeliveryMethod.ReliableOrdered);
        }
    }

    /// <summary>NPC names as the client shows them: "a_rat01" → "a rat".</summary>
    public static string DisplayName(string name) => name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Replace('_', ' ').Trim();

    private void SendNear(ZoneInstance instance, Vec3 at, IMessage message)
    {
        float range2 = CombatHearingRange * CombatHearingRange;
        foreach (var (peer, player) in _players)
            if (player.Instance == instance && instance.Get(player.EntityId) is { } p && Distance2(p.Position, at) <= range2)
                Send(peer, message, DeliveryMethod.ReliableOrdered);
    }

    private static PlayerExperience ExperienceOf(ZoneInstance.Entity p) =>
        new(p.Exp, EQClassic.Server.Combat.Experience.ForLevel(p.Level, p.Fighter.Class, p.Race),
            p.Level >= EQClassic.Server.Combat.Experience.MaxLevel ? uint.MaxValue : EQClassic.Server.Combat.Experience.ForLevel(p.Level + 1, p.Fighter.Class, p.Race),
            p.Level);

    private NetPeer? PeerOf(ZoneInstance instance, int entityId) =>
        _players.FirstOrDefault(kv => kv.Value.Instance == instance && kv.Value.EntityId == entityId).Key;

    private static ZoneDoors DoorsOf(ZoneInstance instance) => new(instance.Doors
        .Select(d => new DoorInfo(d.Door.Id, d.Door.Name, d.Door.Position.X, d.Door.Position.Y, d.Door.Position.Z,
            d.Door.Heading, d.Door.OpenType, d.Door.Size, d.Open))
        .ToList());

    private void ChangeZone(ZoneInstance instance, ZoneInstance.CrossedZoneLine crossed)
    {
        var (peer, player) = _players.FirstOrDefault(kv => kv.Value.Instance == instance && kv.Value.EntityId == crossed.PlayerId);
        if (peer is null)
            return;
        var d = crossed.Destination;
        // The next zone starts from the character as it is now (level, experience, hit points), not as World read it.
        var now = instance.Get(player.EntityId);
        var profile = player.Ticket.Profile is { } p && now is not null
            ? p with { Level = now.Level, Exp = now.Exp, CurHp = now.Hp, Zone = crossed.Line.TargetZone, X = d.X, Y = d.Y, Z = d.Z }
            : player.Ticket.Profile;
        var ticket = player.Ticket with { Zone = crossed.Line.TargetZone, Position = d, Level = now?.Level ?? player.Ticket.Level, Profile = profile };
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
        var state = player.Instance.Get(player.EntityId);
        Characters?.SavePosition(player.Ticket.CharacterName, zone, at.X, at.Y, at.Z, state?.Hp,
            player.Ticket.Profile is null ? null : state?.Exp, player.Ticket.Profile is null ? null : state?.Level);
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
