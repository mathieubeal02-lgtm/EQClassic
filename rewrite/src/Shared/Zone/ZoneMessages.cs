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

    public enum ChatChannel : byte { Say = 0, Shout = 1, Ooc = 2, Auction = 3, Tell = 4, Emote = 5, Group = 6 }

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
    /// <summary>An item as the windows show it; Price is its value in copper (items' price), which merchants multiply.</summary>
    public sealed record ItemView(int ItemId, string Name, int Charges, int Price = 0, int BagSlots = 0);

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
                writer.Put(i.Price);
                writer.Put((byte)i.BagSlots);
            }
        }

        public static List<ItemView> Read(NetDataReader reader)
        {
            int count = reader.GetUShort();
            var list = new List<ItemView>(count);
            for (int n = 0; n < count; n++)
                list.Add(new ItemView(reader.GetInt(), reader.GetString(), reader.GetShort(), reader.GetInt(), reader.GetByte()));
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

    /// <summary>
    /// Zone server to the player: the 30 inventory slots (worn 0-21, general 22-29), money, and the
    /// contents of the bags in the general slots (80 cells, slot numbers 250 + bag × 10 + cell).
    /// </summary>
    public sealed record PlayerInventory(IReadOnlyList<ItemView> Slots, int Platinum, int Gold, int Silver, int Copper, IReadOnlyList<ItemView>? Bags = null) : IMessage
    {
        public const int BagSlotBase = 250, BagCells = 10;
        public IReadOnlyList<ItemView> BagCellsOrEmpty => Bags ?? System.Array.Empty<ItemView>();

        public MessageType Type => MessageType.PlayerInventory;

        public void WriteFields(NetDataWriter writer)
        {
            ItemViews.Write(writer, Slots);
            writer.Put(Platinum);
            writer.Put(Gold);
            writer.Put(Silver);
            writer.Put(Copper);
            ItemViews.Write(writer, BagCellsOrEmpty);
        }

        public static PlayerInventory ReadFields(NetDataReader reader) =>
            new PlayerInventory(ItemViews.Read(reader), reader.GetInt(), reader.GetInt(), reader.GetInt(), reader.GetInt(), ItemViews.Read(reader));
    }

    /// <summary>Client to zone server: move or swap the items of two inventory slots (legacy OP_MoveItem).</summary>
    public sealed record MoveItem(int From, int To) : IMessage
    {
        public MessageType Type => MessageType.MoveItem;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((ushort)From);
            writer.Put((ushort)To);
        }

        public static MoveItem ReadFields(NetDataReader reader) => new MoveItem(reader.GetUShort(), reader.GetUShort());
    }

    /// <summary>A spell as the client shows it in the book and the gems.</summary>
    public sealed record SpellView(int SpellId, string Name, int Level, int Mana, int CastMs, bool Beneficial, int Icon);

    /// <summary>
    /// Zone server to the player: the spells of their book (with their level for the player's class)
    /// and the 8 memorised gems (spell ids, −1 when empty). Sent on entry and when the gems change.
    /// </summary>
    public sealed record SpellBook(IReadOnlyList<SpellView> Spells, IReadOnlyList<int> Gems) : IMessage
    {
        public MessageType Type => MessageType.SpellBook;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((ushort)Spells.Count);
            foreach (var s in Spells)
            {
                writer.Put(s.SpellId);
                writer.Put(s.Name);
                writer.Put((byte)s.Level);
                writer.Put(s.Mana);
                writer.Put(s.CastMs);
                writer.Put(s.Beneficial);
                writer.Put((ushort)s.Icon);
            }
            writer.Put((byte)Gems.Count);
            foreach (int g in Gems)
                writer.Put(g);
        }

        public static SpellBook ReadFields(NetDataReader reader)
        {
            int count = reader.GetUShort();
            var spells = new List<SpellView>(count);
            for (int n = 0; n < count; n++)
                spells.Add(new SpellView(reader.GetInt(), reader.GetString(), reader.GetByte(), reader.GetInt(), reader.GetInt(), reader.GetBool(), reader.GetUShort()));
            int gems = reader.GetByte();
            var list = new List<int>(gems);
            for (int n = 0; n < gems; n++)
                list.Add(reader.GetInt());
            return new SpellBook(spells, list);
        }
    }

    /// <summary>Client to zone server: memorise a spell of the book in a gem (−1 forgets the gem).</summary>
    public sealed record MemorizeSpell(int Gem, int SpellId) : IMessage
    {
        public MessageType Type => MessageType.MemorizeSpell;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((byte)Gem);
            writer.Put(SpellId);
        }

        public static MemorizeSpell ReadFields(NetDataReader reader) => new MemorizeSpell(reader.GetByte(), reader.GetInt());
    }

    /// <summary>Client to zone server: cast the spell of a gem at the current target (legacy OP_CastSpell).</summary>
    public sealed record CastSpell(int Gem) : IMessage
    {
        public MessageType Type => MessageType.CastSpell;
        public void WriteFields(NetDataWriter writer) => writer.Put((byte)Gem);
        public static CastSpell ReadFields(NetDataReader reader) => new CastSpell(reader.GetByte());
    }

    public enum SpellPhase : byte { Begin = 0, Finished = 1, Fizzled = 2, Interrupted = 3, Resisted = 4 }

    /// <summary>
    /// Zone server to the players near a caster: a cast begins (with its time, for the casting bar
    /// and the hands animation, legacy OP_BeginCast) or ends.
    /// </summary>
    public sealed record SpellCast(int CasterId, int SpellId, string SpellName, int CastMs, SpellPhase Phase) : IMessage
    {
        public MessageType Type => MessageType.SpellCast;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(CasterId);
            writer.Put(SpellId);
            writer.Put(SpellName);
            writer.Put(CastMs);
            writer.Put((byte)Phase);
        }

        public static SpellCast ReadFields(NetDataReader reader) =>
            new SpellCast(reader.GetInt(), reader.GetInt(), reader.GetString(), reader.GetInt(), (SpellPhase)reader.GetByte());
    }

    /// <summary>Zone server to the player: current and maximum mana (legacy OP_ManaChange).</summary>
    public sealed record PlayerMana(int Mana, int MaxMana) : IMessage
    {
        public MessageType Type => MessageType.PlayerMana;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Mana);
            writer.Put(MaxMana);
        }

        public static PlayerMana ReadFields(NetDataReader reader) => new PlayerMana(reader.GetInt(), reader.GetInt());
    }

    /// <summary>Client to zone server: scribe the spell scroll of an inventory slot into the book.</summary>
    public sealed record ScribeScroll(int Slot) : IMessage
    {
        public MessageType Type => MessageType.ScribeScroll;
        public void WriteFields(NetDataWriter writer) => writer.Put((ushort)Slot);
        public static ScribeScroll ReadFields(NetDataReader reader) => new ScribeScroll(reader.GetUShort());
    }

    /// <summary>A buff on the player as the buff window shows it.</summary>
    public sealed record BuffView(int SpellId, string Name, int TicsLeft, bool Beneficial);

    /// <summary>
    /// Zone server to the player: their buffs (the buff window), and what the client applies to its
    /// own movement: the speed they add up to (percent: spirit of wolf, snares; −100 when rooted)
    /// and levitation (no falling).
    /// </summary>
    public sealed record PlayerBuffs(IReadOnlyList<BuffView> Buffs, int MovementSpeed, bool Levitating = false) : IMessage
    {
        public MessageType Type => MessageType.PlayerBuffs;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((byte)Buffs.Count);
            foreach (var b in Buffs)
            {
                writer.Put(b.SpellId);
                writer.Put(b.Name);
                writer.Put(b.TicsLeft);
                writer.Put(b.Beneficial);
            }
            writer.Put((short)MovementSpeed);
            writer.Put(Levitating);
        }

        public static PlayerBuffs ReadFields(NetDataReader reader)
        {
            int count = reader.GetByte();
            var buffs = new List<BuffView>(count);
            for (int n = 0; n < count; n++)
                buffs.Add(new BuffView(reader.GetInt(), reader.GetString(), reader.GetInt(), reader.GetBool()));
            return new PlayerBuffs(buffs, reader.GetShort(), reader.GetBool());
        }
    }

    /// <summary>
    /// The legacy merchant (Client::ProcessOP_ShopRequest, ShopPlayerBuy, ShopPlayerSell): a price
    /// multiplier of 2.5; you pay the item's price × 2.5 and get price × 0.4 (price − price ×
    /// (1 − 1/2.5)), rounded to the nearest copper.
    /// </summary>
    public static class MerchantRules
    {
        public const double PriceMultiplier = 2.5;

        public static int BuyPrice(int price) => Round(price * PriceMultiplier);
        public static int SellPrice(int price) => Round(price - price * (1 - 1 / PriceMultiplier));

        /// <summary>The legacy rounding: floor, plus one when the rest is above 0.49.</summary>
        private static int Round(double value)
        {
            int floor = (int)System.Math.Floor(value);
            return value - floor > 0.49 ? floor + 1 : floor;
        }

        /// <summary>"1p 2g 3s 4c".</summary>
        public static string Coins(int copper)
        {
            if (copper <= 0)
                return "0c";
            var parts = new List<string>();
            if (copper >= 1000) parts.Add($"{copper / 1000}p");
            if (copper / 100 % 10 > 0) parts.Add($"{copper / 100 % 10}g");
            if (copper / 10 % 10 > 0) parts.Add($"{copper / 10 % 10}s");
            if (copper % 10 > 0) parts.Add($"{copper % 10}c");
            return string.Join(" ", parts);
        }
    }

    /// <summary>Client to zone server: open a merchant's window (legacy OP_ShopRequest).</summary>
    public sealed record MerchantRequest(int NpcId) : IMessage
    {
        public MessageType Type => MessageType.MerchantRequest;
        public void WriteFields(NetDataWriter writer) => writer.Put(NpcId);
        public static MerchantRequest ReadFields(NetDataReader reader) => new MerchantRequest(reader.GetInt());
    }

    /// <summary>Zone server to the player: the merchant's goods (at most 30); prices are the items' values. NpcId 0 closes the window.</summary>
    public sealed record MerchantGoods(int NpcId, IReadOnlyList<ItemView> Items) : IMessage
    {
        public MessageType Type => MessageType.MerchantGoods;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(NpcId);
            ItemViews.Write(writer, Items);
        }

        public static MerchantGoods ReadFields(NetDataReader reader) => new MerchantGoods(reader.GetInt(), ItemViews.Read(reader));
    }

    /// <summary>Client to zone server: buy one of the goods (legacy OP_ShopPlayerBuy).</summary>
    public sealed record MerchantBuy(int NpcId, int Index) : IMessage
    {
        public MessageType Type => MessageType.MerchantBuy;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(NpcId);
            writer.Put((byte)Index);
        }

        public static MerchantBuy ReadFields(NetDataReader reader) => new MerchantBuy(reader.GetInt(), reader.GetByte());
    }

    /// <summary>Client to zone server: sell the item of an inventory slot (legacy OP_ShopPlayerSell).</summary>
    public sealed record MerchantSell(int NpcId, int Slot) : IMessage
    {
        public MessageType Type => MessageType.MerchantSell;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(NpcId);
            writer.Put((ushort)Slot);
        }

        public static MerchantSell ReadFields(NetDataReader reader) => new MerchantSell(reader.GetInt(), reader.GetUShort());
    }

    /// <summary>Client to zone server: close the merchant's window (legacy OP_ShopEnd).</summary>
    public sealed record MerchantEnd(int NpcId) : IMessage
    {
        public MessageType Type => MessageType.MerchantEnd;
        public void WriteFields(NetDataWriter writer) => writer.Put(NpcId);
        public static MerchantEnd ReadFields(NetDataReader reader) => new MerchantEnd(reader.GetInt());
    }

    /// <summary>Client to zone server: use an ability by its skill id (kick, bash, taunt, mend, hide, sneak, forage).</summary>
    public sealed record UseAbility(int Skill) : IMessage
    {
        public MessageType Type => MessageType.UseAbility;
        public void WriteFields(NetDataWriter writer) => writer.Put((byte)Skill);
        public static UseAbility ReadFields(NetDataReader reader) => new UseAbility(reader.GetByte());
    }

    /// <summary>Zone server to the player: skill values by skill id (the skills window) and the abilities they can use.</summary>
    public sealed record PlayerSkills(IReadOnlyList<int> Values, IReadOnlyList<int> Abilities) : IMessage
    {
        public MessageType Type => MessageType.PlayerSkills;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((byte)Values.Count);
            foreach (int v in Values)
                writer.Put((byte)System.Math.Clamp(v, 0, 255));
            writer.Put((byte)Abilities.Count);
            foreach (int a in Abilities)
                writer.Put((byte)a);
        }

        public static PlayerSkills ReadFields(NetDataReader reader)
        {
            int count = reader.GetByte();
            var values = new List<int>(count);
            for (int i = 0; i < count; i++)
                values.Add(reader.GetByte());
            int abilities = reader.GetByte();
            var list = new List<int>(abilities);
            for (int i = 0; i < abilities; i++)
                list.Add(reader.GetByte());
            return new PlayerSkills(values, list);
        }
    }

    public enum GroupAction : byte { Invite = 0, Accept = 1, Decline = 2, Leave = 3 }

    /// <summary>Client to zone server: /invite name, /follow, /decline, /disband (legacy OP_GroupInvite, OP_GroupFollow, OP_GroupDelete).</summary>
    public sealed record GroupCommand(GroupAction Action, string Name) : IMessage
    {
        public MessageType Type => MessageType.GroupCommand;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put((byte)Action);
            writer.Put(Name);
        }

        public static GroupCommand ReadFields(NetDataReader reader) => new GroupCommand((GroupAction)reader.GetByte(), reader.GetString(64));
    }

    /// <summary>Zone server to a player: their group (leader and members), empty when they are in none.</summary>
    public sealed record GroupUpdate(string Leader, IReadOnlyList<string> Members) : IMessage
    {
        public MessageType Type => MessageType.GroupUpdate;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Leader);
            writer.Put((byte)Members.Count);
            foreach (var m in Members)
                writer.Put(m);
        }

        public static GroupUpdate ReadFields(NetDataReader reader)
        {
            string leader = reader.GetString();
            int count = reader.GetByte();
            var members = new List<string>(count);
            for (int i = 0; i < count; i++)
                members.Add(reader.GetString());
            return new GroupUpdate(leader, members);
        }
    }

    /// <summary>Zone server to the players of a zone: the weather (0 clear, 1 rain, 2 snow), on entry and when it changes.</summary>
    public sealed record ZoneWeather(int Weather) : IMessage
    {
        public MessageType Type => MessageType.ZoneWeather;
        public void WriteFields(NetDataWriter writer) => writer.Put((byte)Weather);
        public static ZoneWeather ReadFields(NetDataReader reader) => new ZoneWeather(reader.GetByte());
    }
}
