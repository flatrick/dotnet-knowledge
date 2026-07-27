using System;

namespace Net10_CSharp10_Library.CSharp7.RefReturnsAndLocals
{
    public class RefSamples
    {
        // A ref return hands back the storage location itself rather than a
        // copy of the value in it. No unsafe context is involved: the compiler
        // proves the reference cannot outlive what it points into.
        public static ref int Find(int[] values, int target)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == target)
                {
                    return ref values[i];
                }
            }

            throw new ArgumentException("not found", nameof(target));
        }

        // A ref local aliases that location, so assigning through it writes
        // into the original array.
        public static int[] ReplaceInPlace(int[] values, int target, int replacement)
        {
            ref int slot = ref Find(values, target);
            slot = replacement;
            return values;
        }

        // Reading through a ref local is an ordinary read; the alias only
        // matters when something writes.
        public static int ReadThroughRefLocal(int[] values)
        {
            ref int first = ref values[0];
            return first;
        }

        // Without ref, this assignment would modify a copy and the array would
        // be left untouched — the contrast is the whole point of the feature.
        public static int[] ByValueLeavesArrayUnchanged(int[] values)
        {
            int copy = values[0];
            copy = -1;
            return values;
        }
    }
}
