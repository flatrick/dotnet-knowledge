using System.Collections.Generic;

namespace Net10_CSharp14_Library.CSharp14.UnboundGenericTypesInNameof
{
    public class UnboundNameof
    {
        // nameof now accepts an unbound generic type, so the name can be taken
        // without inventing a type argument that plays no part in the result.
        public static string ListName()
        {
            return nameof(List<>);
        }

        public static string DictionaryName()
        {
            return nameof(Dictionary<,>);
        }

        // The result is the bare name, with no arity marker and no arguments —
        // the same string the bound form produced.
        public static bool MatchesBoundForm()
        {
            return nameof(List<>) == nameof(List<int>);
        }

        public static bool NoArityInResult()
        {
            return nameof(List<>) == "List";
        }
    }
}
