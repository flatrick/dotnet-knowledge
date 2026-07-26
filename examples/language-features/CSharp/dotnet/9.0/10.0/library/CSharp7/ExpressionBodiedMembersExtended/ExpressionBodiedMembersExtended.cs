using System.Collections.Generic;

namespace CSharpNet7_10.CSharp7.ExpressionBodiedMembersExtended
{
    // C# 7.0 extended expression bodies to the members C# 6.0 had left out:
    // constructors, finalizers, and individual property and indexer accessors.
    public class Counter
    {
        private readonly List<int> _values = new List<int>();
        private int _count;

        // Expression-bodied constructor.
        public Counter(int start) => _count = start;

        // Expression-bodied get and set accessors, written separately. C# 6.0
        // could only do this when the property was read-only.
        public int Count
        {
            get => _count;
            set => _count = value;
        }

        // Expression-bodied indexer accessors.
        public int this[int index]
        {
            get => _values[index];
            set => _values[index] = value;
        }

        public void Add(int value) => _values.Add(value);
    }

    public class Handle
    {
        private readonly string _name;

        public Handle(string name) => _name = name;

        public string Name => _name;

        // Expression-bodied finalizer. Real code should almost never write a
        // finalizer at all; it is here because it is one of the three members
        // this row added.
        ~Handle() => System.Diagnostics.Debug.WriteLine(_name);
    }
}
