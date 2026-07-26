using System;

namespace CSharpNet6_10.CSharp2.DelegateInferenceAndConversions
{
    public class DelegateConversions
    {
        // Method group conversion: the bare method name converts to the delegate type.
        public static Predicate<string> FromMethodGroup()
        {
            return IsNotEmpty;
        }

        // The explicit-construction form — the only spelling available before method
        // group conversions — still compiles and means the same thing.
        public static Predicate<string> FromExplicitConstruction()
        {
            return new Predicate<string>(IsNotEmpty);
        }

        // Inference applies to instance methods just as it does to static ones.
        public Converter<int, string> FromInstanceMethodGroup()
        {
            return Describe;
        }

        private static bool IsNotEmpty(string value)
        {
            return !string.IsNullOrEmpty(value);
        }

        private string Describe(int value)
        {
            return value.ToString();
        }
    }
}
