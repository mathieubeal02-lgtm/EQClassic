namespace EQClassic.Shared.Protocol;

/// <summary>Constants both ends must agree on before any message is exchanged.</summary>
public static class ProtocolInfo
{
    /// <summary>
    /// Sent as the LiteNetLib connection key. A client built against another protocol version is
    /// refused at connection time instead of failing on the first message it cannot parse.
    /// </summary>
    public const string ConnectionKey = "EQClassic/16"; // 2: doors, zone messages (24-27); 3: melee (28-31); 4: consider, sitting (32-35); 5: chat, who (36-38); 6: experience (39); 7: zone info (40); 8: time of day (41); 9: creation options (42-43); 10: corpses, loot, inventory (44-48); 11: moving items (49); 12: spells (50-55); 13: buffs (56); 14: root and levitation in the buff list; 15: merchants (57-61), item prices; 16: abilities and skills (62-63)

    /// <summary>Default UDP port of the login server (the Trilogy login server used 5999 too).</summary>
    public const int DefaultLoginPort = 5999;
}
