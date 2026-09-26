using EQClassic.Server.Quests;
using EQClassic.Shared.Zone;
using LiteNetLib;

namespace EQClassic.Server.Zone;

/// <summary>
/// The quest scripts' events: EVENT_SAY when a player says something to the NPC they target (a
/// hail, a keyword in brackets), EVENT_ITEM when they hand it items. Scripts run outside the game
/// loop; what they did is applied at the next tick.
/// </summary>
public sealed partial class ZoneServer
{
    /// <summary>The legacy Perl quests; null: NPCs have nothing to say.</summary>
    public QuestEngine? Quests { get; set; }

    private readonly List<(ZoneInstance Instance, int NpcId, int PlayerId, Task<IReadOnlyList<QuestAction>> Run)> _questRuns = new();

    /// <summary>Script of an NPC of the zone, or null.</summary>
    private string? ScriptOf(ZoneInstance instance, ZoneInstance.Entity npc) =>
        Quests is { } quests && !npc.IsPlayer && !npc.IsCorpse && npc.Npc is { } template
            ? quests.ScriptFor(instance.ShortName, template.Id, template.Name)
            : null;

    /// <summary>Client::ChannelMessageReceived: a say reaches the targeted NPC's script (Mob::CheckQuests EVENT_SAY).</summary>
    private void Said(ZoneInstance instance, int playerId, string text)
    {
        if (instance.Get(playerId) is not { PlayerTargetId: { } targetId } player || instance.Get(targetId) is not { } npc
            || Distance2(player.Position, npc.Position) > SayRange * SayRange || npc.TargetId is not null // busy fighting
            || ScriptOf(instance, npc) is not { } script)
            return;
        var vars = instance.QuestVariables(npc.Id, playerId);
        vars["text"] = text;
        Start(instance, npc.Id, playerId, Quests!.RunAsync(script, "EVENT_SAY", vars));
    }

    /// <summary>Items and money handed to an NPC: its EVENT_ITEM; an NPC with no script gives the items back.</summary>
    private void HandIn(ZoneInstance instance, ZoneInstance.HandedIn handedIn)
    {
        if (instance.Get(handedIn.NpcId) is not { } npc || ScriptOf(instance, npc) is not { } script)
        {
            instance.ReturnItems(handedIn.PlayerId, handedIn.Items);
            return;
        }
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
        Start(instance, npc.Id, handedIn.PlayerId, Quests!.RunAsync(script, "EVENT_ITEM", vars, count));
    }

    private void Start(ZoneInstance instance, int npcId, int playerId, Task<IReadOnlyList<QuestAction>> run) =>
        _questRuns.Add((instance, npcId, playerId, run));

    /// <summary>Applies the scripts that finished since the last tick.</summary>
    private void ApplyQuests()
    {
        for (int i = 0; i < _questRuns.Count; i++)
        {
            var (instance, npcId, playerId, run) = _questRuns[i];
            if (!run.IsCompleted)
                continue;
            _questRuns.RemoveAt(i--);
            if (run.IsFaulted)
            {
                Log?.Invoke($"quest failed: {run.Exception?.GetBaseException().Message}");
                continue;
            }
            instance.ApplyQuest(npcId, playerId, run.Result, Log);
        }
    }

    /// <summary>An NPC's say (to the players around it), emote, or shout (to the zone).</summary>
    private void NpcSpoke(ZoneInstance instance, ZoneInstance.Entity npc, ZoneInstance.NpcSpoke spoke)
    {
        string name = DisplayName(npc.Name);
        switch (spoke.Channel)
        {
            case "shout":
                SendToZone(instance, new ChatMessage(ChatChannel.Shout, name, "", spoke.Text));
                break;
            case "emote":
                SendNear(instance, npc.Position, new ChatMessage(ChatChannel.Emote, name, "", spoke.Text));
                break;
            default:
                SendNear(instance, npc.Position, new ChatMessage(ChatChannel.Say, name, "", spoke.Text));
                break;
        }
    }
}
