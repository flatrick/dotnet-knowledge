namespace Net6_CSharp10.CSharp8.RecursivePatternsSwitchExpressions
{
    public class Point
    {
        public int X { get; }

        public int Y { get; }

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public void Deconstruct(out int x, out int y)
        {
            x = X;
            y = Y;
        }
    }

    public class Patterns
    {
        // A switch expression is an expression, not a statement: it produces a
        // value, and the compiler checks it for exhaustiveness. A relational
        // pattern would read more directly here, but that is C# 9.0 — in C# 8.0
        // the comparison goes in a when guard.
        public static string Classify(int value) => value switch
        {
            0 => "zero",
            var other when other < 0 => "negative",
            _ => "positive",
        };

        // Property pattern: matches on the values of a member, and may nest.
        public static string Quadrant(Point point) => point switch
        {
            { X: 0, Y: 0 } => "origin",
            { X: var x, Y: var y } when x > 0 && y > 0 => "first",
            { X: var x, Y: var y } when x < 0 && y > 0 => "second",
            _ => "elsewhere",
        };

        // Positional pattern: uses the type's Deconstruct method.
        public static string OnAxis(Point point) => point switch
        {
            (0, 0) => "origin",
            (_, 0) => "x-axis",
            (0, _) => "y-axis",
            _ => "off-axis",
        };

        // Tuple pattern, with a discard for the cases that do not care.
        public static string Compare(int left, int right) => (left, right) switch
        {
            (0, 0) => "both zero",
            (_, 0) => "right zero",
            (0, _) => "left zero",
            _ => "neither zero",
        };
    }
}
