using System;
using System.Runtime.CompilerServices;

namespace Net10_CSharp14_Library.CSharp12.InlineArrays
{
    // An inline array is a struct holding a fixed number of contiguous
    // elements. It is the safe successor to a fixed-size buffer: unlike
    // `fixed byte Payload[4]`, it needs no unsafe context, works with any
    // element type rather than only primitives, and is indexed through Span.
    [InlineArray(4)]
    public struct Buffer4
    {
        // Exactly one instance field; its type is the element type, and the
        // attribute's argument is the length.
        private int _element0;
    }

    public class InlineArraySamples
    {
        // Indexing and foreach work through the compiler's Span conversion.
        public static int Fill()
        {
            Buffer4 buffer = default;
            for (int i = 0; i < 4; i++)
            {
                buffer[i] = i * 2;
            }

            return buffer[3];
        }

        public static int Sum()
        {
            Buffer4 buffer = default;
            buffer[0] = 1;
            buffer[1] = 2;

            int total = 0;
            foreach (int value in buffer)
            {
                total += value;
            }

            return total;
        }

        // It converts to Span and ReadOnlySpan directly, which is how it is
        // usually passed on.
        public static int ViaSpan()
        {
            Buffer4 buffer = default;
            buffer[2] = 9;
            Span<int> span = buffer;
            return span[2];
        }

        // Slicing works because the span conversion is a real span.
        public static int SliceLength()
        {
            Buffer4 buffer = default;
            ReadOnlySpan<int> span = buffer;
            return span.Slice(1, 2).Length;
        }
    }
}
