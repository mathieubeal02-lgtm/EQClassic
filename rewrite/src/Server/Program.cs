using System.Security.Cryptography;
using EQClassic.Server.Accounts;
using EQClassic.Server.Login;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;

// Login server of the rewrite.
//   EQClassic.Server [--port N] [--db "<MySqlConnector connection string>"] [--key login-key.pem] [--allow-plaintext]
// Without --db, only the runbook's test account (test / test) exists (in memory).
// The RSA key is created on first start and kept in --key (default login-key.pem): clients pin its
// fingerprint, printed below, so it must survive restarts.
int port = ProtocolInfo.DefaultLoginPort;
string? db = null;
string keyPath = "login-key.pem";
bool allowPlaintext = false;
for (int i = 0; i < args.Length; i++)
{
    string? next = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--port" when int.TryParse(next, out var p): port = p; i++; break;
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

var worlds = new WorldDirectory();
worlds.Register(new WorldServerInfo(1, "EverQuest Classic", "127.0.0.1", 9000, 0, WorldStatus.Up));

using var server = new LoginServer(new LoginService(accounts), worlds, rsa)
{
    Log = line => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"),
    AllowPlaintextLogin = allowPlaintext,
};
server.Start(port);
Console.WriteLine($"Login server listening on UDP {server.Port} (protocol {ProtocolInfo.ConnectionKey}), accounts: {(db is null ? "test account only" : "database")}.");
Console.WriteLine($"Server key fingerprint: {server.Fingerprint}");
Console.WriteLine("Ctrl+C to stop.");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
while (!stop.IsCancellationRequested)
{
    server.PollEvents();
    Thread.Sleep(15);
}
