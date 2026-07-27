namespace Net10_CSharp13_Library.CSharp8.IsNullOnUnconstrainedTypeParameter
{
    public class NullTests
    {
        // Before C# 8.0 the compiler rejected a null pattern applied to an
        // unconstrained type parameter, because T might be a non-nullable value
        // type and the pattern could then never match. C# 8.0 allows it and
        // defines the result as simply false in that case.
        public static bool IsNull<T>(T value)
        {
            return value is null;
        }

        public static string Describe<T>(T value)
        {
            return value is null ? "null" : "not null";
        }

        // T is unconstrained here, so each of the three shapes it may take is
        // worth showing: a reference type, a non-nullable value type, and a
        // nullable value type.
        public static bool ReferenceTypeCase()
        {
            return IsNull<string>(null);
        }

        // False, because an int can never be null — this is the case the
        // pre-C#8 restriction existed to reject outright.
        public static bool ValueTypeCase()
        {
            return IsNull(0);
        }

        public static bool NullableValueTypeCase()
        {
            int? value = null;
            return IsNull(value);
        }
    }
}
