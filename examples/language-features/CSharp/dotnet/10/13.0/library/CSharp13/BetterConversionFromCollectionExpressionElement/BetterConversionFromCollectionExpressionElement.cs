using System;
using System.Collections.Generic;

namespace Net10_CSharp13_Library.CSharp13.BetterConversionFromCollectionExpressionElement
{
    public class Conversions
    {
        // When two overloads both accept a collection expression, C# 13.0
        // compares the ELEMENT conversions rather than only the collection
        // types, so int elements prefer the int overload.
        public static string Accept(ReadOnlySpan<int> values)
        {
            return "int:" + values.Length;
        }

        public static string Accept(ReadOnlySpan<long> values)
        {
            return "long:" + values.Length;
        }

        // int elements convert to both int and long; the better element
        // conversion decides.
        public static string PreferExactElement()
        {
            return Accept([1, 2, 3]);
        }

        // Explicit long elements select the other overload.
        public static string SelectWider()
        {
            return Accept([1L, 2L]);
        }

        public static string AcceptList(List<int> values)
        {
            return "list:" + values.Count;
        }

        public static string ListStillWorks()
        {
            return AcceptList([1, 2]);
        }
    }
}
