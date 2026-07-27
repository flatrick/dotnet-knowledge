namespace Net10_CSharp13_Library.CSharp7_2.NonTrailingNamedArguments
{
    public class Formatter
    {
        public static string Format(string text, int width, bool upper)
        {
            string result = upper ? text.ToUpperInvariant() : text;
            return result.PadRight(width, '.');
        }

        // Before C# 7.2 a named argument had to be followed only by other named
        // arguments. Now one may be named in its own position and the rest left
        // positional — useful for labeling a bare literal.
        public static string NamedFirst()
        {
            return Format(text: "value", 10, false);
        }

        public static string NamedInMiddle()
        {
            return Format("value", width: 10, false);
        }

        // The C# 7.2 rule only constrains a named argument that is followed by
        // positional arguments: that name must sit in the position its
        // parameter actually occupies. It says nothing about a call where
        // every argument is named — that has been legal, and freely
        // reorderable, since C# 4.0, because there are no positional
        // arguments left to find a parameter for.
        public static string AllNamed()
        {
            return Format(upper: true, text: "value", width: 10);
        }
    }
}
