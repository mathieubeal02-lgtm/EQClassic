using EQClassic.Shared.Characters;
using EQClassic.Shared.Client;
using EQClassic.Shared.Login;
using EQClassic.Shared.Protocol;
using EQClassic.Shared.Zone;

// Command-line client for the rewrite: logs in (encrypted), lists the worlds, gets a world key, then
// joins World, lists the characters and optionally enters the world with one of them.
// One [ OK ]/[FAIL] line per step; exit code 0 when every step passed.
//   EQClassic.Cli <host> <user> <password> [--port N] [--fingerprint HEX] [--world ID] [--enter NAME]
//                 [--create NAME [--race 9] [--class 10] [--start-zone grobb]]   (default: troll shaman in Grobb)
//                 [--zone-seconds 10]   with --enter: stay that long in the zone and report NPC movement
//                 [--hail NPC]          with --enter: walk to that NPC, hail it and print what it answers
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
int zoneSeconds = 10;
string? hail = null;
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
        case "--zone-seconds": zoneSeconds = int.Parse(args[++i]); break;
        case "--hail": hail = args[++i]; break;
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

    if (entered is { Accepted: true, ZonePort: > 0 })
    {
        client.Disconnect();
        WaitUntil(() => false, 200);
        client.Dispose();
        client = new LoginClient();
        inbox.Clear();
        client.Connect(entered.ZoneAddress, entered.ZonePort);
        WaitUntil(() => client.State != ConnectionState.Connecting);
        client.Send(new ZoneEnterRequest(enter, entered.ZoneKey));
        var zone = Next<ZoneEnterResponse>();
        Step(zone?.Accepted == true, "zone", zone is null ? "no answer" : zone.Accepted
            ? $"in {zone.Zone} as entity {zone.YourEntityId}, {zone.Entities.Count(e => !e.IsPlayer)} NPC(s), {zone.Entities.Count(e => e.IsPlayer)} player(s)" : zone.Message);
        if (zone?.Accepted == true)
        {
            var names = zone.Entities.ToDictionary(e => e.Id, e => e.Name);
            var lastZ = zone.Entities.ToDictionary(e => e.Id, e => e.Z);
            var moving = new HashSet<int>();
            (float Step, string Name) worstUp = (0, ""), worstDown = (0, "");
            int updates = 0;
            var until = DateTime.UtcNow.AddSeconds(zoneSeconds);
            while (DateTime.UtcNow < until)
            {
                foreach (var m in client.Poll())
                {
                    if (m is not EntityPositions p) continue;
                    updates++;
                    foreach (var e in p.Positions)
                    {
                        moving.Add(e.Id);
                        float dz = e.Z - lastZ.GetValueOrDefault(e.Id, e.Z);
                        if (dz > worstUp.Step) worstUp = (dz, names.GetValueOrDefault(e.Id, "?"));
                        if (-dz > worstDown.Step) worstDown = (-dz, names.GetValueOrDefault(e.Id, "?"));
                        lastZ[e.Id] = e.Z;
                    }
                }
                Thread.Sleep(10);
            }
            Step(updates > 0 && worstUp.Step <= 6, "npc movement",
                $"{updates} updates in {zoneSeconds} s, {moving.Count} moving entities, largest rise {worstUp.Step:0.0} ({worstUp.Name}), largest drop {worstDown.Step:0.0} ({worstDown.Name})");
            if (hail is not null)
                Hail(zone, hail, lastZ);
        }
    }
}

client.Disconnect();
WaitUntil(() => false, 200);
client.Dispose();
return failures == 0 ? 0 : 1;

// Walks straight to the NPC (below the server's speed limit), targets it, hails it, and prints the replies.
void Hail(ZoneEnterResponse zone, string npcName, Dictionary<int, float> lastZ)
{
    var me = zone.Entities.First(e => e.Id == zone.YourEntityId);
    var npc = zone.Entities.FirstOrDefault(e => !e.IsPlayer && e.Name.StartsWith(npcName, StringComparison.OrdinalIgnoreCase));
    if (npc is null)
    {
        Step(false, "hail", $"no NPC named {npcName}");
        return;
    }
    float x = me.X, y = me.Y, z = me.Z;
    var refusals = new List<string>();
    while (Math.Sqrt((npc.X - x) * (npc.X - x) + (npc.Y - y) * (npc.Y - y)) > 15)
    {
        float dx = npc.X - x, dy = npc.Y - y, d = MathF.Sqrt(dx * dx + dy * dy), step = Math.Min(d - 10, 5f); // 50 units/s
        x += dx / d * step;
        y += dy / d * step;
        z += (npc.Z - z) * Math.Min(1f, step / d);
        client.Send(new PlayerMove(x, y, z, 0));
        Thread.Sleep(100);
        foreach (var m in client.Poll())
            if (m is MoveCorrection c)
            {
                refusals.Add(c.Reason);
                (x, y, z) = (c.X, c.Y, c.Z);
            }
        if (refusals.Count > 20)
            break;
    }
    client.Send(new SetTarget(npc.Id));
    string clean = new string(npc.Name.Where(c => !char.IsAsciiDigit(c)).ToArray()).Replace('_', ' ');
    client.Send(new ChatSend(ChatChannel.Say, "", $"Hail, {clean}"));
    var replies = new List<string>();
    var until = DateTime.UtcNow.AddSeconds(4);
    while (DateTime.UtcNow < until)
    {
        foreach (var m in client.Poll())
            if (m is ChatMessage chat && chat.From != enter)
                replies.Add($"{chat.From} ({chat.Channel}): {chat.Text}");
        Thread.Sleep(20);
    }
    Step(replies.Count > 0, "hail", replies.Count > 0 ? string.Join(" | ", replies)
        : $"no answer from {npc.Name} (at {x:0},{y:0}; {refusals.Count} moves refused: {string.Join(", ", refusals.Distinct())})");
}
