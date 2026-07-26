namespace CSharpNet7_10.CSharp10.ImprovedDefiniteAssignment
{
    public class Source
    {
        public bool TryGet(out object value)
        {
            value = new object();
            return true;
        }
    }

    public class DefiniteAssignment
    {
        // Definite-assignment analysis had three well-known gaps, all fixed in
        // C# 10.0. Each case below was a false "use of unassigned local"
        // (CS0165) before, even though the out parameter is assigned on every
        // path that reaches the use.

        // 1. Comparison to a boolean constant. The analysis did not connect
        //    "== true" to the branch it implies.
        public static string ComparedToConstant(Source source)
        {
            if (source.TryGet(out object value) == true)
            {
                return value.ToString();
            }

            return "none";
        }

        // 2. Conditional access. A null receiver makes the whole expression
        //    null, so the true branch implies the call happened.
        public static string ConditionalAccess(Source source)
        {
            if (source?.TryGet(out object value) == true)
            {
                return value.ToString();
            }

            return "none";
        }

        // 3. Null coalescing. Reaching the true branch means the left operand
        //    was non-null and returned true, so the call happened.
        public static string NullCoalescing(Source source)
        {
            if (source?.TryGet(out object value) ?? false)
            {
                return value.ToString();
            }

            return "none";
        }
    }
}
