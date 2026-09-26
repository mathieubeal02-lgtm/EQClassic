namespace EQClassic.Server.Zone;

/// <summary>Who is grouped with whom, by character name (groups span zones).</summary>
public interface IGroups
{
    /// <summary>The members of the character's group, the character included; empty when not grouped.</summary>
    IReadOnlyList<string> MembersOf(string name);
}

/// <summary>
/// Groups after Zone/Source/groups.cpp and the invite / follow / disband packets: up to six
/// members, a leader who invites, the leader's leaving handing the group to the next member, a
/// group of one disbanded. Kept in memory by the zone server (the legacy zone also stored them in
/// the database for players who went link-dead).
/// </summary>
public sealed class GroupRegistry : IGroups
{
    public const int MaxMembers = 6;

    private sealed class Group
    {
        public string Leader = "";
        public readonly List<string> Members = new();
    }

    private readonly Dictionary<string, Group> _groups = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _invites = new(StringComparer.OrdinalIgnoreCase); // invitee → inviter

    public IReadOnlyList<string> MembersOf(string name) => _groups.TryGetValue(name, out var g) ? g.Members.ToList() : Array.Empty<string>();

    public string? LeaderOf(string name) => _groups.TryGetValue(name, out var g) ? g.Leader : null;

    /// <summary>Invites <paramref name="invitee"/>; returns the refusal to show the inviter, or null.</summary>
    public string? Invite(string inviter, string invitee)
    {
        if (string.Equals(inviter, invitee, StringComparison.OrdinalIgnoreCase))
            return "You cannot invite yourself.";
        if (_groups.ContainsKey(invitee))
            return $"{invitee} is already in a group.";
        if (_groups.TryGetValue(inviter, out var group))
        {
            if (!string.Equals(group.Leader, inviter, StringComparison.OrdinalIgnoreCase))
                return "Only the group leader may invite.";
            if (group.Members.Count >= MaxMembers)
                return "Your group is full.";
        }
        _invites[invitee] = inviter;
        return null;
    }

    /// <summary>The invitee follows: returns the group's members, or null when the invitation is gone or the group full.</summary>
    public IReadOnlyList<string>? Accept(string invitee)
    {
        if (!_invites.Remove(invitee, out var inviter) || _groups.ContainsKey(invitee))
            return null;
        if (!_groups.TryGetValue(inviter, out var group))
        {
            group = new Group { Leader = inviter };
            group.Members.Add(inviter);
            _groups[inviter] = group;
        }
        if (group.Members.Count >= MaxMembers)
            return null;
        group.Members.Add(invitee);
        _groups[invitee] = group;
        return group.Members.ToList();
    }

    /// <summary>Returns who invited the character (the invitation is dropped), or null.</summary>
    public string? Decline(string invitee) => _invites.Remove(invitee, out var inviter) ? inviter : null;

    /// <summary>Leaves the group; returns the members left behind (empty when the group is disbanded).</summary>
    public IReadOnlyList<string> Leave(string name)
    {
        _invites.Remove(name);
        if (!_groups.Remove(name, out var group))
            return Array.Empty<string>();
        group.Members.RemoveAll(m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));
        if (group.Members.Count <= 1)
        {
            foreach (var m in group.Members)
                _groups.Remove(m);
            var last = group.Members.ToList();
            group.Members.Clear();
            return last; // told that the group is gone
        }
        if (string.Equals(group.Leader, name, StringComparison.OrdinalIgnoreCase))
            group.Leader = group.Members[0]; // Group::ChangeLeader on leaving
        return group.Members.ToList();
    }
}
