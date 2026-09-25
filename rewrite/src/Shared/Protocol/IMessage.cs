using LiteNetLib.Utils;

namespace EQClassic.Shared.Protocol;

/// <summary>An application message: a <see cref="MessageType"/> byte followed by its fields.</summary>
public interface IMessage
{
    MessageType Type { get; }

    /// <summary>Writes the fields only; <see cref="MessageCodec"/> writes the type byte.</summary>
    void WriteFields(NetDataWriter writer);
}
