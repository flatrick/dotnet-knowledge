using System.Collections.Generic;

// Using-alias directives (this one and the namespace alias below) are a C# 1.0
// feature; this file's alias happens to target a generic type only because
// generics themselves are new in C# 2.0. The genuinely C# 2.0-only construct in
// this file is the global:: qualifier further down.
using IntList = System.Collections.Generic.List<int>;

// A using alias for a namespace.
using Text = System.Text;

namespace Net9_CSharp10_Library.CSharp2.NamespaceAndTypeAliases
{
    public class Aliases
    {
        public static IntList CreateList()
        {
            IntList values = new IntList();
            values.Add(1);
            values.Add(2);
            return values;
        }

        public static string BuildText()
        {
            Text.StringBuilder builder = new Text.StringBuilder();
            builder.Append("alias");
            return builder.ToString();
        }

        // The global:: qualifier resolves from the global namespace, which
        // disambiguates when a local name shadows a framework one.
        public static global::System.DateTime Epoch()
        {
            return new global::System.DateTime(1970, 1, 1);
        }

        public static List<string> Names()
        {
            return new List<string>();
        }
    }
}
