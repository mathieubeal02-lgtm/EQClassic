using System;
using EQClassic.Shared.Zone;

namespace EQClassic.ClientCore
{
    /// <summary>What a line typed in the chat box asks for.</summary>
    public enum ChatAction { None, Send, Who, Location, Sit, Stand, Camp, Consider, Target, Cast, Ability, Invite, Follow, Decline, Disband, Help, Trade, Pet, Hail, Unknown }

    public readonly struct ParsedChat
    {
        public ParsedChat(ChatAction action, ChatChannel channel = ChatChannel.Say, string target = "", string text = "")
        {
            Action = action;
            Channel = channel;
            Target = target;
            Text = text;
        }

        public ChatAction Action { get; }
        public ChatChannel Channel { get; }
        /// <summary>Tell recipient or /target name.</summary>
        public string Target { get; }
        public string Text { get; }
    }

    /// <summary>
    /// The Trilogy client's chat box: plain text is said; slash commands pick a channel or an
    /// action. Messages are worded as that client wrote them.
    /// </summary>
    public static class Chat
    {
        public static ParsedChat Parse(string line)
        {
            line = line.Trim();
            if (line.Length == 0)
                return new ParsedChat(ChatAction.None);
            if (line[0] != '/')
                return new ParsedChat(ChatAction.Send, ChatChannel.Say, text: line);
            int space = line.IndexOf(' ');
            string command = (space < 0 ? line.Substring(1) : line.Substring(1, space - 1)).ToLowerInvariant();
            string rest = space < 0 ? "" : line.Substring(space + 1).Trim();
            switch (command)
            {
                case "say": case "s": return Said(ChatChannel.Say, rest);
                case "shout": case "sho": return Said(ChatChannel.Shout, rest);
                case "ooc": return Said(ChatChannel.Ooc, rest);
                case "auction": case "auc": return Said(ChatChannel.Auction, rest);
                case "emote": case "em": case "me": return Said(ChatChannel.Emote, rest);
                case "tell": case "t": case "msg":
                {
                    int split = rest.IndexOf(' ');
                    return split <= 0 || split == rest.Length - 1
                        ? new ParsedChat(ChatAction.Unknown, text: "Usage: /tell <name> <message>")
                        : new ParsedChat(ChatAction.Send, ChatChannel.Tell, rest.Substring(0, split), rest.Substring(split + 1).Trim());
                }
                case "who": return new ParsedChat(ChatAction.Who);
                case "loc": return new ParsedChat(ChatAction.Location);
                case "sit": return new ParsedChat(ChatAction.Sit);
                case "stand": return new ParsedChat(ChatAction.Stand);
                case "camp": return new ParsedChat(ChatAction.Camp);
                case "con": case "consider": return new ParsedChat(ChatAction.Consider);
                case "target": case "tar": return new ParsedChat(ChatAction.Target, target: rest);
                case "cast": return new ParsedChat(ChatAction.Cast, target: rest);
                case "help": case "h": return new ParsedChat(ChatAction.Help);
                case "trade": case "give": return new ParsedChat(ChatAction.Trade);
                case "hail": return new ParsedChat(ChatAction.Hail);
                case "pet": return new ParsedChat(ChatAction.Pet, target: rest.ToLowerInvariant());
                case "gsay": case "g": return Said(ChatChannel.Group, rest);
                case "invite": case "inv": return new ParsedChat(ChatAction.Invite, target: rest);
                case "follow": return new ParsedChat(ChatAction.Follow);
                case "decline": return new ParsedChat(ChatAction.Decline);
                case "disband": return new ParsedChat(ChatAction.Disband);
                case "kick": case "bash": case "taunt": case "mend": case "hide": case "sneak": case "forage":
                    return new ParsedChat(ChatAction.Ability, target: command);
                default: return new ParsedChat(ChatAction.Unknown, text: "That is not a valid command. Please use /help.");
            }
        }

        private static ParsedChat Said(ChatChannel channel, string text) =>
            text.Length == 0 ? new ParsedChat(ChatAction.None) : new ParsedChat(ChatAction.Send, channel, text: text);

        /// <summary>A received message as the Trilogy client shows it; <paramref name="you"/> is our character's name.</summary>
        public static string Format(ChatMessage m, string you)
        {
            bool mine = string.Equals(m.From, you, StringComparison.OrdinalIgnoreCase);
            switch (m.Channel)
            {
                case ChatChannel.Shout: return mine ? $"You shout, '{m.Text}'" : $"{m.From} shouts, '{m.Text}'";
                case ChatChannel.Ooc: return mine ? $"You say out of character, '{m.Text}'" : $"{m.From} says out of character, '{m.Text}'";
                case ChatChannel.Auction: return mine ? $"You auction, '{m.Text}'" : $"{m.From} auctions, '{m.Text}'";
                case ChatChannel.Emote: return $"{m.From} {m.Text}";
                case ChatChannel.Tell: return mine ? $"You told {m.To}, '{m.Text}'" : $"{m.From} tells you, '{m.Text}'";
                case ChatChannel.Group: return mine ? $"You tell your party, '{m.Text}'" : $"{m.From} tells the group, '{m.Text}'";
                default: return mine ? $"You say, '{m.Text}'" : $"{m.From} says, '{m.Text}'";
            }
        }

        /// <summary>/help: the commands this client knows.</summary>
        public static readonly string[] HelpLines =
        {
            "Chat: /say /shout /ooc /auction /tell <name> /em /gsay (/g); Enter to type.",
            "Info: /who /loc /con /target <name> /help",
            "Actions: /sit /stand /camp /cast <1-8> /kick /bash /taunt /mend /hide /sneak /forage",
            "Groups: /invite [name] /follow /decline /disband; /trade with the targeted player (or NPC: quest hand-ins); /hail (H) the target; /pet attack, /pet back off, /pet get lost",
            "Keys: I inventory, B spell book, K skills, 1-8 spells, Tab target, F attack, C consider, H hail, L loot, U use (doors, merchants), X sit, Space jump or swim up, Ctrl swim down, F9 view",
        };

        /// <summary>/loc: the Trilogy client prints Y, X, Z.</summary>
        public static string Location(EQClassic.Shared.World.Vec3 p) =>
            FormattableString.Invariant($"Your Location is {p.Y:0.00}, {p.X:0.00}, {p.Z:0.00}");
    }
}
