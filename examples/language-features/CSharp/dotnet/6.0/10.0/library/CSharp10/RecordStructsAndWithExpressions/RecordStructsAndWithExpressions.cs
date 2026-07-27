namespace Net6_CSharp10_Library.CSharp10.RecordStructsAndWithExpressions
{
    // A record struct is a value type with the record members generated for it.
    // Unlike a record class its members are mutable unless declared readonly.
    public record struct Point(int X, int Y);

    // readonly record struct is the immutable form, and the one to prefer.
    public readonly record struct Size(int Width, int Height);

    public class RecordStructSamples
    {
        // with works on a record struct exactly as on a record class.
        public static Point Moved(Point point)
        {
            return point with { X = point.X + 1 };
        }

        // Value equality is generated, so this compares members rather than
        // relying on the reflection-based ValueType.Equals a plain struct gets.
        public static bool EqualByValue()
        {
            return new Point(1, 2) == new Point(1, 2);
        }

        // C# 10.0 also allows with on an anonymous type.
        public static string AnonymousWith()
        {
            var original = new { Name = "a", Count = 1 };
            var updated = original with { Count = 2 };
            return updated.Name + updated.Count;
        }

        public static string Print()
        {
            return new Size(3, 4).ToString();
        }
    }
}
