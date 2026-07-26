namespace CSharpNet6_10.CSharp10.ConstantInterpolatedStrings
{
    public class Constants
    {
        private const string Product = "dotnet";
        private const string Major = "10";

        // An interpolated string may be const when every hole is itself a
        // constant string. The compiler folds it at compile time, so the result
        // is usable anywhere a constant is required.
        public const string Banner = $"{Product} v{Major}";

        // Because it is a constant, it can be a default parameter value...
        public static string Describe(string banner = Banner)
        {
            return banner;
        }

        // ...an attribute argument, or another constant's operand.
        public const string Extended = $"{Banner} (preview)";

        public static bool FoldedAtCompileTime()
        {
            return Banner == "dotnet v10";
        }
    }
}
