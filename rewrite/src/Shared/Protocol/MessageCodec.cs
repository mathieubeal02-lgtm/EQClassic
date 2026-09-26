using System;
using EQClassic.Shared.Login;
using EQClassic.Shared.Security;
using LiteNetLib.Utils;

namespace EQClassic.Shared.Protocol;

/// <summary>Turns messages into bytes and back. Unknown or truncated input raises <see cref="MessageFormatException"/>.</summary>
public static class MessageCodec
{
    public static void Write(NetDataWriter writer, IMessage message)
    {
        writer.Put((byte)message.Type);
        message.WriteFields(writer);
    }

    public static byte[] Encode(IMessage message)
    {
        var writer = new NetDataWriter();
        Write(writer, message);
        return writer.CopyData();
    }

    public static IMessage Decode(byte[] data) => Read(new NetDataReader(data));

    public static IMessage Read(NetDataReader reader)
    {
        try
        {
            var type = (MessageType)reader.GetByte();
            IMessage message = type switch
            {
                MessageType.LoginRequest => LoginRequest.ReadFields(reader),
                MessageType.LoginResponse => LoginResponse.ReadFields(reader),
                MessageType.ServerListRequest => new ServerListRequest(),
                MessageType.ServerListResponse => ServerListResponse.ReadFields(reader),
                MessageType.PlayRequest => PlayRequest.ReadFields(reader),
                MessageType.PlayResponse => PlayResponse.ReadFields(reader),
                MessageType.ServerHello => ServerHello.ReadFields(reader),
                MessageType.SecureLoginRequest => SecureLoginRequest.ReadFields(reader),
                MessageType.Sealed => Sealed.ReadFields(reader),
                _ => throw new MessageFormatException($"unknown message type {(byte)type}"),
            };
            if (!reader.EndOfData)
                throw new MessageFormatException($"{type}: {reader.AvailableBytes} trailing byte(s)");
            return message;
        }
        // LiteNetLib 2.x throws InvalidOperationException when a read runs past the data; older
        // versions and bad string lengths raise the range exceptions. A peer must never be able to
        // crash the receive loop with a malformed packet.
        catch (Exception e) when (e is InvalidOperationException or IndexOutOfRangeException or ArgumentException)
        {
            throw new MessageFormatException("truncated or malformed message", e);
        }
    }
}

public sealed class MessageFormatException : Exception
{
    public MessageFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
