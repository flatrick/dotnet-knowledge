namespace Net48_CSharp7_2_Library.CSharp7_2.RefConditionalExpressions
{
    public class RefConditional
    {
        // The conditional itself yields a reference, so the chosen element can
        // be assigned through. Before C# 7.2 the ref had to be re-derived in
        // each branch of an if statement.
        public static void WriteToChosen(int[] left, int[] right, bool useLeft, int value)
        {
            ref int slot = ref (useLeft ? ref left[0] : ref right[0]);
            slot = value;
        }

        // Read-only use: the reference is dereferenced immediately.
        public static int ReadChosen(int[] left, int[] right, bool useLeft)
        {
            return useLeft ? ref left[0] : ref right[0];
        }

        // Both branches must be references to the same type. Safe-to-return is
        // derived conservatively from both branches' ref-safe-to-escape values:
        // if either operand would be unsafe to return, so is the whole
        // conditional. Here both operands are elements of the incoming array,
        // so both are safe to return and so is the result.
        public static ref int Choose(int[] values, bool first)
        {
            return ref (first ? ref values[0] : ref values[values.Length - 1]);
        }
    }
}
