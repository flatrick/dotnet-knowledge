namespace Net8_CSharp10.CSharp9.PatternMatchingEnhancements9
{
    public class Patterns9
    {
        // Relational patterns compare directly, so the when guards a C# 8.0
        // sample needed are gone.
        public static string Classify(int value) => value switch
        {
            < 0 => "negative",
            0 => "zero",
            > 0 => "positive",
        };

        // and / or / not combine patterns. Ranges read as one expression.
        public static string Band(int value) => value switch
        {
            >= 0 and < 10 => "single digit",
            >= 10 and < 100 => "double digit",
            _ => "large",
        };

        // not is most useful against null.
        public static bool IsPresent(object value)
        {
            return value is not null;
        }

        // A type pattern no longer needs a designator when the binding is
        // unused, so the discard can be dropped entirely.
        public static string TypeName(object value) => value switch
        {
            int => "int",
            string => "string",
            null => "null",
            _ => "other",
        };

        // Parenthesized patterns make precedence explicit.
        public static bool InEitherBand(int value)
        {
            return value is (>= 0 and < 10) or (>= 100 and < 110);
        }
    }
}
