using System;

namespace CSharpNet10Latest.CSharp13.MethodGroupNaturalTypeImprovements
{
    public class NaturalTypes
    {
        private static int Parse(string text)
        {
            return text.Length;
        }

        private static int Parse(string text, int fallback)
        {
            return text.Length + fallback;
        }

        // C# 10.0 gave a method group a natural type only when the group had
        // exactly one candidate. C# 13.0 narrows the candidate set by the
        // target signature first, so an overloaded group resolves when only one
        // overload has a matching shape.
        public static Func<string, int> SingleArgument()
        {
            return Parse;
        }

        public static Func<string, int, int> TwoArguments()
        {
            return Parse;
        }

        // Candidates that fail their constraints are removed rather than
        // treated as ambiguous.
        private static T Identity<T>(T value)
            where T : struct
        {
            return value;
        }

        private static string Identity(string value)
        {
            return value;
        }

        public static Func<string, string> ConstraintFiltered()
        {
            return Identity;
        }
    }
}
