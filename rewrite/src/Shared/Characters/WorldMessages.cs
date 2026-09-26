using System.Collections.Generic;
using System.Linq;
using EQClassic.Shared.Protocol;
using LiteNetLib.Utils;

namespace EQClassic.Shared.Characters
{
    /// <summary>
    /// Client to World: the session id and key the login server issued (legacy OP_SendLoginInfo
    /// "LS#&lt;id&gt;\0&lt;key&gt;"). The key works once.
    /// </summary>
    public sealed record WorldLoginRequest(string SessionId, string WorldKey) : IMessage
    {
        public MessageType Type => MessageType.WorldLoginRequest;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(SessionId);
            writer.Put(WorldKey);
        }

        public static WorldLoginRequest ReadFields(NetDataReader reader) => new WorldLoginRequest(reader.GetString(), reader.GetString());

        public override string ToString() => $"WorldLoginRequest {{ SessionId = {SessionId} }}";
    }

    /// <summary>One line of the character select screen (legacy CharacterSelect_Struct).</summary>
    public sealed record CharacterSummary(string Name, int Race, int Class, int Level, int Gender, string Zone);

    public sealed record WorldLoginResponse(bool Accepted, string Message, IReadOnlyList<CharacterSummary> Characters) : IMessage
    {
        public MessageType Type => MessageType.WorldLoginResponse;

        public static WorldLoginResponse Refused(string message) => new WorldLoginResponse(false, message, new CharacterSummary[0]);

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Accepted);
            writer.Put(Message);
            writer.Put((byte)Characters.Count);
            foreach (var c in Characters)
            {
                writer.Put(c.Name);
                writer.Put((ushort)c.Race);
                writer.Put((byte)c.Class);
                writer.Put((byte)c.Level);
                writer.Put((byte)c.Gender);
                writer.Put(c.Zone);
            }
        }

        public static WorldLoginResponse ReadFields(NetDataReader reader)
        {
            bool accepted = reader.GetBool();
            string message = reader.GetString();
            int count = reader.GetByte();
            var list = new List<CharacterSummary>(count);
            for (int i = 0; i < count; i++)
                list.Add(new CharacterSummary(reader.GetString(), reader.GetUShort(), reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetString()));
            return new WorldLoginResponse(accepted, message, list);
        }

        public bool Equals(WorldLoginResponse? other) =>
            other != null && Accepted == other.Accepted && Message == other.Message && Characters.SequenceEqual(other.Characters);

        public override int GetHashCode() => Characters.Count;
    }

    /// <summary>Client to World: play this character (legacy OP_EnterWorld).</summary>
    public sealed record EnterWorldRequest(string CharacterName) : IMessage
    {
        public MessageType Type => MessageType.EnterWorldRequest;
        public void WriteFields(NetDataWriter writer) => writer.Put(CharacterName);
        public static EnterWorldRequest ReadFields(NetDataReader reader) => new EnterWorldRequest(reader.GetString());
    }

    /// <summary>
    /// Where the character goes (legacy OP_ZoneServerInfo). Until zones run in the rewrite (M3),
    /// this is the zone and position saved in the character's profile.
    /// </summary>
    public sealed record EnterWorldResponse(bool Accepted, string Message, string Zone, float X, float Y, float Z) : IMessage
    {
        public MessageType Type => MessageType.EnterWorldResponse;

        public static EnterWorldResponse Refused(string message) => new EnterWorldResponse(false, message, "", 0, 0, 0);

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Accepted);
            writer.Put(Message);
            writer.Put(Zone);
            writer.Put(X);
            writer.Put(Y);
            writer.Put(Z);
        }

        public static EnterWorldResponse ReadFields(NetDataReader reader) =>
            new EnterWorldResponse(reader.GetBool(), reader.GetString(), reader.GetString(), reader.GetFloat(), reader.GetFloat(), reader.GetFloat());
    }

    /// <summary>Base statistics chosen at creation (legacy PlayerProfile STR..WIS).</summary>
    public sealed record CharacterStats(int Str, int Sta, int Cha, int Dex, int Int, int Agi, int Wis);

    /// <summary>
    /// Client to World: create a character (legacy OP_NameApproval + OP_CharacterCreate in one).
    /// StartZone is the city the player picked (short name), as the Trilogy client puts it in the profile.
    /// </summary>
    public sealed record CreateCharacterRequest(string Name, int Race, int Class, int Gender, int Deity, int Face, string StartZone, CharacterStats Stats) : IMessage
    {
        public MessageType Type => MessageType.CreateCharacterRequest;

        public void WriteFields(NetDataWriter writer)
        {
            writer.Put(Name);
            writer.Put((ushort)Race);
            writer.Put((byte)Class);
            writer.Put((byte)Gender);
            writer.Put((ushort)Deity);
            writer.Put((byte)Face);
            writer.Put(StartZone);
            foreach (var v in new[] { Stats.Str, Stats.Sta, Stats.Cha, Stats.Dex, Stats.Int, Stats.Agi, Stats.Wis })
                writer.Put((byte)v);
        }

        public static CreateCharacterRequest ReadFields(NetDataReader reader) =>
            new CreateCharacterRequest(reader.GetString(), reader.GetUShort(), reader.GetByte(), reader.GetByte(), reader.GetUShort(), reader.GetByte(), reader.GetString(),
                new CharacterStats(reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetByte(), reader.GetByte()));
    }

    /// <summary>World to client: the outcome and, on success, the refreshed character list (legacy SendCharInfo).</summary>
    public sealed record CreateCharacterResponse(bool Accepted, string Message, IReadOnlyList<CharacterSummary> Characters) : IMessage
    {
        public MessageType Type => MessageType.CreateCharacterResponse;

        public void WriteFields(NetDataWriter writer) => new WorldLoginResponse(Accepted, Message, Characters).WriteFields(writer);

        public static CreateCharacterResponse ReadFields(NetDataReader reader)
        {
            var r = WorldLoginResponse.ReadFields(reader);
            return new CreateCharacterResponse(r.Accepted, r.Message, r.Characters);
        }

        public bool Equals(CreateCharacterResponse? other) =>
            other != null && Accepted == other.Accepted && Message == other.Message && Characters.SequenceEqual(other.Characters);

        public override int GetHashCode() => Characters.Count;
    }
}
