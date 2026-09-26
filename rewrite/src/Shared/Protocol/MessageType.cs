namespace EQClassic.Shared.Protocol;

/// <summary>
/// First byte of every application message. Values are part of the wire contract: never renumber,
/// only append (the version handshake in <see cref="ProtocolInfo"/> rejects mismatched peers).
/// </summary>
public enum MessageType : byte
{
    LoginRequest = 1,
    LoginResponse = 2,
    ServerListRequest = 3,
    ServerListResponse = 4,
    PlayRequest = 5,
    PlayResponse = 6,
    ServerHello = 7,
    SecureLoginRequest = 8,
    Sealed = 9,
    WorldLoginRequest = 10,
    WorldLoginResponse = 11,
    EnterWorldRequest = 12,
    EnterWorldResponse = 13,
    CreateCharacterRequest = 14,
    CreateCharacterResponse = 15,
}
