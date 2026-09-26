using System.Buffers.Binary;
using EQClassic.Shared.World;
using MySqlConnector;

namespace EQClassic.Server.Zone;

/// <summary>A player's corpse as stored: where, whose, what it holds (item, original slot, charges), money, time left.</summary>
public sealed record StoredCorpse(int Id, string Owner, string Zone, Vec3 Position, float Heading, int Race, int Gender, int Level, int Class,
    int Deity, Coins Coins, IReadOnlyList<(int ItemId, int Slot, int Charges)> Items, double SecondsLeft);

public interface IPlayerCorpseStore
{
    IReadOnlyList<StoredCorpse> InZone(string zone);
    /// <summary>Stores a new corpse and returns its id.</summary>
    int Create(StoredCorpse corpse);
    void Update(StoredCorpse corpse);
    void Delete(int id);
}

public sealed class InMemoryPlayerCorpseStore : IPlayerCorpseStore
{
    public Dictionary<int, StoredCorpse> Corpses { get; } = new();
    private int _next = 1;
    public IReadOnlyList<StoredCorpse> InZone(string zone) => Corpses.Values.Where(c => string.Equals(c.Zone, zone, StringComparison.OrdinalIgnoreCase)).ToList();
    public int Create(StoredCorpse corpse)
    {
        int id = _next++;
        Corpses[id] = corpse with { Id = id };
        return id;
    }
    public void Update(StoredCorpse corpse) => Corpses[corpse.Id] = corpse;
    public void Delete(int id) => Corpses.Remove(id);
}

/// <summary>
/// player_corpses with the legacy data blob (Zone/Include/zonedump.h, packed): DBPlayerCorpse_Struct
/// (item count, size, level, race, gender, class, deity, textures, copper, silver, gold, platinum)
/// then a ServerLootItem_Struct per item (item, slot, charges, loot slot, looted). `time` is the
/// milliseconds left before it rots.
/// </summary>
public sealed class MySqlPlayerCorpseStore : IPlayerCorpseStore
{
    private const int HeaderSize = 31, ItemSize = 8;
    private readonly string _connectionString;
    public MySqlPlayerCorpseStore(string connectionString) => _connectionString = connectionString;

    public static byte[] Encode(StoredCorpse c)
    {
        var data = new byte[HeaderSize + ItemSize * c.Items.Count];
        BinaryPrimitives.WriteInt32LittleEndian(data, c.Items.Count);
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(4), 6f);
        data[8] = (byte)c.Level;
        data[9] = (byte)c.Race;
        data[10] = (byte)c.Gender;
        data[11] = (byte)c.Class;
        data[12] = (byte)c.Deity;
        data[13] = data[14] = 0xFF; // textures
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(15), c.Coins.Copper);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(19), c.Coins.Silver);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(23), c.Coins.Gold);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(27), c.Coins.Platinum);
        for (int i = 0; i < c.Items.Count; i++)
        {
            var item = data.AsSpan(HeaderSize + ItemSize * i, ItemSize);
            BinaryPrimitives.WriteUInt16LittleEndian(item, (ushort)c.Items[i].ItemId);
            BinaryPrimitives.WriteInt16LittleEndian(item[2..], (short)c.Items[i].Slot);
            item[4] = (byte)(sbyte)Math.Clamp(c.Items[i].Charges, sbyte.MinValue, sbyte.MaxValue);
        }
        return data;
    }

    public static (int Level, int Race, int Gender, int Class, int Deity, Coins Coins, List<(int, int, int)> Items) Decode(byte[] data)
    {
        if (data.Length < HeaderSize)
            return (1, 1, 0, 1, 140, Coins.None, new());
        int count = Math.Min(BinaryPrimitives.ReadInt32LittleEndian(data), (data.Length - HeaderSize) / ItemSize);
        var coins = new Coins(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(27)), BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(23)),
            BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(19)), BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(15)));
        var items = new List<(int, int, int)>();
        for (int i = 0; i < count; i++)
        {
            var item = data.AsSpan(HeaderSize + ItemSize * i, ItemSize);
            int id = BinaryPrimitives.ReadUInt16LittleEndian(item);
            if (id is not (0 or 0xFFFF) && item[7] == 0) // not already looted
                items.Add((id, BinaryPrimitives.ReadInt16LittleEndian(item[2..]), (sbyte)item[4]));
        }
        return (data[8], data[9], data[10], data[11], data[12], coins, items);
    }

    public IReadOnlyList<StoredCorpse> InZone(string zone)
    {
        var corpses = new List<StoredCorpse>();
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, charname, x, y, z, heading, data, time FROM player_corpses WHERE zonename = @zone";
        cmd.Parameters.AddWithValue("@zone", zone);
        try
        {
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var (level, race, gender, cls, deity, coins, items) = Decode((byte[])r.GetValue(6));
                corpses.Add(new StoredCorpse(r.GetInt32(0), r.GetString(1), zone, new Vec3(r.GetFloat(2), r.GetFloat(3), r.GetFloat(4)), r.GetFloat(5),
                    race, gender, level, cls, deity, coins, items, Convert.ToInt64(r.GetValue(7)) / 1000.0));
            }
        }
        catch (MySqlException)
        {
            // no player_corpses table
        }
        return corpses;
    }

    public int Create(StoredCorpse c)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO player_corpses (charid, charname, accountid, zonename, x, y, z, heading, data, time, reztime, rezexp, rezed)
            SELECT c.id, c.name, c.account_id, @zone, @x, @y, @z, @heading, @data, @time, 0, 0, 0 FROM character_ c WHERE c.name = @name;
            SELECT LAST_INSERT_ID();
            """;
        Fill(cmd, c);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Update(StoredCorpse c)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE player_corpses SET data = @data, time = @time WHERE id = @id";
        Fill(cmd, c);
        cmd.Parameters.AddWithValue("@id", c.Id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(int id)
    {
        using var connection = new MySqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM player_corpses WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    private static void Fill(MySqlCommand cmd, StoredCorpse c)
    {
        cmd.Parameters.AddWithValue("@zone", c.Zone);
        cmd.Parameters.AddWithValue("@x", c.Position.X);
        cmd.Parameters.AddWithValue("@y", c.Position.Y);
        cmd.Parameters.AddWithValue("@z", c.Position.Z);
        cmd.Parameters.AddWithValue("@heading", c.Heading);
        cmd.Parameters.AddWithValue("@data", Encode(c));
        cmd.Parameters.AddWithValue("@time", (long)(c.SecondsLeft * 1000));
        cmd.Parameters.AddWithValue("@name", c.Owner);
    }
}
