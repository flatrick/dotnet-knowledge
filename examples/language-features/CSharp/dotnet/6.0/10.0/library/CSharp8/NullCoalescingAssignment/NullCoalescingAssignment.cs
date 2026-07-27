using System.Collections.Generic;

namespace Net6_CSharp10_Library.CSharp8.NullCoalescingAssignment
{
    public class Defaults
    {
        // ??= assigns only when the left operand is null, and does not evaluate
        // the right operand otherwise.
        public static string OrDefault(string value)
        {
            value ??= "fallback";
            return value;
        }

        // The usual use: lazy initialization without an if statement.
        public static List<int> EnsureList(List<int> items)
        {
            items ??= new List<int>();
            items.Add(1);
            return items;
        }

        // It lifts over nullable value types too.
        public static int OrZero(int? value)
        {
            value ??= 0;
            return value.Value;
        }
    }
}
