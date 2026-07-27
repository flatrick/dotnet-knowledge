namespace Net10_CSharpLatest_Library.CSharp8.AlternativeInterpolatedVerbatimStrings
{
    public class TokenOrder
    {
        // C# 6.0 required the tokens in the order $@. C# 8.0 accepts @$ as
        // well, so the two may be written either way round.
        public static string OriginalOrder(string name)
        {
            return $@"C:\logs\{name}.txt";
        }

        public static string AlternativeOrder(string name)
        {
            return @$"C:\logs\{name}.txt";
        }

        // Both are verbatim and interpolated: backslashes are literal, and a
        // newline in the source is a newline in the value.
        public static bool OrdersAgree(string name)
        {
            return OriginalOrder(name) == AlternativeOrder(name);
        }

        public static string MultiLine(string name)
        {
            return @$"name: {name}
path: C:\logs\{name}";
        }
    }
}
