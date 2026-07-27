namespace Net48_CSharp7_Library.CSharp4.NamedAndOptionalArguments
{
    public class Formatter
    {
        // An optional parameter's default must be a compile-time constant, and
        // it is baked into each call site rather than into the method — change
        // it and callers must be recompiled to see the new value.
        public static string Format(string text, bool upper = false, string suffix = "")
        {
            string result = upper ? text.ToUpperInvariant() : text;
            return result + suffix;
        }

        public static string UseDefaults()
        {
            return Format("value");
        }

        // A named argument lets a caller skip over an earlier optional one.
        public static string SkipMiddleParameter()
        {
            return Format("value", suffix: "!");
        }

        // Named arguments may also reorder the argument list for readability.
        public static string Reordered()
        {
            return Format(suffix: "?", text: "value", upper: true);
        }
    }
}
