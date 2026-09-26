using System.Buffers.Binary;
using System.Text;
using EQClassic.Server.Characters;

namespace EQClassic.Tests.Characters;

/// <summary>Builds a legacy profile blob (8104 bytes, offsets of Common/Include/PlayerProfile.h).</summary>
internal static class ProfileBuilder
{
    public static byte[] Build(string name, int race, int @class, int level, string zone, float x = 0, float y = 0, float z = 0, int gender = 0)
    {
        var p = new byte[8104];
        Encoding.Latin1.GetBytes(name).CopyTo(p, 4);
        p[54] = (byte)gender;
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(56), (short)race);
        p[58] = (byte)@class;
        p[60] = (byte)level;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(2408), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(2412), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(2416), z);
        Encoding.Latin1.GetBytes(zone).CopyTo(p, 2424);
        return p;
    }

    public static CharacterRecord Record(int id, int accountId, string name, int race, int @class, int level, string zone, float x = 0, float y = 0, float z = 0) =>
        new(id, accountId, PlayerProfile.Read(Build(name, race, @class, level, zone, x, y, z))!);
}
