using System;
using System.Collections.Generic;

namespace Net10_CSharp13_Library.CSharp13.ParamsCollections
{
    public class ParamsSamples
    {
        // params is no longer restricted to arrays. Any collection type a
        // collection expression can build may be a params parameter, so a
        // variadic call need not allocate an array.
        public static int SumSpan(params ReadOnlySpan<int> values)
        {
            int total = 0;
            foreach (int value in values)
            {
                total += value;
            }

            return total;
        }

        public static int CountList(params List<string> values)
        {
            return values.Count;
        }

        public static int CountEnumerable(params IEnumerable<int> values)
        {
            int count = 0;
            foreach (int unused in values)
            {
                count++;
            }

            return count;
        }

        // The array form still works exactly as before.
        public static int SumArray(params int[] values)
        {
            int total = 0;
            foreach (int value in values)
            {
                total += value;
            }

            return total;
        }

        public static int CallAll()
        {
            return SumSpan(1, 2, 3) + CountList("a", "b") + CountEnumerable(1) + SumArray(4);
        }
    }
}
