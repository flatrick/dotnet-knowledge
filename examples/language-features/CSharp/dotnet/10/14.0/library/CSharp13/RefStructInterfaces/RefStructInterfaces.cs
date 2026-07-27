using System;

namespace Net10_CSharp14_Library.CSharp13.RefStructInterfaces
{
    public interface IMeasurable
    {
        int Measure();
    }

    // A ref struct may now implement an interface. It still cannot be boxed, so
    // it can never be converted to the interface type — the implementation is
    // reachable only through a generic constrained with allows ref struct.
    public ref struct Window : IMeasurable
    {
        private readonly ReadOnlySpan<int> _items;

        public Window(ReadOnlySpan<int> items)
        {
            _items = items;
        }

        public int Measure()
        {
            return _items.Length;
        }
    }

    public class Measuring
    {
        // allows ref struct is an ANTI-constraint: it widens what T may be by
        // permitting a ref struct. In exchange the method may not box T or use
        // it where a reference is required.
        public static int MeasureAny<T>(T value)
            where T : IMeasurable, allows ref struct
        {
            return value.Measure();
        }

        public static int MeasureWindow()
        {
            Window window = new Window(stackalloc int[] { 1, 2, 3 });
            return MeasureAny(window);
        }
    }
}
