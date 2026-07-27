namespace Net48_CSharp8_Library.CSharp6.StringInterpolation
{
    public class Interpolation
    {
        // An interpolated string is a compile-time syntax for building a formatted
        // string. Each {hole} holds a real expression, checked by the compiler —
        // unlike a "{0}" placeholder whose mismatch with arguments surfaces at run time.
        public static string Simple(string name, int count)
        {
            return $"{name} has {count} items";
        }

        // A hole may carry a format specifier after a colon.
        public static string WithFormat(double value)
        {
            return $"{value:F2}";
        }

        // ...and an alignment before it. Negative pads on the right.
        public static string WithAlignment(string label, int value)
        {
            return $"{label,-10}|{value,5}";
        }

        // A hole holds any expression, not just a variable.
        public static string WithExpression(int left, int right)
        {
            return $"{left} + {right} = {left + right}";
        }

        // Doubling a brace escapes it, exactly as in a format string.
        public static string EscapedBraces(int value)
        {
            return $"{{{value}}}";
        }

        // A conditional inside a hole needs parentheses, because the colon
        // would otherwise start a format specifier.
        public static string Pluralized(int count)
        {
            return $"{count} item{(count == 1 ? string.Empty : "s")}";
        }
    }
}
