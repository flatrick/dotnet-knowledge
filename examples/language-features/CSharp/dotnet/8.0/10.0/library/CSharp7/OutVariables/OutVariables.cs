using System.Collections.Generic;

namespace Net8_CSharp10_Library.CSharp7.OutVariables
{
    public class OutVariableSamples
    {
        // The out argument is declared inline, so the two-line
        // declare-then-call dance is gone.
        public static int ParseOrZero(string text)
        {
            if (int.TryParse(text, out int value))
            {
                return value;
            }

            return 0;
        }

        // var works here too; the type comes from the parameter.
        public static string LookupOrDefault(Dictionary<string, string> map, string key)
        {
            if (map.TryGetValue(key, out var found))
            {
                return found;
            }

            return "missing";
        }

        // The variable's scope is the enclosing block, not the condition, so it
        // is still usable after the if.
        public static string DescribeParse(string text)
        {
            if (!int.TryParse(text, out int parsed))
            {
                return "unparsed";
            }

            return "parsed:" + parsed;
        }

        public static void Split(int total, out int half, out int remainder)
        {
            half = total / 2;
            remainder = total % 2;
        }

        public static string UseSplit()
        {
            Split(7, out int half, out int remainder);
            return half + "+" + remainder;
        }
    }
}
