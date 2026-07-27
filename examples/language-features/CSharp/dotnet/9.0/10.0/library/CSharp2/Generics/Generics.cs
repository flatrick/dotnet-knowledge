using System;
using System.Collections.Generic;

namespace Net9_CSharp10_Library.CSharp2.Generics
{
    // Reference-type plus parameterless-constructor constraints.
    public class Repository<T> where T : class, new()
    {
        private readonly List<T> _items = new List<T>();

        public int Count
        {
            get { return _items.Count; }
        }

        public T Add()
        {
            T item = new T();
            _items.Add(item);
            return item;
        }

        public T Get(int index)
        {
            return _items[index];
        }
    }

    // Value-type constraint combined with an interface constraint.
    public struct Range<T> where T : struct, IComparable<T>
    {
        private readonly T _low;
        private readonly T _high;

        public Range(T low, T high)
        {
            _low = low;
            _high = high;
        }

        public bool Contains(T value)
        {
            return value.CompareTo(_low) >= 0 && value.CompareTo(_high) <= 0;
        }
    }

    public interface IConverter<TInput, TOutput>
    {
        TOutput Convert(TInput input);
    }

    public class Conversions
    {
        // A generic method: the type arguments are inferred at the call site.
        public static TOutput[] ConvertAll<TInput, TOutput>(TInput[] inputs, IConverter<TInput, TOutput> converter)
        {
            TOutput[] results = new TOutput[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                results[i] = converter.Convert(inputs[i]);
            }

            return results;
        }
    }

    public class IntToStringConverter : IConverter<int, string>
    {
        public string Convert(int input)
        {
            return input.ToString();
        }
    }
}
