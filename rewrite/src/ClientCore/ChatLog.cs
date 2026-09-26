using System;
using System.Collections.Generic;

namespace EQClassic.ClientCore
{
    /// <summary>What a chat line is, for its colour in the chat window.</summary>
    public enum ChatKind { Other, Say, Tell, Group, Shout, OutOfCharacter, Auction }

    /// <summary>
    /// The chat window's text: the last <see cref="Capacity"/> lines with scrolling (the view stays
    /// where it is while new lines arrive), and the lines the player typed, recalled with the arrows.
    /// </summary>
    public sealed class ChatLog
    {
        public const int Capacity = 500;
        private readonly List<string> _lines = new List<string>();
        private readonly List<string> _typed = new List<string>();
        private int _recall = -1;

        public int Count => _lines.Count;
        /// <summary>Lines scrolled back from the newest (0: at the bottom).</summary>
        public int Scroll { get; private set; }

        public void Add(string line)
        {
            _lines.Add(line);
            if (_lines.Count > Capacity)
                _lines.RemoveAt(0);
            else if (Scroll > 0)
                Scroll++;
        }

        public void ScrollBy(int lines, int visible) => Scroll = Math.Max(0, Math.Min(Scroll + lines, _lines.Count - visible));
        public void ScrollToBottom() => Scroll = 0;

        /// <summary>The <paramref name="count"/> lines on screen, oldest first.</summary>
        public IReadOnlyList<string> Visible(int count)
        {
            int end = _lines.Count - Scroll;
            int start = Math.Max(0, end - count);
            return _lines.GetRange(start, end - start);
        }

        /// <summary>A line the player sent (not twice in a row).</summary>
        public void Typed(string line)
        {
            _recall = -1;
            if (line.Length == 0 || _typed.Count > 0 && _typed[_typed.Count - 1] == line)
                return;
            _typed.Add(line);
            if (_typed.Count > 50)
                _typed.RemoveAt(0);
        }

        /// <summary>Up arrow: the previous typed line (the oldest stays).</summary>
        public string Previous()
        {
            if (_typed.Count == 0)
                return "";
            _recall = _recall < 0 ? _typed.Count - 1 : Math.Max(0, _recall - 1);
            return _typed[_recall];
        }

        /// <summary>Down arrow: the next typed line, then an empty line.</summary>
        public string Next()
        {
            if (_recall < 0 || _recall >= _typed.Count - 1)
            {
                _recall = -1;
                return "";
            }
            return _typed[++_recall];
        }

        /// <summary>The kind of a line as <see cref="Chat.Format"/> writes it.</summary>
        public static ChatKind Kind(string line)
        {
            if (line.Contains(" tells you, '") || line.StartsWith("You told ", StringComparison.Ordinal))
                return ChatKind.Tell;
            if (line.Contains(" tells the group, '") || line.StartsWith("You tell your party, '", StringComparison.Ordinal))
                return ChatKind.Group;
            if (line.Contains(" says out of character, '") || line.StartsWith("You say out of character, '", StringComparison.Ordinal))
                return ChatKind.OutOfCharacter;
            if (line.Contains(" auctions, '") || line.StartsWith("You auction, '", StringComparison.Ordinal))
                return ChatKind.Auction;
            if (line.Contains(" shouts, '") || line.StartsWith("You shout, '", StringComparison.Ordinal))
                return ChatKind.Shout;
            if (line.Contains(" says, '") || line.StartsWith("You say, '", StringComparison.Ordinal))
                return ChatKind.Say;
            return ChatKind.Other;
        }
    }
}
