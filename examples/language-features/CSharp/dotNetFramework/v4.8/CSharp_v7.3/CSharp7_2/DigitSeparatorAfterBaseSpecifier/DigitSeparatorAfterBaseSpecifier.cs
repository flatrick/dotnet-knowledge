namespace Net48_CSharp7_3_Library.CSharp7_2.DigitSeparatorAfterBaseSpecifier
{
    public class BaseSpecifierSeparators
    {
        // C# 7.0 allowed separators only BETWEEN digits. C# 7.2 allows one
        // immediately after the 0b or 0x prefix, so the prefix can be set off
        // from the digits it introduces.
        public const int LeadingSeparatorBinary = 0b_1010_1010;

        public const int LeadingSeparatorHex = 0x_FF_FF;

        // The C# 7.0 form remains legal; the separator is cosmetic either way.
        public const int BetweenDigitsOnly = 0b1010_1010;

        public static bool FormsAreEqual()
        {
            return LeadingSeparatorBinary == BetweenDigitsOnly;
        }

        public static int Mask(int value)
        {
            return value & LeadingSeparatorHex;
        }
    }
}
