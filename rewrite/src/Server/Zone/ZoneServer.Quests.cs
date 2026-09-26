using System.Globalization;
using EQClassic.Server.Quests;
using EQClassic.Shared.World;
using EQClassic.Shared.Zone;

namespace EQClassic.Server.Zone;

/// <summary>
/// The quest scripts' events: EVENT_SAY when a player says something to the NPC they target (a
/// hail, a keyword in brackets), EVENT_ITEM when they hand it items, EVENT_SPAWN, EVENT_AGGRO,
/// EVENT_ATTACK, EVENT_SLAY, EVENT_DEATH, EVENT_TIMER, EVENT_SIGNAL, EVENT_ENTER and EVENT_EXIT. Only scripts that define the event run;
/// they run outside the game loop and what they did is applied at the next tick.
/// </summary>
public sealed partial class ZoneServer
{
    /// <summary>The legacy Perl quests; null: NPCs have nothing to say.</summary>
    public QuestEngine? Quests { get; set; }

    private readonly List<(ZoneInstance Instance, int NpcId, int PlayerId, (string, Vec3) Speaker, Task<IReadOnlyList<QuestAction>> Run)> _questRuns = new();

    /// <summary>Script of an NPC type in a zone when it defines the event, or null.</summary>
    private string? ScriptFor(ZoneInstance instance, NpcTemplate template, string eventName) =>
        Quests is { } quests && quests.ScriptFor(instance.ShortName, template.Id, template.Name) is { } script && quests.HasEvent(script, eventName)
            ? script
            : null;

    /// <summary>An event of a living NPC, with the variables of the moment.</summary>
    private void QuestEvent(ZoneInstance instance, int npcId, int playerId, string eventName, Action<Dictionary<string, string>>? more = null)
    {
        if (Quests is null || instance.Get(npcId) is not { IsPlayer: false, IsCorpse: false, Npc: { } template } || ScriptFor(instance, template, eventName) is null)
            return;
        var vars = instance.QuestVariables(npcId, playerId);
        more?.Invoke(vars);
        Trigger(instance, npcId, playerId, template, eventName, vars);
    }

    private void Trigger(ZoneInstance instance, int npcId, int playerId, NpcTemplate template, string eventName, IReadOnlyDictionary<string, string> vars,
        IReadOnlyDictionary<int, int>? itemCount = null)
    {
        if (ScriptFor(instance, template, eventName) is not { } script)
            return;
        if (playerId != 0 && Quests!.FactionsAsked(script) is { Count: > 0 } factions)
        {
            var withFactions = new Dictionary<string, string>(vars);
            foreach (int faction in factions)
                withFactions[$"factionlevel.{faction}"] = instance.FactionLevel(playerId, faction).ToString();
            vars = withFactions;
        }
        var at = new Vec3(Number(vars, "x"), Number(vars, "y"), Number(vars, "z"));
        var speaker = (vars.GetValueOrDefault("mname") ?? DisplayName(template.Name), at);
        _questRuns.Add((instance, npcId, playerId, speaker, Quests!.RunAsync(script, eventName, vars, itemCount)));
    }

    private static float Number(IReadOnlyDictionary<string, string> vars, string key) =>
        float.TryParse(vars.GetValueOrDefault(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;

    /// <summary>Client::ChannelMessageReceived: a say reaches the targeted NPC's script (Mob::CheckQuests EVENT_SAY).</summary>
    private void Said(ZoneInstance instance, int playerId, string text)
    {
        if (instance.Get(playerId) is not { PlayerTargetId: { } targetId } player || instance.Get(targetId) is not { } npc
            || Distance2(player.Position, npc.Position) > SayRange * SayRange || npc.TargetId is not null) // busy fighting
            return;
        if (Quests is not null && npc.Npc is { } template && ScriptFor(instance, template, "EVENT_SAY") is not null)
            instance.FaceTowards(npc.Id, playerId);
        QuestEvent(instance, npc.Id, playerId, "EVENT_SAY", vars => vars["text"] = text);
    }

    /// <summary>Items and money handed to an NPC: its EVENT_ITEM; an NPC with no script for it gives the items back.</summary>
    private void HandIn(ZoneInstance instance, ZoneInstance.HandedIn handedIn)
    {
        if (instance.Get(handedIn.NpcId) is not { Npc: { } template } npc || ScriptFor(instance, template, "EVENT_ITEM") is null)
        {
            instance.ReturnItems(handedIn.PlayerId, handedIn.Items);
            return;
        }
        instance.FaceTowards(npc.Id, handedIn.PlayerId);
        var vars = instance.QuestVariables(npc.Id, handedIn.PlayerId);
        var count = new Dictionary<int, int>();
        for (int i = 0; i < 4; i++)
        {
            int item = i < handedIn.Items.Count ? handedIn.Items[i] : 0;
            vars[$"item{i + 1}"] = item.ToString();
            if (item != 0)
                count[item] = count.GetValueOrDefault(item) + 1;
        }
        vars["copper"] = handedIn.Coins.Copper.ToString();
        vars["silver"] = handedIn.Coins.Silver.ToString();
        vars["gold"] = handedIn.Coins.Gold.ToString();
        vars["platinum"] = handedIn.Coins.Platinum.ToString();
        Trigger(instance, npc.Id, handedIn.PlayerId, template, "EVENT_ITEM", vars, count);
    }

    /// <summary>Applies the scripts that finished since the last tick.</summary>
    private void ApplyQuests()
    {
        for (int i = 0; i < _questRuns.Count; i++)
        {
            var (instance, npcId, playerId, speaker, run) = _questRuns[i];
            if (!run.IsCompleted)
                continue;
            _questRuns.RemoveAt(i--);
            if (run.IsFaulted)
            {
                Log?.Invoke($"quest failed: {run.Exception?.GetBaseException().Message}");
                continue;
            }
            instance.ApplyQuest(npcId, playerId, run.Result, Log, speaker);
        }
    }

    /// <summary>An NPC's say (to the players around it), emote, or shout (to the zone).</summary>
    private void NpcSpoke(ZoneInstance instance, ZoneInstance.NpcSpoke spoke)
    {
        switch (spoke.Channel)
        {
            case "shout":
                SendToZone(instance, new ChatMessage(ChatChannel.Shout, spoke.Name, "", spoke.Text));
                break;
            case "emote":
                SendNear(instance, spoke.At, new ChatMessage(ChatChannel.Emote, spoke.Name, "", spoke.Text));
                break;
            default:
                SendNear(instance, spoke.At, new ChatMessage(ChatChannel.Say, spoke.Name, "", spoke.Text));
                break;
        }
    }
}
