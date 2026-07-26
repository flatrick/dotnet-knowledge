namespace CSharpNet6_10.CSharp7_1.GenericPatternMatching
{
    public class GenericPatterns
    {
        // C# 7.0 rejected a pattern whose type was a type parameter. C# 7.1
        // allows it, so a generic method can test against its own T.
        public static bool IsOfType<T>(object value)
        {
            return value is T;
        }

        public static string Describe<T>(object value)
        {
            if (value is T typed)
            {
                return "matched:" + typed;
            }

            return "no match";
        }

        public static T AsOrDefault<T>(object value)
        {
            return value is T typed ? typed : default;
        }
    }
}
