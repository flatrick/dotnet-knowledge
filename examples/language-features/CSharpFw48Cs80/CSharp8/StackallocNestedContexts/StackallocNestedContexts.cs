using System;

namespace CSharpFw48Cs80.CSharp8.StackallocNestedContexts
{
    public class NestedStackalloc
    {
        // C# 8.0 allows a stackalloc expression wherever a Span is expected,
        // including nested inside another expression. Earlier versions accepted
        // it only as the whole right-hand side of a local declaration.
        public static int FromArgumentPosition()
        {
            return SumOf(stackalloc int[] { 1, 2, 3 });
        }

        // Nested inside a conditional.
        public static int FromConditional(bool useFirst)
        {
            Span<int> values = useFirst
                ? stackalloc int[] { 1, 2 }
                : stackalloc int[] { 3, 4 };
            return SumOf(values);
        }

        private static int SumOf(Span<int> values)
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
