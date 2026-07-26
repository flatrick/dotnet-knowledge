using System.Runtime.CompilerServices;

namespace CSharpNet6_10.CSharp5.CallerInfoAttributes
{
    public class Diagnostics
    {
        // The compiler fills these optional parameters in at each call site, so
        // the caller writes nothing and there is no run-time stack inspection.
        public static string Describe(
            string message,
            [CallerMemberName] string member = "",
            [CallerFilePath] string file = "",
            [CallerLineNumber] int line = 0)
        {
            return message + " (" + member + " in " + file + ":" + line + ")";
        }

        public static string CalledFromHere()
        {
            // member becomes "CalledFromHere"; file and line describe this call.
            return Describe("checkpoint");
        }

        // An explicit argument wins over the value the compiler would inject.
        public static string CalledWithExplicitMember()
        {
            return Describe("checkpoint", "supplied-by-hand");
        }
    }
}
