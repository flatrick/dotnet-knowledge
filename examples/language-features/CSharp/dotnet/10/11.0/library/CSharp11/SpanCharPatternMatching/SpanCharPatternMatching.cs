using System;

namespace Net10_CSharp11_Library.CSharp11.SpanCharPatternMatching
{
    public class SpanPatterns
    {
        // A Span<char> or ReadOnlySpan<char> may be matched against a constant
        // string pattern. Before C# 11.0 this needed an explicit
        // SequenceEqual call, or a conversion back to string.
        public static bool IsYes(ReadOnlySpan<char> value)
        {
            return value is "yes";
        }

        // It composes with the other patterns, so a switch over a span reads
        // like a switch over a string.
        public static int Parse(ReadOnlySpan<char> value) => value switch
        {
            "zero" => 0,
            "one" => 1,
            "two" => 2,
            _ => -1,
        };

        // The point is avoiding an allocation: the span may address a slice of
        // a larger buffer that was never a string of its own.
        public static int ParseSlice(string text)
        {
            ReadOnlySpan<char> span = text.AsSpan(0, 3);
            return Parse(span);
        }
    }
}
