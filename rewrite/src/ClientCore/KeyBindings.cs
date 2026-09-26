using System;

namespace EQClassic.ClientCore
{
    public enum KeyboardLayout { Azerty, Qwerty }

    /// <summary>
    /// The movement keys, by keyboard layout (the letters as printed on the keys): AZERTY moves with
    /// Z / S, sidesteps with Q / D and turns with A / E; QWERTY moves with W / S, sidesteps with A / D
    /// and turns with Q / E. The arrows work on both (up and down move, left and right turn).
    /// </summary>
    public sealed class KeyBindings
    {
        private KeyBindings(KeyboardLayout layout, char forward, char back, char strafeLeft, char strafeRight, char turnLeft, char turnRight)
        {
            Layout = layout;
            Forward = forward; Back = back; StrafeLeft = strafeLeft; StrafeRight = strafeRight; TurnLeft = turnLeft; TurnRight = turnRight;
        }

        public KeyboardLayout Layout { get; }
        public char Forward { get; }
        public char Back { get; }
        public char StrafeLeft { get; }
        public char StrafeRight { get; }
        public char TurnLeft { get; }
        public char TurnRight { get; }

        public static readonly KeyBindings Azerty = new KeyBindings(KeyboardLayout.Azerty, 'z', 's', 'q', 'd', 'a', 'e');
        public static readonly KeyBindings Qwerty = new KeyBindings(KeyboardLayout.Qwerty, 'w', 's', 'a', 'd', 'q', 'e');

        public static KeyBindings For(KeyboardLayout layout) => layout == KeyboardLayout.Qwerty ? Qwerty : Azerty;

        /// <summary>"azerty" or "qwerty" (any case); null for anything else.</summary>
        public static KeyBindings? Parse(string? name) =>
            string.Equals(name?.Trim(), "qwerty", StringComparison.OrdinalIgnoreCase) ? Qwerty
            : string.Equals(name?.Trim(), "azerty", StringComparison.OrdinalIgnoreCase) ? Azerty
            : null;

        public string Help =>
            $"{char.ToUpperInvariant(Forward)}/{char.ToUpperInvariant(Back)} or arrows move, {char.ToUpperInvariant(StrafeLeft)}/{char.ToUpperInvariant(StrafeRight)} sidestep, "
            + $"{char.ToUpperInvariant(TurnLeft)}/{char.ToUpperInvariant(TurnRight)} or left/right arrows turn";
    }
}
