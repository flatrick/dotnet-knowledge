using System;
using System.Runtime.CompilerServices;

namespace Net7_CSharp10_Library.CSharp10.CallerArgumentExpression
{
    public class Guards
    {
        // The compiler fills this parameter with the SOURCE TEXT of the
        // argument named in the attribute, so a failure message can quote the
        // expression that failed without the caller repeating it as a string.
        public static string Require(
            bool condition,
            [CallerArgumentExpression(nameof(condition))] string expression = null)
        {
            return condition ? "ok" : "failed: " + expression;
        }

        // The message here contains "value > 0", captured from the call site.
        public static string CheckPositive(int value)
        {
            return Require(value > 0);
        }

        // It composes with the other caller-info attributes, which have been
        // available since C# 5.0.
        public static string Describe(
            int value,
            [CallerArgumentExpression(nameof(value))] string expression = null,
            [CallerMemberName] string member = "")
        {
            return expression + " in " + member + " = " + value;
        }

        public static string DescribeCall(int seed)
        {
            return Describe(seed * 2);
        }

        // An explicit argument still wins over the injected one.
        public static string Explicit()
        {
            return Require(false, "supplied by hand");
        }
    }
}
