using MySqlConnector;

namespace EQClassic.Server.Characters;

/// <summary>Database rules the legacy World applies when a character is created.</summary>
public interface ICreationData
{
    /// <summary>Legacy CheckNameFilter: <c>SELECT count(*) FROM name_filter WHERE '&lt;name&gt;' LIKE name</c>.</summary>
    bool IsNameFiltered(string name);

    /// <summary>
    /// Legacy SetStartingLocations: start_zones row for (zone chosen by the player, class, race);
    /// the last match wins. Null keeps the position the profile already has.
    /// </summary>
    (float X, float Y, float Z)? StartPosition(string zone, int race, int @class);

    /// <summary>Legacy SetStartingItems: starting_items for (race or 0, class or 0), by id.</summary>
    IReadOnlyList<int> StartingItems(int race, int @class);
}

public sealed class InMemoryCreationData : ICreationData
{
    public List<string> FilteredPatterns { get; } = new();
    public Dictionary<(string Zone, int Race, int Class), (float, float, float)> StartPositions { get; } = new();
    public List<(int Race, int Class, int Item)> Items { get; } = new();

    public bool IsNameFiltered(string name) =>
        FilteredPatterns.Any(p => System.Text.RegularExpressions.Regex.IsMatch(name,
            "^" + System.Text.RegularExpressions.Regex.Escape(p).Replace("%", ".*").Replace("_", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    public (float X, float Y, float Z)? StartPosition(string zone, int race, int @class) =>
        StartPositions.TryGetValue((zone, race, @class), out var p) ? p : null;

    public IReadOnlyList<int> StartingItems(int race, int @class) =>
        Items.Where(i => (i.Race == race || i.Race == 0) && (i.Class == @class || i.Class == 0)).Select(i => i.Item).ToList();
}

public sealed class MySqlCreationData : ICreationData
{
    private readonly string _connectionString;
    private readonly string _p;

    /// <param name="tablePrefix">Prepended to name_filter, start_zones, zone_ids and starting_items (tests).</param>
    public MySqlCreationData(string connectionString, string tablePrefix = "")
    {
        _connectionString = connectionString;
        _p = tablePrefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            ? tablePrefix : throw new ArgumentException("must be a plain SQL identifier prefix", nameof(tablePrefix));
    }

    public bool IsNameFiltered(string name) =>
        Convert.ToInt64(Scalar($"SELECT COUNT(*) FROM `{_p}name_filter` WHERE @name LIKE name", ("@name", name))) > 0;

    public (float X, float Y, float Z)? StartPosition(string zone, int race, int @class)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT s.x, s.y, s.z FROM `{_p}start_zones` s JOIN `{_p}zone_ids` z ON z.zoneidnumber = s.zone_id
            WHERE z.short_name = @zone AND s.player_class = @class AND s.player_race = @race
            """;
        command.Parameters.AddWithValue("@zone", zone);
        command.Parameters.AddWithValue("@class", @class);
        command.Parameters.AddWithValue("@race", race);
        using var reader = command.ExecuteReader();
        (float, float, float)? last = null;
        while (reader.Read())
            // The legacy code stores atoi() of each value: whole units.
            last = (MathF.Truncate(reader.GetFloat(0)), MathF.Truncate(reader.GetFloat(1)), MathF.Truncate(reader.GetFloat(2)));
        return last;
    }

    public IReadOnlyList<int> StartingItems(int race, int @class)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT itemid FROM `{_p}starting_items` WHERE (race = @race OR race = 0) AND (class = @class OR class = 0) ORDER BY id";
        command.Parameters.AddWithValue("@race", race);
        command.Parameters.AddWithValue("@class", @class);
        using var reader = command.ExecuteReader();
        var items = new List<int>();
        while (reader.Read())
            items.Add(Convert.ToInt32(reader.GetValue(0)));
        return items;
    }

    private MySqlConnection Open()
    {
        var connection = new MySqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private object? Scalar(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (n, v) in parameters)
            command.Parameters.AddWithValue(n, v);
        return command.ExecuteScalar();
    }
}
