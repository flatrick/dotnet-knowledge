using System;
using System.Runtime.CompilerServices;

namespace Net10_CSharpLatest_Library.CSharp13.OverloadResolutionPriority
{
    public class Writer
    {
        // The attribute lets a library author steer resolution toward a
        // preferred overload without a source-breaking change. It exists for
        // exactly this case: adding a Span overload beside an array one and
        // wanting existing calls to start picking the Span.
        [OverloadResolutionPriority(1)]
        public static string Write(ReadOnlySpan<int> values)
        {
            return "span:" + values.Length;
        }

        // Default priority is 0, so this loses when both are applicable.
        public static string Write(int[] values)
        {
            return "array:" + values.Length;
        }

        // An int[] argument is applicable to both, and the higher priority wins.
        public static string CallWithArray()
        {
            return Write(new int[] { 1, 2, 3 });
        }

        // Priority only breaks ties among APPLICABLE candidates; it never makes
        // an inapplicable one apply.
        public static string CallWithSpan()
        {
            return Write(stackalloc int[] { 1 });
        }
    }
}
