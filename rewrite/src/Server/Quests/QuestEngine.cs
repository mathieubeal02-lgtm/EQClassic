using System.Diagnostics;
using System.Text;

namespace EQClassic.Server.Quests;

/// <summary>One quest:: call of a script: its function name and arguments.</summary>
public sealed record QuestAction(string Function, IReadOnlyList<string> Args)
{
    public string Arg(int i) => i < Args.Count ? Args[i] : "";
    public int Int(int i, int fallback = 0) => int.TryParse(Arg(i), out int v) ? v : fallback;
}

/// <summary>
/// The legacy quest scripts (quests/&lt;zone&gt;/&lt;npc&gt;.pl, written for the zone's embedded Perl,
/// Zone/Source/embparser.cpp), run by a real Perl in a separate process: quest-host.pl sets the
/// variables, loads quests/plugins, calls the event, and returns every quest:: call as a line that
/// the zone then applies. Each event gets a few seconds; the game never waits for it.
/// </summary>
public sealed class QuestEngine
{
    public const int TimeoutMs = 3000;

    private readonly string _questsDir;
    private readonly string _host;
    private readonly string _perl;

    public QuestEngine(string questsDir, string hostScript, string perl = "perl")
    {
        _questsDir = questsDir;
        _host = hostScript;
        _perl = perl;
    }

    public string QuestsDirectory => _questsDir;

    /// <summary>
    /// PerlembParser's lookup: quests/&lt;zone&gt;/&lt;npc type id&gt;.pl, then quests/&lt;zone&gt;/&lt;name&gt;.pl
    /// (the name without its trailing digits, ` as -), then quests/templates/&lt;name&gt;.pl.
    /// </summary>
    public string? ScriptFor(string zone, int npcTypeId, string npcName)
    {
        string name = CleanName(npcName);
        foreach (var path in new[]
                 {
                     Path.Combine(_questsDir, zone, npcTypeId + ".pl"),
                     Path.Combine(_questsDir, zone, name + ".pl"),
                     Path.Combine(_questsDir, "templates", name + ".pl"),
                 })
            if (File.Exists(path))
                return path;
        return null;
    }

    public static string CleanName(string npcName) => npcName.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Replace('`', '-');

    /// <summary>Runs an event of a script; returns its quest:: calls (empty when it failed or took too long).</summary>
    public async Task<IReadOnlyList<QuestAction>> RunAsync(string script, string eventName, IReadOnlyDictionary<string, string> variables,
        IReadOnlyDictionary<int, int>? itemCount = null)
    {
        var start = new ProcessStartInfo(_perl)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.Latin1,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_questsDir)) ?? ".",
        };
        start.ArgumentList.Add(_host);
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(eventName);
        start.ArgumentList.Add(Path.Combine(_questsDir, "plugins"));
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Array.Empty<QuestAction>(); // no Perl on this machine
        }
        var input = new StringBuilder();
        foreach (var (key, value) in variables)
            input.Append(key).Append('\t').Append(value.Replace('\t', ' ').Replace('\n', ' ')).Append('\n');
        foreach (var (item, count) in itemCount ?? new Dictionary<int, int>())
            input.Append("itemcount\t").Append(item).Append('\t').Append(count).Append('\n');
        await process.StandardInput.WriteAsync(input.ToString());
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeoutMs);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return [new QuestAction("error", [$"{Path.GetFileName(script)} {eventName}: no answer in {TimeoutMs} ms"])];
        }
        var actions = new List<QuestAction>();
        foreach (var line in (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split('\t');
            actions.Add(new QuestAction(fields[0], fields.Skip(1).ToArray()));
        }
        string err = await errors;
        if (err.Length > 0 && actions.Count == 0)
            actions.Add(new QuestAction("error", [err.Trim()]));
        return actions;
    }
}
