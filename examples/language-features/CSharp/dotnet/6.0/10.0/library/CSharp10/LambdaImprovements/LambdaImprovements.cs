using System;

namespace Net6_CSharp10_Library.CSharp10.LambdaImprovements
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class AuditedAttribute : Attribute
    {
    }

    public class LambdaFeatures
    {
        // A lambda now has a NATURAL delegate type, so var can infer it. The
        // compiler picks Func<int, int> here without being told.
        public static int NaturalType()
        {
            var square = (int value) => value * value;
            return square(4);
        }

        // An explicit return type may be stated, which is needed when inference
        // would pick the wrong one or cannot decide.
        public static object ExplicitReturnType()
        {
            var choose = object (bool first) => first ? "text" : 1;
            return choose(true);
        }

        // Attributes may be applied to a lambda, and to its parameters.
        public static int WithAttribute()
        {
            var audited = [Audited] (int value) => value + 1;
            return audited(1);
        }

        // A method group also has a natural type now, so this needs no cast.
        // It only works when the group is unambiguous: an overloaded group such
        // as int.Parse has no single natural type and is rejected (CS8917).
        private static int Triple(int value)
        {
            return value * 3;
        }

        public static int MethodGroupNaturalType()
        {
            var triple = Triple;
            return triple(5);
        }
    }
}
