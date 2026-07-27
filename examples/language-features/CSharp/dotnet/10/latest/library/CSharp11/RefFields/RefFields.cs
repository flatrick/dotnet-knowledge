using System;
using System.Diagnostics.CodeAnalysis;

namespace Net10_CSharpLatest_Library.CSharp11.RefFields
{
    // A ref struct may now hold a ref FIELD — a reference to storage owned by
    // someone else. This is what lets Span<T> be written in C# rather than
    // requiring compiler magic.
    public ref struct Accessor
    {
        private ref int _slot;

        public Accessor(ref int slot)
        {
            _slot = ref slot;
        }

        public int Value
        {
            get { return _slot; }
            set { _slot = value; }
        }
    }

    public ref struct Window
    {
        // scoped constrains a parameter's reference so it cannot escape the
        // method, which is how the compiler proves a ref field assignment safe.
        private ref int _first;

        public Window(scoped ReadOnlySpan<int> source, ref int first)
        {
            _first = ref first;
            Length = source.Length;
        }

        public int Length { get; }

        public int First
        {
            get { return _first; }
        }
    }

    public struct Counter
    {
        private int _count;

        // UnscopedRef opts a member out of the default scoping, allowing a
        // reference to this instance's field to escape the call.
        [UnscopedRef]
        public ref int CountRef()
        {
            return ref _count;
        }

        public int Count
        {
            get { return _count; }
        }
    }

    public class Usage
    {
        // Writing through the ref field writes the caller's variable.
        public static int WriteThrough()
        {
            int storage = 1;
            Accessor accessor = new Accessor(ref storage);
            accessor.Value = 42;
            return storage;
        }

        public static int ThroughUnscopedRef()
        {
            Counter counter = new Counter();
            ref int slot = ref counter.CountRef();
            slot = 7;
            return counter.Count;
        }
    }
}
