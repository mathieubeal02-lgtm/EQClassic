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
    private readonly GroupRegistry _groups = new();

    /// <summary>The groups of every zone of this server.</summary>
    public GroupRegistry Groups => _groups;
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

    private readonly System.Diagnostics.Stopwatch _uptime = System.Diagnostics.Stopwatch.StartNew();
    /// <summary>Real seconds since the server started, the clock's time base.</summary>
    public double UptimeSeconds => _uptime.Elapsed.TotalSeconds;
    /// <summary>Norrath's clock (from time_of_day at start-up; the legacy World keeps that table).</summary>
    public EqClock Clock { get; set; } = new(8, 0, 0);
    public (int Day, int Month, int Year) Date { get; set; } = (1, 1, 3100);

    // type byte + tick (4) + count (2); each position is id + 4 floats.
    private const int PositionsHeaderSize = 7, PositionSize = 20;

    /// <summary>The characters' faction values (loaded on entry, saved after each kill). Optional.</summary>
    public IFactionValueStore? FactionValues { get; set; }

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

    /// <summary>A running zone, if booted (tests, administration).</summary>
    public ZoneInstance? Instance(string zone) => _instances.GetValueOrDefault(zone);

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
                    if (entered.Instance.Info is { } info)
                        Send(peer, info, DeliveryMethod.ReliableOrdered);
                    var (hour, minute) = Clock.TimeAt(UptimeSeconds);
                    Send(peer, new TimeOfDay(hour, minute, Date.Day, Date.Month, Date.Year), DeliveryMethod.ReliableOrdered);
                    if (entered.Instance.Get(entered.EntityId) is { } me)
                    {
                        Send(peer, new PlayerHealth(me.Hp, me.Fighter.MaxHp), DeliveryMethod.ReliableOrdered);
                        Send(peer, ExperienceOf(me), DeliveryMethod.ReliableOrdered);
                        Send(peer, InventoryOf(me), DeliveryMethod.ReliableOrdered);
                        Send(peer, SpellBookOf(entered.Instance, me), DeliveryMethod.ReliableOrdered);
                        Send(peer, new PlayerMana(me.Mana, me.MaxMana), DeliveryMethod.ReliableOrdered);
                        Send(peer, BuffsOf(me), DeliveryMethod.ReliableOrdered);
                        Send(peer, SkillsOf(me), DeliveryMethod.ReliableOrdered);
                        Send(peer, GroupOf(entered.Ticket.CharacterName), DeliveryMethod.ReliableOrdered);
                        Send(peer, new ZoneWeather(entered.Instance.Weather), DeliveryMethod.ReliableOrdered);
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

            case LootRequest loot when _players.TryGetValue(peer, out var looter):
                looter.Instance.OpenLoot(looter.EntityId, loot.CorpseId);
                Broadcast(looter.Instance, looter.Instance.DrainEvents());
                break;

            case LootTake take when _players.TryGetValue(peer, out var taker):
                taker.Instance.TakeLoot(taker.EntityId, take.CorpseId, take.Index);
                Broadcast(taker.Instance, taker.Instance.DrainEvents());
                break;

            case MoveItem move when _players.TryGetValue(peer, out var mover):
                mover.Instance.MoveItem(mover.EntityId, move.From, move.To);
                Broadcast(mover.Instance, mover.Instance.DrainEvents());
                break;

            case LootEnd end when _players.TryGetValue(peer, out var closer):
                closer.Instance.CloseLoot(closer.EntityId, end.CorpseId);
                Broadcast(closer.Instance, closer.Instance.DrainEvents());
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

            case MemorizeSpell memorize when _players.TryGetValue(peer, out var memorizer):
                memorizer.Instance.MemorizeSpell(memorizer.EntityId, memorize.Gem, memorize.SpellId);
                Broadcast(memorizer.Instance, memorizer.Instance.DrainEvents());
                break;

            case ScribeScroll scribe when _players.TryGetValue(peer, out var scriber):
                scriber.Instance.ScribeScroll(scriber.EntityId, scribe.Slot);
                Broadcast(scriber.Instance, scriber.Instance.DrainEvents());
                break;

            case CastSpell cast when _players.TryGetValue(peer, out var caster):
                caster.Instance.CastSpell(caster.EntityId, cast.Gem);
                Broadcast(caster.Instance, caster.Instance.DrainEvents());
                break;

            case GroupCommand group when _players.TryGetValue(peer, out var grouper) && group.Name.Length <= 64:
                Group(peer, grouper, group);
                break;

            case UseAbility ability when _players.TryGetValue(peer, out var user):
                user.Instance.UseAbility(user.EntityId, ability.Skill);
                Broadcast(user.Instance, user.Instance.DrainEvents());
                break;

            case MerchantRequest shop when _players.TryGetValue(peer, out var shopper):
                shopper.Instance.OpenMerchant(shopper.EntityId, shop.NpcId);
                Broadcast(shopper.Instance, shopper.Instance.DrainEvents());
                break;

            case MerchantBuy buy when _players.TryGetValue(peer, out var buyer):
                buyer.Instance.Buy(buyer.EntityId, buy.NpcId, buy.Index);
                Broadcast(buyer.Instance, buyer.Instance.DrainEvents());
                break;

            case MerchantSell sell when _players.TryGetValue(peer, out var seller):
                seller.Instance.Sell(seller.EntityId, sell.NpcId, sell.Slot);
                Broadcast(seller.Instance, seller.Instance.DrainEvents());
                break;

            case MerchantEnd shopEnd when _players.TryGetValue(peer, out var leaver):
                leaver.Instance.CloseMerchant(leaver.EntityId);
                Broadcast(leaver.Instance, leaver.Instance.DrainEvents());
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
            instance.Groups = _groups;
            _instances[ticket.Zone] = instance;
            Log?.Invoke($"zone {ticket.Zone} booted: {instance.Entities.Count()} NPC(s)");
        }
        var profile = ticket.Profile;
        var inventory = profile is null ? null : ZoneInstance.PlayerInventory.From(profile.Inventory, profile.Charges, profile.Coins, profile.BagItems, profile.BagCharges);
        // One skills array for the zone's skill-ups and the fighter builder below.
        var skills = profile is null ? null : Enumerable.Range(0, EQClassic.Server.Combat.SkillCaps.SkillCount).Select(profile.Skill).ToArray();
        var progress = profile is null ? null : new ZoneInstance.PlayerProgress(profile.Exp, profile.BindZone,
            new Vec3(profile.BindX, profile.BindY, profile.BindZ),
            // The fighter reads the inventory as it is when rebuilt (level up, equipment change).
            (level, bonuses) => EQClassic.Server.Combat.Combatant.ForPlayer(profile with { Level = level, Inventory = inventory!.Items.ToArray(), Skills = skills! }, Items, bonuses),
            inventory,
            new ZoneInstance.PlayerMagic(profile.Wis, profile.Int, skills!, profile.SpellBook, profile.SpellGemIds, profile.Mana,
                profile.Buffs.Select(b => new ZoneInstance.SavedBuff(b.SpellId, b.CasterLevel, b.Tics)).ToList()))
            {
                Deity = profile.Deity,
                Skills = skills,
                Str = profile.Str,
                Factions = FactionValues?.Load(ticket.CharacterName),
            };
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
                case ZoneInstance.LootShown shown when PeerOf(instance, shown.PlayerId) is { } looterPeer:
                    Send(looterPeer, new LootContents(shown.CorpseId, shown.Items.Select(i => View(i.ItemId, i.Charges)).ToList()), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.InventoryChanged changed when PeerOf(instance, changed.PlayerId) is { } ownerPeer && instance.Get(changed.PlayerId) is { } owner:
                    Send(ownerPeer, InventoryOf(owner), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.AppearanceChanged appearance:
                    SendToZone(instance, new EntityAppearance(appearance.EntityId, appearance.Sitting));
                    break;
                case ZoneInstance.Slain slain:
                    AnnounceDeath(instance, slain);
                    break;
                case ZoneInstance.CastStarted started when instance.Get(started.CasterId) is { } startedBy:
                    SendNear(instance, startedBy.Position, new SpellCast(started.CasterId, started.SpellId, started.SpellName, started.CastMs, SpellPhase.Begin));
                    break;
                case ZoneInstance.CastEnded ended when instance.Get(ended.CasterId) is { } endedBy:
                    SendNear(instance, endedBy.Position, new SpellCast(ended.CasterId, ended.SpellId, instance.SpellById(ended.SpellId)?.Name ?? "", 0,
                        ended.Outcome switch
                        {
                            ZoneInstance.CastOutcome.Fizzled => SpellPhase.Fizzled,
                            ZoneInstance.CastOutcome.Interrupted => SpellPhase.Interrupted,
                            ZoneInstance.CastOutcome.Resisted => SpellPhase.Resisted,
                            _ => SpellPhase.Finished,
                        }));
                    break;
                case ZoneInstance.SpellLanded landed:
                    AnnounceSpell(instance, landed);
                    break;
                case ZoneInstance.ManaChanged mana when PeerOf(instance, mana.PlayerId) is { } casterPeer:
                    Send(casterPeer, new PlayerMana(mana.Mana, mana.MaxMana), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.BuffsChanged buffs when PeerOf(instance, buffs.EntityId) is { } buffedPeer && instance.Get(buffs.EntityId) is { } buffed:
                    Send(buffedPeer, BuffsOf(buffed), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.BuffFaded faded when PeerOf(instance, faded.EntityId) is { } fadedPeer
                        && instance.SpellById(faded.SpellId) is { Fades.Length: > 0 } fadedSpell:
                    Send(fadedPeer, new ZoneMessage(fadedSpell.Fades), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.BindChanged bound when instance.Get(bound.PlayerId) is { } boundPlayer
                        && _players.Values.FirstOrDefault(p => p.Instance == instance && p.EntityId == bound.PlayerId) is { Ticket.Profile: not null } boundBy:
                    Characters?.SaveBind(boundBy.Ticket.CharacterName, boundPlayer.BindZone, boundPlayer.Bind.X, boundPlayer.Bind.Y, boundPlayer.Bind.Z);
                    break;
                case ZoneInstance.FactionsChanged factions when instance.Get(factions.PlayerId) is { } fighter
                        && _players.Values.FirstOrDefault(p => p.Instance == instance && p.EntityId == factions.PlayerId) is { } factionPlayer:
                    FactionValues?.Save(factionPlayer.Ticket.CharacterName, fighter.FactionValues);
                    break;
                case ZoneInstance.SkillsChanged changedSkills when PeerOf(instance, changedSkills.PlayerId) is { } skilledPeer && instance.Get(changedSkills.PlayerId) is { } skilled:
                    Send(skilledPeer, SkillsOf(skilled), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.WeatherChanged weather:
                    SendToZone(instance, new ZoneWeather(weather.Weather));
                    SendToZone(instance, new ZoneMessage(ZoneInstance.WeatherMessage(weather.Weather)));
                    break;
                case ZoneInstance.MerchantShown shown when PeerOf(instance, shown.PlayerId) is { } shopPeer:
                    Send(shopPeer, new MerchantGoods(shown.NpcId, shown.Goods.Select(id => View(id, 1)).ToList()), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.MerchantClosed closed when PeerOf(instance, closed.PlayerId) is { } closedPeer:
                    Send(closedPeer, new MerchantGoods(0, Array.Empty<ItemView>()), DeliveryMethod.ReliableOrdered);
                    break;
                case ZoneInstance.GemsChanged gems when PeerOf(instance, gems.PlayerId) is { } memPeer && instance.Get(gems.PlayerId) is { } memorizer:
                    Send(memPeer, SpellBookOf(instance, memorizer), DeliveryMethod.ReliableOrdered);
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
        if (chat.Channel == ChatChannel.Group)
        {
            var members = _groups.MembersOf(from);
            if (members.Count == 0)
            {
                Send(peer, new ZoneMessage("You are not in a group."), DeliveryMethod.ReliableOrdered);
                return;
            }
            var line = new ChatMessage(ChatChannel.Group, from, "", chat.Text);
            foreach (var (other, p) in _players)
                if (members.Contains(p.Ticket.CharacterName, StringComparer.OrdinalIgnoreCase))
                    Send(other, line, DeliveryMethod.ReliableOrdered);
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

    /// <summary>/invite, /follow, /decline, /disband and their messages; every member hears of a change.</summary>
    private void Group(NetPeer peer, Player player, GroupCommand command)
    {
        string me = player.Ticket.CharacterName;
        switch (command.Action)
        {
            case GroupAction.Invite:
                var (inviteePeer, invitee) = _players.FirstOrDefault(kv => string.Equals(kv.Value.Ticket.CharacterName, command.Name, StringComparison.OrdinalIgnoreCase));
                if (inviteePeer is null)
                {
                    Tell(peer, $"{command.Name} is not online at this time.");
                    return;
                }
                if (_groups.Invite(me, invitee.Ticket.CharacterName) is { } refusal)
                {
                    Tell(peer, refusal);
                    return;
                }
                Tell(peer, $"You invite {invitee.Ticket.CharacterName} to join your group.");
                Tell(inviteePeer, $"{me} invites you to join a group. (/follow to accept, /decline to refuse)");
                break;
            case GroupAction.Accept:
                if (_groups.Accept(me) is not { } members)
                {
                    Tell(peer, "You have not been invited to a group, or it is full.");
                    return;
                }
                foreach (var name in members)
                    if (PeerOfName(name) is { } memberPeer)
                        Tell(memberPeer, string.Equals(name, me, StringComparison.OrdinalIgnoreCase) ? "You have joined the group." : $"{me} has joined the group.");
                UpdateGroup(members);
                break;
            case GroupAction.Decline:
                if (_groups.Decline(me) is { } inviter && PeerOfName(inviter) is { } inviterPeer)
                    Tell(inviterPeer, $"{me} declines your invitation.");
                break;
            case GroupAction.Leave:
                LeaveGroup(me, peer);
                break;
        }
    }

    private void LeaveGroup(string name, NetPeer? peer)
    {
        if (_groups.MembersOf(name).Count == 0)
            return;
        var left = _groups.Leave(name);
        if (peer is not null)
        {
            Tell(peer, "You have left the group.");
            Send(peer, new GroupUpdate("", Array.Empty<string>()), DeliveryMethod.ReliableOrdered);
        }
        foreach (var m in left)
            if (PeerOfName(m) is { } memberPeer)
                Tell(memberPeer, left.Count == 1 ? $"{name} has left the group. Your group has been disbanded." : $"{name} has left the group.");
        UpdateGroup(left);
    }

    private void UpdateGroup(IReadOnlyList<string> names)
    {
        foreach (var name in names)
            if (PeerOfName(name) is { } memberPeer)
                Send(memberPeer, GroupOf(name), DeliveryMethod.ReliableOrdered);
    }

    private GroupUpdate GroupOf(string name) => new(_groups.LeaderOf(name) ?? "", _groups.MembersOf(name));

    private NetPeer? PeerOfName(string name) =>
        _players.FirstOrDefault(kv => string.Equals(kv.Value.Ticket.CharacterName, name, StringComparison.OrdinalIgnoreCase)).Key;

    private void Tell(NetPeer peer, string text) => Send(peer, new ZoneMessage(text), DeliveryMethod.ReliableOrdered);

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

    /// <summary>
    /// A spell took effect: the target reads the spell's "cast on you" line, the players around its
    /// "cast on other" line after the target's name; the caster of a damage spell reads the damage
    /// (the Trilogy client's "... was hit by non-melee for N points of damage.").
    /// </summary>
    private void AnnounceSpell(ZoneInstance instance, ZoneInstance.SpellLanded landed)
    {
        if (instance.SpellById(landed.SpellId) is not { } spell || instance.Get(landed.TargetId) is not { } target)
            return;
        string targetName = DisplayName(target.Name);
        foreach (var (peer, player) in _players)
        {
            if (player.Instance != instance || instance.Get(player.EntityId) is not { } p || Distance2(p.Position, target.Position) > CombatHearingRange * CombatHearingRange)
                continue;
            string? text = player.EntityId == landed.TargetId ? spell.CastOnYou
                : spell.CastOnOther.Length > 0 ? targetName + spell.CastOnOther : null;
            if (!string.IsNullOrEmpty(text))
                Send(peer, new ZoneMessage(text), DeliveryMethod.ReliableOrdered);
            if (player.EntityId == landed.CasterId && landed.Amount < 0 && landed.TargetId != landed.CasterId)
                Send(peer, new ZoneMessage($"{targetName} was hit by non-melee for {-landed.Amount} points of damage."), DeliveryMethod.ReliableOrdered);
        }
    }

    private static PlayerSkills SkillsOf(ZoneInstance.Entity p) =>
        new(p.Skills, ZoneInstance.ReuseSeconds.Keys.Where(s => ZoneInstance.CanUse(s, p.Fighter.Class)).OrderBy(s => s).ToList());

    private static PlayerBuffs BuffsOf(ZoneInstance.Entity p) =>
        new(p.Buffs.Select(b => new BuffView(b.Spell.Id, b.Spell.Name, b.TicsLeft, b.Spell.Beneficial)).ToList(),
            p.Bonuses.Rooted || p.Bonuses.Mezzed ? -100 : p.Bonuses.MovementSpeed, p.Bonuses.Levitating);

    private static SpellBook SpellBookOf(ZoneInstance instance, ZoneInstance.Entity p)
    {
        var spells = p.Book.Distinct().Select(instance.SpellById).OfType<EQClassic.Server.Spells.Spell>()
            .Select(s => new SpellView(s.Id, s.Name, s.LevelFor(p.Fighter.Class) ?? 0, s.Mana, s.CastTimeMs, s.Beneficial, s.MemIcon))
            .OrderBy(v => v.Level).ThenBy(v => v.Name, StringComparer.Ordinal)
            .ToList();
        return new SpellBook(spells, p.Gems);
    }

    /// <summary>NPC names as the client shows them: "a_rat01" → "a rat", digits dropped anywhere.</summary>
    public static string DisplayName(string name) => new string(name.Where(c => !char.IsAsciiDigit(c)).ToArray()).Replace('_', ' ').Trim();

    private void SendNear(ZoneInstance instance, Vec3 at, IMessage message)
    {
        float range2 = CombatHearingRange * CombatHearingRange;
        foreach (var (peer, player) in _players)
            if (player.Instance == instance && instance.Get(player.EntityId) is { } p && Distance2(p.Position, at) <= range2)
                Send(peer, message, DeliveryMethod.ReliableOrdered);
    }

    private ItemView View(int itemId, int charges) =>
        itemId == 0 ? new(0, "", charges) : Items?.Get(itemId) is { } item ? new(itemId, item.Name, charges, item.Price, item.IsContainer ? item.BagSlots : 0)
            : new(itemId, $"item #{itemId}", charges);

    private PlayerInventory InventoryOf(ZoneInstance.Entity p)
    {
        var inventory = p.Inventory ?? new ZoneInstance.PlayerInventory();
        var slots = Enumerable.Range(0, ZoneInstance.PlayerInventory.Slots).Select(i => View(inventory.Items[i], inventory.Charges[i])).ToList();
        var bags = Enumerable.Range(0, ZoneInstance.PlayerInventory.BagSlotsTotal).Select(i => View(inventory.BagItems[i], inventory.BagCharges[i])).ToList();
        var c = inventory.Coins;
        return new PlayerInventory(slots, c.Platinum, c.Gold, c.Silver, c.Copper, bags);
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
            ? p with
            {
                Level = now.Level, Exp = now.Exp, CurHp = now.Hp, Zone = crossed.Line.TargetZone, X = d.X, Y = d.Y, Z = d.Z,
                Inventory = now.Inventory?.Items.ToArray() ?? p.Inventory, Charges = now.Inventory?.Charges.ToArray() ?? p.Charges,
                Coins = now.Inventory?.Coins ?? p.Coins,
                BagItems = now.Inventory?.BagItems.ToArray() ?? p.BagItems, BagCharges = now.Inventory?.BagCharges.ToArray() ?? p.BagCharges,
                Mana = now.Mana, SpellBook = now.Book, SpellGemIds = now.Gems.ToArray(),
                Buffs = ZoneInstance.SaveBuffs(now).Select(b => (b.SpellId, b.CasterLevel, b.TicsLeft)).ToArray(),
                BindZone = now.BindZone, BindX = now.Bind.X, BindY = now.Bind.Y, BindZ = now.Bind.Z,
                Skills = now.Skills.ToArray(),
            }
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
        if (saveAs is null)
            LeaveGroup(player.Ticket.CharacterName, null); // camped or disconnected; zoning keeps the group
        var last = player.Instance.Get(player.EntityId)?.Position ?? player.Ticket.Position;
        var (zone, at) = saveAs ?? (player.Instance.ShortName, last);
        var state = player.Instance.Get(player.EntityId);
        Characters?.SavePosition(player.Ticket.CharacterName, zone, at.X, at.Y, at.Z, state?.Hp,
            player.Ticket.Profile is null ? null : state?.Exp, player.Ticket.Profile is null ? null : state?.Level);
        if (player.Ticket.Profile is not null && state?.Inventory is { } inventory)
            Characters?.SaveInventory(player.Ticket.CharacterName, inventory.Items, inventory.Charges, inventory.Coins, inventory.BagItems, inventory.BagCharges);
        if (player.Ticket.Profile is not null && state is not null)
            Characters?.SaveSkills(player.Ticket.CharacterName, state.Skills);
        if (player.Ticket.Profile is not null && state is not null)
            Characters?.SaveSpells(player.Ticket.CharacterName, state.Book, state.Gems, state.Mana,
                ZoneInstance.SaveBuffs(state).Select(b => (b.SpellId, b.CasterLevel, b.TicsLeft)).ToList());
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
