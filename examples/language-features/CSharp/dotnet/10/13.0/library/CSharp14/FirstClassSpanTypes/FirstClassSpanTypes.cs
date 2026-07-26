using System;

namespace CSharpNet10Latest
{
    public class SpanConversions
    {
        // C# 14.0 gives Span<T> and ReadOnlySpan<T> first-class conversions, so
        // an array converts to a span in more positions than before — including
        // generic inference and extension-method receivers, which previously
        // needed an explicit AsSpan call.
        public static int SumSpan(ReadOnlySpan<int> values)
        {
            int total = 0;
            foreach (int value in values)
            {
                total += value;
            }

            return total;
        }

        // An array argument converts implicitly.
        public static int FromArray()
        {
            int[] values = new int[] { 1, 2, 3 };
            return SumSpan(values);
        }

        // Span<T> converts to ReadOnlySpan<T>, and the variance now also
        // applies where a type argument is inferred.
        public static int FromWritableSpan()
        {
            Span<int> values = stackalloc int[] { 1, 2, 3 };
            return SumSpan(values);
        }

        // A ReadOnlySpan<Derived> converts to ReadOnlySpan<Base>, matching the
        // covariance arrays have always had.
        public static int CountBases()
        {
            string[] items = new string[] { "a", "b" };
            ReadOnlySpan<object> bases = items;
            return bases.Length;
        }
    }
}
