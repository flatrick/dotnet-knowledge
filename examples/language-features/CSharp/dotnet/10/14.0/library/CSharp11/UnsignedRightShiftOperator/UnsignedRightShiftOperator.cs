namespace CSharpNet10Latest.CSharp11.UnsignedRightShiftOperator
{
    public class UnsignedShift
    {
        // >>> shifts in zeros regardless of sign. Before C# 11.0 the same
        // result needed a cast to an unsigned type, shift, and cast back.
        public static int ShiftUnsigned(int value, int count)
        {
            return value >>> count;
        }

        // >> keeps the sign bit, so a negative value stays negative.
        public static int ShiftSigned(int value, int count)
        {
            return value >> count;
        }

        // The difference only shows on a negative operand.
        public static bool DifferOnNegative()
        {
            return (-8 >>> 1) != (-8 >> 1);
        }

        // The pre-C#11 idiom, kept as contrast.
        public static int ViaCast(int value, int count)
        {
            return (int)((uint)value >> count);
        }

        public static bool CastMatchesOperator()
        {
            return ViaCast(-8, 1) == (-8 >>> 1);
        }
    }
}
