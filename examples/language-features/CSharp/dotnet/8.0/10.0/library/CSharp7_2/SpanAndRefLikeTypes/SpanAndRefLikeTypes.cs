using System;

namespace Net8_CSharp10_Library.CSharp7_2.SpanAndRefLikeTypes
{
    // A ref struct may live only on the stack: it cannot be boxed, captured by
    // a lambda, or stored in a field of a normal class. That restriction is
    // what makes it safe for a type to hold a Span.
    public ref struct Window
    {
        private readonly Span<int> _items;

        public Window(Span<int> items)
        {
            _items = items;
        }

        public int First
        {
            get { return _items[0]; }
        }

        public int Length
        {
            get { return _items.Length; }
        }
    }

    public class SpanSamples
    {
        // Span addresses memory of any origin — array, stack, or unmanaged —
        // behind one type, without copying it.
        public static int Sum(Span<int> values)
        {
            int total = 0;
            foreach (int value in values)
            {
                total += value;
            }

            return total;
        }

        // A slice is a view, not a copy: writing through it writes the array.
        public static int[] WriteThroughSlice(int[] values)
        {
            Span<int> slice = new Span<int>(values, 1, 2);
            slice[0] = -1;
            return values;
        }

        // stackalloc assigned to a Span needs no unsafe context, because the
        // ref struct rules already stop the reference outliving the frame.
        public static int FromStack()
        {
            Span<byte> buffer = stackalloc byte[4];
            buffer[0] = 7;
            return buffer[0];
        }

        public static int UseRefStruct(int[] values)
        {
            Window window = new Window(values);
            return window.First + window.Length;
        }
    }
}
