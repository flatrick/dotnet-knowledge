namespace CSharpNet10Latest.CSharp11.RawStringLiterals
{
    public class RawStrings
    {
        // A raw string literal is delimited by at least three quotes. Nothing
        // inside is escaped, so quotes and backslashes are literal.
        public static string Simple()
        {
            return """He said "hello" and left a \path\here.""";
        }

        // In the multi-line form the opening and closing delimiters sit on
        // their own lines, and the closing delimiter's indentation is stripped
        // from every content line — so the literal can be indented with the code
        // without that indentation ending up in the value.
        public static string Json()
        {
            return """
                {
                    "name": "value"
                }
                """;
        }

        // More quotes in the delimiter allow a run of quotes in the content.
        public static string ContainsTripleQuote()
        {
            return """"
                a """ sequence inside
                """";
        }

        // Interpolation uses one $ per brace level required, so JSON braces
        // need no escaping when the hole uses doubled braces.
        public static string Interpolated(string name)
        {
            return $$"""
                { "name": "{{name}}" }
                """;
        }
    }
}
