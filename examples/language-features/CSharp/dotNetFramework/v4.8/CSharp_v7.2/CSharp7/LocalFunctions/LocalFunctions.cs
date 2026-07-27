using System;
using System.Collections.Generic;

namespace Net48_CSharp7_2_Library.CSharp7.LocalFunctions
{
    public class LocalFunctionSamples
    {
        // A local function may be recursive, which a lambda assigned to a local
        // cannot be without a forward declaration.
        public static int Factorial(int n)
        {
            int Compute(int value)
            {
                return value <= 1 ? 1 : value * Compute(value - 1);
            }

            return Compute(n);
        }

        // It captures enclosing locals like a lambda, but needs no delegate
        // instance, so a non-escaping local function allocates nothing.
        public static int SumWithCapture(int[] values)
        {
            int total = 0;

            void Add(int value)
            {
                total += value;
            }

            foreach (int value in values)
            {
                Add(value);
            }

            return total;
        }

        // The canonical use: an iterator's body is deferred until first
        // enumeration, so validation placed in it would not run at call time.
        // Splitting the iterator into a local function makes the check eager.
        public static IEnumerable<int> Range(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return Iterate();

            IEnumerable<int> Iterate()
            {
                for (int i = 0; i < count; i++)
                {
                    yield return i;
                }
            }
        }
    }
}
