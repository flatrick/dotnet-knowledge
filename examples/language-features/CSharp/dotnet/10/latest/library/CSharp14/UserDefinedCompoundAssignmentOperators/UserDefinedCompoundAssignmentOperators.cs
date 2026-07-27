namespace Net10_CSharpLatest_Library.CSharp14.UserDefinedCompoundAssignmentOperators
{
    // A compound assignment used to be defined entirely by its binary operator:
    // `x += y` meant `x = x + y`, allocating a new instance every time. C# 14.0
    // lets a type define `+=` itself, so a mutable type can update in place.
    public class Accumulator
    {
        public int Total { get; private set; }

        public Accumulator(int total)
        {
            Total = total;
        }

        // The binary operator still produces a new instance.
        public static Accumulator operator +(Accumulator left, Accumulator right)
        {
            return new Accumulator(left.Total + right.Total);
        }

        // The compound operator mutates the receiver instead, which is the
        // point: no allocation per accumulation.
        public void operator +=(Accumulator other)
        {
            Total += other.Total;
        }

        public void operator -=(Accumulator other)
        {
            Total -= other.Total;
        }
    }

    public class Usage
    {
        // The instance is updated in place; no new Accumulator is created.
        public static int Accumulate()
        {
            Accumulator total = new Accumulator(0);
            total += new Accumulator(3);
            total += new Accumulator(4);
            return total.Total;
        }

        // The binary form still allocates, and is unchanged.
        public static int Combine()
        {
            Accumulator combined = new Accumulator(3) + new Accumulator(4);
            return combined.Total;
        }
    }
}
