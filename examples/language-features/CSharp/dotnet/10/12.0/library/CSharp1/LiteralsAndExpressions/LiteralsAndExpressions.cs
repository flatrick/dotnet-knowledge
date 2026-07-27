namespace Net10_CSharp12_Library.CSharp1.LiteralsAndExpressions
{
    public class Literals
    {
        // Verbatim string literals: no escape processing, quotes are doubled.
        public const string WindowsPath = @"C:\temp\report.txt";
        public const string Quoted = @"He said ""hello"".";

        // Regular string literal with escape sequences.
        public const string Escaped = "line one\nline two\ttabbed";

        // Character, hexadecimal, unsigned, and suffixed numeric literals.
        public const char Letter = 'A';
        public const char Accented = '\u00e9';
        public const int Hex = 0x2A;
        public const uint UnsignedInt = 3000000000u;
        public const ulong UnsignedLong = 18000000000000000000UL;
        public const long Long = 9000000000L;
        public const float Float = 1.5f;
        public const double Double = 1.5d;
        public const decimal Decimal = 1.5m;

        // A verbatim identifier lets a reserved keyword be used as a name.
        public static readonly int @class = 1;

        public static int Precedence()
        {
            int result = (2 + 3) * 4;
            result += Hex;
            result = result > 20 ? result : -result;
            return result;
        }

        // && and || evaluate their right operand only when needed.
        public static bool ShortCircuit(int value)
        {
            return value != 0 && 100 / value > 2;
        }

        // unchecked suppresses overflow checking; the wrap-around is intentional.
        public static int Wrapped()
        {
            unchecked
            {
                return int.MaxValue + 1;
            }
        }

        // checked forces an OverflowException instead of silent wrap-around.
        public static int Guarded(int value)
        {
            checked
            {
                return value * 2;
            }
        }
    }
}
