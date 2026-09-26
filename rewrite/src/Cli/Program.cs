using EQClassic.Shared.Characters;
using EQClassic.Shared.Client;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;

// Command-line client for the rewrite: logs in (encrypted), lists the worlds, gets a world key, then
// joins World, lists the characters and optionally enters the world with one of them.
// One [ OK ]/[FAIL] line per step; exit code 0 when every step passed.
//   EQClassic.Cli <host> <user> <password> [--port N] [--fingerprint HEX] [--world ID] [--enter NAME]
//                 [--create NAME [--race 9] [--class 10] [--start-zone grobb]]   (default: troll shaman in Grobb)
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: EQClassic.Cli <host> <user> <password> [--port N] [--fingerprint HEX] [--world ID] [--enter NAME]");
    return 2;
}
string host = args[0], user = args[1], password = args[2];
int port = ProtocolInfo.DefaultLoginPort;
int? worldId = null;
string? fingerprint = null;
string? enter = null;
string? create = null;
int race = 9, @class = 10;
string startZone = "grobb";
for (int i = 3; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--port": port = int.Parse(args[++i]); break;
        case "--fingerprint": fingerprint = args[++i]; break;
        case "--world": worldId = int.Parse(args[++i]); break;
        case "--enter": enter = args[++i]; break;
        case "--create": create = args[++i]; break;
        case "--race": race = int.Parse(args[++i]); break;
        case "--class": @class = int.Parse(args[++i]); break;
        case "--start-zone": startZone = args[++i]; break;
    }
}

int failures = 0;
void Step(bool ok, string name, string detail)
{
    Console.WriteLine($"[{(ok ? " OK " : "FAIL")}] {name,-14} {detail}");
    if (!ok) failures++;
}

var client = new LoginClient { ExpectedServerFingerprint = fingerprint };
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

if (play?.Accepted != true)
    return 1;
string sessionId = login.SessionId;
client.Disconnect();
WaitUntil(() => false, 200);
client.Dispose();

// World: a new connection to the address the login server gave.
client = new LoginClient();
inbox.Clear();
client.Connect(play.Address, play.Port);
if (!WaitUntil(() => client.State != ConnectionState.Connecting) || client.State != ConnectionState.Connected)
{
    Step(false, "world", $"{play.Address}:{play.Port}: {client.DisconnectReason ?? "no answer"}");
    return 1;
}
client.Send(new WorldLoginRequest(sessionId, play.SessionKey));
var world = Next<WorldLoginResponse>();
Step(world?.Accepted == true, "characters", world is null ? "no answer" : !world.Accepted ? world.Message
    : world.Characters.Count == 0 ? "(none)" : string.Join(", ", world.Characters.Select(c => $"{c.Name} (race {c.Race}, class {c.Class}, level {c.Level}, {c.Zone})")));

if (create is not null && world?.Accepted == true)
{
    // Stats of the troll shaman the Trilogy client sent in the captured creation packet.
    client.Send(new CreateCharacterRequest(create, race, @class, 0, 203, 1, startZone, new CharacterStats(108, 119, 45, 75, 52, 83, 95)));
    var created = Next<CreateCharacterResponse>();
    Step(created?.Accepted == true, "create", created is null ? "no answer" : created.Accepted
        ? $"{create} created; characters: {string.Join(", ", created.Characters.Select(c => c.Name))}" : created.Message);
}

if (enter is not null && world?.Accepted == true)
{
    client.Send(new EnterWorldRequest(enter));
    var entered = Next<EnterWorldResponse>();
    Step(entered?.Accepted == true, "enter world", entered is null ? "no answer" : entered.Accepted ? $"{enter} in {entered.Zone} at ({entered.X:0.#}, {entered.Y:0.#}, {entered.Z:0.#})" : entered.Message);
}

client.Disconnect();
WaitUntil(() => false, 200);
client.Dispose();
return failures == 0 ? 0 : 1;
