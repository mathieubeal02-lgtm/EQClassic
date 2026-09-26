using System;
using EQClassic.Shared.Characters;
using EQClassic.Shared.Login;
using EQClassic.Shared.Security;
using EQClassic.Shared.Zone;
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
                MessageType.WorldLoginRequest => WorldLoginRequest.ReadFields(reader),
                MessageType.WorldLoginResponse => WorldLoginResponse.ReadFields(reader),
                MessageType.EnterWorldRequest => EnterWorldRequest.ReadFields(reader),
                MessageType.EnterWorldResponse => EnterWorldResponse.ReadFields(reader),
                MessageType.CreateCharacterRequest => CreateCharacterRequest.ReadFields(reader),
                MessageType.CreateCharacterResponse => CreateCharacterResponse.ReadFields(reader),
                MessageType.ZoneEnterRequest => ZoneEnterRequest.ReadFields(reader),
                MessageType.ZoneEnterResponse => ZoneEnterResponse.ReadFields(reader),
                MessageType.PlayerMove => PlayerMove.ReadFields(reader),
                MessageType.EntityPositions => EntityPositions.ReadFields(reader),
                MessageType.MoveCorrection => MoveCorrection.ReadFields(reader),
                MessageType.EntitySpawned => EntitySpawned.ReadFields(reader),
                MessageType.EntityRemoved => EntityRemoved.ReadFields(reader),
                MessageType.ZoneChange => ZoneChange.ReadFields(reader),
                MessageType.ZoneDoors => ZoneDoors.ReadFields(reader),
                MessageType.ClickDoor => ClickDoor.ReadFields(reader),
                MessageType.DoorState => DoorState.ReadFields(reader),
                MessageType.ZoneMessage => ZoneMessage.ReadFields(reader),
                MessageType.SetTarget => SetTarget.ReadFields(reader),
                MessageType.AutoAttack => AutoAttack.ReadFields(reader),
                MessageType.CombatEvent => CombatEvent.ReadFields(reader),
                MessageType.PlayerHealth => PlayerHealth.ReadFields(reader),
                MessageType.ConsiderRequest => ConsiderRequest.ReadFields(reader),
                MessageType.ConsiderResult => ConsiderResult.ReadFields(reader),
                MessageType.SetSitting => SetSitting.ReadFields(reader),
                MessageType.EntityAppearance => EntityAppearance.ReadFields(reader),
                MessageType.ChatSend => ChatSend.ReadFields(reader),
                MessageType.ChatMessage => ChatMessage.ReadFields(reader),
                MessageType.WhoRequest => new WhoRequest(),
                MessageType.PlayerExperience => PlayerExperience.ReadFields(reader),
                MessageType.ZoneInfo => ZoneInfo.ReadFields(reader),
                MessageType.TimeOfDay => TimeOfDay.ReadFields(reader),
                MessageType.CreationOptionsRequest => new CreationOptionsRequest(),
                MessageType.CreationOptionsResponse => CreationOptionsResponse.ReadFields(reader),
                MessageType.LootRequest => LootRequest.ReadFields(reader),
                MessageType.LootTake => LootTake.ReadFields(reader),
                MessageType.LootEnd => LootEnd.ReadFields(reader),
                MessageType.LootContents => LootContents.ReadFields(reader),
                MessageType.PlayerInventory => PlayerInventory.ReadFields(reader),
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
