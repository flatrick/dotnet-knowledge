namespace Net10_CSharp14_Library.CSharp7_3.RefLocalReassignment
{
    public class RefReassignment
    {
        // C# 7.0 fixed a ref local to one target for its whole lifetime.
        // C# 7.3 allows re-pointing it with = ref, so a single alias can walk
        // a structure instead of needing one local per target.
        public static void SetFirstAndLast(int[] values, int value)
        {
            ref int slot = ref values[0];
            slot = value;

            slot = ref values[values.Length - 1];
            slot = value;
        }

        // Walking every element through one alias.
        public static void Fill(int[] values, int value)
        {
            ref int slot = ref values[0];
            for (int i = 0; i < values.Length; i++)
            {
                slot = ref values[i];
                slot = value;
            }
        }

        // Note the two different operators: `= ref` re-points the alias, while
        // a plain `=` writes through it to the target.
        public static int PointThenWrite(int[] left, int[] right)
        {
            ref int slot = ref left[0];
            slot = ref right[0];
            slot = 9;
            return right[0];
        }
    }
}
