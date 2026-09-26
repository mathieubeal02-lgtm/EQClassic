using System.Diagnostics;
using System.Text;
using EQClassic.ClientCore;
using EQClassic.Shared.Zone;
using MySqlConnector;

// Plays the game against a server, the way a player would, through the client library
// (EQClassic.ClientCore): each scenario prints [ OK ] or [FAIL] with what it saw, and the whole
// run is written to a Markdown report. The account must be a GM (account.status >= 80): the
// scenarios place the character with #zone / #goto and give it what they need.
//   EQClassic.PlayTest <host> <port> <fingerprint|-> <user> <password> <character> [--db "<connection>"] [--report file.md]
if (args.Length < 6)
{
    Console.Error.WriteLine("usage: EQClassic.PlayTest <host> <port> <fingerprint|-> <user> <password> <character> [--db cs] [--report file]");
    return 2;
}
string host = args[0], fingerprint = args[2], user = args[3], password = args[4], character = args[5];
int port = int.Parse(args[1]);
string? db = null, reportPath = null;
for (int i = 6; i + 1 < args.Length; i++)
{
    if (args[i] == "--db") db = args[++i];
    else if (args[i] == "--report") reportPath = args[++i];
}

var heard = new List<string>();
var results = new List<(string Name, bool Ok, string Detail)>();
var client = new GameClient(fingerprint == "-" ? null : fingerprint);
client.MessageReceived += line => { lock (heard) heard.Add(line); };
var clients = new List<GameClient> { client };

bool Run(Func<bool> done, double seconds = 10)
{
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed.TotalSeconds < seconds)
    {
        foreach (var c in clients)
            c.Update(0.02f);
        if (done())
            return true;
        Thread.Sleep(20);
    }
    return done();
}

int mark = 0;
List<string> Since() { lock (heard) return heard.Skip(mark).ToList(); }
void Mark() { lock (heard) mark = heard.Count; }
bool Heard(Func<string, bool> match, double seconds = 8) => Run(() => Since().Any(match), seconds);
string Last(int n = 3) => string.Join(" | ", Since().TakeLast(n));

void Step(string name, Func<(bool Ok, string Detail)> body)
{
    Mark();
    (bool ok, string detail) outcome;
    try { outcome = body(); }
    catch (Exception e) { outcome = (false, "exception: " + e.Message); }
    results.Add((name, outcome.ok, outcome.detail));
    Console.WriteLine($"{(outcome.ok ? "[ OK ]" : "[FAIL]")} {name,-28} {outcome.detail}");
}

void Chat(string line) => client.ExecuteChat(line);
bool InZone(string zone) => client.State == GameState.InZone && string.Equals(client.Zone?.Zone, zone, StringComparison.OrdinalIgnoreCase);

// An NPC of the zone from the database (merchants, bankers...) that is up right now.
string? Npc(string zone, string condition)
{
    if (db is null) return null;
    using var connection = new MySqlConnection(db);
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = $"SELECT DISTINCT n.name FROM spawn2 s JOIN spawnentry e ON e.spawngroupID = s.spawngroupID JOIN npc_types_without n ON n.id = e.npcID WHERE s.zone = @zone AND {condition}";
    cmd.Parameters.AddWithValue("@zone", zone);
    var names = new List<string>();
    using (var r = cmd.ExecuteReader())
        while (r.Read()) names.Add(r.GetString(0));
    return names.FirstOrDefault(n => client.Zone?.Entities.Any(e => !e.Spawn.IsPlayer && e.Spawn.Name.StartsWith(n, StringComparison.OrdinalIgnoreCase)) == true);
}

object? Scalar(string sql, params (string, object)[] parameters)
{
    if (db is null) return null;
    using var connection = new MySqlConnection(db);
    connection.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = sql;
    foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
    return cmd.ExecuteScalar();
}

// Goes next to the first NPC whose name starts with the given one.
bool GoTo(string name)
{
    var npc = client.Zone?.Entities.FirstOrDefault(e => !e.Spawn.IsPlayer && !e.Spawn.IsCorpse && e.Spawn.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
    if (npc is null)
        return false;
    client.SetTarget(npc.Id);
    Chat("#goto");
    Run(() => false, 1.5);
    return true;
}

Step("connect and log in", () =>
{
    client.Connect(host, port);
    if (!Run(() => client.State == GameState.Login, 10)) return (false, $"state {client.State} {client.LastError}");
    client.Login(user, password);
    if (!Run(() => client.State == GameState.ServerSelect, 10)) return (false, $"state {client.State} {client.LastError}");
    return (true, $"{client.Worlds.Count} world(s)");
});
Step("world and characters", () =>
{
    client.SelectWorld(client.Worlds[0].Id);
    if (!Run(() => client.State == GameState.CharacterSelect, 10)) return (false, $"state {client.State} {client.LastError}");
    return (client.Characters.Any(c => c.Name == character), string.Join(", ", client.Characters.Select(c => $"{c.Name} {c.Level} {c.Zone}")));
});
Step("enter the world", () =>
{
    client.EnterWorld(character);
    bool ok = Run(() => client.State == GameState.InZone && client.Player != null, 30);
    return (ok, ok ? $"{client.Zone!.Zone}, {client.Zone.Count} entities" : $"state {client.State} {client.LastError}");
});
Step("GM: #zone qeynos2", () =>
{
    Chat("#zone qeynos2");
    bool ok = Run(() => InZone("qeynos2") && client.Player != null, 30);
    return (ok, ok ? $"in qeynos2, {client.Zone!.Count} entities" : $"state {client.State} zone {client.Zone?.Zone} {Last()}");
});
Step("say, /who, /loc", () =>
{
    Chat("playtest here");
    bool said = Heard(l => l == "You say, 'playtest here'");
    Chat("/who");
    bool who = Heard(l => l.StartsWith("There are") || l.StartsWith("There is"));
    Chat("/loc");
    bool loc = Heard(l => l.StartsWith("Your Location is"));
    return (said && who && loc, Last(4));
});
Step("GM: #clearinventory", () =>
{
    Chat("#clearinventory");
    bool ok = Run(() => client.Inventory != null && Enumerable.Range(22, 8).All(i => client.Inventory.Slots[i].ItemId == 0), 8);
    return (ok, Last(1));
});
Step("GM: #level 10", () =>
{
    Chat("#level 10");
    bool ok = Run(() => client.Experience?.Level == 10, 8);
    return (ok, $"level {client.Experience?.Level}, hp {client.Hp}/{client.MaxHp}");
});
Step("target and consider", () =>
{
    var target = client.TargetNearest();
    if (target is null) return (false, "nobody near");
    client.Consider();
    bool ok = Heard(l => l.Contains(" -- "));
    return (ok, ok ? Since().Last(l => l.Contains(" -- ")) : Last());
});
Step("quest: hail Brohan", () =>
{
    if (!GoTo("Brohan")) return (false, "Brohan Ironforge not in the zone");
    client.Hail();
    bool ok = Heard(l => l.StartsWith("Brohan Ironforge says"), 10);
    return (ok, ok ? Since().First(l => l.StartsWith("Brohan Ironforge says")) : Last());
});
Step("melee: kill a rodent", () =>
{
    if (!GoTo("a_rodent")) return (false, "no rodent in the zone");
    client.ToggleAutoAttack();
    bool ok = Heard(l => l.StartsWith("You have slain"), 90);
    if (client.AutoAttacking) client.ToggleAutoAttack();
    return (ok, ok ? string.Join(" | ", Since().Where(l => l.StartsWith("You have slain") || l.Contains("experience"))) : Last(4));
});
Step("loot the corpse", () =>
{
    client.Loot();
    bool opened = Run(() => client.LootingCorpse != null, 8);
    int items = client.LootItems.Count;
    for (int i = items - 1; i >= 0; i--) client.TakeLoot(i);
    Run(() => false, 1);
    client.EndLoot();
    bool closed = Run(() => client.LootingCorpse == null, 5);
    return (opened && closed, $"{items} item(s); {Last(2)}");
});
Step("spells: scribe, memorise, cast", () =>
{
    Chat("#scribespells 10");
    if (!Run(() => client.SpellBook?.Spells.Count > 0, 8)) return (false, "the book stayed empty");
    var spell = client.SpellBook!.Spells.FirstOrDefault(s => s.Beneficial && s.Mana <= client.MaxMana) ?? client.SpellBook.Spells[0];
    client.Memorize(0, spell.SpellId);
    if (!Run(() => client.GemSpell(0)?.SpellId == spell.SpellId, 8)) return (false, $"{spell.Name} not memorised");
    Chat("#mana");
    client.SetTarget(client.Zone!.YourEntityId);
    int before = client.Mana;
    client.Cast(0);
    bool cast = Heard(l => l.StartsWith("You begin casting") || l.Contains("fizzle"), 5) && Run(() => client.Casting == null, 15);
    return (cast, $"{spell.Name}: mana {before} -> {client.Mana}; {Last(3)}");
});
Step("GM: #summonitem, eat", () =>
{
    Chat("#summonitem 13014 2");
    bool got = Run(() => client.Inventory?.Slots.Any(s => s.ItemId == 13014) == true, 8);
    if (!got) return (false, "no muffin arrived");
    int slot = client.Inventory!.Slots.ToList().FindIndex(s => s.ItemId == 13014);
    client.Consume(slot);
    bool ate = Heard(l => l.StartsWith("Chomp") || l.Contains("could not possibly eat"), 5);
    return (ate, Last(2));
});
Step("equipment change", () =>
{
    // A chest armour the character's class may wear, with a material others see.
    int cls = Scalar("SELECT profile FROM character_ WHERE name = @n", ("@n", character)) is byte[] profile && profile.Length > 58 ? profile[58] : 1; // PlayerProfile class
    int item = Convert.ToInt32(Scalar("SELECT id FROM items_axclassic WHERE (slots & 131072) <> 0 AND (classes & @c) <> 0 AND material > 0 LIMIT 1", ("@c", 1 << (cls - 1))) ?? 4204);
    if (client.Inventory?.Slots[17].ItemId is int old and not 0)
        client.MoveItem(17, client.Inventory!.Slots.ToList().FindIndex(22, s => s.ItemId == 0)); // make room
    Run(() => false, 1);
    Chat($"#summonitem {item}");
    if (!Run(() => client.Inventory?.Slots.Any(s => s.ItemId == item) == true, 8)) return (false, $"item {item} did not arrive");
    int slot = client.Inventory!.Slots.ToList().FindIndex(22, s => s.ItemId == item);
    client.MoveItem(slot, 17);
    bool worn = Run(() => client.Inventory!.Slots[17].ItemId == item, 8);
    return (worn, worn ? $"{client.Inventory!.Slots[17].Name} worn on the chest" : Last(2));
});
Step("GM: #givemoney", () =>
{
    Chat("#givemoney 0 0 0 20");
    bool ok = Run(() => client.Inventory?.Platinum >= 20, 8);
    return (ok, $"{client.Inventory?.Platinum}p {client.Inventory?.Gold}g");
});
Step("merchant: buy", () =>
{
    var name = Npc("qeynos2", "n.merchant_id > 0");
    if (name is null || !GoTo(name)) return (false, name is null ? "no merchant known (--db)" : $"{name} not in the zone");
    string? used = client.Use();
    if (!Run(() => client.Merchant?.Items.Count > 0, 8)) return (false, $"{name}'s goods did not come; use: {used ?? "sent"}; {Last(2)}");
    // Sell what the general slots carry (not bags), which tests selling and makes room.
    int sold = 0;
    for (int slot = 22; slot < 30; slot++)
        if (client.Inventory!.Slots[slot] is { ItemId: not 0, BagSlots: 0 } item)
        {
            int count = client.Inventory.Slots.Count(s => s.ItemId != 0);
            client.Sell(slot);
            if (Run(() => client.Inventory!.Slots.Count(s => s.ItemId != 0) < count, 5)) sold++;
        }
    long before = client.Inventory!.Platinum * 1000L + client.Inventory.Gold * 100 + client.Inventory.Silver * 10 + client.Inventory.Copper;
    string good = client.Merchant!.Items[0].Name;
    client.Buy(0);
    bool paid = Run(() => client.Inventory!.Platinum * 1000L + client.Inventory.Gold * 100 + client.Inventory.Silver * 10 + client.Inventory.Copper < before, 8);
    client.CloseMerchant();
    return (paid && sold > 0, $"{name}: sold {sold}, bought {good}; {Last(2)}");
});
Step("bank: deposit", () =>
{
    Chat("#zone qeynos"); // the bank is in South Qeynos
    if (!Run(() => InZone("qeynos"), 30)) return (false, "could not reach qeynos");
    var name = Npc("qeynos", "n.class = 40");
    if (name is null || !GoTo(name)) return (false, name is null ? "no banker known (--db)" : $"{name} not in the zone");
    client.Use();
    if (!Run(() => client.Bank != null, 8)) return (false, $"{name}'s bank did not open; {Last(2)}");
    int before = client.Bank!.Platinum;
    client.BankMoney(1, 0, 0, 0, 0, 0, 0, 0);
    bool ok = Run(() => client.Bank?.Platinum == before + 1, 8);
    int after = client.Bank?.Platinum ?? -1;
    client.CloseBank();
    return (ok, $"{name}: vault {before}p -> {after}p");
});
Step("zone change and back", () =>
{
    Chat("#zone qeynos");
    bool there = Run(() => InZone("qeynos"), 30);
    Chat("#zone qeynos2");
    bool back = Run(() => InZone("qeynos2"), 30);
    return (there && back, $"qeynos {(there ? "ok" : "no")}, back {(back ? "ok" : "no")}");
});

// Two players: a second account (bot2 / bot2) with Qpartner, made if missing, meets the first in Grobb.
var partner = new GameClient(fingerprint == "-" ? null : fingerprint);
var partnerHeard = new List<string>();
partner.MessageReceived += line => { lock (partnerHeard) partnerHeard.Add(line); };
Step("second player enters", () =>
{
    clients.Add(partner);
    partner.Connect(host, port);
    if (!Run(() => partner.State == GameState.Login, 10)) return (false, $"state {partner.State}");
    partner.Login("bot2", "bot2");
    if (!Run(() => partner.State == GameState.ServerSelect, 10)) return (false, $"login: {partner.LastError}");
    partner.SelectWorld(partner.Worlds[0].Id);
    if (!Run(() => partner.State == GameState.CharacterSelect, 10)) return (false, $"world: {partner.LastError}");
    if (!partner.Characters.Any(c => c.Name == "Qpartner"))
    {
        // As the creation screen: the server's combinations, a troll shaman of Grobb, every bonus point spent.
        partner.RequestCreationOptions();
        if (!Run(() => partner.CreationOptions?.Count > 0, 10)) return (false, "no creation options");
        var builder = new CharacterBuilder(partner.CreationOptions!);
        builder.SelectRace(9);
        builder.SelectClass(10);
        for (int stat = 0; builder.PointsLeft > 0 && stat < 70; stat++)
            builder.AddPoint(stat % 7);
        partner.CreateCharacter(builder.Request("Qpartner"));
        if (!Run(() => partner.Characters.Any(c => c.Name == "Qpartner"), 10)) return (false, $"creation: {partner.LastError} {partnerHeard.LastOrDefault()}");
    }
    partner.EnterWorld("Qpartner");
    if (!Run(() => partner.State == GameState.InZone, 30)) return (false, $"enter: {partner.State} {partner.LastError}");
    string zone = partner.Zone!.Zone;
    Chat($"#zone {zone}");
    if (!Run(() => InZone(zone), 30)) return (false, $"the first player could not reach {zone}");
    Chat("#goto Qpartner");
    Run(() => false, 2);
    // Room in Qpartner's packs for what the trade gives.
    if (client.Zone!.Entities.FirstOrDefault(e => e.Spawn.IsPlayer && e.Spawn.Name == "Qpartner") is { } him)
    {
        client.SetTarget(him.Id);
        Chat("#clearinventory");
        Run(() => false, 1);
    }
    return (true, $"Qpartner in {zone} with {character}");
});
Step("group: invite, follow, chat", () =>
{
    Chat("/invite Qpartner");
    bool invited = Run(() => partnerHeard.Any(l => l.Contains("invites you")), 8);
    partner.ExecuteChat("/follow");
    bool grouped = Run(() => client.Group?.Members.Count == 2 && partner.Group?.Members.Count == 2, 8);
    Chat("/g playtest group");
    bool chat = Run(() => partnerHeard.Any(l => l.Contains("tells the group, 'playtest group'")), 8);
    Chat("/disband");
    Run(() => client.Group == null || client.Group.Members.Count == 0, 5);
    return (invited && grouped && chat, $"invited {invited}, grouped {grouped}, group chat {chat}");
});
Step("trade an item", () =>
{
    Chat("#summonitem 13014");
    if (!Run(() => client.Inventory?.Slots.Any(s => s.ItemId == 13014) == true, 8)) return (false, "no muffin to give");
    var them = client.Zone!.Entities.FirstOrDefault(e => e.Spawn.IsPlayer && e.Spawn.Name == "Qpartner");
    if (them is null) return (false, "Qpartner not seen");
    client.SetTarget(them.Id);
    Chat("/trade");
    if (!Run(() => client.Trade != null && partner.Trade != null, 8)) return (false, $"no trade window; {Last(2)}");
    int slot = client.Inventory!.Slots.ToList().FindIndex(22, s => s.ItemId == 13014);
    int before = partner.Inventory?.Slots.Count(s => s.ItemId == 13014) ?? 0;
    client.OfferItem(slot);
    Run(() => client.Trade?.Mine.Items.Count > 0, 5);
    client.AcceptTrade();
    partner.AcceptTrade();
    bool got = Run(() => (partner.Inventory?.Slots.Count(s => s.ItemId == 13014) ?? 0) > before, 8);
    return (got, got ? "Qpartner received the muffin" : Last(2));
});
partner.Dispose();

int failed = results.Count(r => !r.Ok);
Console.WriteLine($"{results.Count - failed} passed, {failed} failed.");
if (reportPath != null)
{
    var md = new StringBuilder($"# Play test {DateTime.Now:yyyy-MM-dd HH:mm}\n\n{character} on {host}:{port}\n\n| Scenario | Result | Seen |\n|---|---|---|\n");
    foreach (var (name, ok, detail) in results)
        md.Append($"| {name} | {(ok ? "OK" : "**FAIL**")} | {detail.Replace("|", "/")} |\n");
    File.WriteAllText(reportPath, md.ToString());
}
client.Dispose();
return failed == 0 ? 0 : 1;
