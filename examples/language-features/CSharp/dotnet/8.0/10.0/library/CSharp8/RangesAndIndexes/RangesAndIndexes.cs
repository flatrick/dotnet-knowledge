using System;

namespace Net8_CSharp10.CSharp8.RangesAndIndexes
{
    // This row is absent from the net48 project because System.Index and
    // System.Range have no official backport package for that target.
    public class Slicing
    {
        // ^ counts from the end: ^1 is the last element, ^0 is one past it.
        public static int Last(int[] values)
        {
            return values[^1];
        }

        // A range produces a slice. The start is inclusive, the end exclusive.
        public static int[] Middle(int[] values)
        {
            return values[1..^1];
        }

        public static int[] FirstTwo(int[] values)
        {
            return values[..2];
        }

        public static int[] FromSecond(int[] values)
        {
            return values[1..];
        }

        // Index and Range are ordinary types, so they can be stored and reused.
        public static int[] ByStoredRange(int[] values)
        {
            Range range = 1..3;
            return values[range];
        }

        public static int ByStoredIndex(int[] values)
        {
            Index index = ^2;
            return values[index];
        }

        // On an array the slice is a copy; on a Span it is a view. The syntax
        // is identical, which is worth knowing before relying on either.
        public static int SpanSlice(int[] values)
        {
            Span<int> span = values;
            Span<int> slice = span[1..3];
            slice[0] = -1;
            return values[1];
        }
    }
}
