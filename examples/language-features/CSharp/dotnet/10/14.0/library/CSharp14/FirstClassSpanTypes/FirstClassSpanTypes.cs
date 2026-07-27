using System;

namespace Net10_CSharp14_Library.CSharp14.FirstClassSpanTypes
{
    public static class SpanExtensions
    {
        public static int Tally<T>(this ReadOnlySpan<T> values)
        {
            return values.Length;
        }
    }

    public class SpanConversions
    {
        // C# 14.0 makes the array-to-span and span-to-span conversions part of
        // the language rather than library operators the compiler happens to
        // find. The difference is visible in the positions where the compiler is
        // now willing to apply them: type inference, extension-method receivers,
        // and variance.
        private static int Total<T>(ReadOnlySpan<T> values)
        {
            return values.Length;
        }

        // The conversion now takes part in generic type inference, so `T` is
        // inferred as `int` from an `int[]` argument bound to a
        // `ReadOnlySpan<T>` parameter. Through C# 13.0 this was CS0411, "the
        // type arguments ... cannot be inferred from the usage".
        public static int InferredFromArray()
        {
            int[] values = new int[] { 1, 2, 3 };
            return Total(values);
        }

        // It also applies to an extension-method receiver, so an array can call
        // a span extension without an AsSpan() hop. Also CS0411 before C# 14.0.
        public static int ExtensionReceiver()
        {
            int[] values = new int[] { 1, 2, 3, 4 };
            return values.Tally();
        }

        // ReadOnlySpan<T> is covariant where its element type has a reference
        // conversion, with no array anywhere in the chain. Through C# 13.0 this
        // was CS0029, "cannot implicitly convert type".
        public static int Covariant()
        {
            ReadOnlySpan<string> source = new string[] { "a", "b" };
            ReadOnlySpan<object> bases = source;
            return bases.Length;
        }

        // A writable Span<T> reaches ReadOnlySpan<U> in a single step, giving up
        // writability and widening the element type at once. CS0029 before
        // C# 14.0 as well.
        public static int WritableToCovariantReadOnly()
        {
            Span<string> source = new string[] { "a", "b", "c" };
            ReadOnlySpan<object> bases = source;
            return bases.Length;
        }

        // What does NOT demonstrate this feature: passing an array where the
        // parameter's element type is already fixed. That conversion has existed
        // since ReadOnlySpan shipped and compiles as far back as C# 7.2, so a
        // sample built only from this form proves nothing about C# 14.0.
        public static int PredatesTheFeature()
        {
            int[] values = new int[] { 1, 2, 3 };
            return Total<int>(values);
        }
    }
}
