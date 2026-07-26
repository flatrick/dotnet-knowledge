namespace CSharpNet10Latest.CSharp7_1.DefaultLiteralExpressions
{
    public class DefaultLiterals
    {
        // The bare default literal takes its type from the context, so the type
        // never has to be written twice the way default(T) requires.
        public static int DefaultInt()
        {
            int value = default;
            return value;
        }

        public static string DefaultString()
        {
            string value = default;
            return value;
        }

        // In a comparison the context is the other operand.
        public static bool IsDefault(int value)
        {
            return value == default;
        }

        // In a generic method the context is the return type.
        public static T DefaultOf<T>()
        {
            return default;
        }

        // As an optional parameter's default value.
        public static int WithOptional(int value = default)
        {
            return value;
        }
    }
}
