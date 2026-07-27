using System.Collections.Generic;
using System.Linq;

namespace Net10_CSharp14_Library.CSharp11.NewlinesInInterpolations
{
    public class Interpolations
    {
        // The expression inside a hole may span lines. Before C# 11.0 a
        // non-verbatim interpolated string required the whole hole on one line,
        // which forced long expressions into a temporary variable.
        public static string Describe(IEnumerable<int> values)
        {
            return $"count={values
                .Where(value => value > 0)
                .Count()}";
        }

        // A switch expression as a hole is the case this most obviously helps.
        public static string Classify(int value)
        {
            return $"value is {value switch
            {
                < 0 => "negative",
                0 => "zero",
                _ => "positive",
            }}";
        }
    }
}
