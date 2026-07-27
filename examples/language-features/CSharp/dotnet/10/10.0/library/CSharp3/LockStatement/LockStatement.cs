using System.Collections.Generic;

namespace Net10_CSharp10_Library.CSharp3.LockStatement
{
    // The lock statement shipped in C# 1.0. This folder follows the section
    // placement of the corpus's source document; MANIFEST.md's Note column
    // records that discrepancy.
    public class Counter
    {
        private readonly object _gate = new object();
        private readonly List<int> _values = new List<int>();
        private int _total;

        // lock takes a monitor on the given object for the block's duration and
        // releases it on every exit path, exceptions included.
        public void Add(int value)
        {
            lock (_gate)
            {
                _values.Add(value);
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

        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _values.Count;
                }
            }
        }
    }
}
