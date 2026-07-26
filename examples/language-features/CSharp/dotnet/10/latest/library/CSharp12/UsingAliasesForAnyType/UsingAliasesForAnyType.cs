using System.Collections.Generic;

// A using alias may now name any type, not only a named type. Tuples, arrays,
// pointers, and generic instantiations all become aliasable, which is what
// makes a tuple shape reusable without declaring a record for it.
using Coordinate = (int X, int Y);
using Matrix = int[][];
using Lookup = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>;

namespace CSharpNet10Latest.CSharp12.UsingAliasesForAnyType
{
    public class Aliases
    {
        // The alias carries the tuple's element names with it.
        public static Coordinate Origin()
        {
            return (0, 0);
        }

        public static int SumCoordinate(Coordinate point)
        {
            return point.X + point.Y;
        }

        public static Matrix EmptyMatrix()
        {
            return new int[0][];
        }

        // The alias and the type it names are the same type, so they are
        // interchangeable.
        public static Lookup NewLookup()
        {
            Dictionary<string, List<int>> lookup = new Lookup();
            return lookup;
        }
    }
}
