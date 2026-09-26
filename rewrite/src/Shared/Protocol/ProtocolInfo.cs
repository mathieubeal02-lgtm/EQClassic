namespace EQClassic.Shared.Protocol;

/// <summary>Constants both ends must agree on before any message is exchanged.</summary>
public static class ProtocolInfo
{
    /// <summary>
    /// Sent as the LiteNetLib connection key. A client built against another protocol version is
    /// refused at connection time instead of failing on the first message it cannot parse.
    /// </summary>
    public const string ConnectionKey = "EQClassic/9"; // 2: doors, zone messages (24-27); 3: melee (28-31); 4: consider, sitting (32-35); 5: chat, who (36-38); 6: experience (39); 7: zone info (40); 8: time of day (41); 9: creation options (42-43)

    /// <summary>Default UDP port of the login server (the Trilogy login server used 5999 too).</summary>
    public const int DefaultLoginPort = 5999;
}
