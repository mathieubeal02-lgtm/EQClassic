using EQClassic.Server.Accounts;
using EQClassic.Server.Login;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;

// Minimal login server of the rewrite. Usage: EQClassic.Server [--port N]
// Accounts: the runbook's test account (test / test) until the database store exists.
int port = ProtocolInfo.DefaultLoginPort;
for (int i = 0; i < args.Length - 1; i++)
    if (args[i] == "--port" && int.TryParse(args[i + 1], out var p))
        port = p;

var worlds = new WorldDirectory();
worlds.Register(new WorldServerInfo(1, "EverQuest Classic", "127.0.0.1", 9000, 0, WorldStatus.Up));

using var server = new LoginServer(new LoginService(InMemoryAccountStore.WithTestAccount()), worlds)
{
    Log = line => Console.WriteLine($"{DateTime.Now:HH:mm:ss} {line}"),
};
server.Start(port);
Console.WriteLine($"Login server listening on UDP {server.Port} (protocol {ProtocolInfo.ConnectionKey}). Ctrl+C to stop.");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
while (!stop.IsCancellationRequested)
{
    server.PollEvents();
    Thread.Sleep(15);
}
