using System;

namespace CSharpNet7_10.CSharp7_3.StackallocInitializers
{
    public class StackallocInitializerSamples
    {
        // C# 7.3 gave stackalloc the same initializer forms an array has, so
        // the values no longer need a following loop to write them in.
        public static int SumExplicit()
        {
            Span<int> values = stackalloc int[] { 1, 2, 3 };
            return Total(values);
        }

        // The element type may be inferred from the initializer.
        public static int SumInferred()
        {
            Span<int> values = stackalloc[] { 4, 5, 6 };
            return Total(values);
        }

        // A size may be stated as well; it must match the initializer's count.
        public static int SumSized()
        {
            Span<int> values = stackalloc int[3] { 7, 8, 9 };
            return Total(values);
        }

        private static int Total(Span<int> values)
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
