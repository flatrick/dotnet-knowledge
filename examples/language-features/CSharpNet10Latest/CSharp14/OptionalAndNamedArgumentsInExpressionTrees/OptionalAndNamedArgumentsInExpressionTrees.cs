using System;
using System.Linq.Expressions;

namespace CSharpNet10Latest.CSharp14.OptionalAndNamedArgumentsInExpressionTrees
{
    public class Formatting
    {
        public static string Format(string text, bool upper = false, string suffix = "")
        {
            string result = upper ? text.ToUpperInvariant() : text;
            return result + suffix;
        }

        // An expression tree may now contain a call that omits an optional
        // argument. Before C# 14.0 a tree had to supply every parameter.
        public static Expression<Func<string>> OmitsOptional()
        {
            return () => Format("value");
        }

        // Named arguments are allowed too, as long as each name sits in its
        // own parameter position.
        public static Expression<Func<string>> NamedInPosition()
        {
            return () => Format(text: "value", upper: true, suffix: "!");
        }

        // The relaxation stops short of reordering: a named argument OUT of
        // position is still rejected in a tree (CS9307), even though the same
        // call is perfectly legal outside one. This is the boundary of the
        // feature, not an oversight in the sample.
        public static string ReorderedOutsideATree()
        {
            return Format(suffix: "?", text: "value", upper: true);
        }

        // The tree is a real tree: compiling and invoking it produces what the
        // direct call would.
        public static string CompileAndInvoke()
        {
            return NamedInPosition().Compile()();
        }
    }
}
