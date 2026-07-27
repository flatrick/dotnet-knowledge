namespace Net9_CSharp10_Library.CSharp7.PatternMatching70
{
    public class PatternMatchingSamples
    {
        // The is-type pattern tests and casts in one step, binding the result
        // to a new variable that is definitely assigned only when it matches.
        public static string Describe(object value)
        {
            if (value is int number)
            {
                return "int:" + number;
            }

            if (value is string text && text.Length > 0)
            {
                return "string:" + text;
            }

            // The constant pattern; null is the one that earns its keep,
            // because it never invokes a user-defined == operator.
            if (value is null)
            {
                return "null";
            }

            return "other";
        }

        // switch gained the same patterns. Unlike a constant switch, the cases
        // are tested in source order, which is why the guarded case must come
        // before the unguarded one that would otherwise swallow it.
        public static string Classify(object value)
        {
            switch (value)
            {
                case int number when number < 0:
                    return "negative";
                case int number:
                    return "int:" + number;
                case string text:
                    return "string:" + text.Length;
                case null:
                    return "null";
                default:
                    return "other";
            }
        }
    }
}
