namespace Net6_CSharp10.CSharp2.NullableValueTypes
{
    public class NullableSamples
    {
        // int? is shorthand for System.Nullable<int>.
        public static int? Parse(string text)
        {
            int value;
            if (int.TryParse(text, out value))
            {
                return value;
            }

            return null;
        }

        public static bool HasValue(int? value)
        {
            return value.HasValue;
        }

        // The null-coalescing operator shipped alongside nullable value types.
        public static int OrDefault(int? value, int fallback)
        {
            return value ?? fallback;
        }

        public static int Unwrap(int? value)
        {
            return value.Value;
        }

        // Lifted operators: an operator over int also applies to int?,
        // and yields null when either operand is null.
        public static int? Add(int? left, int? right)
        {
            return left + right;
        }
    }
}
