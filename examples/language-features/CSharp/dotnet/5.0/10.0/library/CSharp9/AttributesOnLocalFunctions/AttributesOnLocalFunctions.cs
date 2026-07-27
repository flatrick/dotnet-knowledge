using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Net5_CSharp10_Library.CSharp9.AttributesOnLocalFunctions
{
    public class LocalFunctionAttributes
    {
        // A local function, its parameters, and its type parameters may all
        // carry attributes from C# 9.0. Before that, the only way to attribute
        // such a helper was to promote it to a private method.
        public static int Parse(string text)
        {
            [return: NotNullIfNotNull(nameof(text))]
            static string Normalize(string value)
            {
                return value == null ? null : value.Trim();
            }

            string normalized = Normalize(text);
            return normalized == null ? 0 : normalized.Length;
        }

        // An attribute on a parameter of a local function.
        public static int Measure(string text)
        {
            static int Length([AllowNull] string value)
            {
                return value == null ? 0 : value.Length;
            }

            return Length(text);
        }

        // Conditional compilation attributes work here too.
        public static void Trace()
        {
            [Conditional("DEBUG")]
            static void Log()
            {
            }

            Log();
        }
    }
}
