using EQClassic.Shared.Client;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;

// Command-line client for the rewrite's login server: logs in (encrypted), lists the worlds and
// asks for a world key. One [ OK ]/[FAIL] line per step; exit code 0 when every step passed.
//   EQClassic.Cli <host> <user> <password> [--port N] [--fingerprint HEX] [--world ID]
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: EQClassic.Cli <host> <user> <password> [--port N] [--fingerprint HEX] [--world ID]");
    return 2;
}
string host = args[0], user = args[1], password = args[2];
int port = ProtocolInfo.DefaultLoginPort;
int? worldId = null;
string? fingerprint = null;
for (int i = 3; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[++i]); break;
        case "--fingerprint": fingerprint = args[++i]; break;
        case "--world": worldId = int.Parse(args[++i]); break;
    }
}

int failures = 0;
void Step(bool ok, string name, string detail)
{
    Console.WriteLine($"[{(ok ? " OK " : "FAIL")}] {name,-14} {detail}");
    if (!ok) failures++;
}

using var client = new LoginClient { ExpectedServerFingerprint = fingerprint };
var inbox = new Queue<IMessage>();
bool WaitUntil(Func<bool> condition, int ms = 5000)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(ms);
    while (DateTime.UtcNow < deadline)
    {
        foreach (var m in client.Poll())
            inbox.Enqueue(m);
        if (condition())
            return true;
        Thread.Sleep(10);
    }
    return false;
}
T? Next<T>() where T : class, IMessage
{
    WaitUntil(() => inbox.Any(m => m is T));
    while (inbox.Count > 0)
        if (inbox.Dequeue() is T found)
            return found;
    return null;
}

client.Connect(host, port);
if (!WaitUntil(() => client.IsReadyToLogin || client.State == ConnectionState.Disconnected) || !client.IsReadyToLogin)
{
    Step(false, "connect", $"{host}:{port}: {client.DisconnectReason ?? "no answer"}");
    return 1;
}
Step(true, "connect", $"{host}:{port}, server key {client.ServerFingerprint}{(fingerprint is null ? " (not pinned)" : " (pinned)")}");

client.Login(user, password);
var login = Next<LoginResponse>();
Step(login?.Result == LoginResult.Success, "login", login is null ? "no answer" : login.Result == LoginResult.Success ? $"session id {login.SessionId}" : $"{login.Result}: {login.Message}");
if (login?.Result != LoginResult.Success)
    return 1;

client.Send(new ServerListRequest());
var list = Next<ServerListResponse>();
Step(list is { Worlds.Count: > 0 }, "server list", list is null ? "no answer" : string.Join(", ", list.Worlds.Select(w => $"#{w.Id} {w.Name} {w.Address}:{w.Port} {w.Status}")));
if (list is not { Worlds.Count: > 0 })
    return 1;

client.Send(new PlayRequest(worldId ?? list.Worlds[0].Id));
var play = Next<PlayResponse>();
Step(play?.Accepted == true, "world key", play is null ? "no answer" : play.Accepted ? $"key issued for {play.Address}:{play.Port} ({play.SessionKey.Length} characters)" : play.Message);

client.Disconnect();
WaitUntil(() => false, 200);
return failures == 0 ? 0 : 1;
