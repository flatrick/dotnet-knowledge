using System.Threading;

namespace CSharpNet10Latest.CSharp13.LockObject
{
    public class Counter
    {
        // System.Threading.Lock is a dedicated mutual-exclusion type. When the
        // lock statement's operand has this type the compiler emits
        // Lock.EnterScope rather than Monitor.Enter, which is faster and cannot
        // be confused with locking on an arbitrary object.
        private readonly Lock _gate = new Lock();
        private int _total;

        public void Add(int value)
        {
            lock (_gate)
            {
                _total += value;
            }
        }

        // The same thing written out, which is what the statement above
        // compiles into for this type.
        public void AddExplicitly(int value)
        {
            using (_gate.EnterScope())
            {
                _total += value;
            }
        }

        public int Total
        {
            get
            {
                lock (_gate)
                {
                    return _total;
                }
            }
        }
    }

    public class LegacyCounter
    {
        // Locking on a plain object still works and still uses Monitor — the
        // C# 1.0 behavior, kept as contrast.
        private readonly object _gate = new object();
        private int _total;

        public void Add(int value)
        {
            lock (_gate)
            {
                _total += value;
            }
        }

        public int Total
        {
            get { return _total; }
        }
    }
}
