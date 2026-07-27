namespace Net6_CSharp10.CSharp7.NumericLiteralImprovements
{
    public class NumericLiterals
    {
        // The digit separator is ignored by the compiler; it exists purely so
        // a long literal can be grouped the way a reader would group it.
        public const int Million = 1_000_000;

        // Binary literals spell out a bit pattern directly, which a decimal or
        // hex constant only implies.
        public const int LowNibbleMask = 0b0000_1111;

        public const int AlternatingBits = 0b1010_1010;

        // Separators work in hex and in floating-point literals too.
        public const long FullWordMask = 0xFF_FF_FF_FF;

        public const double Pi = 3.141_592_653;

        // Grouping carries no meaning of its own — these two are the same value.
        public static bool SeparatorsAreCosmetic()
        {
            return 1_0_0 == 100;
        }

        public static int MaskLowNibble(int value)
        {
            return value & LowNibbleMask;
        }
    }
}
