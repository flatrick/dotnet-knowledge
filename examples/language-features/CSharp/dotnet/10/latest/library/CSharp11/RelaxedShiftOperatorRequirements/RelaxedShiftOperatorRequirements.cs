namespace Net10_CSharpLatest_Library.CSharp11.RelaxedShiftOperatorRequirements
{
    // Before C# 11.0 a user-defined shift operator's second operand had to be
    // int. That blocked generic math, where the shift amount may be any type
    // the interface constraint allows. The requirement is now dropped.
    public readonly struct BitSet
    {
        public BitSet(int bits)
        {
            Bits = bits;
        }

        public int Bits { get; }

        // Second operand is not int — legal only from C# 11.0.
        public static BitSet operator <<(BitSet value, byte count)
        {
            return new BitSet(value.Bits << count);
        }

        public static BitSet operator >>(BitSet value, byte count)
        {
            return new BitSet(value.Bits >> count);
        }

        // The unsigned form takes the relaxed operand too.
        public static BitSet operator >>>(BitSet value, byte count)
        {
            return new BitSet(value.Bits >>> count);
        }
    }

    public class Usage
    {
        public static int ShiftLeft()
        {
            BitSet set = new BitSet(1) << (byte)3;
            return set.Bits;
        }
    }
}
