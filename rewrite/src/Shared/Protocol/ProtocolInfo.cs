namespace EQClassic.Shared.Protocol;

/// <summary>Constants both ends must agree on before any message is exchanged.</summary>
public static class ProtocolInfo
{
    /// <summary>
    /// Sent as the LiteNetLib connection key. A client built against another protocol version is
    /// refused at connection time instead of failing on the first message it cannot parse.
    /// </summary>
    public const string ConnectionKey = "EQClassic/3"; // 2: doors and zone messages (24-27); 3: melee (28-31)

    /// <summary>Default UDP port of the login server (the Trilogy login server used 5999 too).</summary>
    public const int DefaultLoginPort = 5999;
}
