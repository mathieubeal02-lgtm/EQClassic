using EQClassic.Shared.Zone;
using System.Security.Cryptography;
using EQClassic.Server.Accounts;
using EQClassic.Server.Characters;
using EQClassic.Server.WorldServer;
using EQClassic.Server.Zone;
using EQClassic.Shared.World;
using EQClassic.Server.Login;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;

// Login, World and zone servers of the rewrite, in one process.
//   EQClassic.Server [--port N] [--world-port N] [--zone-port N] [--public-address A]
//                    [--db "<MySqlConnector connection string>"] [--lantern build/lantern-work/Exports]
//                    [--key login-key.pem] [--allow-plaintext] [--zone-cfg runtime/cfg] [--spells runtime/spdat.eff]
// --lantern: LanternExtractor exports; a zone uses its collision mesh (with its solid objects) when
// <zone>/Zone/Meshes/<zone>_collision.txt exists.
// Without --db, only the runbook's test account (test / test) exists (in memory), with no characters.
// The RSA key is created on first start and kept in --key (default login-key.pem): clients pin its
// fingerprint, printed below, so it must survive restarts.
int port = ProtocolInfo.DefaultLoginPort;
int worldPort = WorldServer.DefaultPort;
int zonePort = ZoneServer.DefaultPort;
string worldAddress = "127.0.0.1";
string? lantern = null;
string? db = null;
string keyPath = "login-key.pem";
bool allowPlaintext = false;
string? zoneCfg = null;
string? spellFile = null;
for (int i = 0; i < args.Length; i++)
{
    string? next = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--port" when int.TryParse(next, out var p): port = p; i++; break;
        case "--world-port" when int.TryParse(next, out var wp): worldPort = wp; i++; break;
        case "--world-address" or "--public-address" when next is not null: worldAddress = next; i++; break;
        case "--zone-port" when int.TryParse(next, out var zp): zonePort = zp; i++; break;
        case "--lantern" when next is not null: lantern = next; i++; break;
        case "--db" when next is not null: db = next; i++; break;
        case "--key" when next is not null: keyPath = next; i++; break;
        case "--allow-plaintext": allowPlaintext = true; break;
        case "--zone-cfg" when next is not null: zoneCfg = next; i++; break;
        case "--spells" when next is not null: spellFile = next; i++; break;
    }
}

// The legacy zone headers (fog, sky, safe point, underworld, clip): runtime/cfg in the repository by default.
zoneCfg ??= new[] { "runtime/cfg", "../runtime/cfg", "../../runtime/cfg" }.FirstOrDefault(Directory.Exists);
// The client's spell file, as the legacy zone reads it (spdat.eff in the working directory; runtime/ in the repository).
spellFile ??= new[] { "spdat.eff", "runtime/spdat.eff", "../runtime/spdat.eff", "../../runtime/spdat.eff" }.FirstOrDefault(File.Exists);
IReadOnlyList<EQClassic.Server.Spells.Spell>? spells = spellFile is null ? null : EQClassic.Server.Spells.Spell.ReadFile(File.ReadAllBytes(spellFile));

var rsa = RSA.Create(2048);
if (File.Exists(keyPath))
    rsa.ImportFromPem(File.ReadAllText(keyPath));
else
{
    File.WriteAllText(keyPath, rsa.ExportPkcs8PrivateKeyPem());
    if (!OperatingSystem.IsWindows())
        File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
}
IAccountStore accounts = db is null ? InMemoryAccountStore.WithTestAccount() : new MySqlAccountStore(db);
IWorldAccountStore worldAccounts = db is null ? new InMemoryWorldAccountStore() : new MySqlWorldAccountStore(db);
ICharacterStore characters = db is null ? new InMemoryCharacterStore() : new MySqlCharacterStore(db);
ICreationData creationData = db is null ? new InMemoryCreationData() : new MySqlCreationData(db);
IZoneDataSource zoneData = db is null ? new InMemoryZoneDataSource() : new MySqlZoneDataSource(db);

var worlds = new WorldDirectory();
worlds.Register(new WorldServerInfo(1, "EverQuest Classic", worldAddress, worldPort, 0, WorldStatus.Up));

using var server = new LoginServer(new LoginService(accounts), worlds, rsa)
{
    Log = line => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"),
    AllowPlaintextLogin = allowPlaintext,
};
server.Start(port);
var zoneKeys = new ZoneKeys();
EQClassic.Server.Combat.IItemSource? items = db is null ? null : new EQClassic.Server.Combat.MySqlItemSource(db);
ILootSource? loot = db is null ? null : new MySqlLootSource(db);
// Factions (faction_list, npc_faction, npc_faction_entries); without a database every NPC is indifferent.
IMerchantSource? merchants = db is null ? null : new MySqlMerchantSource(db);
IFactionStandings factions = db is null ? new IndifferentFactions() : new DatabaseFactions(new MySqlFactionData(db));
using var zones = new ZoneServer(zoneKeys, name =>
{
    var data = zoneData.Load(name);
    if (data is null)
        return null;
    var meshPath = lantern is null ? null : Path.Combine(lantern, name, "Zone", "Meshes", name + "_collision.txt");
    var mesh = meshPath is not null && File.Exists(meshPath) ? ZoneCollisionMesh.LoadLanternZone(lantern!, name) : null;
    var bsp = lantern is null ? null : Path.Combine(lantern, name, "Zone", "bsp_tree.txt");
    var regions = bsp is not null && File.Exists(bsp) ? ZoneRegions.Load(bsp) : null;
    var cfg = zoneCfg is null ? null : Path.Combine(zoneCfg, name + ".cfg");
    var info = cfg is not null && File.Exists(cfg) ? ZoneInfo.FromLegacyCfg(File.ReadAllBytes(cfg)) : null;
    return new ZoneInstance(data, mesh) { Info = info, Loot = loot, Items = items, Spells = spells, Factions = factions, Merchants = merchants, Regions = regions };
})
{
    Log = server.Log, Characters = characters, PublicAddress = worldAddress, Items = items,
    FactionValues = db is null ? null : new MySqlFactionValueStore(db),
};
zones.Start(zonePort);
if (db is not null && ReadTimeOfDay(db) is { } tod)
{
    zones.Clock = new EqClock(tod.Hour, 0, zones.UptimeSeconds);
    zones.Date = (tod.Day, tod.Month, tod.Year);
}

using var world = new WorldServer(1, worlds, worldAccounts, characters, creationData)
{
    Log = server.Log,
    Zones = new ZoneHandoff(zoneKeys, worldAddress, zonePort),
};
world.Start(worldPort);
Console.WriteLine($"World on UDP {world.Port}, zones on UDP {zones.Port}, announced as {worldAddress}.");
Console.WriteLine($"Login server listening on UDP {server.Port} (protocol {ProtocolInfo.ConnectionKey}), accounts: {(db is null ? "test account only" : "database")}.");
Console.WriteLine(spells is null ? "No spdat.eff found: nobody can cast." : $"{spells.Count} spells read from {spellFile}.");
Console.WriteLine($"Server key fingerprint: {server.Fingerprint}");
Console.WriteLine("Ctrl+C to stop.");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
// Fixed 20 Hz simulation; network events are handled between ticks.
const float TickSeconds = 0.05f;
var clock = System.Diagnostics.Stopwatch.StartNew();
double nextTick = 0;
while (!stop.IsCancellationRequested)
{
    server.PollEvents();
    world.PollEvents();
    zones.PollEvents();
    while (clock.Elapsed.TotalSeconds >= nextTick)
    {
        zones.Tick(TickSeconds);
        nextTick += TickSeconds;
    }
    Thread.Sleep(5);
}

// World/Source/TimeOfDay.cpp: the legacy World keeps Norrath's time in time_of_day (hour, day, month,
// year) and advances it; the rewrite reads it once and runs its own clock from there (no write back
// while both servers share the database).
static (int Hour, int Day, int Month, int Year)? ReadTimeOfDay(string connectionString)
{
    try
    {
        using var connection = new MySqlConnector.MySqlConnection(connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT hour, day, month, year FROM time_of_day LIMIT 1";
        using var r = cmd.ExecuteReader();
        return r.Read() ? (Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)), Convert.ToInt32(r.GetValue(3))) : null;
    }
    catch (MySqlConnector.MySqlException)
    {
        return null; // no table: the default clock
    }
}
