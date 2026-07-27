namespace Net10_CSharp13_Library.CSharp13.EscEscapeSequence
{
    public class EscapeSequences
    {
        // \e is the ESC character, U+001B. Before C# 13.0 it had to be written
        // as  or \x1b, and \x is variable-length: "\x1b5" is a single
        // character U+01B5 rather than ESC followed by '5'. \e is fixed-length
        // and removes that hazard.
        public const string Escape = "\e";

        public static bool IsEscapeCharacter()
        {
            return Escape[0] == '';
        }

        // The usual use: an ANSI terminal control sequence.
        public static string Red(string text)
        {
            return "\e[31m" + text + "\e[0m";
        }

        // The older spellings remain legal and produce the same character.
        public static bool SpellingsAgree()
        {
            return "\e" == "";
        }

        // The hazard the new sequence avoids, shown explicitly: \x consumes as
        // many hex digits as it can, so these two are different strings.
        public static bool VariableLengthHexIsDifferent()
        {
            return "\x1b5".Length == 1 && "\e5".Length == 2;
        }
    }
}
