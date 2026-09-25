using System.Linq;
using System.Collections.Generic;
using EQClassic.Shared.Protocol;
using LiteNetLib.Utils;

namespace EQClassic.Shared.Login;

/// <summary>Client to login server: credentials (the Trilogy client sends them DES-encrypted, see Legacy/).</summary>
public sealed record LoginRequest(string Username, string Password) : IMessage
{
    public MessageType Type => MessageType.LoginRequest;

    public void WriteFields(NetDataWriter writer)
    {
        writer.Put(Username);
        writer.Put(Password);
    }

    public static LoginRequest ReadFields(NetDataReader reader) => new(reader.GetString(), reader.GetString());

    /// <summary>Never log the password.</summary>
    public override string ToString() => $"LoginRequest {{ Username = {Username} }}";
}

/// <summary>Login server to client. On success SessionId is "LS#&lt;account id&gt;".</summary>
public sealed record LoginResponse(LoginResult Result, string Message, string SessionId, int AccountId) : IMessage
{
    public MessageType Type => MessageType.LoginResponse;

    public static LoginResponse Failure(LoginResult result) => new(result, LoginMessages.For(result), "", 0);

    public static LoginResponse Success(int accountId) => new(LoginResult.Success, "", SessionIds.ForAccount(accountId), accountId);

    public void WriteFields(NetDataWriter writer)
    {
        writer.Put((byte)Result);
        writer.Put(Message);
        writer.Put(SessionId);
        writer.Put(AccountId);
    }

    public static LoginResponse ReadFields(NetDataReader reader) =>
        new((LoginResult)reader.GetByte(), reader.GetString(), reader.GetString(), reader.GetInt());
}

/// <summary>Client to login server, after a successful login.</summary>
public sealed record ServerListRequest : IMessage
{
    public MessageType Type => MessageType.ServerListRequest;
    public void WriteFields(NetDataWriter writer) { }
}

public enum WorldStatus : byte { Up = 0, Down = 1, Locked = 2 }

public sealed record WorldServerInfo(int Id, string Name, string Address, int Port, int PlayersOnline, WorldStatus Status);

public sealed record ServerListResponse(IReadOnlyList<WorldServerInfo> Worlds) : IMessage
{
    public MessageType Type => MessageType.ServerListResponse;

    public void WriteFields(NetDataWriter writer)
    {
        writer.Put((ushort)Worlds.Count);
        foreach (var w in Worlds)
        {
            writer.Put(w.Id);
            writer.Put(w.Name);
            writer.Put(w.Address);
            writer.Put(w.Port);
            writer.Put(w.PlayersOnline);
            writer.Put((byte)w.Status);
        }
    }

    public static ServerListResponse ReadFields(NetDataReader reader)
    {
        int count = reader.GetUShort();
        var worlds = new List<WorldServerInfo>(count);
        for (int i = 0; i < count; i++)
            worlds.Add(new WorldServerInfo(reader.GetInt(), reader.GetString(), reader.GetString(), reader.GetInt(), reader.GetInt(), (WorldStatus)reader.GetByte()));
        return new ServerListResponse(worlds);
    }

    public bool Equals(ServerListResponse? other) => other is not null && Worlds.SequenceEqual(other.Worlds);
    public override int GetHashCode() => Worlds.Count;
}

/// <summary>Client to login server: "I want to play on this world" (legacy OP_SessionKey request).</summary>
public sealed record PlayRequest(int WorldId) : IMessage
{
    public MessageType Type => MessageType.PlayRequest;
    public void WriteFields(NetDataWriter writer) => writer.Put(WorldId);
    public static PlayRequest ReadFields(NetDataReader reader) => new(reader.GetInt());
}

/// <summary>
/// On success, the client connects to World at Address:Port and presents SessionId + SessionKey
/// (legacy OP_SessionKey: a random 15-character key also sent to World by the login server).
/// </summary>
public sealed record PlayResponse(bool Accepted, string Message, string SessionKey, string Address, int Port) : IMessage
{
    public MessageType Type => MessageType.PlayResponse;

    public static PlayResponse Refused(string message) => new(false, message, "", "", 0);

    public void WriteFields(NetDataWriter writer)
    {
        writer.Put(Accepted);
        writer.Put(Message);
        writer.Put(SessionKey);
        writer.Put(Address);
        writer.Put(Port);
    }

    public static PlayResponse ReadFields(NetDataReader reader) =>
        new(reader.GetBool(), reader.GetString(), reader.GetString(), reader.GetString(), reader.GetInt());
}
