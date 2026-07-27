using System.Collections;
using System.Collections.Generic;

namespace Net48_CSharp4_Library.CSharp2.Iterators
{
    // yield return makes the compiler generate the enumerator state machine.
    public class NumberSequence : IEnumerable<int>
    {
        private readonly int _count;

        public NumberSequence(int count)
        {
            _count = count;
        }

        public IEnumerator<int> GetEnumerator()
        {
            for (int i = 0; i < _count; i++)
            {
                yield return i;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    public class IteratorMethods
    {
        // An iterator method may return IEnumerable<T> directly.
        public static IEnumerable<int> EvenNumbers(int limit)
        {
            for (int i = 0; i <= limit; i++)
            {
                if (i % 2 != 0)
                {
                    continue;
                }

                yield return i;
            }
        }

        // yield break ends the iteration early.
        public static IEnumerable<string> UntilEmpty(string[] values)
        {
            foreach (string value in values)
            {
                if (string.IsNullOrEmpty(value))
                {
                    yield break;
                }

                yield return value;
            }
        }
    }
}
