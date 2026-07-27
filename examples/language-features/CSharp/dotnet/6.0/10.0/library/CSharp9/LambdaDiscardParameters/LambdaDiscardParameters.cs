using System;

namespace Net6_CSharp10.CSharp9.LambdaDiscardParameters
{
    public class DiscardParameters
    {
        // Two or more parameters may all be named _, which states that the
        // lambda ignores them. Before C# 9.0 a second _ was a duplicate-name
        // error, so unused parameters needed invented names.
        public static Func<int, int, int> Constant()
        {
            return static (_, _) => 42;
        }

        // Mixed: one parameter used, the rest discarded.
        public static Func<int, int, int, int> FirstOnly()
        {
            return static (value, _, _) => value;
        }

        // The same in an anonymous method.
        public static Func<int, int, string> AnonymousMethod()
        {
            return static delegate(int _, int _) { return "ignored"; };
        }

        // A single _ is still an ordinary parameter name, usable as a value —
        // the discard meaning only applies from two upward.
        public static Func<int, int> SingleUnderscoreIsAParameter()
        {
            return static _ => _ + 1;
        }
    }
}
