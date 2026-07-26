namespace CSharpNet6_10.CSharp6.ExpressionBodiedMembers
{
    // C# 6.0 allows an expression body on methods, read-only properties,
    // indexers, and operators. Constructors and property accessors had to wait
    // for C# 7.0 — see the ExpressionBodiedMembersExtended folder.
    public class Vector
    {
        private readonly int[] _values;

        public Vector(int[] values)
        {
            _values = values;
        }

        // Read-only property.
        public int Count => _values.Length;

        // Indexer.
        public int this[int index] => _values[index];

        // Method.
        public int Sum() => Total();

        public override string ToString() => "Vector(" + Count + ")";

        // Operator.
        public static Vector operator +(Vector left, Vector right) => Combine(left, right);

        private int Total()
        {
            int total = 0;
            foreach (int value in _values)
            {
                total += value;
            }

            return total;
        }

        private static Vector Combine(Vector left, Vector right)
        {
            int length = left.Count < right.Count ? left.Count : right.Count;
            int[] combined = new int[length];
            for (int i = 0; i < length; i++)
            {
                combined[i] = left[i] + right[i];
            }

            return new Vector(combined);
        }
    }
}
