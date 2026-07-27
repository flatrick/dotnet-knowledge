using System.Collections.Generic;

namespace Net10_CSharp10_Library.CSharp3.ExtensionMethods
{
    // An extension method lives in a non-generic static class and marks its
    // first parameter with this.
    public static class StringExtensions
    {
        public static bool IsNullOrBlank(this string value)
        {
            return value == null || value.Trim().Length == 0;
        }

        public static string Repeat(this string value, int times)
        {
            string result = string.Empty;
            for (int i = 0; i < times; i++)
            {
                result += value;
            }

            return result;
        }
    }

    public static class EnumerableExtensions
    {
        // Extending an interface makes the method available on every type that
        // implements it.
        public static int CountEven(this IEnumerable<int> values)
        {
            int count = 0;
            foreach (int value in values)
            {
                if (value % 2 == 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public class Usage
    {
        public static string CallAsInstanceMethod()
        {
            return "ab".Repeat(3);
        }

        // The same call written as the plain static invocation it compiles to.
        public static string CallAsStaticMethod()
        {
            return StringExtensions.Repeat("ab", 3);
        }

        public static bool BlankCheck()
        {
            return "  ".IsNullOrBlank();
        }

        public static int Evens(List<int> values)
        {
            return values.CountEven();
        }
    }
}
