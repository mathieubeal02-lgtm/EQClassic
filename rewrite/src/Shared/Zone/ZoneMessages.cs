using System.Collections.Generic;
using System.Linq;
using EQClassic.Shared.Protocol;
using LiteNetLib.Utils;

namespace EQClassic.Shared.Zone
{
    /// <summary>Client to zone server: enter with the key World issued for this character.</summary>
    public sealed record ZoneEnterRequest(string CharacterName, string ZoneKey) : IMessage
    {
        public MessageType Type => MessageType.ZoneEnterRequest;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(CharacterName);
            writer.Put(ZoneKey);
        }

        public static ZoneEnterRequest ReadFields(NetDataReader reader) => new ZoneEnterRequest(reader.GetString(), reader.GetString());
    }

    /// <summary>An entity as a client first sees it (legacy NewSpawn / zone spawn list).</summary>
    public sealed record EntitySpawn(int Id, string Name, bool IsPlayer, int Race, int Gender, int Level, float Size, float X, float Y, float Z, float Heading,
        bool IsCorpse = false);

    public sealed record ZoneEnterResponse(bool Accepted, string Message, string Zone, int YourEntityId, IReadOnlyList<EntitySpawn> Entities) : IMessage
    {
        public MessageType Type => MessageType.ZoneEnterResponse;

        public static ZoneEnterResponse Refused(string message) => new ZoneEnterResponse(false, message, "", 0, new EntitySpawn[0]);

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Accepted);
            writer.Put(Message);
            writer.Put(Zone);
            writer.Put(YourEntityId);
            writer.Put((ushort)Entities.Count);
            foreach (var e in Entities)
            {
                writer.Put(e.Id);
                writer.Put(e.Name);
                writer.Put(e.IsPlayer);
                writer.Put((ushort)e.Race);
                writer.Put((byte)e.Gender);
                writer.Put((byte)e.Level);
                writer.Put(e.Size);
                writer.Put(e.X);
                writer.Put(e.Y);
                writer.Put(e.Z);
                writer.Put(e.Heading);
                writer.Put(e.IsCorpse);
            }
        }

        public static ZoneEnterResponse ReadFields(NetDataReader reader)
        {
            bool accepted = reader.GetBool();
            string message = reader.GetString(), zone = reader.GetString();
            int you = reader.GetInt();
            int count = reader.GetUShort();
            var list = new List<EntitySpawn>(count);
            for (int i = 0; i < count; i++)
                list.Add(new EntitySpawn(reader.GetInt(), reader.GetString(), reader.GetBool(), reader.GetUShort(), reader.GetByte(), reader.GetByte(),
                    reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetBool()));
            return new ZoneEnterResponse(accepted, message, zone, you, list);
        }

        public bool Equals(ZoneEnterResponse? other) =>
            other != null && Accepted == other.Accepted && Message == other.Message && Zone == other.Zone
            && YourEntityId == other.YourEntityId && Entities.SequenceEqual(other.Entities);

        public override int GetHashCode() => Entities.Count;
    }

    /// <summary>Client to zone: where the player is now. Sent unreliable-sequenced; the server may refuse it.</summary>
    public sealed record PlayerMove(float X, float Y, float Z, float Heading) : IMessage
    {
        public MessageType Type => MessageType.PlayerMove;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(X);
            writer.Put(Y);
            writer.Put(Z);
            writer.Put(Heading);
        }

        public static PlayerMove ReadFields(NetDataReader reader) => new PlayerMove(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
    }

    public readonly struct EntityPosition
    {
        public EntityPosition(int id, float x, float y, float z, float heading)
        {
            Id = id; X = x; Y = y; Z = z; Heading = heading;
        }

        public int Id { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Heading { get; }
    }

    /// <summary>
    /// Zone to client, every tick something nearby moved (legacy OP_MobUpdate). Unreliable and split
    /// into several messages per tick when many entities move; keep the newest Tick per entity.
    /// </summary>
    public sealed record EntityPositions(uint Tick, IReadOnlyList<EntityPosition> Positions) : IMessage
    {
        public MessageType Type => MessageType.EntityPositions;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Tick);
            writer.Put((ushort)Positions.Count);
            foreach (var p in Positions)
            {
                writer.Put(p.Id);
                writer.Put(p.X);
                writer.Put(p.Y);
                writer.Put(p.Z);
                writer.Put(p.Heading);
            }
        }

        public static EntityPositions ReadFields(NetDataReader reader)
        {
            uint tick = reader.GetUInt();
            int count = reader.GetUShort();
            var list = new List<EntityPosition>(count);
            for (int i = 0; i < count; i++)
                list.Add(new EntityPosition(reader.GetInt(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat()));
            return new EntityPositions(tick, list);
        }

        public bool Equals(EntityPositions? other) => other != null && Tick == other.Tick && Positions.SequenceEqual(other.Positions);
        public override int GetHashCode() => (int)Tick;
    }

    /// <summary>Zone to client: that move was refused (too fast, through the world...); here is where you are.</summary>
    public sealed record MoveCorrection(float X, float Y, float Z, string Reason) : IMessage
    {
        public MessageType Type => MessageType.MoveCorrection;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(X);
            writer.Put(Y);
            writer.Put(Z);
            writer.Put(Reason);
        }

        public static MoveCorrection ReadFields(NetDataReader reader) => new MoveCorrection(reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetString());
    }

    /// <summary>Zone to client: an entity appeared (a player entered, an NPC spawned).</summary>
    public sealed record EntitySpawned(EntitySpawn Entity) : IMessage
    {
        public MessageType Type => MessageType.EntitySpawned;

        public void WriteFields(NetDataWriter writer) =>
            new ZoneEnterResponse(true, "", "", 0, new[] { Entity }).WriteFields(writer);

        public static EntitySpawned ReadFields(NetDataReader reader)
        {
            var list = ZoneEnterResponse.ReadFields(reader).Entities;
            if (list.Count != 1)
                throw new MessageFormatException("EntitySpawned carries exactly one entity");
            return new EntitySpawned(list[0]);
        }
    }

    /// <summary>Zone to client: an entity is gone (player left, NPC died or despawned).</summary>
    public sealed record EntityRemoved(int Id) : IMessage
    {
        public MessageType Type => MessageType.EntityRemoved;
        public void WriteFields(NetDataWriter writer) => writer.Put(Id);
        public static EntityRemoved ReadFields(NetDataReader reader) => new EntityRemoved(reader.GetInt());
    }

    /// <summary>
    /// Zone to client: you crossed a zone line. Reconnect to Address:Port and send a
    /// <see cref="ZoneEnterRequest"/> with this key; you arrive at (X, Y, Z) in Zone.
    /// </summary>
    public sealed record ZoneChange(string Zone, string Address, int Port, string ZoneKey, float X, float Y, float Z) : IMessage
    {
        public MessageType Type => MessageType.ZoneChange;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Zone);
            writer.Put(Address);
            writer.Put(Port);
            writer.Put(ZoneKey);
            writer.Put(X);
            writer.Put(Y);
            writer.Put(Z);
        }

        public static ZoneChange ReadFields(NetDataReader reader) =>
            new ZoneChange(reader.GetString(), reader.GetString(), reader.GetInt(), reader.GetString(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
    }

    /// <summary>
    /// A door as the client draws it (legacy doors table): position in EverQuest coordinates,
    /// heading in the table's 0-512 units, size in percent. OpenType 54 doors are invisible
    /// (click spots, usually teleports).
    /// </summary>
    public sealed record DoorInfo(int Id, string Name, float X, float Y, float Z, float Heading, int OpenType, int Size, bool Open);

    /// <summary>Zone server to client after entering: the zone's doors and their state. Reliable (fragmented when large).</summary>
    public sealed record ZoneDoors(IReadOnlyList<DoorInfo> Doors) : IMessage
    {
        public MessageType Type => MessageType.ZoneDoors;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((ushort)Doors.Count);
            foreach (var d in Doors)
            {
                writer.Put(d.Id);
                writer.Put(d.Name);
                writer.Put(d.X);
                writer.Put(d.Y);
                writer.Put(d.Z);
                writer.Put(d.Heading);
                writer.Put((byte)d.OpenType);
                writer.Put((ushort)d.Size);
                writer.Put(d.Open);
            }
        }

        public static ZoneDoors ReadFields(NetDataReader reader)
        {
            int count = reader.GetUShort();
            var doors = new List<DoorInfo>(count);
            for (int i = 0; i < count; i++)
                doors.Add(new DoorInfo(reader.GetInt(), reader.GetString(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat(),
                    reader.GetFloat(), reader.GetByte(), reader.GetUShort(), reader.GetBool()));
            return new ZoneDoors(doors);
        }
    }

    /// <summary>Client to zone server: use a door (legacy OP_ClickDoor).</summary>
    public sealed record ClickDoor(int DoorId) : IMessage
    {
        public MessageType Type => MessageType.ClickDoor;
        public void WriteFields(NetDataWriter writer) => writer.Put(DoorId);
        public static ClickDoor ReadFields(NetDataReader reader) => new ClickDoor(reader.GetInt());
    }

    /// <summary>Zone server to every client in the zone: a door opened or closed (legacy OP_OpenDoor).</summary>
    public sealed record DoorState(int DoorId, bool Open) : IMessage
    {
        public MessageType Type => MessageType.DoorState;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(DoorId);
            writer.Put(Open);
        }

        public static DoorState ReadFields(NetDataReader reader) => new DoorState(reader.GetInt(), reader.GetBool());
    }

    /// <summary>Zone server to one client: a line of text for the chat window ("The door swings open!").</summary>
    public sealed record ZoneMessage(string Text) : IMessage
    {
        public MessageType Type => MessageType.ZoneMessage;
        public void WriteFields(NetDataWriter writer) => writer.Put(Text);
        public static ZoneMessage ReadFields(NetDataReader reader) => new ZoneMessage(reader.GetString());
    }

    /// <summary>Client to zone server: target an entity (0 clears the target).</summary>
    public sealed record SetTarget(int EntityId) : IMessage
    {
        public MessageType Type => MessageType.SetTarget;
        public void WriteFields(NetDataWriter writer) => writer.Put(EntityId);
        public static SetTarget ReadFields(NetDataReader reader) => new SetTarget(reader.GetInt());
    }

    /// <summary>Client to zone server: auto-attack the target, or stop (legacy OP_AutoAttack).</summary>
    public sealed record AutoAttack(bool On) : IMessage
    {
        public MessageType Type => MessageType.AutoAttack;
        public void WriteFields(NetDataWriter writer) => writer.Put(On);
        public static AutoAttack ReadFields(NetDataReader reader) => new AutoAttack(reader.GetBool());
    }

    /// <summary>
    /// Zone server to the clients near the fight: one melee swing. Damage 0 is a miss; the
    /// defender's hit points after it, in percent (target window).
    /// </summary>
    public sealed record CombatEvent(int AttackerId, int DefenderId, int Damage, int DefenderHpPercent) : IMessage
    {
        public MessageType Type => MessageType.CombatEvent;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(AttackerId);
            writer.Put(DefenderId);
            writer.Put(Damage);
            writer.Put((byte)DefenderHpPercent);
        }

        public static CombatEvent ReadFields(NetDataReader reader) =>
            new CombatEvent(reader.GetInt(), reader.GetInt(), reader.GetInt(), reader.GetByte());
    }

    /// <summary>Zone server to one client: their own hit points.</summary>
    public sealed record PlayerHealth(int Hp, int MaxHp) : IMessage
    {
        public MessageType Type => MessageType.PlayerHealth;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Hp);
            writer.Put(MaxHp);
        }

        public static PlayerHealth ReadFields(NetDataReader reader) => new PlayerHealth(reader.GetInt(), reader.GetInt());
    }

    /// <summary>Client to zone server: consider an entity (legacy OP_Consider).</summary>
    public sealed record ConsiderRequest(int EntityId) : IMessage
    {
        public MessageType Type => MessageType.ConsiderRequest;
        public void WriteFields(NetDataWriter writer) => writer.Put(EntityId);
        public static ConsiderRequest ReadFields(NetDataReader reader) => new ConsiderRequest(reader.GetInt());
    }

    /// <summary>Zone server to the client: the standing and colour it words (<see cref="ConsiderRules.Message"/>).</summary>
    public sealed record ConsiderResult(int EntityId, Standing Standing, ConColor Con) : IMessage
    {
        public MessageType Type => MessageType.ConsiderResult;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(EntityId);
            writer.Put((byte)Standing);
            writer.Put((byte)Con);
        }

        public static ConsiderResult ReadFields(NetDataReader reader) =>
            new ConsiderResult(reader.GetInt(), (Standing)reader.GetByte(), (ConColor)reader.GetByte());
    }

    /// <summary>Client to zone server: sit down or stand up (legacy OP_SpawnAppearance).</summary>
    public sealed record SetSitting(bool Sitting) : IMessage
    {
        public MessageType Type => MessageType.SetSitting;
        public void WriteFields(NetDataWriter writer) => writer.Put(Sitting);
        public static SetSitting ReadFields(NetDataReader reader) => new SetSitting(reader.GetBool());
    }

    /// <summary>Zone server to the clients: an entity sat down or stood up.</summary>
    public sealed record EntityAppearance(int EntityId, bool Sitting) : IMessage
    {
        public MessageType Type => MessageType.EntityAppearance;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(EntityId);
            writer.Put(Sitting);
        }

        public static EntityAppearance ReadFields(NetDataReader reader) => new EntityAppearance(reader.GetInt(), reader.GetBool());
    }

    public enum ChatChannel : byte { Say = 0, Shout = 1, Ooc = 2, Auction = 3, Tell = 4, Emote = 5 }

    /// <summary>Client to zone server: a chat line (To is the tell recipient).</summary>
    public sealed record ChatSend(ChatChannel Channel, string To, string Text) : IMessage
    {
        public MessageType Type => MessageType.ChatSend;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((byte)Channel);
            writer.Put(To);
            writer.Put(Text);
        }

        public static ChatSend ReadFields(NetDataReader reader) => new ChatSend((ChatChannel)reader.GetByte(), reader.GetString(), reader.GetString());
    }

    /// <summary>Zone server to the clients that hear it (the sender too, for the echo).</summary>
    public sealed record ChatMessage(ChatChannel Channel, string From, string To, string Text) : IMessage
    {
        public MessageType Type => MessageType.ChatMessage;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((byte)Channel);
            writer.Put(From);
            writer.Put(To);
            writer.Put(Text);
        }

        public static ChatMessage ReadFields(NetDataReader reader) =>
            new ChatMessage((ChatChannel)reader.GetByte(), reader.GetString(), reader.GetString(), reader.GetString());
    }

    /// <summary>Client to zone server: /who (the answer comes as ZoneMessage lines).</summary>
    public sealed record WhoRequest : IMessage
    {
        public MessageType Type => MessageType.WhoRequest;
        public void WriteFields(NetDataWriter writer) { }
    }

    /// <summary>Zone server to one client: experience, the totals at the start of this level and of the next, and the level.</summary>
    public sealed record PlayerExperience(uint Exp, uint LevelStart, uint NextLevel, int Level) : IMessage
    {
        public MessageType Type => MessageType.PlayerExperience;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Exp);
            writer.Put(LevelStart);
            writer.Put(NextLevel);
            writer.Put(Level);
        }

        public static PlayerExperience ReadFields(NetDataReader reader) =>
            new PlayerExperience(reader.GetUInt(), reader.GetUInt(), reader.GetUInt(), reader.GetInt());

        /// <summary>Progress through the level, 0 to 1.</summary>
        public float Fraction => NextLevel > LevelStart ? (float)((double)(Exp - System.Math.Min(Exp, LevelStart)) / (NextLevel - LevelStart)) : 0f;
    }

    /// <summary>
    /// Zone server to the client after entering: what the legacy OP_NewZone told the Trilogy client
    /// (cfg/&lt;zone&gt;.cfg): long name, fog colour and distances, sky, safe point, the underworld
    /// depth (below it you are returned to the safe point) and the view distance limits.
    /// </summary>
    public sealed record ZoneInfo(string LongName, byte FogRed, byte FogGreen, byte FogBlue, float FogMin, float FogMax, byte Sky,
        float SafeX, float SafeY, float SafeZ, float Underworld, float MinClip, float MaxClip, byte ZoneType) : IMessage
    {
        public MessageType Type => MessageType.ZoneInfo;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(LongName);
            writer.Put(FogRed);
            writer.Put(FogGreen);
            writer.Put(FogBlue);
            writer.Put(FogMin);
            writer.Put(FogMax);
            writer.Put(Sky);
            writer.Put(SafeX);
            writer.Put(SafeY);
            writer.Put(SafeZ);
            writer.Put(Underworld);
            writer.Put(MinClip);
            writer.Put(MaxClip);
            writer.Put(ZoneType);
        }

        public static ZoneInfo ReadFields(NetDataReader reader) =>
            new ZoneInfo(reader.GetString(), reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetFloat(), reader.GetFloat(),
                reader.GetByte(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat(),
                reader.GetByte());

        /// <summary>
        /// Reads a legacy cfg/&lt;zone&gt;.cfg (Zone::SaveZoneCFG: short name[20], long name[180], then the
        /// NewZone_Struct from its byte 230, so a field at struct offset n is at n - 30). Null when too short.
        /// </summary>
        public static ZoneInfo? FromLegacyCfg(byte[] cfg)
        {
            const int shift = -30;
            if (cfg.Length < 372 + shift)
                return null;
            float F(int offset) => System.BitConverter.ToSingle(cfg, offset + shift);
            int end = System.Array.IndexOf(cfg, (byte)0, 20);
            string longName = System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(cfg, 20, (end < 20 || end > 200 ? 200 : end) - 20);
            return new ZoneInfo(longName, cfg[231 + shift], cfg[235 + shift], cfg[239 + shift], F(244), F(260), cfg[330 + shift],
                F(344), F(348), F(352), F(360), F(364), F(368), cfg[230 + shift]);
        }
    }

    /// <summary>Zone server to the client after entering: Norrath's time (legacy OP_TimeOfDay); the client advances it.</summary>
    public sealed record TimeOfDay(int Hour, int Minute, int Day, int Month, int Year) : IMessage
    {
        public MessageType Type => MessageType.TimeOfDay;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((byte)Hour);
            writer.Put((byte)Minute);
            writer.Put((byte)Day);
            writer.Put((byte)Month);
            writer.Put((ushort)Year);
        }

        public static TimeOfDay ReadFields(NetDataReader reader) =>
            new TimeOfDay(reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetUShort());
    }

    /// <summary>An item as the client shows it: id, name, charges (0 when not stackable).</summary>
    public sealed record ItemView(int ItemId, string Name, int Charges);

    internal static class ItemViews
    {
        public static void Write(NetDataWriter writer, IReadOnlyList<ItemView> items)
        {
            writer.Put((ushort)items.Count);
            foreach (var i in items)
            {
                writer.Put(i.ItemId);
                writer.Put(i.Name);
                writer.Put((short)i.Charges);
            }
        }

        public static List<ItemView> Read(NetDataReader reader)
        {
            int count = reader.GetUShort();
            var list = new List<ItemView>(count);
            for (int n = 0; n < count; n++)
                list.Add(new ItemView(reader.GetInt(), reader.GetString(), reader.GetShort()));
            return list;
        }
    }

    /// <summary>Client to zone server: open a corpse (legacy OP_LootRequest).</summary>
    public sealed record LootRequest(int CorpseId) : IMessage
    {
        public MessageType Type => MessageType.LootRequest;
        public void WriteFields(NetDataWriter writer) => writer.Put(CorpseId);
        public static LootRequest ReadFields(NetDataReader reader) => new LootRequest(reader.GetInt());
    }

    /// <summary>Client to zone server: take the item at this position of the loot window (legacy OP_LootItem).</summary>
    public sealed record LootTake(int CorpseId, int Index) : IMessage
    {
        public MessageType Type => MessageType.LootTake;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(CorpseId);
            writer.Put((byte)Index);
        }

        public static LootTake ReadFields(NetDataReader reader) => new LootTake(reader.GetInt(), reader.GetByte());
    }

    /// <summary>Client to zone server: close the loot window (legacy OP_EndLootRequest).</summary>
    public sealed record LootEnd(int CorpseId) : IMessage
    {
        public MessageType Type => MessageType.LootEnd;
        public void WriteFields(NetDataWriter writer) => writer.Put(CorpseId);
        public static LootEnd ReadFields(NetDataReader reader) => new LootEnd(reader.GetInt());
    }

    /// <summary>Zone server to the looter: what is left on the corpse.</summary>
    public sealed record LootContents(int CorpseId, IReadOnlyList<ItemView> Items) : IMessage
    {
        public MessageType Type => MessageType.LootContents;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(CorpseId);
            ItemViews.Write(writer, Items);
        }

        public static LootContents ReadFields(NetDataReader reader) => new LootContents(reader.GetInt(), ItemViews.Read(reader));
    }

    /// <summary>Zone server to the player: the 30 inventory slots (worn 0-21, general 22-29) and money.</summary>
    public sealed record PlayerInventory(IReadOnlyList<ItemView> Slots, int Platinum, int Gold, int Silver, int Copper) : IMessage
    {
        public MessageType Type => MessageType.PlayerInventory;

        public void WriteFields(NetDataWriter writer)
        {
            ItemViews.Write(writer, Slots);
            writer.Put(Platinum);
            writer.Put(Gold);
            writer.Put(Silver);
            writer.Put(Copper);
        }

        public static PlayerInventory ReadFields(NetDataReader reader) =>
            new PlayerInventory(ItemViews.Read(reader), reader.GetInt(), reader.GetInt(), reader.GetInt(), reader.GetInt());
    }
}
