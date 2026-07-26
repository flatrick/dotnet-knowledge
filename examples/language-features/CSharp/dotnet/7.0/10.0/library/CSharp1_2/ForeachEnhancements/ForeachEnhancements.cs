using System;
using System.Collections;

namespace CSharpNet7_10.CSharp1_2.ForeachEnhancements
{
    // C# 1.2 made foreach dispose the enumerator when it implements IDisposable.
    public class DisposableEnumerator : IEnumerator, IDisposable
    {
        private readonly int[] _values;
        private int _index = -1;
        private bool _disposed;

        public DisposableEnumerator(int[] values)
        {
            _values = values;
        }

        public bool Disposed
        {
            get { return _disposed; }
        }

        public object Current
        {
            get { return _values[_index]; }
        }

        public bool MoveNext()
        {
            _index++;
            return _index < _values.Length;
        }

        public void Reset()
        {
            _index = -1;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

    public class DisposableSequence : IEnumerable
    {
        private readonly DisposableEnumerator _enumerator;

        public DisposableSequence(int[] values)
        {
            _enumerator = new DisposableEnumerator(values);
        }

        public DisposableEnumerator Enumerator
        {
            get { return _enumerator; }
        }

        public IEnumerator GetEnumerator()
        {
            return _enumerator;
        }
    }

    public class ForeachBehavior
    {
        // The loop below disposes the enumerator on exit without any explicit call.
        public static bool DisposesEnumerator()
        {
            DisposableSequence sequence = new DisposableSequence(new int[] { 1, 2, 3 });
            int total = 0;
            foreach (object value in sequence)
            {
                total += (int)value;
            }

            return sequence.Enumerator.Disposed && total == 6;
        }

        // foreach over a string is specialized to index the string directly
        // rather than allocating an enumerator.
        public static int CountLetters(string text)
        {
            int letters = 0;
            foreach (char character in text)
            {
                if (char.IsLetter(character))
                {
                    letters++;
                }
            }

            return letters;
        }
    }
}
