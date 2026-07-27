using System.Collections.Generic;
using System.Linq;

namespace Net6_CSharp10_Library.CSharp7_3.ExpressionVariablesInInitializers
{
    public class Parsed
    {
        // C# 7.0 allowed out-variables and pattern variables only in method
        // bodies. C# 7.3 allows them in a field initializer as well.
        private static readonly bool _seedIsNumeric = int.TryParse("42", out int seed) && seed > 0;

        private readonly int _length;

        public static bool SeedIsNumeric
        {
            get { return _seedIsNumeric; }
        }

        public int Length
        {
            get { return _length; }
        }

        // ...and in a constructor initializer, where a pattern variable can be
        // computed and passed on without a helper method.
        public Parsed(object value)
            : this(value is string text ? text.Length : 0)
        {
        }

        private Parsed(int length)
        {
            _length = length;
        }

        // ...and inside a query clause.
        public static List<string> NumericItems(IEnumerable<string> items)
        {
            return (from item in items
                    where int.TryParse(item, out int parsed) && parsed > 0
                    select item).ToList();
        }
    }
}
