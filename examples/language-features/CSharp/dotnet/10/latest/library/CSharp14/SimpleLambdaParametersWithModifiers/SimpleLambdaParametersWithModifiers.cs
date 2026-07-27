using System;

namespace Net10_CSharpLatest_Library.CSharp14.SimpleLambdaParametersWithModifiers
{
    public delegate bool TryParser(string text, out int value);

    public delegate void Mutator(ref int value);

    public class ModifiedParameters
    {
        // A lambda parameter carrying a modifier no longer has to state its
        // type. Before C# 14.0 adding `ref` or `out` forced the type to be
        // written as well, so one modifier cost every parameter its inference.
        public static TryParser Parser()
        {
            return (text, out value) => int.TryParse(text, out value);
        }

        public static Mutator Incrementer()
        {
            return (ref value) => value += 1;
        }

        // The fully-typed form remains legal, and is what was required before.
        public static Mutator ExplicitlyTyped()
        {
            return (ref int value) => value += 2;
        }

        public static int UseMutator()
        {
            int value = 1;
            Incrementer()(ref value);
            return value;
        }
    }
}
