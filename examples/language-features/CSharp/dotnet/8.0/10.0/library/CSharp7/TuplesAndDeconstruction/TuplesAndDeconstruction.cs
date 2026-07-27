namespace Net8_CSharp10.CSharp7.TuplesAndDeconstruction
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

        // A Deconstruct method makes any type deconstructible; it need not be
        // a tuple, and it may be supplied as an extension method.
        public void Deconstruct(out int x, out int y)
        {
            x = X;
            y = Y;
        }
    }

    public class TupleSamples
    {
        // A tuple type is written in parentheses. This one's members are the
        // default Item1/Item2.
        public static (int, string) Unnamed()
        {
            return (1, "one");
        }

        // Named elements are compile-time only: the names live in metadata as
        // an attribute, and the runtime type is still ValueTuple<int, string>.
        public static (int count, string name) Named()
        {
            return (2, "two");
        }

        public static int UseNamed()
        {
            var result = Named();
            return result.count;
        }

        // Deconstruction into freshly declared variables.
        public static string DeconstructTuple()
        {
            (int count, string name) = Named();
            return name + count;
        }

        // The same with inferred types.
        public static string DeconstructWithVar()
        {
            var (count, name) = Named();
            return name + count;
        }

        // Deconstructing a non-tuple, via its Deconstruct method.
        public static int DeconstructType()
        {
            Point point = new Point(3, 4);
            var (x, y) = point;
            return x + y;
        }

        // The usual reason to reach for a tuple: several results without
        // declaring a type or resorting to out parameters.
        public static (int min, int max) MinMax(int[] values)
        {
            int min = values[0];
            int max = values[0];
            foreach (int value in values)
            {
                if (value < min)
                {
                    min = value;
                }

                if (value > max)
                {
                    max = value;
                }
            }

            return (min, max);
        }
    }
}
