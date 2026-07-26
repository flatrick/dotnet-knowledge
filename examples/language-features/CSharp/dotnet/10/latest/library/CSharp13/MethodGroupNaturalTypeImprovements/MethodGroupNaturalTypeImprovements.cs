using System;

namespace CSharpNet10Latest.CSharp13.MethodGroupNaturalTypeImprovements
{
    public class NaturalTypes
    {
        // A method group's natural type is the signature `var` infers from it.
        // C# 10.0 introduced it, but built the candidate set from every method
        // sharing the name, so a group whose overloads disagreed on shape had no
        // natural type at all — even when only one of them could ever apply.
        //
        // C# 13.0 filters the candidate set before looking for a unique
        // signature: a candidate whose type arguments violate its constraints is
        // removed rather than left in to create an ambiguity.
        private static string Render<T>(T value)
            where T : struct
        {
            return value.ToString();
        }

        private static string Render<T>(T value, string format)
            where T : class
        {
            return format + value;
        }

        // `int` satisfies `struct` but not `class`, so the two-parameter
        // overload is discarded and the group resolves to the one that is left.
        // Before C# 13.0 this was CS8917, "the delegate type could not be
        // inferred".
        public static string ConstraintFiltered()
        {
            var render = Render<int>;
            return render(42);
        }

        // The mirror case, to show the filter is driven by the type argument and
        // not by a preference for one overload: `string` satisfies `class` but
        // not `struct`, so here the surviving candidate is the two-parameter
        // overload and the inferred delegate has a different shape.
        public static string ConstraintFilteredOther()
        {
            var render = Render<string>;
            return render("value", "G");
        }

        // Nothing changed where a target type is already supplied. Converting a
        // method group to a known delegate type has picked the matching overload
        // since C# 2.0 and never consulted a natural type, which is why this
        // form alone cannot demonstrate the C# 13.0 rule.
        public static Func<int, string> TargetTypedNeedsNoNaturalType()
        {
            return Render<int>;
        }
    }
}
