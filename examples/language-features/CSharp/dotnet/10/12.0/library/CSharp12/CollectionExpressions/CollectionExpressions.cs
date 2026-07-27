using System;
using System.Collections.Generic;

namespace Net10_CSharp12_Library.CSharp12.CollectionExpressions
{
    public class CollectionExpressionSamples
    {
        // One bracket syntax builds any supported collection type; the target
        // type decides what is constructed. Before C# 12.0 each of these
        // needed its own initializer form.
        public static int[] Array()
        {
            int[] values = [1, 2, 3];
            return values;
        }

        public static List<int> List()
        {
            List<int> values = [1, 2, 3];
            return values;
        }

        // A collection expression targeting Span may be stack-allocated, so
        // the span cannot escape the method — returning it is CS8352. Consume
        // it in place instead.
        public static int Span()
        {
            Span<int> values = [1, 2, 3];
            return values[2];
        }

        // The spread element .. inlines another sequence's items.
        public static int[] Spread(int[] first, int[] second)
        {
            return [.. first, 0, .. second];
        }

        public static int[] Empty()
        {
            return [];
        }

        // As an argument, the parameter type is the target.
        public static int SumLiteral()
        {
            return Total([1, 2, 3, 4]);
        }

        private static int Total(ReadOnlySpan<int> values)
        {
            int total = 0;
            foreach (int value in values)
            {
                total += value;
            }

            return total;
        }
    }
}
