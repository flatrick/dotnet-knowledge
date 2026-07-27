using System.Runtime.CompilerServices;
using System.Text;

namespace Net10_CSharp11_Library.CSharp10.InterpolatedStringHandlers
{
    // A handler intercepts an interpolated string before it becomes a string.
    // The compiler rewrites the literal into calls on this type — one
    // AppendLiteral per fixed chunk and one AppendFormatted per hole — so the
    // work can be skipped or redirected entirely.
    [InterpolatedStringHandler]
    public ref struct UpperCaseHandler
    {
        private readonly StringBuilder _builder;

        // The compiler passes the literal length and hole count. Extra
        // parameters can be requested; these two are the required shape.
        public UpperCaseHandler(int literalLength, int formattedCount)
        {
            _builder = new StringBuilder(literalLength);
        }

        public void AppendLiteral(string value)
        {
            _builder.Append(value);
        }

        public void AppendFormatted<T>(T value)
        {
            _builder.Append(value?.ToString()?.ToUpperInvariant());
        }

        public string GetResult()
        {
            return _builder.ToString();
        }
    }

    public class Logging
    {
        // Because the parameter is the handler type rather than string, the
        // interpolated string at the call site is built through the handler.
        public static string Shout(UpperCaseHandler handler)
        {
            return handler.GetResult();
        }

        // The holes are upper-cased by the handler; the literal parts are not.
        public static string Call(string name)
        {
            return Shout($"hello {name}, welcome");
        }
    }
}
