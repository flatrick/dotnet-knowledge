namespace Net10_CSharp12_Library.CSharp11.ListPatterns
{
    public class ListPatternSamples
    {
        // A list pattern matches an indexable, countable sequence element by
        // element. The slice pattern .. matches any number of elements.
        public static string Describe(int[] values) => values switch
        {
            [] => "empty",
            [var single] => "one: " + single,
            [var first, var second] => "two: " + first + "," + second,
            [var head, .. var rest] => "head " + head + " then " + rest.Length,
        };

        // A slice may be bound, or discarded, and may sit anywhere in the
        // pattern — including between two fixed positions.
        public static bool FirstAndLast(int[] values)
        {
            return values is [1, .., 9];
        }

        // List patterns nest with the other pattern kinds.
        public static bool StartsPositive(int[] values)
        {
            return values is [> 0, ..];
        }

        // They work on any type with a Length or Count and an indexer, so a
        // string matches too.
        public static bool ThreeChars(string text)
        {
            return text is ['a', _, 'c'];
        }
    }
}
