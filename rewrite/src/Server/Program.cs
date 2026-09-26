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
//                    [--key login-key.pem] [--allow-plaintext]
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
    }
}

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
using var zones = new ZoneServer(zoneKeys, name =>
{
    var data = zoneData.Load(name);
    if (data is null)
        return null;
    var meshPath = lantern is null ? null : Path.Combine(lantern, name, "Zone", "Meshes", name + "_collision.txt");
    var mesh = meshPath is not null && File.Exists(meshPath) ? ZoneCollisionMesh.LoadLanternZone(lantern!, name) : null;
    return new ZoneInstance(data, mesh);
}) { Log = server.Log, Characters = characters, PublicAddress = worldAddress, Items = db is null ? null : new EQClassic.Server.Combat.MySqlItemSource(db) };
zones.Start(zonePort);

using var world = new WorldServer(1, worlds, worldAccounts, characters, creationData)
{
    Log = server.Log,
    Zones = new ZoneHandoff(zoneKeys, worldAddress, zonePort),
};
world.Start(worldPort);
Console.WriteLine($"World on UDP {world.Port}, zones on UDP {zones.Port}, announced as {worldAddress}.");
Console.WriteLine($"Login server listening on UDP {server.Port} (protocol {ProtocolInfo.ConnectionKey}), accounts: {(db is null ? "test account only" : "database")}.");
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
