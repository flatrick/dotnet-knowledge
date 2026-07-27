namespace Net10_CSharpLatest_Library.CSharp14.ExtensionOperators
{
    public readonly struct Meters
    {
        public Meters(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    // An extension block may declare OPERATORS, so a type gains them without
    // being modified. Before C# 14.0 an operator had to be a member of one of
    // its operand types, which put this out of reach for types you do not own.
    public static class MetersOperators
    {
        extension(Meters)
        {
            public static Meters operator +(Meters left, Meters right)
            {
                return new Meters(left.Value + right.Value);
            }

            public static Meters operator -(Meters left, Meters right)
            {
                return new Meters(left.Value - right.Value);
            }

            public static bool operator >(Meters left, Meters right)
            {
                return left.Value > right.Value;
            }

            public static bool operator <(Meters left, Meters right)
            {
                return left.Value < right.Value;
            }
        }
    }

    public class Usage
    {
        public static int Add()
        {
            Meters total = new Meters(3) + new Meters(4);
            return total.Value;
        }

        public static bool Compare()
        {
            return new Meters(5) > new Meters(2);
        }
    }
}
